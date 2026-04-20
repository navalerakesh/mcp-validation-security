using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;

using Mcp.Benchmark.Infrastructure.Strategies.Scoring;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Infrastructure.Validators;

public class ResourceValidator : BaseValidator<ResourceValidator>, IResourceValidator
{
    private readonly IMcpHttpClient _httpClient;
    private readonly ISchemaValidator _schemaValidator;
    private readonly IScoringStrategy<ResourceTestResult> _scoringStrategy;
    private readonly ISchemaRegistry _schemaRegistry;
    private readonly IContentSafetyAnalyzer _contentSafetyAnalyzer;

    public ResourceValidator(ILogger<ResourceValidator> logger, IMcpHttpClient httpClient, ISchemaValidator schemaValidator, ISchemaRegistry schemaRegistry, IContentSafetyAnalyzer contentSafetyAnalyzer) 
        : base(logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
        _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
        _contentSafetyAnalyzer = contentSafetyAnalyzer ?? throw new ArgumentNullException(nameof(contentSafetyAnalyzer));
        _scoringStrategy = new ResourceScoringStrategy();
    }

    public async Task<ResourceTestResult> ValidateResourceDiscoveryAsync(McpServerConfig serverConfig, ResourceTestingConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, "Resource Discovery", async (ct) =>
        {
            var result = new ResourceTestResult();
            var capabilitySnapshot = config.CapabilitySnapshot;
            var cachedResponse = CapabilitySnapshotUtils.CloneResponse(capabilitySnapshot?.Payload?.ResourceListResponse);

            // Test resources/list
            var response = cachedResponse;
            if (response == null)
            {
                response = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ResourcesList, null, ct);
            }
            
            // MCP spec allows both public and auth-protected servers
            if (response.StatusCode == 401 || response.StatusCode == 403)
            {
                var hasWwwAuth = response.Headers?.ContainsKey("WWW-Authenticate") == true || 
                                 response.Headers?.ContainsKey("www-authenticate") == true;
                
                result.Status = TestStatus.Skipped;
                result.ResourcesDiscovered = 0;
                result.Issues.Add(hasWwwAuth 
                    ? "✅ COMPLIANT: Server properly secured with authentication (OAuth 2.1)"
                    : "⚠️  ACCEPTED: Authentication required but missing WWW-Authenticate header");
                return result;
            }
            
            if (!response.IsSuccess)
            {
                result.Status = TestStatus.Failed;
                result.Issues.Add($"❌ NON-COMPLIANT: resources/list failed: {response.Error}");
                return result;
            }

            var rawJson = response.RawJson ?? "{}";
            var jsonDoc = JsonDocument.Parse(rawJson);
            if (jsonDoc.RootElement.TryGetProperty("result", out var resultElement) &&
                resultElement.TryGetProperty("resources", out var resourcesElement) &&
                resourcesElement.ValueKind == JsonValueKind.Array)
            {
                result.ResourcesDiscovered = resourcesElement.GetArrayLength();
                
                foreach (var resource in resourcesElement.EnumerateArray())
                {
                    var resourceResult = new IndividualResourceResult
                    {
                        DiscoveredCorrectly = true,
                        MetadataValid = true
                    };

                    if (resource.TryGetProperty("uri", out var uriElement))
                    {
                        resourceResult.ResourceUri = uriElement.GetString() ?? "";
                    }
                    else
                    {
                        resourceResult.MetadataValid = false;
                        resourceResult.AddFinding(new ValidationFinding
                        {
                            RuleId = ValidationFindingRuleIds.ResourceMissingUri,
                            Category = "ProtocolCompliance",
                            Component = resourceResult.ResourceName,
                            Severity = ValidationFindingSeverity.High,
                            Summary = "Resource is missing 'uri' property",
                            Recommendation = "Include a stable uri for every resource returned by resources/list."
                        }, "Missing 'uri' property");
                    }

                    if (resource.TryGetProperty("name", out var nameElement))
                    {
                        resourceResult.ResourceName = nameElement.GetString() ?? "";
                    }
                    else
                    {
                        resourceResult.MetadataValid = false;
                        resourceResult.Issues.Add("Missing 'name' property");
                    }

                    resourceResult.Status = resourceResult.MetadataValid ? TestStatus.Passed : TestStatus.Failed;

                    // Static, metadata-only content safety analysis
                    if (!string.IsNullOrWhiteSpace(resourceResult.ResourceUri) || !string.IsNullOrWhiteSpace(resourceResult.ResourceName))
                    {
                        var safetyFindings = _contentSafetyAnalyzer.AnalyzeResource(resourceResult.ResourceName, resourceResult.ResourceUri);
                        if (safetyFindings.Count > 0)
                        {
                            resourceResult.ContentSafetyFindings.AddRange(safetyFindings);
                            result.ContentSafetyFindings.AddRange(safetyFindings);
                        }
                    }

                    result.ResourceResults.Add(resourceResult);

                    // EXECUTION VALIDATION: resources/read
                    if (config.TestResourceReading && resourceResult.MetadataValid && !string.IsNullOrEmpty(resourceResult.ResourceUri)) 
                    {
                        try 
                        {
                            var readStart = DateTime.UtcNow;
                            var readResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ResourcesRead, new { uri = resourceResult.ResourceUri }, serverConfig.Authentication, ct);
                            resourceResult.AccessTimeMs = (DateTime.UtcNow - readStart).TotalMilliseconds;

                            if (readResponse.IsSuccess)
                            {
                                resourceResult.AccessSuccessful = true;
                                
                                // MCP Spec Compliance: Validate resources/read response structure
                                using var readDoc = JsonDocument.Parse(readResponse.RawJson ?? "{}");
                                if (readDoc.RootElement.TryGetProperty("result", out var readRes) &&
                                    readRes.TryGetProperty("contents", out var contents) &&
                                    contents.ValueKind == JsonValueKind.Array && 
                                    contents.GetArrayLength() > 0)
                                {
                                    resourceResult.Issues.Add("✅ Resource read: contents[] array present");
                                    var firstContent = contents[0];

                                    // Validate uri field (MUST per spec)
                                    if (firstContent.TryGetProperty("uri", out var uriVal))
                                    {
                                        if (string.IsNullOrEmpty(uriVal.GetString()))
                                            resourceResult.Issues.Add("❌ MCP Compliance: contents[0].uri is empty (MUST be valid URI)");
                                    }
                                    else
                                    {
                                        resourceResult.AddFinding(new ValidationFinding
                                        {
                                            RuleId = ValidationFindingRuleIds.ResourceReadMissingContentUri,
                                            Category = "ProtocolCompliance",
                                            Component = resourceResult.ResourceUri,
                                            Severity = ValidationFindingSeverity.Critical,
                                            Summary = "resources/read contents[0] missing 'uri' field",
                                            Recommendation = "Return uri on each resource content item."
                                        }, "❌ MCP Compliance: contents[0] missing 'uri' field (MUST per spec)");
                                    }

                                    if (firstContent.TryGetProperty("mimeType", out var mime)) 
                                        resourceResult.MimeType = mime.GetString();
                                    
                                    // MUST have text OR blob
                                    if (firstContent.TryGetProperty("text", out var text))
                                        resourceResult.ContentSize = text.GetString()?.Length ?? 0;
                                    else if (firstContent.TryGetProperty("blob", out var blob))
                                        resourceResult.ContentSize = blob.GetString()?.Length ?? 0;
                                    else
                                        resourceResult.AddFinding(new ValidationFinding
                                        {
                                            RuleId = ValidationFindingRuleIds.ResourceReadMissingTextOrBlob,
                                            Category = "ProtocolCompliance",
                                            Component = resourceResult.ResourceUri,
                                            Severity = ValidationFindingSeverity.Critical,
                                            Summary = "resources/read contents[0] missing both text and blob",
                                            Recommendation = "Return either text or blob in each resource content item."
                                        }, "❌ MCP Compliance: contents[0] missing both 'text' and 'blob' (MUST have one)");
                                }
                                else
                                {
                                    resourceResult.AddFinding(new ValidationFinding
                                    {
                                        RuleId = ValidationFindingRuleIds.ResourceReadMissingContentArray,
                                        Category = "ProtocolCompliance",
                                        Component = resourceResult.ResourceUri,
                                        Severity = ValidationFindingSeverity.Critical,
                                        Summary = "resources/read response missing result.contents[] array",
                                        Recommendation = "Return result.contents as an array for resources/read responses."
                                    }, "❌ MCP Compliance: resources/read response missing result.contents[] array");
                                    resourceResult.AccessSuccessful = false;
                                }
                            }
                            else
                            {
                                resourceResult.AccessSuccessful = false;
                                resourceResult.Issues.Add($"⚠️ Resource read failed: {readResponse.Error ?? readResponse.StatusCode.ToString()}");
                            }
                        }
                        catch (Exception ex)
                        {
                            resourceResult.AccessSuccessful = false; 
                            resourceResult.Issues.Add($"❌ Resource read exception: {ex.Message}");
                        }
                    }
                }
            }

                    // Schema-based validation of resources/list response shape (best-effort)
                    var protocolVersion = SchemaValidationHelpers.ResolveProtocolVersion(serverConfig.ProtocolVersion);
                    if (SchemaValidationHelpers.TryValidateListResult(
                    _schemaRegistry,
                    _schemaValidator,
                    protocolVersion,
                    SchemaValidationHelpers.ListResourcesResultDefinition,
                    rawJson,
                    Logger,
                    out var schemaValidationResult) &&
                schemaValidationResult is not null &&
                !schemaValidationResult.IsValid)
            {
                result.Status = TestStatus.Failed;
                result.Issues.Add("❌ NON-COMPLIANT: resources/list response does not conform to MCP JSON Schema");
                foreach (var error in schemaValidationResult.Errors)
                {
                    result.Issues.Add($"   • {error}");
                }
            }
            
            if (result.Status != TestStatus.Failed)
            {
                result.Status = result.ResourceResults.All(r => r.Status == TestStatus.Passed) ? TestStatus.Passed : TestStatus.Failed;
            }

            // Resource Templates Validation (MCP spec: resources/templates/list)
            try
            {
                var templatesResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ResourcesTemplatesList, null, serverConfig.Authentication, ct);
                if (templatesResponse.IsSuccess && !string.IsNullOrEmpty(templatesResponse.RawJson))
                {
                    using var templatesDoc = JsonDocument.Parse(templatesResponse.RawJson);
                    if (templatesDoc.RootElement.TryGetProperty("result", out var templatesResult) &&
                        templatesResult.TryGetProperty("resourceTemplates", out var templates) &&
                        templates.ValueKind == JsonValueKind.Array)
                    {
                        var templateCount = templates.GetArrayLength();
                        result.Issues.Add($"✅ Resource templates: {templateCount} templates discovered");
                        
                        // Validate each template has uriTemplate and name
                        foreach (var template in templates.EnumerateArray())
                        {
                            if (!template.TryGetProperty("uriTemplate", out _))
                                result.Issues.Add("❌ MCP Compliance: Resource template missing 'uriTemplate' field (MUST per spec)");
                            if (!template.TryGetProperty("name", out _))
                                result.Issues.Add("⚠️ MCP Compliance: Resource template missing 'name' field");
                        }
                    }
                }
                else if (templatesResponse.StatusCode == 401 || templatesResponse.StatusCode == 403)
                {
                    result.Issues.Add("🔒 Resource templates: Auth required");
                }
                else if (!string.IsNullOrEmpty(templatesResponse.RawJson) && templatesResponse.RawJson.Contains("-32601"))
                {
                    result.Issues.Add("ℹ️ Resource templates: Not supported by server (MethodNotFound)");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Resource templates validation failed");
            }
            
            // Calculate Score using Strategy
            result.Score = _scoringStrategy.CalculateScore(result);

            result.Issues.Add($"✅ COMPLIANT: {result.ResourcesDiscovered} resources discovered and validated");
            
            return result;
        }, cancellationToken);
    }

    public async Task<ResourceTestResult> ValidateResourceAccessAsync(McpServerConfig serverConfig, ResourceTestingConfig config, CancellationToken cancellationToken = default)
    {
        // Reuse discovery validation since both test the same capability
        return await ValidateResourceDiscoveryAsync(serverConfig, config, cancellationToken);
    }
}
