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

            if (CapabilitySnapshotUtils.IsCapabilityExplicitlyNotAdvertised(capabilitySnapshot, McpSpecConstants.Capabilities.Resources))
            {
                result.Status = TestStatus.Skipped;
                result.Score = 100.0;
                result.Message = "Server does not advertise the resources capability.";
                result.Issues.Add("Resources capability was not advertised during initialize; resources/list, resources/read, templates, and subscription probes were skipped.");
                return result;
            }

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

                    ApplyResourceGuidelineFindings(result, resourceResult, serverConfig, resource);
                    resourceResult.Status = resourceResult.MetadataValid ? TestStatus.Passed : TestStatus.Failed;

                    // Static, metadata-only content safety analysis
                    if (!string.IsNullOrWhiteSpace(resourceResult.ResourceUri) || !string.IsNullOrWhiteSpace(resourceResult.ResourceName))
                    {
                        var safetyContext = ContentSafetyAnalysisContext.FromServerConfig(serverConfig);
                        var safetyFindings = _contentSafetyAnalyzer.AnalyzeResource(resourceResult.ResourceName, resourceResult.ResourceUri, safetyContext);
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

                                    ValidateResourceReadContentItem(result, resourceResult, firstContent);
                                    
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

            await ValidateResourceSubscriptionsIfAdvertisedAsync(serverConfig, config, result, capabilitySnapshot, ct);

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

                            foreach (var finding in McpContentValidationUtils.ValidateAnnotations(
                                template,
                                component,
                                ValidationFindingRuleIds.ResourceAnnotationInvalid,
                                "ProtocolCompliance"))
                            {
                                result.Findings.Add(finding);
                            }

                            if (hasUriTemplate && ResourceTemplateNeedsBoundaryGuidance(uriTemplateElement.GetString(), hasDescription ? descriptionElement.GetString() : null))
                            {
                                result.Findings.Add(new ValidationFinding
                                {
                                    RuleId = ValidationFindingRuleIds.ResourceTemplateBoundaryGuidanceMissing,
                                    Category = "AiSafety",
                                    Component = component,
                                    Severity = ValidationFindingSeverity.Medium,
                                    Summary = $"Resource template '{component}' accepts broad URI parameters without access-boundary guidance.",
                                    Recommendation = "Document allowed path/URI boundaries, authorization requirements, and validation rules for parameterized resource templates that can read files, URLs, or user-selected targets.",
                                    Metadata =
                                    {
                                        ["uriTemplate"] = uriTemplateElement.GetString() ?? string.Empty,
                                        ["safetyLane"] = "resource-access-boundary"
                                    }
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

    private async Task ValidateResourceSubscriptionsIfAdvertisedAsync(
        McpServerConfig serverConfig,
        ResourceTestingConfig config,
        ResourceTestResult result,
        TransportResult<CapabilitySummary>? capabilitySnapshot,
        CancellationToken ct)
    {
        if (!config.TestSubscriptions)
        {
            return;
        }

        if (CapabilitySnapshotUtils.HasCapabilityDeclarations(capabilitySnapshot) &&
            !CapabilitySnapshotUtils.IsCapabilityAdvertised(capabilitySnapshot, McpSpecConstants.Capabilities.ResourcesSubscribe))
        {
            result.Issues.Add("resources.subscribe was not advertised during initialize; resource subscription probes were skipped.");
            return;
        }

        if (!CapabilitySnapshotUtils.IsCapabilityAdvertised(capabilitySnapshot, McpSpecConstants.Capabilities.ResourcesSubscribe))
        {
            return;
        }

        var resourceUri = result.ResourceResults
            .FirstOrDefault(resource => resource.MetadataValid && !string.IsNullOrWhiteSpace(resource.ResourceUri))
            ?.ResourceUri;

        if (string.IsNullOrWhiteSpace(resourceUri))
        {
            result.Issues.Add("resources.subscribe was advertised, but no valid resource URI was available for a subscription probe.");
            return;
        }

        var subscriptionResult = new SubscriptionTestResult { ResourceUri = resourceUri };
        result.SubscriptionResults.Add(subscriptionResult);

        try
        {
            var subscribeResponse = await _httpClient.CallAsync(
                serverConfig.Endpoint!,
                ValidationConstants.Methods.ResourcesSubscribe,
                new { uri = resourceUri },
                serverConfig.Authentication,
                ct);

            if (AuthenticationChallengeInterpreter.Inspect(subscribeResponse).RequiresAuthentication)
            {
                subscriptionResult.Issues.Add("Auth required for resources/subscribe; subscription behavior was not validated.");
                result.Issues.Add("resources.subscribe advertised but requires authentication before subscription behavior can be validated.");
                return;
            }

            if (!subscribeResponse.IsSuccess)
            {
                AddSubscriptionFailureFinding(result, subscriptionResult, ValidationConstants.Methods.ResourcesSubscribe, resourceUri, subscribeResponse.Error);
                return;
            }

            subscriptionResult.SubscriptionSuccessful = true;
            subscriptionResult.Issues.Add("resources/subscribe succeeded for an advertised subscription-capable resource surface.");

            var unsubscribeResponse = await _httpClient.CallAsync(
                serverConfig.Endpoint!,
                ValidationConstants.Methods.ResourcesUnsubscribe,
                new { uri = resourceUri },
                serverConfig.Authentication,
                ct);

            if (unsubscribeResponse.IsSuccess)
            {
                subscriptionResult.UnsubscriptionSuccessful = true;
                subscriptionResult.Issues.Add("resources/unsubscribe succeeded after subscription probe.");
            }
            else if (!AuthenticationChallengeInterpreter.Inspect(unsubscribeResponse).RequiresAuthentication)
            {
                AddSubscriptionFailureFinding(result, subscriptionResult, ValidationConstants.Methods.ResourcesUnsubscribe, resourceUri, unsubscribeResponse.Error);
            }
        }
        catch (Exception ex)
        {
            AddSubscriptionFailureFinding(result, subscriptionResult, ValidationConstants.Methods.ResourcesSubscribe, resourceUri, ex.Message);
        }
    }

    private static void AddSubscriptionFailureFinding(
        ResourceTestResult result,
        SubscriptionTestResult subscriptionResult,
        string method,
        string resourceUri,
        string? error)
    {
        result.Status = TestStatus.Failed;
        result.ResourcesTestFailed++;
        var issue = $"{method} was advertised but failed for resource '{resourceUri}': {error ?? "no JSON-RPC error detail"}";
        subscriptionResult.Issues.Add(issue);
        result.Issues.Add(issue);
        result.Findings.Add(new ValidationFinding
        {
            RuleId = ValidationFindingRuleIds.ResourceSubscribeAdvertisedButUnsupported,
            Category = "ProtocolCompliance",
            Component = method,
            Severity = ValidationFindingSeverity.High,
            Summary = $"Server advertises resources.subscribe but {method} is not callable.",
            Recommendation = "Implement the advertised resource subscription method or stop advertising resources.subscribe.",
            Metadata =
            {
                ["capability"] = McpSpecConstants.Capabilities.ResourcesSubscribe,
                ["method"] = method,
                ["resourceUri"] = resourceUri
            }
        });
    }

    private static void ApplyResourceGuidelineFindings(
        ResourceTestResult aggregateResult,
        IndividualResourceResult resourceResult,
        McpServerConfig serverConfig,
        JsonElement resource)
    {
        var component = string.IsNullOrWhiteSpace(resourceResult.ResourceUri) ? resourceResult.ResourceName : resourceResult.ResourceUri;

        if (resourceResult.MetadataValid && string.IsNullOrWhiteSpace(resourceResult.MimeType))
        {
            var finding = new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ResourceGuidelineMimeTypeMissing,
                Category = "McpGuideline",
                Component = component,
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

        foreach (var finding in McpContentValidationUtils.ValidateAnnotations(
            resource,
            component,
            ValidationFindingRuleIds.ResourceAnnotationInvalid,
            "ProtocolCompliance"))
        {
            AddResourceFinding(aggregateResult, resourceResult, finding, finding.Summary);
        }

        if (ResourceNeedsAccessControlAdvisory(resourceResult, serverConfig))
        {
            var context = ContentSafetyAnalysisContext.FromServerConfig(serverConfig);
            var finding = new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ResourceAccessControlAdvisory,
                Category = "AiSafety",
                Component = component,
                Severity = ValidationFindingSeverity.High,
                Summary = $"Resource '{component}' appears sensitive and was exposed without an observable authentication boundary.",
                Recommendation = "Require authorization before listing or reading sensitive resources, enforce per-resource permissions, and avoid exposing file, credential, customer, or database resources on unauthenticated public surfaces.",
                Metadata =
                {
                    ["resourceUri"] = resourceResult.ResourceUri,
                    ["resourceName"] = resourceResult.ResourceName,
                    ["contextProfile"] = context.Profile.ToString(),
                    ["authenticationRequired"] = context.AuthenticationRequired.ToString().ToLowerInvariant(),
                    ["safetyLane"] = "resource-access-control"
                }
            };

            AddResourceFinding(aggregateResult, resourceResult, finding, finding.Summary);
        }
    }

    private static void ValidateResourceReadContentItem(
        ResourceTestResult aggregateResult,
        IndividualResourceResult resourceResult,
        JsonElement content)
    {
        var component = string.IsNullOrWhiteSpace(resourceResult.ResourceUri) ? resourceResult.ResourceName : resourceResult.ResourceUri;

        if (content.TryGetProperty("uri", out var contentUri) &&
            contentUri.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(contentUri.GetString()) &&
            !string.Equals(contentUri.GetString(), resourceResult.ResourceUri, StringComparison.Ordinal))
        {
            var finding = new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ResourceReadContentUriMismatch,
                Category = "AiSafety",
                Component = component,
                Severity = ValidationFindingSeverity.Medium,
                Summary = "resources/read returned content for a URI that does not match the requested resource URI.",
                Recommendation = "Return content only for the requested URI unless the response explicitly documents authorized related resources; mismatches can confuse clients and leak unintended resource context.",
                Metadata =
                {
                    ["requestedUri"] = resourceResult.ResourceUri,
                    ["returnedUri"] = contentUri.GetString() ?? string.Empty,
                    ["safetyLane"] = "resource-access-control"
                }
            };

            AddResourceFinding(aggregateResult, resourceResult, finding, finding.Summary);
        }

        if (content.TryGetProperty("blob", out var blob) &&
            (blob.ValueKind != JsonValueKind.String || !McpContentValidationUtils.IsValidBase64(blob.GetString())))
        {
            var finding = new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ResourceReadBlobInvalidBase64,
                Category = "ProtocolCompliance",
                Component = component,
                Severity = ValidationFindingSeverity.High,
                Summary = "resources/read returned blob content that is not valid base64.",
                Recommendation = "Encode binary resource contents as base64 strings before returning them in the blob field."
            };

            AddResourceFinding(aggregateResult, resourceResult, finding, finding.Summary);
        }

        foreach (var finding in McpContentValidationUtils.ValidateAnnotations(
            content,
            component,
            ValidationFindingRuleIds.ResourceAnnotationInvalid,
            "ProtocolCompliance"))
        {
            AddResourceFinding(aggregateResult, resourceResult, finding, finding.Summary);
        }
    }

    private static void AddResourceFinding(
        ResourceTestResult aggregateResult,
        IndividualResourceResult resourceResult,
        ValidationFinding finding,
        string issue)
    {
        if (IsBlockingProtocolFinding(finding))
        {
            resourceResult.MetadataValid = false;
            resourceResult.Status = TestStatus.Failed;
        }

        resourceResult.AddFinding(finding, issue);
        aggregateResult.Findings.Add(finding);
    }

    private static bool IsBlockingProtocolFinding(ValidationFinding finding)
    {
        return string.Equals(finding.Category, "ProtocolCompliance", StringComparison.OrdinalIgnoreCase) &&
            finding.Severity >= ValidationFindingSeverity.High;
    }

    private static bool HasAbsoluteUriScheme(string resourceUri)
    {
        return Uri.TryCreate(resourceUri, UriKind.Absolute, out var parsed) && !string.IsNullOrWhiteSpace(parsed.Scheme);
    }

    private static bool ResourceNeedsAccessControlAdvisory(IndividualResourceResult resourceResult, McpServerConfig serverConfig)
    {
        var context = ContentSafetyAnalysisContext.FromServerConfig(serverConfig);
        if (context.AuthenticationRequired || context.Profile == ContentSafetyContextProfile.LocalDeveloper)
        {
            return false;
        }

        return ResourceLooksSensitive(resourceResult.ResourceUri) || ResourceLooksSensitive(resourceResult.ResourceName);
    }

    private static bool ResourceLooksSensitive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "file" or "ssh" or "sftp" or "postgres" or "mysql" or "mongodb" or "redis" or "s3" or "vault")
        {
            return true;
        }

        return new[] { "secret", "token", "credential", "password", "private", "customer", "database", "admin", "ssh", "key" }
            .Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ResourceTemplateNeedsBoundaryGuidance(string? uriTemplate, string? description)
    {
        if (string.IsNullOrWhiteSpace(uriTemplate))
        {
            return false;
        }

        var broadParameters = new[] { "{path", "{file", "{url", "{uri", "{host", "{domain", "{target" };
        if (!broadParameters.Any(parameter => uriTemplate.Contains(parameter, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return true;
        }

        return !new[] { "allow", "allowed", "authorize", "permission", "scope", "boundary", "validate", "sanitize", "safe" }
            .Any(cue => description.Contains(cue, StringComparison.OrdinalIgnoreCase));
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
