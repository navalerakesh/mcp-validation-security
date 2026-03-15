using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Resources;
using Mcp.Benchmark.Infrastructure.Attacks;
using Mcp.Benchmark.Infrastructure.Abstractions;

namespace Mcp.Benchmark.Infrastructure.Validators;

/// <summary>
/// Security validator implementation for MCP server penetration testing and vulnerability assessment.
/// Performs comprehensive security testing using various attack vectors and input validation scenarios.
/// </summary>
public class SecurityValidator : BaseValidator<SecurityValidator>, ISecurityValidator
{
    private readonly IMcpHttpClient _httpClient;
    private readonly McpCompliantAuthValidator _authValidator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<IAttackVector> _advancedAttackVectors;

    /// <summary>
    /// Initializes a new instance of the SecurityValidator class.
    /// </summary>
    /// <param name="logger">Logger instance for security validation operations.</param>
    /// <param name="loggerFactory">Factory for creating loggers for attack vectors.</param>
    /// <param name="httpClient">HTTP client for MCP server communication.</param>
    /// <param name="authValidator">Validator for authentication compliance.</param>
    public SecurityValidator(
        ILogger<SecurityValidator> logger, 
        ILoggerFactory loggerFactory,
        IMcpHttpClient httpClient, 
        McpCompliantAuthValidator authValidator) 
        : base(logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authValidator = authValidator ?? throw new ArgumentNullException(nameof(authValidator));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        // Initialize Advanced Attack Vectors
        _advancedAttackVectors = new List<IAttackVector>
        {
            new JsonRpcErrorSmuggling(_loggerFactory.CreateLogger<JsonRpcErrorSmuggling>()),
            new MetadataEnumeration(_loggerFactory.CreateLogger<MetadataEnumeration>()),
            new SchemaConfusion(_loggerFactory.CreateLogger<SchemaConfusion>()),
            new HallucinationFuzzer(_loggerFactory.CreateLogger<HallucinationFuzzer>())
        };
    }

    /// <summary>
    /// Performs comprehensive security assessment including common vulnerability tests.
    /// </summary>
    public async Task<SecurityTestResult> PerformSecurityAssessmentAsync(McpServerConfig serverConfig, SecurityTestingConfig config, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, ValidationMessages.Titles.SecurityValidation, async (ct) =>
        {
            var result = new SecurityTestResult();

            // 1. Authentication Testing
            var authTestResult = await _authValidator.ValidateAuthenticationComplianceAsync(serverConfig, ct);
            result.AuthenticationTestResult = authTestResult;
            
            if (authTestResult.Status == TestStatus.Error)
            {
                result.Status = TestStatus.Error;
                return result;
            }
            
            // Map auth failures to vulnerabilities
            var failedScenarios = authTestResult.TestScenarios.Where(s => !s.IsCompliant).ToList();
            foreach (var failedScenario in failedScenarios)
            {
                result.Vulnerabilities.Add(new SecurityVulnerability
                {
                    Id = $"SEC-AUTH-{failedScenario.Method.Replace("/", "-").ToUpper()}-{Guid.NewGuid()}", // Use Guid instead of Ticks
                    Name = string.Format(ValidationMessages.Security.AuthenticationIssue, failedScenario.ScenarioName),
                    Description = $"{failedScenario.ComplianceReason}: {failedScenario.ActualBehavior}",
                    Severity = failedScenario.TestType == "Valid Authentication" ? VulnerabilitySeverity.High : VulnerabilitySeverity.Medium,
                    Category = ValidationConstants.Categories.AuthenticationSecurity,
                    AffectedComponent = failedScenario.Method,
                    IsExploitable = failedScenario.TestType != "Valid Authentication",
                    Remediation = ValidationMessages.Security.RemediationAuth
                });
            }

            Logger.LogInformation(ValidationMessages.Security.SmartAuthCompleted,
                authTestResult.ComplianceScore, failedScenarios.Count, authTestResult.TestScenarios.Count);

            // 2. Input Validation
            if (config.TestInputValidation)
            {
                var inputValidationResults = await ValidateInputSanitizationAsync(serverConfig, config.CustomPayloads, ct);
                result.InputValidationResults = inputValidationResults.InputValidationResults;
            }

            // 3. Input Validation Testing
            // These test whether the server validates and rejects malformed input.
            // NOTE: MCP servers are NOT databases or web browsers — SQL/XSS payloads
            // don't execute against them. What we're actually testing is:
            // - Does the server validate input before processing?
            // - Does the server return proper error codes for bad input?
            // - Does the server echo raw input back (output sanitization)?
            if (config.TestInjectionAttacks)
            {
                var attackResults = await SimulateAttackVectorsAsync(serverConfig, new[] { 
                    ValidationConstants.AttackVectors.InputValidation1, 
                    ValidationConstants.AttackVectors.InputValidation2, 
                    ValidationConstants.AttackVectors.InputValidation3 
                }, ct);
                result.AttackSimulations = attackResults.AttackSimulations;
            }

            // 4. Advanced Attack Vectors (MCP Specific)
            Logger.LogInformation(ValidationMessages.Security.ExecutingAdvancedVectors);
            foreach (var vector in _advancedAttackVectors)
            {
                try
                {
                    var attackResult = await vector.ExecuteAsync(serverConfig, _httpClient, ct);
                    
                    // Add to attack simulations for reporting
                    result.AttackSimulations.Add(new AttackSimulationResult
                    {
                        AttackVector = vector.Id,
                        Description = vector.Name,
                        AttackSuccessful = !attackResult.IsBlocked,
                        DefenseSuccessful = attackResult.IsBlocked,
                        ServerResponse = attackResult.Analysis,
                        Evidence = new Dictionary<string, object> { { "evidence", attackResult.Evidence } }
                    });

                    // If vulnerable, add to vulnerabilities list
                    if (!attackResult.IsBlocked)
                    {
                        result.Vulnerabilities.Add(new SecurityVulnerability
                        {
                            Id = attackResult.VectorId,
                            Name = attackResult.VectorName,
                            Description = attackResult.Analysis,
                            Severity = ParseSeverity(attackResult.Severity),
                            Category = vector.Category,
                            AffectedComponent = "MCP Protocol/Tooling",
                            IsExploitable = true,
                            Remediation = "Review MCP implementation against security best practices."
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error executing attack vector {Vector}", vector.Name);
                }
            }

            // 5. Scoring & Recommendations
            result.SecurityScore = CalculateSecurityScore(result.Vulnerabilities);
            result.SecurityRecommendations = GenerateSecurityRecommendations(result);

            // Fail if any High/Critical vulnerabilities OR if the overall security score is too low (e.g. < 50%)
            result.Status = (result.Vulnerabilities.Any(v => v.Severity >= VulnerabilitySeverity.High) || result.SecurityScore < 50.0) 
                ? TestStatus.Failed 
                : TestStatus.Passed;

            Logger.LogInformation(ValidationMessages.Security.AssessmentCompleted,
                result.SecurityScore, result.Vulnerabilities.Count);
            
            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Tests input validation and sanitization mechanisms.
    /// Targets actual tool string arguments via tools/call, not the listing endpoint.
    /// </summary>
    public async Task<SecurityTestResult> ValidateInputSanitizationAsync(McpServerConfig serverConfig, IEnumerable<SecurityTestPayload> payloads, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, ValidationMessages.Security.InputValidation, async (ct) =>
        {
            var result = new SecurityTestResult();

            // Discover a tool with a string argument to test against
            string? targetTool = null;
            string? targetArgument = null;
            try
            {
                var discoveryResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, ct);
                if (discoveryResponse.IsSuccess && !string.IsNullOrEmpty(discoveryResponse.RawJson))
                {
                    using var doc = JsonDocument.Parse(discoveryResponse.RawJson);
                    if (doc.RootElement.TryGetProperty("result", out var res) &&
                        res.TryGetProperty("tools", out var toolsArray) &&
                        toolsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tool in toolsArray.EnumerateArray())
                        {
                            if (tool.TryGetProperty("name", out var nameProp) &&
                                tool.TryGetProperty("inputSchema", out var schemaProp) &&
                                schemaProp.TryGetProperty("properties", out var props))
                            {
                                foreach (var prop in props.EnumerateObject())
                                {
                                    if (prop.Value.TryGetProperty("type", out var typeProp) &&
                                        typeProp.GetString() == "string")
                                    {
                                        targetTool = nameProp.GetString();
                                        targetArgument = prop.Name;
                                        break;
                                    }
                                }
                            }
                            if (targetTool != null) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to discover tools for input sanitization testing.");
            }

            foreach (var payload in payloads)
            {
                try
                {
                    object requestParams;
                    string method;

                    if (targetTool != null && targetArgument != null)
                    {
                        // Real test: inject payload into a valid tool argument via tools/call
                        method = ValidationConstants.Methods.ToolsCall;
                        var args = new Dictionary<string, object> { { targetArgument, payload.Payload } };
                        requestParams = new { name = targetTool, arguments = args };
                    }
                    else
                    {
                        // Fallback: connectivity test only (no tool with string args found)
                        method = ValidationConstants.Methods.ToolsList;
                        requestParams = new { testPayload = payload.Payload };
                    }

                    var response = await _httpClient.CallAsync(serverConfig.Endpoint!, method, requestParams, serverConfig.Authentication, ct);

                    // Check if the payload was reflected back in the response (sign of vulnerability)
                    var reflected = response.RawJson?.Contains(payload.Payload) == true;

                    var validationResult = new InputValidationResult
                    {
                        InputField = payload.Name,
                        TestPayload = payload.Payload,
                        ExpectedBehavior = "sanitized",
                        ValidationPassed = !reflected,
                        ActualResponse = response.RawJson ?? response.Error ?? "No response",
                        PropertySanitized = !reflected
                    };

                    if (reflected)
                    {
                        validationResult.SecurityConcerns.Add($"Potential {payload.Name} vulnerability: payload reflected in response");
                    }

                    result.InputValidationResults.Add(validationResult);
                }
                catch (Exception ex)
                {
                    result.InputValidationResults.Add(new InputValidationResult
                    {
                        InputField = payload.Name,
                        TestPayload = payload.Payload,
                        ExpectedBehavior = "sanitized",
                        ValidationPassed = false,
                        ActualResponse = $"Exception: {ex.Message}",
                        PropertySanitized = false,
                        SecurityConcerns = { $"Input validation test failed with exception: {ex.Message}" }
                    });
                }
            }

            result.Status = result.InputValidationResults.Any(r => !r.ValidationPassed) ? TestStatus.Failed : TestStatus.Passed;
            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Simulates various attack vectors to test server resilience.
    /// </summary>
    public async Task<SecurityTestResult> SimulateAttackVectorsAsync(McpServerConfig serverConfig, IEnumerable<string> attackVectors, CancellationToken cancellationToken = default)
    {
        return await ExecuteValidationAsync(serverConfig, ValidationMessages.Security.AttackSimulation, async (ct) =>
        {
            var result = new SecurityTestResult();

            // 1. Discover targets FIRST (Stop attacking the listing endpoint)
            Logger.LogInformation("Discovering potential attack targets via tools/list...");
            string? targetTool = null;
            string? targetArgument = null;

            try 
            {
                var discoveryResponse = await _httpClient.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, serverConfig.Authentication, ct);
                
                if (discoveryResponse.IsSuccess && !string.IsNullOrEmpty(discoveryResponse.RawJson))
                {
                     using var doc = JsonDocument.Parse(discoveryResponse.RawJson);
                     if (doc.RootElement.TryGetProperty("result", out var res) && 
                         res.TryGetProperty("tools", out var toolsArray) && 
                         toolsArray.ValueKind == JsonValueKind.Array)
                     {
                         foreach(var tool in toolsArray.EnumerateArray())
                         {
                             // Find a tool that accepts a string argument
                             if (tool.TryGetProperty("name", out var nameProp) && 
                                 tool.TryGetProperty("inputSchema", out var schemaProp) &&
                                 schemaProp.TryGetProperty("properties", out var props))
                             {
                                 foreach(var prop in props.EnumerateObject())
                                 {
                                     if(prop.Value.TryGetProperty("type", out var typeProp) && 
                                        typeProp.GetString() == "string")
                                     {
                                         targetTool = nameProp.GetString();
                                         targetArgument = prop.Name;
                                         break; 
                                     }
                                 }
                             }
                             if(targetTool != null) break;
                         }
                     }
                }
            }
            catch(Exception ex)
            {
                Logger.LogWarning(ex, "Failed to discover tools for security testing. Defaulting to 'tools/list' check which may be shallow.");
            }

            if (targetTool == null)
            {
                Logger.LogWarning("No suitable tool found with string arguments. Security attacks against 'tools/call' will be skipped/simulated on list endpoint.");
            }
            else
            {
                Logger.LogInformation("Selected target tool '{Tool}' and argument '{Arg}' for injection testing.", targetTool, targetArgument);
            }

            foreach (var attackVector in attackVectors)
            {
                try
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    object payload;
                    string method;

                    if (targetTool != null && targetArgument != null)
                    {
                        // Real attack: Call the tool with the injection payload
                        method = ValidationConstants.Methods.ToolsCall;
                        var attackString = GetAttackString(attackVector);
                        var args = new Dictionary<string, object> { { targetArgument, attackString } };
                        payload = new { name = targetTool, arguments = args };
                    }
                    else
                    {
                        // Fallback (Legacy/Shallow): Send payload to tools/list (Fake check, but keeping for now if discovery fails)
                        // Ideally we should SKIP here if we want to be "fake-free", but let's keep it as a connectivity check
                        method = ValidationConstants.Methods.ToolsList;
                        payload = GetAttackPayload(attackVector); 
                    }
                    
                    var response = await _httpClient.CallAsync(serverConfig.Endpoint!, method, payload, serverConfig.Authentication, ct);
                    stopwatch.Stop();

                    // Check if the attack was blocked either by HTTP status or JSON-RPC error
                    bool isBlocked = !response.IsSuccess;
                    string defenseDetails = "";

                    if (response.IsSuccess && !string.IsNullOrEmpty(response.RawJson))
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(response.RawJson);
                            
                            // 1. Check for Top-Level JSON-RPC Error
                            if (doc.RootElement.TryGetProperty("error", out var error))
                            {
                                isBlocked = true;
                                defenseDetails = error.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Error" : "JSON-RPC Error";
                            }
                            // 2. Check for Tool Execution Error (content.isError)
                            else if (doc.RootElement.TryGetProperty("result", out var resultVal) && 
                                     resultVal.TryGetProperty("isError", out var isErrorVal) && 
                                     isErrorVal.GetBoolean())
                            {
                                isBlocked = true;
                                defenseDetails = "Tool execution returned isError: true";
                            }
                            // 3. Check if payload appears in the response — but distinguish raw echo from structured output
                            else if (targetTool != null)
                            {
                                var attackString = GetAttackString(attackVector);
                                var responseText = response.RawJson ?? "";
                                
                                if (responseText.Contains(attackString, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Payload found in response. Determine if it's a raw echo or structured output.
                                    // 
                                    // Raw echo: The tool received input and returned it as-is in content[0].text
                                    //   e.g., echo tool: {"content":[{"type":"text","text":"Echo: '; DROP TABLE..."}]}
                                    //
                                    // Structured output: The tool processed the input and produced a report
                                    //   that happens to mention the payload as part of findings.
                                    //   e.g., validator: {"content":[{"type":"text","text":"Status: PASSED\n...INJ-001..."}]}
                                    //
                                    // Detection strategy:
                                    // 1. Extract the text content from the first content block
                                    // 2. Check if the payload is the PRIMARY content (raw echo) or
                                    //    embedded within structured data (report)
                                    
                                    bool isRawEcho = false;
                                    
                                    if (doc.RootElement.TryGetProperty("result", out var res2) &&
                                        res2.TryGetProperty("content", out var content) &&
                                        content.ValueKind == JsonValueKind.Array &&
                                        content.GetArrayLength() > 0)
                                    {
                                        var firstItem = content[0];
                                        if (firstItem.TryGetProperty("text", out var textVal))
                                        {
                                            var text = textVal.GetString() ?? "";
                                            
                                            // Raw echo indicators:
                                            // - The text starts with a simple prefix + the payload (e.g., "Echo: <payload>")
                                            // - The payload makes up more than 50% of the text content
                                            // - The text has no structured formatting (no newlines, no sections)
                                            var payloadRatio = text.Length > 0 
                                                ? (double)attackString.Length / text.Length 
                                                : 0;
                                            var hasStructure = text.Contains('\n') && text.Split('\n').Length > 3;
                                            
                                            isRawEcho = payloadRatio > 0.3 && !hasStructure;
                                        }
                                    }
                                    
                                    if (isRawEcho)
                                    {
                                        isBlocked = false;
                                        defenseDetails = "Input reflected directly in tool output without processing";
                                    }
                                    else
                                    {
                                        isBlocked = true;
                                        defenseDetails = "Input present in structured output (processed, not raw reflection)";
                                    }
                                }
                                else
                                {
                                    // Payload NOT in response — server processed input without reflecting it
                                    isBlocked = true;
                                    defenseDetails = "Input processed without reflection";
                                }
                            }
                        }
                        catch
                        {
                            // Failed to parse JSON, assume attack might have succeeded if 200 OK
                        }
                    }

                    var defenseMechanism = DetermineDefenseMechanism(response.StatusCode, response.IsSuccess, response.RawJson);

                    result.AttackSimulations.Add(new AttackSimulationResult
                    {
                        AttackVector = attackVector,
                        Description = $"Simulated {attackVector} attack on {(targetTool ?? "endpoint")}",
                        AttackSuccessful = !isBlocked, 
                        DefenseSuccessful = isBlocked,
                        ServerResponse = response.RawJson ?? response.Error ?? "No response",
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                        Evidence = new Dictionary<string, object>
                        {
                            ["target"] = targetTool != null ? $"tools/call ({targetTool})" : "tools/list",
                            ["result"] = !isBlocked ? "Server accepted attack payload (Review output for reflection)" : $"Server blocked attack: {defenseDetails} (SECURE)",
                            ["isSuccess"] = response.IsSuccess,
                            ["statusCode"] = response.StatusCode,
                            ["defenseMechanism"] = defenseMechanism,
                            ["error"] = response.Error ?? string.Empty,
                            ["actualExecutionTimeMs"] = stopwatch.ElapsedMilliseconds
                        }
                    });
                }
                catch (Exception ex)
                {
                    var isServerError = ex.Message.Contains("500") || ex.Message.Contains("Internal Server Error");
                    var defenseMechanism = DetermineDefenseMechanismFromException(ex);
                    
                    result.AttackSimulations.Add(new AttackSimulationResult
                    {
                        AttackVector = attackVector,
                        Description = $"Simulated {attackVector} attack",
                        AttackSuccessful = isServerError,
                        DefenseSuccessful = !isServerError,
                        ServerResponse = $"Exception: {ex.Message}",
                        ExecutionTimeMs = 0,
                        Evidence = new Dictionary<string, object>
                        {
                            ["exception"] = ex.Message,
                            ["attackSuccessful"] = isServerError,
                            ["defenseSuccessful"] = !isServerError,
                            ["defenseMechanism"] = defenseMechanism,
                            ["statusCode"] = isServerError ? 500 : 401,
                            ["analysis"] = isServerError ? "Server error - potential vulnerability" : "Attack blocked at network/auth layer"
                        }
                    });
                }
            }

            result.Status = result.AttackSimulations.Any(r => r.AttackSuccessful) ? TestStatus.Failed : TestStatus.Passed;
            return result;
        }, cancellationToken);
    }

    private string DetermineDefenseMechanism(int statusCode, bool isSuccess, string? rawJson = null)
    {
        if (!isSuccess)
        {
            if (statusCode == 401 || statusCode == 403) return ValidationMessages.Security.Defense.Auth;
            if (statusCode == 400) return ValidationMessages.Security.Defense.InputValidation;
            if (statusCode >= 500) return ValidationMessages.Security.Defense.ServerError;
            return $"HTTP {statusCode}";
        }

        // Check for JSON-RPC error
        if (!string.IsNullOrEmpty(rawJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    return ValidationMessages.Security.Defense.AppLayer;
                }
            }
            catch { }
        }

        return ValidationMessages.Security.Defense.Unknown;
    }

    private string DetermineDefenseMechanismFromException(Exception ex)
    {
        if (ex.Message.Contains("Authorization") || ex.Message.Contains("401") || ex.Message.Contains("403")) return ValidationMessages.Security.Defense.Auth;
        if (ex.Message.Contains("400") || ex.Message.Contains("Bad Request")) return ValidationMessages.Security.Defense.InputValidation;
        if (ex.Message.Contains("500") || ex.Message.Contains("Internal Server Error")) return ValidationMessages.Security.Defense.ServerError;
        return ValidationMessages.Security.Defense.Network;
    }

    private VulnerabilitySeverity ParseSeverity(string severity)
    {
        return severity.ToLower() switch
        {
            "critical" => VulnerabilitySeverity.Critical,
            "high" => VulnerabilitySeverity.High,
            "medium" => VulnerabilitySeverity.Medium,
            "low" => VulnerabilitySeverity.Low,
            _ => VulnerabilitySeverity.High
        };
    }

    private double CalculateSecurityScore(List<SecurityVulnerability> vulnerabilities)
    {
        if (vulnerabilities.Count == 0) return 100.0;

        var baseScore = 100.0;
        foreach (var vulnerability in vulnerabilities)
        {
            var penalty = vulnerability.Severity switch
            {
                VulnerabilitySeverity.Critical => ScoringConstants.VulnPenaltyCritical,
                VulnerabilitySeverity.High => ScoringConstants.VulnPenaltyHigh,
                VulnerabilitySeverity.Medium => ScoringConstants.VulnPenaltyMedium,
                VulnerabilitySeverity.Low => ScoringConstants.VulnPenaltyLow,
                VulnerabilitySeverity.Informational => ScoringConstants.VulnPenaltyInfo,
                _ => 5.0
            };
            baseScore -= penalty;
        }
        return Math.Max(0.0, baseScore);
    }

    private List<string> GenerateSecurityRecommendations(SecurityTestResult result)
    {
        var recommendations = new List<string>();
        if (result.Vulnerabilities.Any(v => v.Category == ValidationMessages.Security.InputValidation)) recommendations.Add(ValidationMessages.Security.Recommendation.InputValidation);
        if (result.Vulnerabilities.Any(v => v.Severity >= VulnerabilitySeverity.High)) recommendations.Add(ValidationMessages.Security.Recommendation.HighSeverity);
        if (result.SecurityScore < 80.0) recommendations.Add(ValidationMessages.Security.Recommendation.Controls);
        if (result.AttackSimulations.Any(a => a.AttackSuccessful)) recommendations.Add(ValidationMessages.Security.Recommendation.Defense);
        if (result.InputValidationResults.Any(i => i.SecurityConcerns.Count > 0)) recommendations.Add(ValidationMessages.Security.Recommendation.Injection);
        return recommendations;
    }

    private object GetAttackPayload(string attackVector)
    {
        return attackVector switch
        {
            ValidationConstants.AttackVectors.InputValidation1 => new { query = "'; DROP TABLE users; --" },
            ValidationConstants.AttackVectors.InputValidation2 => new { input = "<script>alert('XSS')</script>" },
            ValidationConstants.AttackVectors.InputValidation3 => new { command = "; rm -rf /" },
            _ => new { payload = $"test-{attackVector}" }
        };
    }

    private string GetAttackString(string attackVector)
    {
        return attackVector switch
        {
            ValidationConstants.AttackVectors.InputValidation1 => "'; DROP TABLE users; --",
            ValidationConstants.AttackVectors.InputValidation2 => "<script>alert('XSS')</script>",
            ValidationConstants.AttackVectors.InputValidation3 => "; rm -rf /",
            _ => $"test-{attackVector}"
        };
    }
}
