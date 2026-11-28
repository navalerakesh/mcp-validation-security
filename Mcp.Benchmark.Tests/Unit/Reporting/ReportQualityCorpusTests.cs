using System.Text.Json;
using AngleSharp.Html.Parser;
using FluentAssertions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Mcp.Benchmark.Tests.Fixtures;

namespace Mcp.Benchmark.Tests.Unit.Reporting;

public class ReportQualityCorpusTests
{
    [Theory]
    [InlineData("client-unverified")]
    [InlineData("client-undiscovered")]
    public void UnverifiedClientRequirements_ShouldRemainVisibleThroughCanonicalReplay(string name)
    {
        var result = ReportQualityTestData.Create(name);
        var options = Mcp.Benchmark.CLI.Utilities.ArtifactJsonOptions.Create();
        var saved = JsonSerializer.Deserialize<ValidationResult>(JsonSerializer.Serialize(result.CloneWithoutSecrets(), options), options)!;
        var assessment = saved.ClientCompatibility!.Assessments.Single();
        assessment.FailedRequirements.Should().Be(0, "missing inventory is not demonstrated client incompatibility");
        assessment.Requirements.Single(requirement => requirement.RequirementId == "tool-tools-contract")
            .Outcome.Should().Be(ClientProfileRequirementOutcome.Warning);
        foreach (var verbose in new[] { false, true })
        {
            var html = new ValidationReportRenderer().GenerateHtmlReport(saved, saved.ValidationConfig.Reporting, verbose);
            var text = new HtmlParser().ParseDocument(html).Body!.TextContent;
            text.Should().Contain("required compatibility check(s) remain unverified")
                .And.NotContain("Required compatibility checks passed");
        }
    }

    [Fact]
    public void InventoryReason_ShouldExplainRecordedScopeWithoutHidingPolicyFailure()
    {
        var result = ReportQualityTestData.Create("inventory");
        result.PolicyOutcome = new() { Passed = true, RecommendedExitCode = 0 };
        var factory = new ValidationHtmlReportDocumentFactory();
        factory.Create(result, result.ValidationConfig.Reporting, false).DecisionReason.Should()
            .Contain("discovery and static checks").And.NotContain("certificate");
        result.PolicyOutcome = new() { Passed = false, Summary = "Recorded inventory policy failed.", RecommendedExitCode = 1 };
        factory.Create(result, result.ValidationConfig.Reporting, false).DecisionReason.Should().Be("Recorded inventory policy failed.");
    }

    [Theory]
    [InlineData(0, false, "unavailable")]
    [InlineData(0, true, "0")]
    [InlineData(5, false, "5")]
    public void TemplateEnumeration_ShouldNotBeLostOrImplyResourceExecution(int count, bool complete, string expected)
    {
        var result = ReportQualityTestData.Create("sparse");
        result.ResourceTesting = new ResourceTestResult
        {
            ResourceTemplatesDiscovered = count,
            TemplateCatalogEnumeration = complete ? new() { IsComplete = true, Termination = CatalogEnumerationTermination.Complete } : null
        };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.CatalogSummary.Should().Contain($"Resource templates: {expected} (enumeration only; not resource reads)");
    }

    [Theory]
    [InlineData(0, "Unavailable / Unavailable / Unavailable")]
    [InlineData(5, "12.50 ms / 40.00 ms / Unavailable")]
    public void MinimalReport_ShouldShowMeasuredRequestTiming_WithoutInventingMissingSamples(int samples, string expected)
    {
        var result = ReportQualityTestData.Create("sparse");
        result.Run.OperationalMetrics = new()
        {
            TargetLatencySampleCount = samples, TargetLatencyP50Ms = 12.5, TargetLatencyP95Ms = 40
        };
        var html = new ValidationReportRenderer().GenerateHtmlReport(result, result.ValidationConfig.Reporting, false);
        var document = new HtmlParser().ParseDocument(html);
        document.QuerySelectorAll("tr").Single(row => row.TextContent.Contains("Request latency P50 / P95 / P99"))
            .TextContent.Should().Contain(expected);
        document.Body!.TextContent.Should().Contain("not a load-test result").And.Contain("Client comparison");
    }

    [Theory]
    [InlineData("ci", "Selected CI requirements not met")]
    public void Decision_ShouldNotPresentCoreSuccessAsPassingFailedPolicy(string profileId, string title)
    {
        var result = ReportQualityTestData.Create("sparse");
        result.Assurance = new()
        {
            Profile = AssuranceProfileCatalog.Resolve(profileId),
            Assessments = new() { CoreConformance = new() { Status = CoreConformanceStatus.Conformant } },
            CertificateEligibility = new() { ProfileId = profileId, Status = CertificateEligibilityStatus.Eligible }
        };
        result.PolicyOutcome = new() { Passed = false, RecommendedExitCode = 1, Summary = "Recorded deployment policy failed." };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.DecisionTitle.Should().Be(title);
        model.DecisionReason.Should().Be("Recorded deployment policy failed.");
    }

    [Fact]
    public void ProviderDecision_ShouldPreserveIndependentCertificateAndPolicyDispositions()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.Assurance = new()
        {
            Profile = AssuranceProfileCatalog.Provider,
            CertificateEligibility = new() { ProfileId = "provider", Status = CertificateEligibilityStatus.Eligible }
        };
        result.PolicyOutcome = new() { Passed = false, RecommendedExitCode = 1, Summary = "Separate recorded policy failed." };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.DecisionTitle.Should().Be("Provider certificate eligible; issuance not established");
        model.DecisionFacts.Single(item => item.Label == "Policy disposition").Value.Should().Be("Failed");
        model.DecisionReason.Should().Be("Separate recorded policy failed.");
    }

    [Fact]
    public void ActionAnchors_ShouldNotCollideWhenIdentityFieldsContainSeparators()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.Assurance = new AssuranceEvaluation
        {
            Profile = AssuranceProfileCatalog.Inventory,
            CertificateEligibility = new() { ProfileId = "inventory" },
            Claims = new[] { ("a:b", "c"), ("a", "b:c") }.Select(pair => new AssuranceClaim
            {
                ClaimId = pair.Item1, Subject = "component", Dimension = AssuranceDimension.CapabilityContract,
                RequirementLevel = AssuranceRequirementLevel.Should, Authority = AssuranceAuthority.ProviderPolicy,
                Outcome = AssuranceClaimOutcome.ReviewRequired, Source = new() { Revision = "fixture" },
                Summary = "Recorded advice.", Remediation = "Review the contract.", Metadata = new() { ["ruleId"] = pair.Item2 }
            }).ToArray()
        };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.PriorityActions.Should().HaveCount(2);
        model.PriorityActions.Select(action => action.Anchor).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComprehensiveProjection_ShouldLeadWithSelectedScopeAndSeparateCertificateIssuance(bool missingBaseline)
    {
        var result = ReportQualityTestData.Create("sparse");
        result.Assurance = Mcp.Benchmark.Tests.Unit.Services.AssuranceExitCodeMapperTests.CreateEvaluation(
            AssuranceProfileCatalog.Comprehensive, AssuranceClaimOutcome.Violated, AssuranceRequirementLevel.May,
            omittedBaseline: missingBaseline ? "selection" : null);
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "official-conformance", Scope = "optional", Lane = EvaluationLane.ExternalConformance,
            Status = ValidationCoverageStatus.Inconclusive, Reason = "Optional fixture prerequisite missing."
        });
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "protocol", Scope = "required", Status = ValidationCoverageStatus.Covered, ObservedOutcome = ValidationOutcome.Succeeded
        });
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.DecisionTitle.Should().Be(missingBaseline ? "Selected comprehensive assessment is incomplete" : "Selected comprehensive requirements satisfied");
        if (!missingBaseline) model.DecisionReason.Should().Contain("1 of 1").And.NotContain("certificate");
        model.ScopeLimit.Should().NotContain("Optional fixture");
        model.OverviewMetrics.Single(item => item.Label == "Certificate Outcome").Value.Should().Be("Not issued by this profile");
    }

    [Fact]
    public void HistoricalAuthenticationOnlyRows_ShouldNotBecomeExecutionEvidence()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.ToolValidation = new ToolTestResult
        {
            ToolResults =
            [
                new() { ToolName = "Authentication Discovery", ExecutionSuccessful = true, WwwAuthenticateHeader = "Bearer" },
                new() { ToolName = "tools/list (Auth Check)", ExecutionSuccessful = true, Status = TestStatus.AuthRequired }
            ]
        };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.AgentFacts.Single(fact => fact.Label == "Observed").Value.Should().StartWith("Runtime evidence unavailable");
    }

    [Fact]
    public void PaginationAdvice_ShouldUseTypedCapabilityPriorityAndRecordedSeverity()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.ToolValidation = new ToolTestResult
        {
            Findings = [new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.ToolListPaginationRecommended, Component = "tools/list",
                Summary = "Catalog uses many tokens; authentication words must not change its domain.",
                Recommendation = "Provide pagination.", Severity = ValidationFindingSeverity.Low
            }]
        };
        result.Assurance = Mcp.Benchmark.Core.Services.AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);
        var groups = RemediationOrderBuilder.Build(result);
        var group = groups.Single(g => g.Items.Any(item => item.Evidence == ValidationFindingRuleIds.ToolListPaginationRecommended));
        group.Priority.Should().Be(3);
        group.Items.Single(item => item.Evidence == ValidationFindingRuleIds.ToolListPaginationRecommended).Severity.Should().Be("Low");
        group.Impact.Should().NotContain("Auth fixes");
    }

    [Fact]
    public void OptionalExternalFailure_ShouldRemainVisibleWithoutInventedSeverityOrPrimaryRemediation()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.Assurance = new AssuranceEvaluation
        {
            Profile = AssuranceProfileCatalog.Comprehensive, RequiredExtensionIds = [],
            CertificateEligibility = new() { ProfileId = "comprehensive", Status = CertificateEligibilityStatus.Ineligible },
            Claims = [new AssuranceClaim
            {
                ClaimId = "optional-check", Subject = "external-check", Dimension = AssuranceDimension.ExternalConformance,
                RequirementLevel = AssuranceRequirementLevel.May, Authority = AssuranceAuthority.OfficialConformance,
                Outcome = AssuranceClaimOutcome.Violated, Source = new() { Revision = "fixture" }, Summary = "Optional check failed."
            }]
        };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        var action = model.PriorityActions.Should().ContainSingle().Subject;
        action.Priority.Should().Be(5);
        action.Kind.Should().Be("Optional review");
        action.Item.Severity.Should().Be("Not recorded");
        action.Claims.Should().ContainSingle().Which.Claim.Outcome.Should().Be(AssuranceClaimOutcome.Violated);
    }

    [Fact]
    public void ActionList_ShouldRetainAllComponentsEvenWhenAdviceMatchesAndGroupExceedsFive()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.Assurance = new AssuranceEvaluation
        {
            Profile = AssuranceProfileCatalog.Inventory,
            CertificateEligibility = new() { ProfileId = "inventory" },
            Claims = Enumerable.Range(0, 7).Select(index => new AssuranceClaim
            {
                ClaimId = $"finding-{index}", Subject = $"component-{index}", Dimension = AssuranceDimension.CapabilityContract,
                RequirementLevel = AssuranceRequirementLevel.Should, Authority = AssuranceAuthority.ProviderPolicy,
                Outcome = AssuranceClaimOutcome.ReviewRequired, Source = new() { Revision = "fixture" },
                Summary = $"Review component {index}.", Remediation = "Provide accurate metadata.",
                Metadata = new() { ["ruleId"] = $"rule-{index}", ["severity"] = "Low" }
            }).ToArray()
        };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.PriorityActions.Should().HaveCount(7);
        model.PriorityActions.Select(action => action.Item.Component).Should().OnlyHaveUniqueItems();
        foreach (var action in model.PriorityActions)
            action.Claims.Should().ContainSingle().Which.Claim.Subject.Should().Be(action.Item.Component);
    }

    [Fact]
    public void ActionKind_UsesItsComponentEvidence_NotAnotherComponentSharingTheRule()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.Assurance = new AssuranceEvaluation
        {
            Profile = AssuranceProfileCatalog.Inventory,
            CertificateEligibility = new CertificateEligibilityAssessment { ProfileId = "inventory" },
            Claims = new[] { AssuranceClaimOutcome.Violated, AssuranceClaimOutcome.Incomplete }.Select((outcome, index) => new AssuranceClaim
            {
                ClaimId = $"action-{index}", Subject = $"tool-{index}", Dimension = AssuranceDimension.CapabilityContract,
                Outcome = outcome, RequirementLevel = AssuranceRequirementLevel.Must, Authority = AssuranceAuthority.OfficialSpecification,
                Source = new AssuranceSourceReference { Revision = "fixture" }, Summary = $"Recorded issue {index}.",
                Remediation = $"Verify component {index}.", Metadata = new() { ["ruleId"] = "shared-rule" }
            }).ToArray()
        };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.PriorityActions.Single(action => action.Item.Component == "tool-0").Kind.Should().Be("Remediation");
        model.PriorityActions.Single(action => action.Item.Component == "tool-1").Kind.Should().Be("Collect evidence");
        foreach (var action in model.PriorityActions)
            action.Claims.Should().ContainSingle().Which.Claim.Subject.Should().Be(action.Item.Component);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MastheadAndAdvertisement_UseEncodedRecordedHandshakeMetadata(bool advertisesTools)
    {
        var result = ReportQualityTestData.Create("sparse");
        result.InitializationHandshake = new TransportResult<ModelContextProtocol.Protocol.InitializeResult>
        {
            IsSuccessful = true,
            Payload = new()
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new() { Name = "<b>Agent gateway</b>", Version = "fixture" },
                Capabilities = advertisesTools ? new() { Tools = new() } : new()
            }
        };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.TargetName.Should().Be("<b>Agent gateway</b>");
        model.AgentFacts[0].Value.Should().Be(advertisesTools ? "tools" : "No capabilities advertised in the recorded declaration.");
        var html = new ValidationReportRenderer().GenerateHtmlReport(result, result.ValidationConfig.Reporting, false);
        var document = new HtmlParser().ParseDocument(html);
        document.QuerySelector("h1")!.TextContent.Should().Be(model.TargetName);
        document.QuerySelector("h1 b").Should().BeNull();
    }

    [Fact]
    public void AgentCard_DistinguishesUnrecordedAndKnownEmptyCatalogs()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.ToolValidation = new ToolTestResult();
        var factory = new ValidationHtmlReportDocumentFactory();
        factory.Create(result, result.ValidationConfig.Reporting, false).CatalogSummary.Should().Contain("Tools: unavailable");
        result.ToolValidation.CatalogEnumeration = new CatalogEnumerationEvidence
        {
            IsComplete = true, Termination = CatalogEnumerationTermination.Complete
        };
        factory.Create(result, result.ValidationConfig.Reporting, false).CatalogSummary.Should().Contain("Tools: 0;");
        result.ToolValidation = null;
        result.CapabilitySnapshot = new TransportResult<CapabilitySummary>
        {
            Payload = new CapabilitySummary { ToolListingSucceeded = true, DiscoveredToolsCount = 2 }
        };
        factory.Create(result, result.ValidationConfig.Reporting, false).CatalogSummary.Should().Contain("Tools: 2 (snapshot)");
    }

    [Fact]
    public void AgentCard_RetainsLaterExecutionAndIndependentSnapshotFailure()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.ToolValidation = new ToolTestResult
        {
            ToolResults = [new IndividualToolResult { ToolName = "fixture", ExecutionSuccessful = true }]
        };
        result.CapabilitySnapshot = new TransportResult<CapabilitySummary>
        {
            Payload = new CapabilitySummary { ToolInvocationAttempted = true, ToolInvocationSucceeded = false }
        };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.AgentFacts[2].Value.Should().Contain("Successful tool-execution result records: 1")
            .And.Contain("Tool invocation failed in the capability snapshot");
    }

    [Fact]
    public void SupportingCards_UseExistingRanking_AndOmitEmptyAction()
    {
        foreach (var name in ReportQualityTestData.Names)
        {
            var result = ReportQualityTestData.Create(name);
            var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, true);
            var html = new ValidationReportRenderer().GenerateHtmlReport(result, result.ValidationConfig.Reporting, true);
            var document = new HtmlParser().ParseDocument(html);
            model.PriorityActions.Select(action => action.Item).Should().Equal(model.RemediationOrder.SelectMany(group => group.Items));
            document.QuerySelectorAll(".support-card").Should().HaveCount(model.PriorityActions.Count == 0 ? 1 : 2);
            if (model.PriorityActions.FirstOrDefault() is { } action)
            {
                document.QuerySelector(".next-action-link")!.GetAttribute("href").Should().Be("#" + action.Anchor);
                document.GetElementById(action.Anchor)!.QuerySelector("h3")!.TextContent.Should().Be(action.Item.Remediation);
            }
            document.QuerySelectorAll(".agent-facts dt").Select(item => item.TextContent)
                .Should().Equal("Advertised", "Permitted", "Observed");
        }
    }

    [Fact]
    public void AgentCard_DoesNotInferAdvertisementOrExecutionFromCatalogCounts()
    {
        var result = ReportQualityTestData.Create("inventory");
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, true);
        model.AgentFacts[0].Value.Should().StartWith("Unavailable");
        model.AgentFacts[1].Value.Should().Contain("Safe mode");
        model.AgentFacts[2].Value.Should().Contain("Skipped");
        model.CatalogSummary.Should().Contain("Tools: 3");
    }

    [Theory]
    [InlineData(false, false, "No tool invocation")]
    [InlineData(true, false, "Tool invocation failed")]
    [InlineData(true, true, "Tool invocation succeeded")]
    public void AgentCard_RetainsRecordedSnapshotOutcome(bool attempted, bool succeeded, string expected)
    {
        var result = ReportQualityTestData.Create("sparse");
        result.CapabilitySnapshot = new TransportResult<CapabilitySummary>
        {
            IsSuccessful = true,
            Payload = new CapabilitySummary
            {
                CapabilityDeclarationsAvailable = true, AdvertisedCapabilities = ["tools", "resources"],
                ToolInvocationAttempted = attempted, ToolInvocationSucceeded = succeeded
            }
        };
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        model.AgentFacts[0].Value.Should().Be("resources, tools");
        model.AgentFacts[2].Value.Should().StartWith(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ValidationOutcome.NotEvaluated)]
    [InlineData(ValidationOutcome.Running)]
    [InlineData(ValidationOutcome.Failed)]
    [InlineData(ValidationOutcome.Skipped)]
    [InlineData(ValidationOutcome.AuthRequired)]
    [InlineData(ValidationOutcome.Unavailable)]
    [InlineData(ValidationOutcome.Blocked)]
    public void UnfinishedExecution_CannotPresentCompletedAssurance(ValidationOutcome? outcome)
    {
        var result = ReportQualityTestData.Create("sparse");
        result.Run.TerminationOutcome = outcome;
        var document = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        document.DecisionTitle.Should().Be(outcome == null
            ? "Execution outcome not recorded" : "Execution not completed; assessment is limited");
        document.DecisionFacts[0].Value.Should().Be(outcome?.ToString() ?? "Not recorded");
    }

    [Fact]
    public void MissingTimeAndBlankReason_DoNotBecomeInventedRecordedFacts()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.StartTime = default;
        result.EndTime = null;
        result.Assurance = new AssuranceEvaluation
        {
            Profile = AssuranceProfileCatalog.Inventory,
            CertificateEligibility = new CertificateEligibilityAssessment
            {
                ProfileId = "inventory", Status = CertificateEligibilityStatus.Ineligible, Reasons = [" "]
            }
        };
        var document = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, false);
        document.GeneratedAtLabel.Should().Be("Recorded run: unavailable");
        document.DecisionReason.Should().Be("No certificate-eligibility explanation was recorded for this profile.");
        document.ScopeLimit.Should().Contain("No coverage declarations");
        document.DomainSummaries.Single(summary => summary.Domain == "Coverage").Tone.Should().Be(HtmlReportTone.Neutral);
    }

    [Fact]
    public void Opening_HasFixedOrder_AndDoesNotChangeWithDetail()
    {
        var renderer = new ValidationReportRenderer();
        foreach (var name in ReportQualityTestData.Names)
        {
            var result = ReportQualityTestData.Create(name);
            string? decision = null;
            foreach (var verbose in new[] { false, true })
            {
                var html = renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose);
                var document = new HtmlParser().ParseDocument(html);
                var ids = new[] { "assessment-decision", "evidence-and-actions", "priority-actions", "technical-evidence", "method-and-provenance", "recorded-claims" };
                ids.Select(id => html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal)).Should().BeInAscendingOrder();
                var opening = document.GetElementById("assessment-decision")!;
                opening.QuerySelectorAll("dt").Take(6).Select(item => item.TextContent).Should().Equal(
                    "Execution", "Assurance", "Policy disposition", "Certificate", "Profile and revision", "Execution scope");
                if (decision != null) opening.TextContent.Should().Be(decision);
                decision = opening.TextContent;
                if (name == "interrupted") document.GetElementById("assessment-decision-title")!.TextContent.Should().Contain("interrupted");
                else document.GetElementById("assessment-decision-title")!.TextContent.Should().NotContain("interrupted");
                document.QuerySelectorAll(".scope-limit").Should().ContainSingle();
                document.QuerySelectorAll("footer").Should().ContainSingle();
            }
        }
    }

    [Fact]
    public void EvidenceLinks_PreserveDuplicateClaimIdentities_AndReachExactObservation()
    {
        var result = ReportQualityTestData.Create("sparse");
        result.Assurance = new AssuranceEvaluation
        {
            Profile = AssuranceProfileCatalog.Inventory,
            CertificateEligibility = new CertificateEligibilityAssessment
            {
                ProfileId = "inventory", Status = CertificateEligibilityStatus.Ineligible
            },
            Claims = new[] { "first component", "second component" }.Select((subject, index) => new AssuranceClaim
            {
                ClaimId = "same-id", Subject = subject, Dimension = (AssuranceDimension)index,
                Outcome = AssuranceClaimOutcome.Incomplete, Authority = AssuranceAuthority.ProviderPolicy,
                RequirementLevel = AssuranceRequirementLevel.Policy, Source = new AssuranceSourceReference { Revision = "fixture" },
                Summary = "Synthetic unavailable evidence.", EvidenceIds = ["observed-id"], Remediation = "Collect the missing evidence."
            }).ToArray()
        };
        result.Evidence.Observations.Add(new ValidationObservation
        {
            Id = "observed-id", LayerId = "fixture", Component = "first component",
            ObservationKind = "synthetic", RedactedPayloadPreview = "Redacted synthetic observation."
        });
        var model = new ValidationHtmlReportDocumentFactory().Create(result, result.ValidationConfig.Reporting, true);
        foreach (var action in model.PriorityActions)
            action.Claims.Should().ContainSingle().Which.Claim.Subject.Should().Be(action.Item.Component);
        var html = new ValidationReportRenderer().GenerateHtmlReport(result, result.ValidationConfig.Reporting, true);
        var document = new HtmlParser().ParseDocument(html);
        document.QuerySelectorAll("[id]").Select(element => element.Id).Should().OnlyHaveUniqueItems();
        foreach (var link in document.QuerySelectorAll("a[href^='#']"))
            document.GetElementById(link.GetAttribute("href")![1..]).Should().NotBeNull();
        document.QuerySelectorAll("article[data-claim-id]").Should().HaveCount(2);
        foreach (var row in document.QuerySelectorAll("tr[id^='domain-claim-']"))
        {
            var claim = document.GetElementById(row.QuerySelector("a")!.GetAttribute("href")![1..])!;
            claim.QuerySelector("h3")!.TextContent.Should().Be(row.QuerySelector("th")!.TextContent);
            var observationLink = claim.QuerySelector("a[href^='#observation-']")!;
            document.GetElementById(observationLink.GetAttribute("href")![1..])!.TextContent.Should().Contain("Redacted synthetic observation.");
        }
    }

    [Fact]
    public void SyntheticCorpus_IsDeterministic_AndCanBeExportedForCliReview()
    {
        var output = Environment.GetEnvironmentVariable("MCPVAL_REPORT_CORPUS");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "corpus.txt"), string.Join("\n", ReportQualityTestData.Names) + "\n");
        }
        var renderer = new ValidationReportRenderer();
        var options = Mcp.Benchmark.CLI.Utilities.ArtifactJsonOptions.Create();
        foreach (var name in ReportQualityTestData.Names)
        {
            var result = ReportQualityTestData.Create(name);
            var json = JsonSerializer.Serialize(result.CloneWithoutSecrets(), options);
            json.Should().Be(JsonSerializer.Serialize(ReportQualityTestData.Create(name).CloneWithoutSecrets(), options));
            var saved = JsonSerializer.Deserialize<Mcp.Benchmark.Core.Models.ValidationResult>(json, options)!;
            var factory = new ValidationHtmlReportDocumentFactory();
            factory.Create(saved, saved.ValidationConfig.Reporting, false).DecisionTitle
                .Should().Be(factory.Create(result, result.ValidationConfig.Reporting, false).DecisionTitle,
                    "offline reading must use recorded execution rather than a nonserialized default status");
            foreach (var verbose in new[] { false, true })
                renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose)
                    .Should().Be(renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose));
            if (output != null) File.WriteAllText(Path.Combine(output, $"{name}.json"), json);
        }

    }

    [Theory]
    [InlineData("ci", 0)]
    [InlineData("comprehensive", 0)]
    [InlineData("comprehensive-failed", 1)]
    [InlineData("comprehensive-missing", 2)]
    public void SavedProfileControls_ShouldRetainDistinctGateAndDiagnosticRoles(string name, int exit)
    {
        var result = ReportQualityTestData.Create(name);
        Mcp.Benchmark.Core.Services.AssuranceExitCodeMapper.Map(result).Should().Be(exit);
        if (name == "comprehensive")
        {
            var renderer = new ValidationReportRenderer();
            var junit = System.Xml.Linq.XDocument.Parse(renderer.GenerateJunitReport(result));
            junit.Descendants("testsuite").Single(suite => (string?)suite.Attribute("name") == "assurance-externalconformance")
                .Descendants("failure").Should().NotBeEmpty("optional diagnostic failures are not the selected-profile gate");
            var html = renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, false);
            html.Should().Contain("Selected comprehensive requirements satisfied").And.Contain("Optional review");
        }
    }
}
