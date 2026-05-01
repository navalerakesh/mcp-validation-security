using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Services;

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
            var templateSpecFailure = false;

            // Test resources/list
            var response = cachedResponse;
            if (response == null)
            {
                response = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ResourcesList, null, ct);
            }
            
            // MCP spec allows both public and auth-protected servers
            var authChallenge = AuthenticationChallengeInterpreter.Inspect(response);
            if (authChallenge.RequiresAuthentication)
            {
                result.Status = TestStatus.AuthRequired;
                result.ResourcesDiscovered = 0;
                result.Score = 0;
                result.Issues.Add(authChallenge.HasWwwAuthenticateHeader 
                    ? "Auth required: server returned an authentication challenge; resource contents were not validated."
                    : "Auth required: server rejected resource discovery but did not include WWW-Authenticate metadata.");
                return result;
            }

            if (JsonRpcResponseInspector.IsMethodNotFound(response))
            {
                result.Status = TestStatus.Passed;
                result.ResourcesDiscovered = 0;
                result.Score = 100;
                result.Issues.Add("✅ COMPLIANT: No resources were advertised; no resource reads were required");
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
                        resourceResult.AddFinding(new ValidationFinding
                        {
                            RuleId = ValidationFindingRuleIds.ResourceMissingName,
                            Category = "ProtocolCompliance",
                            Component = resourceResult.ResourceUri,
                            Severity = ValidationFindingSeverity.High,
                            Summary = "Resource is missing 'name' property",
                            Recommendation = "Include a human-readable name for every resource returned by resources/list."
                        }, "Missing 'name' property");
                    }

                    if (resource.TryGetProperty("mimeType", out var mimeTypeElement) && mimeTypeElement.ValueKind == JsonValueKind.String)
                    {
                        resourceResult.MimeType = mimeTypeElement.GetString();
                    }

                    ApplyResourceGuidelineFindings(result, resourceResult);
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
                                result.ResourcesAccessible++;
                                
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
                    var protocolVersion = SchemaValidationHelpers.ResolveProtocolVersion(_schemaRegistry, serverConfig.ProtocolVersion);
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
                var schemaErrors = schemaValidationResult.Errors ?? new List<string>();
                var hasProcessingError = SchemaValidationHelpers.HasSchemaProcessingError(schemaValidationResult);
                if (!hasProcessingError)
                {
                    result.Status = TestStatus.Failed;
                }

                result.Issues.Add(SchemaValidationHelpers.FormatListSchemaIssueHeader(ValidationConstants.Methods.ResourcesList, hasProcessingError));
                foreach (var error in schemaErrors)
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
                            var component = GetTemplateComponent(template);
                            var hasUriTemplate = template.TryGetProperty("uriTemplate", out var uriTemplateElement) &&
                                uriTemplateElement.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(uriTemplateElement.GetString());
                            var hasName = template.TryGetProperty("name", out var nameElement) &&
                                nameElement.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(nameElement.GetString());
                            var hasDescription = template.TryGetProperty("description", out var descriptionElement) &&
                                descriptionElement.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(descriptionElement.GetString());

                            if (!hasUriTemplate)
                            {
                                result.Issues.Add("❌ MCP Compliance: Resource template missing 'uriTemplate' field (MUST per spec)");
                                result.Findings.Add(new ValidationFinding
                                {
                                    RuleId = ValidationFindingRuleIds.ResourceTemplateMissingUriTemplate,
                                    Category = "ProtocolCompliance",
                                    Component = component,
                                    Severity = ValidationFindingSeverity.High,
                                    Summary = $"Resource template '{component}' is missing uriTemplate.",
                                    Recommendation = "Return a valid uriTemplate for every item in resources/templates/list."
                                });
                                templateSpecFailure = true;
                            }

                            if (!hasName)
                            {
                                result.Issues.Add("⚠️ MCP Compliance: Resource template missing 'name' field");
                                result.Findings.Add(new ValidationFinding
                                {
                                    RuleId = ValidationFindingRuleIds.ResourceTemplateNameMissing,
                                    Category = "McpGuideline",
                                    Component = component,
                                    Severity = ValidationFindingSeverity.Low,
                                    Summary = $"Resource template '{component}' does not include a name.",
                                    Recommendation = "Add a stable name so clients can present resource templates clearly."
                                });
                            }

                            if (hasUriTemplate && UriTemplateLooksParameterized(uriTemplateElement.GetString()) && !hasDescription)
                            {
                                result.Findings.Add(new ValidationFinding
                                {
                                    RuleId = ValidationFindingRuleIds.ResourceTemplateDescriptionMissing,
                                    Category = "McpGuideline",
                                    Component = component,
                                    Severity = ValidationFindingSeverity.Low,
                                    Summary = $"Resource template '{component}' accepts URI parameters but does not describe how callers should populate them.",
                                    Recommendation = "Add a description for parameterized resource templates so clients know how to form valid URIs."
                                });
                            }
                        }
                    }
                }
                else if (AuthenticationChallengeInterpreter.Inspect(templatesResponse).RequiresAuthentication)
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

            if (templateSpecFailure)
            {
                result.Status = TestStatus.Failed;
            }
            
            // Calculate Score using Strategy
            result.Score = _scoringStrategy.CalculateScore(result);

            if (result.Status == TestStatus.Passed)
            {
                result.Issues.Add($"✅ COMPLIANT: {result.ResourcesDiscovered} resources discovered and validated");
            }
            else
            {
                result.Issues.Add($"⚠️ Resource validation found {result.Findings.Count} structured finding(s) across resources and templates");
            }
            
            return result;
        }, cancellationToken);
    }

    public async Task<ResourceTestResult> ValidateResourceAccessAsync(McpServerConfig serverConfig, ResourceTestingConfig config, CancellationToken cancellationToken = default)
    {
        // Reuse discovery validation since both test the same capability
        return await ValidateResourceDiscoveryAsync(serverConfig, config, cancellationToken);
    }

    private static void ApplyResourceGuidelineFindings(ResourceTestResult aggregateResult, IndividualResourceResult resourceResult)
    {
        if (resourceResult.MetadataValid && string.IsNullOrWhiteSpace(resourceResult.MimeType))
        {
            var finding = new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ResourceGuidelineMimeTypeMissing,
                Category = "McpGuideline",
                Component = string.IsNullOrWhiteSpace(resourceResult.ResourceUri) ? resourceResult.ResourceName : resourceResult.ResourceUri,
                Severity = ValidationFindingSeverity.Low,
                Summary = $"Resource '{resourceResult.ResourceName}' does not declare mimeType.",
                Recommendation = "Add mimeType metadata so clients can render or route resources correctly."
            };

            resourceResult.Findings.Add(finding);
            aggregateResult.Findings.Add(finding);
        }

        if (!string.IsNullOrWhiteSpace(resourceResult.ResourceUri) && !HasAbsoluteUriScheme(resourceResult.ResourceUri))
        {
            var finding = new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ResourceUriSchemeUnclear,
                Category = "AiReadiness",
                Component = resourceResult.ResourceUri,
                Severity = ValidationFindingSeverity.Low,
                Summary = $"Resource URI '{resourceResult.ResourceUri}' does not expose a clear absolute URI scheme.",
                Recommendation = "Use absolute URIs with stable schemes so clients can reason about resource provenance and handlers."
            };

            resourceResult.Findings.Add(finding);
            aggregateResult.Findings.Add(finding);
        }
    }

    private static bool HasAbsoluteUriScheme(string resourceUri)
    {
        return Uri.TryCreate(resourceUri, UriKind.Absolute, out var parsed) && !string.IsNullOrWhiteSpace(parsed.Scheme);
    }

    private static string GetTemplateComponent(JsonElement template)
    {
        if (template.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
        {
            return name.GetString()!;
        }

        if (template.TryGetProperty("uriTemplate", out var uriTemplate) && uriTemplate.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(uriTemplate.GetString()))
        {
            return uriTemplate.GetString()!;
        }

        return "resource-template";
    }

    private static bool UriTemplateLooksParameterized(string? uriTemplate)
    {
        return !string.IsNullOrWhiteSpace(uriTemplate) && uriTemplate.Contains('{') && uriTemplate.Contains('}');
    }
}
