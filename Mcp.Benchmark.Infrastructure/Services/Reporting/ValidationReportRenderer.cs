using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

/// <summary>
/// Renders detailed HTML and XML reports from validation results.
/// This is reusable across hosts (CLI, services, etc.).
/// </summary>
public class ValidationReportRenderer : IValidationReportRenderer
{
    private readonly ValidationHtmlReportDocumentFactory _htmlDocumentFactory;
    private readonly ValidationHtmlReportComposer _htmlReportComposer;

    public ValidationReportRenderer()
        : this(new ValidationHtmlReportDocumentFactory(), new ValidationHtmlReportComposer())
    {
    }

    internal ValidationReportRenderer(
        ValidationHtmlReportDocumentFactory htmlDocumentFactory,
        ValidationHtmlReportComposer htmlReportComposer)
    {
        _htmlDocumentFactory = htmlDocumentFactory ?? throw new ArgumentNullException(nameof(htmlDocumentFactory));
        _htmlReportComposer = htmlReportComposer ?? throw new ArgumentNullException(nameof(htmlReportComposer));
    }

    public string GenerateHtmlReport(ValidationResult validationResult, ReportingConfig reportConfig, bool verbose)
    {
        reportConfig ??= new ReportingConfig();
        var document = _htmlDocumentFactory.Create(validationResult, reportConfig, verbose);
        return _htmlReportComposer.Compose(document);
    }

    public string GenerateXmlReport(ValidationResult validationResult, bool verbose)
    {
        var reportElement = new XElement("ValidationReport",
            new XElement("Report",
                new XElement("Title", "MCP Server Validation Report"),
                new XElement("GeneratedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XElement("ValidationId", validationResult.ValidationId)
            ),
            new XElement("Server",
                new XElement("Endpoint", validationResult.ServerConfig.Endpoint),
                new XElement("Transport", validationResult.ServerConfig.Transport)
            ),
            new XElement("Results",
                new XElement("OverallStatus", validationResult.OverallStatus.ToString()),
                new XElement("ComplianceScore", validationResult.ComplianceScore.ToString("F1")),
                new XElement("DurationSeconds", (validationResult.Duration?.TotalSeconds ?? 0).ToString("F1")),
                new XElement("StartTime", validationResult.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XElement("EndTime", validationResult.EndTime?.ToString("yyyy-MM-ddTHH:mm:ssZ"))
            ),
            new XElement("Summary",
                new XElement("TotalTests", validationResult.Summary.TotalTests),
                new XElement("PassedTests", validationResult.Summary.PassedTests),
                new XElement("FailedTests", validationResult.Summary.FailedTests),
                new XElement("SkippedTests", validationResult.Summary.SkippedTests),
                new XElement("PassRate", validationResult.Summary.PassRate.ToString("F1")),
                new XElement("CriticalIssues", validationResult.Summary.CriticalIssues),
                new XElement("Warnings", validationResult.Summary.Warnings),
                new XElement("CoverageRatio", validationResult.Summary.CoverageRatio.ToString("F3"))
            )
        );

        var specProfile = validationResult.ValidationConfig?.Reporting?.SpecProfile ?? "latest";
        reportElement.Add(new XElement("Profiles",
            new XElement("ServerProfile", validationResult.ServerProfile.ToString()),
            new XElement("ServerProfileSource", validationResult.ServerProfileSource.ToString()),
            new XElement("SpecProfile", specProfile)
        ));

        var bootstrapElement = BuildBootstrapHealthElement(validationResult);
        if (bootstrapElement != null)
        {
            reportElement.Add(bootstrapElement);
        }

        var severityBreakdown = BuildSeverityBreakdownElement(validationResult);
        if (severityBreakdown != null)
        {
            reportElement.Add(severityBreakdown);
        }

        if (validationResult.CapabilitySnapshot?.Payload is CapabilitySummary snapshot)
        {
            reportElement.Add(BuildCapabilitySnapshotElement(snapshot));
        }

        var clientCompatibilityElement = BuildClientCompatibilityElement(validationResult);
        if (clientCompatibilityElement != null)
        {
            reportElement.Add(clientCompatibilityElement);
        }

        var testCategories = new XElement("TestCategories");

        if (validationResult.ProtocolCompliance != null)
        {
            var protocol = validationResult.ProtocolCompliance;
            var protocolElement = new XElement("ProtocolCompliance",
                new XAttribute("status", protocol.Status.ToString()),
                new XAttribute("score", protocol.ComplianceScore.ToString("F1")),
                new XAttribute("durationSeconds", protocol.Duration.TotalSeconds.ToString("F1")),
                new XElement("ViolationsCount", protocol.Violations.Count)
            );

            if (verbose && protocol.Violations.Any())
            {
                var violationsElement = new XElement("Violations",
                    protocol.Violations.Select(v =>
                    {
                        var violationElement = new XElement("Violation",
                            new XAttribute("severity", v.Severity.ToString()),
                            new XAttribute("source", ValidationRuleSourceClassifier.GetLabel(v)),
                            new XAttribute("category", v.Category ?? string.Empty));

                        if (!string.IsNullOrWhiteSpace(v.CheckId))
                        {
                            violationElement.Add(new XAttribute("checkId", v.CheckId));
                        }

                        if (!string.IsNullOrWhiteSpace(v.Rule))
                        {
                            violationElement.Add(new XAttribute("rule", v.Rule));
                        }

                        violationElement.Add(new XElement("Description", v.Description ?? string.Empty));

                        if (!string.IsNullOrWhiteSpace(v.SpecReference))
                        {
                            violationElement.Add(new XElement("SpecReference", v.SpecReference));
                        }

                        if (!string.IsNullOrWhiteSpace(v.Recommendation))
                        {
                            violationElement.Add(new XElement("Recommendation", v.Recommendation));
                        }

                        if (v.Context?.Any() == true)
                        {
                            violationElement.Add(new XElement("Context",
                                v.Context.Select(kvp => new XElement("Item",
                                    new XAttribute("key", kvp.Key ?? string.Empty),
                                    new XAttribute("value", kvp.Value?.ToString() ?? string.Empty)))));
                        }

                        return violationElement;
                    }));
                protocolElement.Add(violationsElement);
            }

            testCategories.Add(protocolElement);
        }

        if (validationResult.SecurityTesting != null)
        {
            var security = validationResult.SecurityTesting;
            var securityElement = new XElement("SecurityTesting",
                new XAttribute("status", security.Status.ToString()),
                new XAttribute("securityScore", security.SecurityScore.ToString("F1")),
                new XAttribute("durationSeconds", security.Duration.TotalSeconds.ToString("F1")),
                new XElement("VulnerabilitiesCount", security.Vulnerabilities.Count)
            );

            if (verbose && security.Vulnerabilities.Any())
            {
                var vulnerabilitiesElement = new XElement("Vulnerabilities",
                    security.Vulnerabilities.Select(v =>
                    {
                        var vulnerabilityElement = new XElement("Vulnerability",
                            new XAttribute("severity", v.Severity.ToString()),
                            new XAttribute("source", ValidationRuleSourceClassifier.GetLabel(v)),
                            new XAttribute("category", v.Category ?? string.Empty));

                        if (!string.IsNullOrWhiteSpace(v.Id))
                        {
                            vulnerabilityElement.Add(new XAttribute("id", v.Id));
                        }

                        vulnerabilityElement.Add(new XElement("Name", v.Name ?? string.Empty));
                        vulnerabilityElement.Add(new XElement("Description", v.Description ?? string.Empty));

                        if (!string.IsNullOrWhiteSpace(v.AffectedComponent))
                        {
                            vulnerabilityElement.Add(new XElement("AffectedComponent", v.AffectedComponent));
                        }

                        if (v.CvssScore.HasValue)
                        {
                            vulnerabilityElement.Add(new XElement("CvssScore", v.CvssScore.Value.ToString("F1")));
                        }

                        vulnerabilityElement.Add(new XElement("IsExploitable", v.IsExploitable));

                        if (!string.IsNullOrWhiteSpace(v.ProofOfConcept))
                        {
                            vulnerabilityElement.Add(new XElement("ProofOfConcept", v.ProofOfConcept));
                        }

                        if (!string.IsNullOrWhiteSpace(v.Remediation))
                        {
                            vulnerabilityElement.Add(new XElement("Remediation", v.Remediation));
                        }

                        return vulnerabilityElement;
                    }));
                securityElement.Add(vulnerabilitiesElement);
            }

            if (verbose && security.AuthenticationTestResult?.TestScenarios?.Any() == true)
            {
                var authElement = new XElement("AuthenticationScenarios",
                    security.AuthenticationTestResult.TestScenarios.Select(s =>
                        new XElement("Scenario",
                            new XAttribute("method", s.Method ?? string.Empty),
                            new XElement("ScenarioName", s.ScenarioName ?? string.Empty),
                            new XElement("ExpectedBehavior", s.ExpectedBehavior ?? string.Empty),
                            new XElement("ActualBehavior", s.ActualBehavior ?? string.Empty),
                            new XElement("Analysis", s.Analysis ?? string.Empty))));
                securityElement.Add(authElement);
            }

            if (verbose && security.AttackSimulations?.Any() == true)
            {
                var attacksElement = new XElement("AttackSimulations",
                    security.AttackSimulations.Select(a =>
                        new XElement("Simulation",
                            new XAttribute("defenseSuccessful", a.DefenseSuccessful),
                            new XElement("AttackVector", a.AttackVector ?? string.Empty),
                            new XElement("Description", a.Description ?? string.Empty),
                            new XElement("ServerResponse", a.ServerResponse ?? string.Empty))));
                securityElement.Add(attacksElement);
            }

            testCategories.Add(securityElement);
        }

        if (validationResult.ToolValidation != null)
        {
            var tools = validationResult.ToolValidation;
            var toolsElement = new XElement("ToolValidation",
                new XAttribute("status", tools.Status.ToString()),
                new XAttribute("durationSeconds", tools.Duration.TotalSeconds.ToString("F1")),
                new XElement("ToolsDiscovered", tools.ToolsDiscovered),
                new XElement("ToolsPassed", tools.ToolsTestPassed),
                new XElement("ToolsFailed", tools.ToolsTestFailed)
            );

            if (verbose && tools.ToolResults?.Any() == true)
            {
                var toolResultsElement = new XElement("ToolResults",
                    tools.ToolResults.Select(t =>
                        new XElement("ToolResult",
                            new XElement("ToolName", t.ToolName ?? string.Empty),
                            new XElement("Status", t.Status.ToString()),
                            new XElement("ExecutionTimeMs", t.ExecutionTimeMs.ToString("F2")))));
                toolsElement.Add(toolResultsElement);
            }

            if (tools.AuthenticationSecurity != null)
            {
                var auth = tools.AuthenticationSecurity;
                var authElement = new XElement("AuthenticationSecurity",
                    new XAttribute("enforced", tools.AuthenticationProperlyEnforced),
                    new XElement("AuthenticationRequired", auth.AuthenticationRequired),
                    new XElement("RejectsUnauthenticated", auth.RejectsUnauthenticated),
                    new XElement("HasProperAuthHeaders", auth.HasProperAuthHeaders),
                    new XElement("SecurityScore", auth.SecurityScore.ToString("F1")));

                if (auth.ChallengeStatusCode.HasValue)
                {
                    authElement.Add(new XElement("ChallengeStatusCode", auth.ChallengeStatusCode.Value));
                }

                if (auth.ChallengeDurationMs > 0)
                {
                    authElement.Add(new XElement("ChallengeDurationMs", auth.ChallengeDurationMs.ToString("F1")));
                }

                if (!string.IsNullOrWhiteSpace(auth.WwwAuthenticateHeader))
                {
                    authElement.Add(new XElement("WwwAuthenticate", auth.WwwAuthenticateHeader));
                }

                if (auth.AuthMetadata != null)
                {
                    var metadataElement = new XElement("AuthMetadata");
                    if (!string.IsNullOrWhiteSpace(auth.AuthMetadata.Resource))
                    {
                        metadataElement.Add(new XElement("Resource", auth.AuthMetadata.Resource));
                    }
                    if (auth.AuthMetadata.AuthorizationServers?.Any() == true)
                    {
                        metadataElement.Add(new XElement("AuthorizationServers",
                            auth.AuthMetadata.AuthorizationServers.Select(s => new XElement("Server", s))));
                    }
                    if (auth.AuthMetadata.ScopesSupported?.Any() == true)
                    {
                        metadataElement.Add(new XElement("ScopesSupported",
                            auth.AuthMetadata.ScopesSupported.Select(s => new XElement("Scope", s))));
                    }
                    if (auth.AuthMetadata.BearerMethodsSupported?.Any() == true)
                    {
                        metadataElement.Add(new XElement("BearerMethodsSupported",
                            auth.AuthMetadata.BearerMethodsSupported.Select(s => new XElement("Method", s))));
                    }
                    authElement.Add(metadataElement);
                }

                if (auth.Findings.Any())
                {
                    authElement.Add(new XElement("Findings", auth.Findings.Select(f => new XElement("Finding", f))));
                }

                toolsElement.Add(authElement);
            }

            testCategories.Add(toolsElement);
        }

        if (validationResult.PerformanceTesting != null)
        {
            var perf = validationResult.PerformanceTesting;
            var hasObservedPerformanceMetrics = PerformanceMeasurementEvaluator.HasObservedMetrics(perf);
            var perfElement = new XElement("PerformanceTesting",
                new XAttribute("status", perf.Status.ToString()),
                new XAttribute("durationSeconds", perf.Duration.TotalSeconds.ToString("F1"))
            );

            if (verbose && hasObservedPerformanceMetrics)
            {
                var loadElement = new XElement("LoadTesting",
                    new XElement("AverageLatencyMs", perf.LoadTesting.AverageResponseTimeMs.ToString("F2")),
                    new XElement("P95LatencyMs", perf.LoadTesting.P95ResponseTimeMs.ToString("F2")),
                    new XElement("RequestsPerSecond", perf.LoadTesting.RequestsPerSecond.ToString("F2")),
                    new XElement("ErrorRate", perf.LoadTesting.ErrorRate.ToString("F2"))
                );
                perfElement.Add(loadElement);
            }
            else if (verbose)
            {
                perfElement.Add(
                    new XElement("MeasurementStatus", "Unavailable"),
                    new XElement(
                        "Reason",
                        PerformanceMeasurementEvaluator.GetUnavailableReason(
                            perf,
                            "Performance measurements were not captured before the run ended.")));
            }

            testCategories.Add(perfElement);
        }

        if (validationResult.ResourceTesting != null)
        {
            var resources = validationResult.ResourceTesting;
            var resourcesElement = new XElement("ResourceTesting",
                new XAttribute("status", resources.Status.ToString()),
                new XAttribute("durationSeconds", resources.Duration.TotalSeconds.ToString("F1")),
                new XElement("ResourcesDiscovered", resources.ResourcesDiscovered),
                new XElement("ResourcesAccessible", resources.ResourcesAccessible)
            );

            if (verbose && resources.ResourceResults?.Any() == true)
            {
                var resourceResultsElement = new XElement("ResourceResults",
                    resources.ResourceResults.Select(r =>
                        new XElement("Resource",
                            new XElement("ResourceName", r.ResourceName ?? string.Empty),
                            new XElement("ResourceUri", r.ResourceUri ?? string.Empty),
                            new XElement("MimeType", r.MimeType ?? string.Empty),
                            new XElement("ContentSize", r.ContentSize?.ToString() ?? string.Empty),
                            new XElement("Status", r.Status.ToString()))));
                resourcesElement.Add(resourceResultsElement);
            }

            testCategories.Add(resourcesElement);
        }

        if (validationResult.PromptTesting != null)
        {
            var prompts = validationResult.PromptTesting;
            var promptsElement = new XElement("PromptTesting",
                new XAttribute("status", prompts.Status.ToString()),
                new XAttribute("durationSeconds", prompts.Duration.TotalSeconds.ToString("F1")),
                new XElement("PromptsDiscovered", prompts.PromptsDiscovered),
                new XElement("PromptsPassed", prompts.PromptsTestPassed)
            );

            testCategories.Add(promptsElement);
        }

        reportElement.Add(testCategories);

        var issuesElement = new XElement("Issues");

        if (validationResult.CriticalErrors.Any())
        {
            var errorsElement = new XElement("CriticalErrors",
                validationResult.CriticalErrors.Select(e => new XElement("Error", e)));
            issuesElement.Add(errorsElement);
        }

        if (validationResult.Recommendations.Any())
        {
            var recommendationsElement = new XElement("Recommendations",
                validationResult.Recommendations.Select(r => new XElement("Recommendation", r)));
            issuesElement.Add(recommendationsElement);
        }

        reportElement.Add(issuesElement);

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), reportElement);
        return document.ToString();
    }

    public string GenerateSarifReport(ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var entries = BuildSarifEntries(validationResult)
            .GroupBy(entry => entry.Fingerprint, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var rules = entries
            .GroupBy(entry => entry.RuleId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return new
                {
                    id = first.RuleId,
                    name = first.RuleId,
                    shortDescription = new { text = first.ShortDescription },
                    fullDescription = new { text = first.FullDescription },
                    help = string.IsNullOrWhiteSpace(first.Recommendation)
                        ? null
                        : new { text = first.Recommendation },
                    helpUri = first.HelpUri,
                    properties = new
                    {
                        source = first.Source,
                        category = first.Category,
                        tags = BuildSarifTags(first.Source, first.Category, first.Component)
                    }
                };
            })
            .ToList();

        var run = new
        {
            tool = new
            {
                driver = new
                {
                    name = "MCP Benchmark",
                    fullName = "MCP Benchmark Validation Suite",
                    semanticVersion = "1.0.0",
                    informationUri = "https://github.com/navalerakesh/mcp-validation-security",
                    rules
                }
            },
            automationDetails = new
            {
                id = validationResult.ValidationId,
                description = new { text = "MCP validation run" }
            },
            invocations = new[]
            {
                new
                {
                    executionSuccessful = validationResult.OverallStatus != ValidationStatus.InProgress,
                    startTimeUtc = validationResult.StartTime.ToUniversalTime().ToString("O"),
                    endTimeUtc = validationResult.EndTime?.ToUniversalTime().ToString("O"),
                    properties = new
                    {
                        validationId = validationResult.ValidationId,
                        endpoint = validationResult.ServerConfig.Endpoint,
                        transport = validationResult.ServerConfig.Transport,
                        overallStatus = validationResult.OverallStatus.ToString(),
                        serverProfile = validationResult.ServerProfile.ToString(),
                        specProfile = validationResult.ValidationConfig.Reporting.SpecProfile
                    }
                }
            },
            results = entries.Select(entry => new
            {
                ruleId = entry.RuleId,
                level = entry.Level,
                kind = "fail",
                message = new { text = entry.Message },
                partialFingerprints = new { logicalIdentity = entry.Fingerprint },
                locations = string.IsNullOrWhiteSpace(entry.Component)
                    ? null
                    : new[]
                    {
                        new
                        {
                            logicalLocations = new[]
                            {
                                new
                                {
                                    kind = "component",
                                    name = entry.Component
                                }
                            }
                        }
                    },
                properties = entry.Properties
            }).ToList()
        };

        var sarif = new
        {
            version = "2.1.0",
            schema = "https://json.schemastore.org/sarif-2.1.0.json",
            runs = new[] { run }
        };

        return JsonSerializer.Serialize(sarif, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }).Replace("\"schema\"", "\"$schema\"");
    }

    public string GenerateJunitReport(ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var testCases = BuildJunitTestCases(validationResult);
        var suiteTimestamp = (validationResult.StartTime == default ? DateTime.UtcNow : validationResult.StartTime)
            .ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);

        var suites = testCases
            .GroupBy(testCase => testCase.SuiteName, StringComparer.Ordinal)
            .Select(group =>
            {
                var tests = group.Count();
                var failures = group.Count(testCase => testCase.Outcome == JunitOutcome.Failure);
                var errors = group.Count(testCase => testCase.Outcome == JunitOutcome.Error);
                var skipped = group.Count(testCase => testCase.Outcome == JunitOutcome.Skipped);
                var suite = new XElement("testsuite",
                    new XAttribute("name", group.Key),
                    new XAttribute("tests", tests),
                    new XAttribute("failures", failures),
                    new XAttribute("errors", errors),
                    new XAttribute("skipped", skipped),
                    new XAttribute("time", group.Sum(testCase => testCase.TimeSeconds).ToString("F3", CultureInfo.InvariantCulture)),
                    new XAttribute("timestamp", suiteTimestamp));

                suite.Add(new XElement("properties",
                    new XElement("property", new XAttribute("name", "validationId"), new XAttribute("value", validationResult.ValidationId)),
                    new XElement("property", new XAttribute("name", "endpoint"), new XAttribute("value", validationResult.ServerConfig.Endpoint ?? string.Empty)),
                    new XElement("property", new XAttribute("name", "transport"), new XAttribute("value", validationResult.ServerConfig.Transport ?? string.Empty)),
                    new XElement("property", new XAttribute("name", "overallStatus"), new XAttribute("value", validationResult.OverallStatus.ToString())),
                    new XElement("property", new XAttribute("name", "policyMode"), new XAttribute("value", validationResult.PolicyOutcome?.Mode ?? string.Empty)),
                    new XElement("property", new XAttribute("name", "specProfile"), new XAttribute("value", validationResult.ValidationConfig?.Reporting?.SpecProfile ?? "latest"))));

                foreach (var testCase in group)
                {
                    var testCaseElement = new XElement("testcase",
                        new XAttribute("classname", testCase.ClassName),
                        new XAttribute("name", testCase.Name),
                        new XAttribute("time", testCase.TimeSeconds.ToString("F3", CultureInfo.InvariantCulture)));

                    switch (testCase.Outcome)
                    {
                        case JunitOutcome.Failure:
                            testCaseElement.Add(new XElement("failure",
                                new XAttribute("message", testCase.Message),
                                testCase.Details ?? string.Empty));
                            break;
                        case JunitOutcome.Error:
                            testCaseElement.Add(new XElement("error",
                                new XAttribute("message", testCase.Message),
                                testCase.Details ?? string.Empty));
                            break;
                        case JunitOutcome.Skipped:
                            testCaseElement.Add(new XElement("skipped",
                                new XAttribute("message", testCase.Message),
                                testCase.Details ?? string.Empty));
                            break;
                    }

                    if (!string.IsNullOrWhiteSpace(testCase.SystemOut))
                    {
                        testCaseElement.Add(new XElement("system-out", testCase.SystemOut));
                    }

                    suite.Add(testCaseElement);
                }

                return suite;
            })
            .ToList();

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement("testsuites",
                new XAttribute("name", "MCP Validator"),
                new XAttribute("tests", testCases.Count),
                new XAttribute("failures", testCases.Count(testCase => testCase.Outcome == JunitOutcome.Failure)),
                new XAttribute("errors", testCases.Count(testCase => testCase.Outcome == JunitOutcome.Error)),
                new XAttribute("skipped", testCases.Count(testCase => testCase.Outcome == JunitOutcome.Skipped)),
                new XAttribute("time", testCases.Sum(testCase => testCase.TimeSeconds).ToString("F3", CultureInfo.InvariantCulture)),
                suites));

        return document.ToString();
    }

    private static XElement? BuildBootstrapHealthElement(ValidationResult validationResult)
    {
        var bootstrapHealth = ResolveBootstrapHealth(validationResult);
        if (bootstrapHealth == null)
        {
            return null;
        }

        var element = new XElement("BootstrapHealth",
            new XAttribute("disposition", bootstrapHealth.Disposition.ToString()),
            new XAttribute("isHealthy", bootstrapHealth.IsHealthy),
            new XAttribute("allowsDeferredValidation", bootstrapHealth.AllowsDeferredValidation),
            new XElement("ResponseTimeMs", bootstrapHealth.ResponseTimeMs.ToString("F1", CultureInfo.InvariantCulture)));

        if (!string.IsNullOrWhiteSpace(bootstrapHealth.ErrorMessage))
        {
            element.Add(new XElement("ErrorMessage", bootstrapHealth.ErrorMessage));
        }

        if (!string.IsNullOrWhiteSpace(bootstrapHealth.ServerVersion))
        {
            element.Add(new XElement("ServerVersion", bootstrapHealth.ServerVersion));
        }

        var protocolVersion = validationResult.ProtocolVersion ?? bootstrapHealth.ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(protocolVersion))
        {
            element.Add(new XElement("ProtocolVersion", protocolVersion));
        }

        if (bootstrapHealth.InitializationDetails?.Transport.StatusCode is int statusCode)
        {
            element.Add(new XElement("HandshakeHttpStatus", statusCode));
        }

        return element;
    }

    private static XElement? BuildClientCompatibilityElement(ValidationResult validationResult)
    {
        if (validationResult.ClientCompatibility?.Assessments.Count > 0 != true)
        {
            return null;
        }

        var element = new XElement("ClientCompatibility");

        if (validationResult.ClientCompatibility.RequestedProfiles.Count > 0)
        {
            element.Add(new XElement("RequestedProfiles",
                validationResult.ClientCompatibility.RequestedProfiles.Select(profileId => new XElement("ProfileId", profileId))));
        }

        element.Add(validationResult.ClientCompatibility.Assessments.Select(assessment =>
        {
            var profileElement = new XElement("Profile",
                new XAttribute("id", assessment.ProfileId),
                new XAttribute("status", assessment.Status.ToString()),
                new XAttribute("passedRequirements", assessment.PassedRequirements),
                new XAttribute("warningRequirements", assessment.WarningRequirements),
                new XAttribute("failedRequirements", assessment.FailedRequirements),
                new XElement("DisplayName", assessment.DisplayName),
                new XElement("Revision", assessment.Revision),
                new XElement("EvidenceBasis", assessment.EvidenceBasis.ToString()),
                new XElement("Summary", assessment.Summary));

            if (!string.IsNullOrWhiteSpace(assessment.DocumentationUrl))
            {
                profileElement.Add(new XElement("DocumentationUrl", assessment.DocumentationUrl));
            }

            if (assessment.Requirements.Count > 0)
            {
                profileElement.Add(new XElement("Requirements",
                    assessment.Requirements.Select(requirement =>
                    {
                        var requirementElement = new XElement("Requirement",
                            new XAttribute("id", requirement.RequirementId),
                            new XAttribute("level", requirement.Level.ToString()),
                            new XAttribute("outcome", requirement.Outcome.ToString()),
                            new XElement("Title", requirement.Title),
                            new XElement("Summary", requirement.Summary),
                            new XElement("EvidenceBasis", requirement.EvidenceBasis.ToString()));

                        if (requirement.RuleIds.Count > 0)
                        {
                            requirementElement.Add(new XElement("RuleIds", requirement.RuleIds.Select(ruleId => new XElement("RuleId", ruleId))));
                        }

                        if (requirement.ExampleComponents.Count > 0)
                        {
                            requirementElement.Add(new XElement("ExampleComponents", requirement.ExampleComponents.Select(component => new XElement("Component", component))));
                        }

                        if (!string.IsNullOrWhiteSpace(requirement.Recommendation))
                        {
                            requirementElement.Add(new XElement("Recommendation", requirement.Recommendation));
                        }

                        if (!string.IsNullOrWhiteSpace(requirement.DocumentationUrl))
                        {
                            requirementElement.Add(new XElement("DocumentationUrl", requirement.DocumentationUrl));
                        }

                        return requirementElement;
                    })));
            }

            return profileElement;
        }));

        return element;
    }

    private static HealthCheckResult? ResolveBootstrapHealth(ValidationResult validationResult)
    {
        if (validationResult.BootstrapHealth != null)
        {
            return validationResult.BootstrapHealth;
        }

        if (validationResult.InitializationHandshake == null)
        {
            return null;
        }

        return new HealthCheckResult
        {
            IsHealthy = validationResult.InitializationHandshake.IsSuccessful,
            Disposition = ValidationReliability.ClassifyHealthCheck(validationResult.InitializationHandshake),
            ResponseTimeMs = validationResult.InitializationHandshake.Transport.Duration.TotalMilliseconds,
            ServerVersion = validationResult.InitializationHandshake.Payload?.ServerInfo?.Version,
            ProtocolVersion = validationResult.InitializationHandshake.Payload?.ProtocolVersion,
            ErrorMessage = validationResult.InitializationHandshake.IsSuccessful ? null : validationResult.InitializationHandshake.Error,
            InitializationDetails = validationResult.InitializationHandshake
        };
    }

    private static Dictionary<string, int> BuildSeverityBuckets(IEnumerable<ComplianceViolation> violations, IEnumerable<SecurityVulnerability> vulnerabilities)
    {
        var buckets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var severity in SeverityOrder)
        {
            buckets[severity] = 0;
        }

        foreach (var violation in violations)
        {
            var bucket = MapViolationSeverity(violation.Severity);
            buckets[bucket]++;
        }

        foreach (var vulnerability in vulnerabilities)
        {
            var bucket = MapVulnerabilitySeverity(vulnerability.Severity);
            buckets[bucket]++;
        }

        return buckets;
    }

    private static string MapViolationSeverity(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical => "Critical",
            ViolationSeverity.High => "High",
            ViolationSeverity.Medium => "Medium",
            ViolationSeverity.Low => "Low",
            _ => "Low"
        };
    }

    private static string MapVulnerabilitySeverity(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Critical => "Critical",
            VulnerabilitySeverity.High => "High",
            VulnerabilitySeverity.Medium => "Medium",
            VulnerabilitySeverity.Low => "Low",
            VulnerabilitySeverity.Informational => "Informational",
            _ => "Low"
        };
    }

    private static readonly string[] SeverityOrder = new[] { "Critical", "High", "Medium", "Low", "Informational" };

    private XElement? BuildSeverityBreakdownElement(ValidationResult validationResult)
    {
        var violations = validationResult.ProtocolCompliance?.Violations ?? Enumerable.Empty<ComplianceViolation>();
        var vulnerabilities = validationResult.SecurityTesting?.Vulnerabilities ?? Enumerable.Empty<SecurityVulnerability>();

        if (!violations.Any() && !vulnerabilities.Any())
        {
            return null;
        }

        var buckets = BuildSeverityBuckets(violations, vulnerabilities);
        if (buckets.Values.All(value => value == 0))
        {
            return null;
        }

        var element = new XElement("SeverityBreakdown");
        foreach (var severity in SeverityOrder)
        {
            buckets.TryGetValue(severity, out var count);
            element.Add(new XElement("Severity",
                new XAttribute("level", severity),
                new XAttribute("count", count)));
        }

        return element;
    }

    private XElement BuildCapabilitySnapshotElement(CapabilitySummary snapshot)
    {
        var element = new XElement("CapabilitySnapshot");
        element.Add(CreateCapabilityProbeElement("tools/list", snapshot.DiscoveredToolsCount, snapshot.ToolListResponse?.StatusCode, snapshot.ToolListDurationMs, snapshot.ToolListingSucceeded, snapshot.ToolInvocationSucceeded));
        element.Add(CreateCapabilityProbeElement("resources/list", snapshot.DiscoveredResourcesCount, snapshot.ResourceListResponse?.StatusCode, snapshot.ResourceListDurationMs, snapshot.ResourceListingSucceeded));
        element.Add(CreateCapabilityProbeElement("prompts/list", snapshot.DiscoveredPromptsCount, snapshot.PromptListResponse?.StatusCode, snapshot.PromptListDurationMs, snapshot.PromptListingSucceeded));

        if (!string.IsNullOrWhiteSpace(snapshot.FirstToolName))
        {
            element.Add(new XElement("FirstTool", snapshot.FirstToolName));
        }

        return element;
    }

    private XElement CreateCapabilityProbeElement(string name, int discovered, int? statusCode, double durationMs, bool succeeded, bool? invocationSucceeded = null)
    {
        var probeElement = new XElement("Probe",
            new XAttribute("name", name),
            new XAttribute("discovered", discovered),
            new XAttribute("durationMs", durationMs.ToString("F1")),
            new XAttribute("succeeded", succeeded));

        if (statusCode.HasValue)
        {
            probeElement.Add(new XAttribute("httpStatus", statusCode.Value));
        }

        if (invocationSucceeded.HasValue)
        {
            probeElement.Add(new XAttribute("invocationSucceeded", invocationSucceeded.Value));
        }

        return probeElement;
    }

    private static IReadOnlyList<SarifEntry> BuildSarifEntries(ValidationResult validationResult)
    {
        var entries = new List<SarifEntry>();

        AddFindingRange(entries, validationResult.ProtocolCompliance?.Findings, "protocol");
        AddFindingRange(entries, validationResult.ToolValidation?.Findings, "tools");
        AddFindingRange(entries, validationResult.ToolValidation?.AiReadinessFindings, "tools/ai-readiness");
        AddFindingRange(entries, validationResult.ResourceTesting?.Findings, "resources");
        AddFindingRange(entries, validationResult.PromptTesting?.Findings, "prompts");
        AddFindingRange(entries, validationResult.SecurityTesting?.Findings, "security");
        AddFindingRange(entries, validationResult.PerformanceTesting?.Findings, "performance");
        AddFindingRange(entries, validationResult.ErrorHandling?.Findings, "error-handling");

        if (validationResult.ToolValidation?.AuthenticationSecurity?.StructuredFindings is { Count: > 0 } authFindings)
        {
            AddFindingRange(entries, authFindings, "auth");
        }

        if (validationResult.ProtocolCompliance?.Violations is { Count: > 0 } violations)
        {
            entries.AddRange(violations.Select(violation => CreateSarifEntry(violation)));
        }

        if (validationResult.ToolValidation?.ToolResults is { Count: > 0 } toolResults)
        {
            foreach (var tool in toolResults)
            {
                AddFindingRange(entries, tool.Findings, tool.ToolName);
            }
        }

        if (validationResult.ResourceTesting?.ResourceResults is { Count: > 0 } resourceResults)
        {
            foreach (var resource in resourceResults)
            {
                AddFindingRange(entries, resource.Findings, string.IsNullOrWhiteSpace(resource.ResourceUri) ? resource.ResourceName : resource.ResourceUri);
            }
        }

        if (validationResult.PromptTesting?.PromptResults is { Count: > 0 } promptResults)
        {
            foreach (var prompt in promptResults)
            {
                AddFindingRange(entries, prompt.Findings, prompt.PromptName);
            }
        }

        if (validationResult.SecurityTesting?.Vulnerabilities is { Count: > 0 } vulnerabilities)
        {
            entries.AddRange(vulnerabilities.Select(CreateSarifEntry));
        }

        return entries;
    }

    private static void AddFindingRange(List<SarifEntry> entries, IEnumerable<ValidationFinding>? findings, string defaultComponent)
    {
        if (findings == null)
        {
            return;
        }

        foreach (var finding in findings)
        {
            entries.Add(CreateSarifEntry(finding, defaultComponent));
        }
    }

    private static SarifEntry CreateSarifEntry(ValidationFinding finding, string defaultComponent)
    {
        var component = string.IsNullOrWhiteSpace(finding.Component) ? defaultComponent : finding.Component;
        var properties = new Dictionary<string, object?>
        {
            ["source"] = "structured-finding",
            ["authority"] = finding.EffectiveSourceLabel,
            ["category"] = finding.Category,
            ["component"] = component,
            ["severity"] = finding.Severity.ToString()
        };

        if (!string.IsNullOrWhiteSpace(finding.Recommendation))
        {
            properties["recommendation"] = finding.Recommendation;
        }

        if (finding.Metadata.Count > 0)
        {
            properties["metadata"] = finding.Metadata;
        }

        return new SarifEntry(
            RuleId: finding.RuleId,
            Category: string.IsNullOrWhiteSpace(finding.Category) ? "validation" : finding.Category,
            Component: component,
            Level: MapFindingLevel(finding.Severity),
            Message: finding.Summary,
            ShortDescription: finding.Summary,
            FullDescription: finding.Summary,
            Recommendation: finding.Recommendation,
            HelpUri: TryGetHelpUri(finding.Metadata),
            Properties: properties,
                Source: finding.EffectiveSourceLabel,
            Fingerprint: $"finding|{finding.RuleId}|{component}|{finding.Summary}");
    }

    private static SarifEntry CreateSarifEntry(ComplianceViolation violation)
    {
        var ruleId = string.IsNullOrWhiteSpace(violation.CheckId)
            ? BuildFallbackRuleId("MCP.PROTOCOL", violation.Category, violation.Rule, violation.Description)
            : violation.CheckId;

        var component = string.IsNullOrWhiteSpace(violation.Category) ? "protocol" : violation.Category;
        var properties = new Dictionary<string, object?>
        {
            ["source"] = "protocol-violation",
            ["authority"] = ValidationRuleSourceClassifier.GetLabel(violation),
            ["category"] = violation.Category,
            ["component"] = component,
            ["severity"] = violation.Severity.ToString(),
            ["rule"] = violation.Rule
        };

        if (!string.IsNullOrWhiteSpace(violation.Recommendation))
        {
            properties["recommendation"] = violation.Recommendation;
        }

        if (violation.Context.Count > 0)
        {
            properties["context"] = violation.Context.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString());
        }

        return new SarifEntry(
            RuleId: ruleId,
            Category: string.IsNullOrWhiteSpace(violation.Category) ? "ProtocolCompliance" : violation.Category,
            Component: component,
            Level: MapViolationLevel(violation.Severity),
            Message: violation.Description,
            ShortDescription: violation.Rule ?? violation.Description,
            FullDescription: violation.Description,
            Recommendation: violation.Recommendation,
            HelpUri: violation.SpecReference,
            Properties: properties,
                Source: ValidationRuleSourceClassifier.GetLabel(violation),
            Fingerprint: $"protocol|{ruleId}|{component}|{violation.Description}");
    }

    private static SarifEntry CreateSarifEntry(SecurityVulnerability vulnerability)
    {
        var ruleId = string.IsNullOrWhiteSpace(vulnerability.Id)
            ? BuildFallbackRuleId("MCP.SECURITY", vulnerability.Category, vulnerability.Name, vulnerability.AffectedComponent)
            : vulnerability.Id;

        var component = string.IsNullOrWhiteSpace(vulnerability.AffectedComponent)
            ? (string.IsNullOrWhiteSpace(vulnerability.Category) ? "security" : vulnerability.Category)
            : vulnerability.AffectedComponent;

        var properties = new Dictionary<string, object?>
        {
            ["source"] = "security-vulnerability",
            ["authority"] = ValidationRuleSourceClassifier.GetLabel(vulnerability),
            ["category"] = vulnerability.Category,
            ["component"] = component,
            ["severity"] = vulnerability.Severity.ToString(),
            ["isExploitable"] = vulnerability.IsExploitable
        };

        if (vulnerability.CvssScore.HasValue)
        {
            properties["cvssScore"] = vulnerability.CvssScore.Value;
        }

        if (!string.IsNullOrWhiteSpace(vulnerability.Remediation))
        {
            properties["recommendation"] = vulnerability.Remediation;
        }

        if (!string.IsNullOrWhiteSpace(vulnerability.ProofOfConcept))
        {
            properties["proofOfConcept"] = vulnerability.ProofOfConcept;
        }

        return new SarifEntry(
            RuleId: ruleId,
            Category: string.IsNullOrWhiteSpace(vulnerability.Category) ? "SecurityTesting" : vulnerability.Category,
            Component: component,
            Level: MapVulnerabilityLevel(vulnerability.Severity),
            Message: vulnerability.Description,
            ShortDescription: vulnerability.Name,
            FullDescription: vulnerability.Description,
            Recommendation: vulnerability.Remediation,
            HelpUri: null,
            Properties: properties,
                Source: ValidationRuleSourceClassifier.GetLabel(vulnerability),
            Fingerprint: $"vuln|{ruleId}|{component}|{vulnerability.Description}");
    }

    private static string MapFindingLevel(ValidationFindingSeverity severity)
    {
        return severity switch
        {
            ValidationFindingSeverity.Critical => "error",
            ValidationFindingSeverity.High => "error",
            ValidationFindingSeverity.Medium => "warning",
            ValidationFindingSeverity.Low => "note",
            _ => "note"
        };
    }

    private static string MapViolationLevel(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical => "error",
            ViolationSeverity.High => "error",
            ViolationSeverity.Medium => "warning",
            ViolationSeverity.Low => "note",
            _ => "note"
        };
    }

    private static string MapVulnerabilityLevel(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Critical => "error",
            VulnerabilitySeverity.High => "error",
            VulnerabilitySeverity.Medium => "warning",
            VulnerabilitySeverity.Low => "note",
            VulnerabilitySeverity.Informational => "note",
            _ => "note"
        };
    }

    private static string[] BuildSarifTags(string source, string category, string component)
    {
        var tags = new List<string>();

        if (!string.IsNullOrWhiteSpace(source))
        {
            tags.Add(source);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            tags.Add(category);
        }

        if (!string.IsNullOrWhiteSpace(component))
        {
            tags.Add(component);
        }

        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? TryGetHelpUri(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("specReference", out var specReference) &&
            Uri.TryCreate(specReference, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || absoluteUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            return absoluteUri.ToString();
        }

        return null;
    }

    private static string BuildFallbackRuleId(params string?[] parts)
    {
        var normalized = parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => new string(part!
                .Trim()
                .ToUpperInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray()).Trim('_'))
            .Where(part => !string.IsNullOrWhiteSpace(part));

        var joined = string.Join('.', normalized);
        return string.IsNullOrWhiteSpace(joined) ? "MCP.UNKNOWN" : joined;
    }

    private sealed record SarifEntry(
        string RuleId,
        string Category,
        string Component,
        string Level,
        string Message,
        string ShortDescription,
        string FullDescription,
        string? Recommendation,
        string? HelpUri,
        Dictionary<string, object?> Properties,
        string Source,
        string Fingerprint);

    private static IReadOnlyList<JunitTestCase> BuildJunitTestCases(ValidationResult validationResult)
    {
        var testCases = new List<JunitTestCase>
        {
            CreateOverallJunitTestCase(validationResult)
        };

        if (validationResult.PolicyOutcome != null)
        {
            testCases.Add(CreatePolicyJunitTestCase(validationResult.PolicyOutcome, validationResult));
        }

        AddCategoryJunitTestCase(testCases, "protocol-compliance", "Protocol Compliance", validationResult.ProtocolCompliance, BuildProtocolDetails(validationResult.ProtocolCompliance));
        AddCategoryJunitTestCase(testCases, "tool-validation", "Tool Validation", validationResult.ToolValidation, BuildToolDetails(validationResult.ToolValidation));
        AddCategoryJunitTestCase(testCases, "resource-testing", "Resource Testing", validationResult.ResourceTesting, BuildResourceDetails(validationResult.ResourceTesting));
        AddCategoryJunitTestCase(testCases, "prompt-testing", "Prompt Testing", validationResult.PromptTesting, BuildPromptDetails(validationResult.PromptTesting));
        AddCategoryJunitTestCase(testCases, "security-testing", "Security Testing", validationResult.SecurityTesting, BuildSecurityDetails(validationResult.SecurityTesting));
        AddCategoryJunitTestCase(testCases, "performance-testing", "Performance Testing", validationResult.PerformanceTesting, BuildPerformanceDetails(validationResult.PerformanceTesting));
        AddCategoryJunitTestCase(testCases, "error-handling", "Error Handling", validationResult.ErrorHandling, BuildErrorHandlingDetails(validationResult.ErrorHandling));

        return testCases;
    }

    private static JunitTestCase CreateOverallJunitTestCase(ValidationResult validationResult)
    {
        var details = new List<string>
        {
            $"Compliance Score: {validationResult.ComplianceScore:F1}%",
            $"Summary: total={validationResult.Summary.TotalTests}, passed={validationResult.Summary.PassedTests}, failed={validationResult.Summary.FailedTests}, skipped={validationResult.Summary.SkippedTests}",
            $"Endpoint: {validationResult.ServerConfig.Endpoint}",
            $"Transport: {validationResult.ServerConfig.Transport}"
        };

        if (validationResult.CriticalErrors.Count > 0)
        {
            details.AddRange(validationResult.CriticalErrors.Select(error => $"Critical Error: {error}"));
        }

        if (validationResult.Recommendations.Count > 0)
        {
            details.AddRange(validationResult.Recommendations.Select(recommendation => $"Recommendation: {recommendation}"));
        }

        return CreateJunitTestCase(
            suiteName: "overall-validation",
            className: "validation.overall",
            name: "Overall Validation Run",
            status: MapValidationStatus(validationResult.OverallStatus),
            duration: validationResult.Duration ?? TimeSpan.Zero,
            message: $"Overall status: {validationResult.OverallStatus}",
            details: string.Join(Environment.NewLine, details));
    }

    private static JunitTestCase CreatePolicyJunitTestCase(ValidationPolicyOutcome policyOutcome, ValidationResult validationResult)
    {
        var details = new List<string>
        {
            policyOutcome.Summary,
            $"Recommended Exit Code: {policyOutcome.RecommendedExitCode}",
            $"Suppressed Signals: {policyOutcome.SuppressedSignalCount}"
        };

        if (policyOutcome.Reasons.Count > 0)
        {
            details.AddRange(policyOutcome.Reasons.Select(reason => $"Reason: {reason}"));
        }

        if (policyOutcome.AppliedSuppressions.Count > 0)
        {
            details.AddRange(policyOutcome.AppliedSuppressions.Select(suppression => $"Applied Suppression: {suppression.Id} by {suppression.Owner} ({suppression.MatchedSignalCount} signal(s))"));
        }

        if (policyOutcome.IgnoredSuppressions.Count > 0)
        {
            details.AddRange(policyOutcome.IgnoredSuppressions.Select(suppression => $"Ignored Suppression: {suppression.Id} - {suppression.Reason}"));
        }

        return CreateJunitTestCase(
            suiteName: "host-policy",
            className: "validation.policy",
            name: $"Policy Gate ({policyOutcome.Mode})",
            status: policyOutcome.Passed ? TestStatus.Passed : TestStatus.Failed,
            duration: validationResult.Duration ?? TimeSpan.Zero,
            message: policyOutcome.Summary,
            details: string.Join(Environment.NewLine, details));
    }

    private static void AddCategoryJunitTestCase(
        ICollection<JunitTestCase> testCases,
        string suiteName,
        string name,
        TestResultBase? result,
        string? details)
    {
        if (result == null)
        {
            return;
        }

        testCases.Add(CreateJunitTestCase(
            suiteName: suiteName,
            className: $"validation.{suiteName.Replace('-', '.')}",
            name: name,
            status: result.Status,
            duration: result.Duration,
            message: result.Message ?? $"{name} finished with status {result.Status}.",
            details: details));
    }

    private static JunitTestCase CreateJunitTestCase(
        string suiteName,
        string className,
        string name,
        TestStatus status,
        TimeSpan duration,
        string message,
        string? details)
    {
        var normalizedDetails = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        return status switch
        {
            TestStatus.Passed => new JunitTestCase(suiteName, className, name, duration.TotalSeconds, JunitOutcome.Passed, message, null, normalizedDetails),
            TestStatus.Skipped => new JunitTestCase(suiteName, className, name, duration.TotalSeconds, JunitOutcome.Skipped, message, normalizedDetails, normalizedDetails),
            TestStatus.Error or TestStatus.Cancelled or TestStatus.InProgress => new JunitTestCase(suiteName, className, name, duration.TotalSeconds, JunitOutcome.Error, message, normalizedDetails, normalizedDetails),
            _ => new JunitTestCase(suiteName, className, name, duration.TotalSeconds, JunitOutcome.Failure, message, normalizedDetails, normalizedDetails)
        };
    }

    private static TestStatus MapValidationStatus(ValidationStatus status)
    {
        return status switch
        {
            ValidationStatus.Passed => TestStatus.Passed,
            ValidationStatus.Failed => TestStatus.Failed,
            _ => TestStatus.Error
        };
    }

    private static string? BuildProtocolDetails(ComplianceTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result, $"Compliance Score: {result.ComplianceScore:F1}%");
        lines.AddRange(result.Violations.Select(violation => $"Violation [{violation.Severity}] {violation.CheckId ?? violation.Rule}: {violation.Description}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildToolDetails(ToolTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result,
            $"Score: {result.Score:F1}%",
            $"Tools Discovered: {result.ToolsDiscovered}",
            $"Tool Pass Count: {result.ToolsTestPassed}",
            $"Tool Fail Count: {result.ToolsTestFailed}");
        lines.AddRange(result.ToolResults.Select(tool => $"Tool {tool.ToolName}: {tool.Status}"));
        lines.AddRange(result.Issues.Select(issue => $"Issue: {issue}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildResourceDetails(ResourceTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result,
            $"Resources Discovered: {result.ResourcesDiscovered}",
            $"Resources Accessible: {result.ResourcesAccessible}",
            $"Resources Failed: {result.ResourcesTestFailed}");
        lines.AddRange(result.ResourceResults.Select(resource => $"Resource {resource.ResourceName ?? resource.ResourceUri}: {resource.Status}"));
        lines.AddRange(result.Issues.Select(issue => $"Issue: {issue}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildPromptDetails(PromptTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result,
            $"Prompts Discovered: {result.PromptsDiscovered}",
            $"Prompts Passed: {result.PromptsTestPassed}",
            $"Prompts Failed: {result.PromptsTestFailed}");
        lines.AddRange(result.PromptResults.Select(prompt => $"Prompt {prompt.PromptName}: {prompt.Status}"));
        lines.AddRange(result.Issues.Select(issue => $"Issue: {issue}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildSecurityDetails(SecurityTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result, $"Security Score: {result.SecurityScore:F1}%");
        lines.AddRange(result.Vulnerabilities.Select(vulnerability => $"Vulnerability [{vulnerability.Severity}] {vulnerability.Id}: {vulnerability.Description}"));
        lines.AddRange(result.SecurityRecommendations.Select(recommendation => $"Recommendation: {recommendation}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildPerformanceDetails(PerformanceTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result,
            $"Average Latency Ms: {result.LoadTesting.AverageResponseTimeMs:F2}",
            $"P95 Latency Ms: {result.LoadTesting.P95ResponseTimeMs:F2}",
            $"Requests Per Second: {result.LoadTesting.RequestsPerSecond:F2}",
            $"Error Rate: {result.LoadTesting.ErrorRate:F2}");
        lines.AddRange(result.PerformanceBottlenecks.Select(bottleneck => $"Bottleneck: {bottleneck}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string? BuildErrorHandlingDetails(ErrorHandlingTestResult? result)
    {
        if (result == null)
        {
            return null;
        }

        var lines = BuildCommonDetailLines(result,
            $"Error Scenarios Tested: {result.ErrorScenariosTestCount}",
            $"Error Scenarios Handled Correctly: {result.ErrorScenariosHandledCorrectly}");
        lines.AddRange(result.ErrorScenarioResults.Select(error =>
            $"Scenario {error.ScenarioName}: {(error.HandledCorrectly ? "Handled correctly" : "Handling failed")}"));
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static List<string> BuildCommonDetailLines(TestResultBase result, params string[] additionalLines)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            lines.Add($"Message: {result.Message}");
        }

        lines.AddRange(additionalLines.Where(line => !string.IsNullOrWhiteSpace(line)));
        lines.AddRange(result.CriticalErrors.Select(error => $"Critical Error: {error}"));
        lines.AddRange(result.Findings.Select(finding => $"Finding [{finding.EffectiveSourceLabel}/{finding.Severity}] {finding.RuleId}: {finding.Summary}"));
        return lines;
    }

    private enum JunitOutcome
    {
        Passed,
        Failure,
        Error,
        Skipped
    }

    private sealed record JunitTestCase(
        string SuiteName,
        string ClassName,
        string Name,
        double TimeSeconds,
        JunitOutcome Outcome,
        string Message,
        string? Details,
        string? SystemOut);
}
