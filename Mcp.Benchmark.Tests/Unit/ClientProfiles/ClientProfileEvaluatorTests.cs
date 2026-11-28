using Mcp.Benchmark.ClientProfiles;
using Mcp.Benchmark.Core.Models;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Tests.Unit.ClientProfiles;

public class ClientProfileEvaluatorTests
{
    private readonly ClientProfileEvaluator _evaluator = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_CallContracts_ShouldRequirePerItemExecutionEvidence(bool completed)
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults.Single().ExecutionSuccessful = completed;
        result.PromptTesting = new()
        {
            Status = TestStatus.Passed, PromptsDiscovered = 1,
            PromptResults = [new() { PromptName = "review", Status = TestStatus.Passed, ExecutionSuccessful = completed }]
        };
        result.ResourceTesting = new()
        {
            Status = TestStatus.Passed, ResourcesDiscovered = 1,
            TemplateCatalogEnumeration = CompletedEmptyCatalog(),
            ResourceResults = [new() { ResourceUri = "file:///review", Status = TestStatus.Passed, AccessSuccessful = completed }]
        };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["claude-code"] })!.Assessments.Single();
        foreach (var id in new[] { "tool-tools-contract", "prompt-prompts-contract", "resource-resources-contract" })
            assessment.Requirements.Single(requirement => requirement.RequirementId == id).Outcome.Should()
                .Be(completed ? ClientProfileRequirementOutcome.Satisfied : ClientProfileRequirementOutcome.Warning);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(CatalogEnumerationTermination.AuthenticationRequired, false)]
    [InlineData(CatalogEnumerationTermination.Complete, true)]
    public void Evaluate_CompletedStaticReads_ShouldNotHideUnknownTemplateDiscovery(
        CatalogEnumerationTermination? termination, bool satisfied)
    {
        var result = CreateToolOnlyValidationResult();
        var templates = termination is null ? null : CompletedEmptyCatalog();
        if (templates is not null)
        {
            templates.Termination = termination!.Value;
            templates.IsComplete = satisfied;
        }
        result.ResourceTesting = new()
        {
            Status = TestStatus.Passed, ResourcesDiscovered = 1, TemplateCatalogEnumeration = templates,
            ResourceResults = [new() { ResourceUri = "file:///review", Status = TestStatus.Passed, AccessSuccessful = true }]
        };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["claude-code"] })!.Assessments.Single();
        assessment.Requirements.Single(requirement => requirement.RequirementId == "resource-resources-contract")
            .Outcome.Should().Be(satisfied ? ClientProfileRequirementOutcome.Satisfied : ClientProfileRequirementOutcome.Warning);
    }

    [Theory]
    [InlineData("""{"result":{"tools":[],"nextCursor":"page2"}}""")]
    [InlineData("""{"result":{"tools":42}}""")]
    [InlineData("""{"result":{"tools":[]}}""")]
    public void Evaluate_SnapshotAlone_ShouldNotCertifyAnEmptyCatalog(string raw)
    {
        var result = new ValidationResult
        {
            CapabilitySnapshot = new()
            {
                Payload = new()
                {
                    CapabilityDeclarationsAvailable = true, AdvertisedCapabilities = ["tools"],
                    ToolListingSucceeded = true, ToolListResponse = new() { IsSuccess = true, RawJson = raw }
                }
            }
        };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] })!.Assessments.Single();
        foreach (var id in new[] { "tool-tools-present", "tool-tools-contract" })
            assessment.Requirements.Single(requirement => requirement.RequirementId == id).Outcome.Should()
                .Be(ClientProfileRequirementOutcome.Warning, "snapshot success is not bounded complete-enumeration evidence");
    }

    [Fact]
    public void Evaluate_RejectedTemplate_ShouldNotEarnSurfaceCreditOrHideItsContractFailure()
    {
        var result = new ValidationResult
        {
            ToolValidation = new() { CatalogEnumeration = CompletedEmptyCatalog() },
            PromptTesting = new() { CatalogEnumeration = CompletedEmptyCatalog() },
            ResourceTesting = new()
            {
                Status = TestStatus.Failed, CatalogEnumeration = CompletedEmptyCatalog(), ResourceTemplatesDiscovered = 1,
                TemplateCatalogEnumeration = new()
                {
                    IsComplete = true, Termination = CatalogEnumerationTermination.Complete,
                    PageAttempts = 1, ParsedPages = 1, RawItemsObserved = 1, RejectedItems = 1
                },
                Findings = [new()
                {
                    RuleId = ValidationFindingRuleIds.ResourceTemplateListItemInvalid,
                    Component = "resources/templates/list", Severity = ValidationFindingSeverity.High,
                    Summary = "Template entry is not a metadata object."
                }]
            }
        };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["claude-code"] })!.Assessments.Single();
        assessment.Requirements.Single(requirement => requirement.RequirementId == "interactive-surfaces")
            .Outcome.Should().Be(ClientProfileRequirementOutcome.Warning);
        assessment.Requirements.Single(requirement => requirement.RequirementId == "resource-resources-contract")
            .Outcome.Should().Be(ClientProfileRequirementOutcome.Failed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TestStatus.AuthRequired)]
    [InlineData(TestStatus.Skipped)]
    [InlineData(TestStatus.Error)]
    [InlineData(TestStatus.Passed)]
    public void Evaluate_UnobservedCatalogs_ShouldNotClaimAbsentCapabilities(TestStatus? status)
    {
        var result = new ValidationResult();
        if (status is { } recorded)
        {
            result.ToolValidation = new() { Status = recorded };
            result.PromptTesting = new() { Status = recorded };
            result.ResourceTesting = new() { Status = recorded };
        }

        var compatibility = _evaluator.Evaluate(result, new() { Profiles = ["all"] })!;
        var surfaceRequirements = new[]
        {
            "interactive-surfaces", "tool-tools-present", "tool-tools-contract",
            "prompt-prompts-contract", "resource-resources-contract"
        };
        foreach (var assessment in compatibility.Assessments)
        {
            assessment.FailedRequirements.Should().Be(0, "unavailable discovery is not a demonstrated incompatibility");
            assessment.Requirements.Where(requirement => surfaceRequirements.Contains(requirement.RequirementId))
                .Should().NotBeEmpty().And.OnlyContain(requirement =>
                    requirement.Outcome == ClientProfileRequirementOutcome.Warning &&
                    !string.IsNullOrWhiteSpace(requirement.Recommendation));
            assessment.Summary.Should().Contain("required compatibility check(s) remain unverified");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_EmptyCatalogs_ShouldRequireEnumerationRatherThanSnapshotCounts(bool useSnapshot)
    {
        var result = new ValidationResult();
        if (useSnapshot)
        {
            result.CapabilitySnapshot = new()
            {
                IsSuccessful = true,
                Payload = new() { ToolListingSucceeded = true, PromptListingSucceeded = true, ResourceListingSucceeded = true }
            };
            result.ResourceTesting = new() { TemplateCatalogEnumeration = CompletedEmptyCatalog() };
        }
        else
        {
            result.ToolValidation = new() { CatalogEnumeration = CompletedEmptyCatalog() };
            result.PromptTesting = new() { CatalogEnumeration = CompletedEmptyCatalog() };
            result.ResourceTesting = new()
            {
                CatalogEnumeration = CompletedEmptyCatalog(), TemplateCatalogEnumeration = CompletedEmptyCatalog()
            };
        }

        var compatibility = _evaluator.Evaluate(result, new() { Profiles = ["all"] })!;
        foreach (var assessment in compatibility.Assessments)
        {
            assessment.Requirements.Where(requirement => requirement.RequirementId.EndsWith("-contract", StringComparison.Ordinal))
                .Should().OnlyContain(requirement => requirement.Outcome == (useSnapshot
                    ? ClientProfileRequirementOutcome.Warning : ClientProfileRequirementOutcome.NotApplicable));
            assessment.Requirements.Where(requirement => requirement.RequirementId is "interactive-surfaces" or "tool-tools-present")
                .Should().ContainSingle().Which.Outcome.Should().Be(useSnapshot
                    ? ClientProfileRequirementOutcome.Warning : ClientProfileRequirementOutcome.Failed);
        }
    }

    [Fact]
    public void Evaluate_RejectedCatalogItems_ShouldNotEstablishAnEmptySurface()
    {
        var enumeration = CompletedEmptyCatalog();
        enumeration.RawItemsObserved = 1;
        enumeration.RejectedItems = 1;
        var result = new ValidationResult { ToolValidation = new() { CatalogEnumeration = enumeration } };

        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] })!.Assessments.Single();
        assessment.Requirements.Single(requirement => requirement.RequirementId == "tool-tools-present")
            .Outcome.Should().Be(ClientProfileRequirementOutcome.Warning);
        assessment.Requirements.Single(requirement => requirement.RequirementId == "tool-tools-contract")
            .Outcome.Should().Be(ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_PartialCatalog_ShouldNotClaimCompleteMetadataForRecoveredTools()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.CatalogEnumeration = new()
        {
            Termination = CatalogEnumerationTermination.BudgetExceeded, PageAttempts = 1, ParsedPages = 1,
            RawItemsObserved = 1, RecoveredItems = 1, ContinuationObserved = true
        };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] })!.Assessments.Single();
        foreach (var id in new[] { "tool-tools-contract", "tool-tool-descriptions", "tool-tool-annotations" })
            assessment.Requirements.Single(requirement => requirement.RequirementId == id)
                .Outcome.Should().Be(ClientProfileRequirementOutcome.Warning);
    }

    [Theory]
    [InlineData(false, false, ClientProfileRequirementOutcome.Warning)]
    [InlineData(true, false, ClientProfileRequirementOutcome.Failed)]
    [InlineData(true, true, ClientProfileRequirementOutcome.Warning)]
    public void Evaluate_DeclaredCapabilityAbsence_ShouldRequireRecordedDeclarations(
        bool declarationsAvailable, bool toolsAdvertised, ClientProfileRequirementOutcome expected)
    {
        var result = new ValidationResult
        {
            CapabilitySnapshot = new()
            {
                Payload = new()
                {
                    CapabilityDeclarationsAvailable = declarationsAvailable,
                    AdvertisedCapabilities = toolsAdvertised ? ["tools"] : []
                }
            }
        };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] })!.Assessments.Single();
        assessment.Requirements.Single(requirement => requirement.RequirementId == "tool-tools-present")
            .Outcome.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_KnownContractFailure_ShouldSurviveZeroRecoveredComponents()
    {
        var result = CreateToolOnlyValidationResult();
        result.PromptTesting = new()
        {
            Status = TestStatus.Failed,
            Findings = [new()
            {
                RuleId = ValidationFindingRuleIds.PromptMissingName, Severity = ValidationFindingSeverity.High,
                Component = "prompts/list", Summary = "A recorded prompt entry has no name."
            }]
        };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["claude-code"] })!.Assessments.Single();
        var requirement = assessment.Requirements.Single(requirement => requirement.RequirementId == "prompt-prompts-contract");
        requirement.Outcome.Should().Be(ClientProfileRequirementOutcome.Failed);
        requirement.RuleIds.Should().Contain(ValidationFindingRuleIds.PromptMissingName);
    }

    [Fact]
    public void Evaluate_RecordedSnapshotCounts_ShouldEstablishPresenceButNotContractSuccess()
    {
        var result = new ValidationResult
        {
            CapabilitySnapshot = new()
            {
                Payload = new() { ToolListingSucceeded = true, DiscoveredToolsCount = 2 }
            }
        };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] })!.Assessments.Single();
        assessment.Requirements.Single(requirement => requirement.RequirementId == "tool-tools-present")
            .Outcome.Should().Be(ClientProfileRequirementOutcome.Satisfied);
        assessment.Requirements.Single(requirement => requirement.RequirementId == "tool-tools-contract")
            .Outcome.Should().Be(ClientProfileRequirementOutcome.Warning);
    }

    private static CatalogEnumerationEvidence CompletedEmptyCatalog() => new()
    {
        IsComplete = true, Termination = CatalogEnumerationTermination.Complete, PageAttempts = 1, ParsedPages = 1
    };

    [Theory]
    [InlineData(null, ClientProfileRequirementOutcome.Warning, ClientProfileRequirementOutcome.Warning)]
    [InlineData(0, ClientProfileRequirementOutcome.Failed, ClientProfileRequirementOutcome.NotApplicable)]
    [InlineData(2, ClientProfileRequirementOutcome.Satisfied, ClientProfileRequirementOutcome.Warning)]
    public void Evaluate_ResourceTemplates_ShouldNotBeLostOrCountedAsReadContracts(
        int? templates, ClientProfileRequirementOutcome surfaceOutcome, ClientProfileRequirementOutcome contractOutcome)
    {
        var result = new ValidationResult
        {
            ToolValidation = new() { CatalogEnumeration = CompletedEmptyCatalog() },
            PromptTesting = new() { CatalogEnumeration = CompletedEmptyCatalog() },
            ResourceTesting = new() { CatalogEnumeration = CompletedEmptyCatalog() }
        };
        if (templates is { } count)
        {
            result.ResourceTesting.ResourceTemplatesDiscovered = count;
            result.ResourceTesting.TemplateCatalogEnumeration = CompletedEmptyCatalog();
            result.ResourceTesting.TemplateCatalogEnumeration.RawItemsObserved = count;
            result.ResourceTesting.TemplateCatalogEnumeration.RecoveredItems = count;
        }
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["claude-code"] })!.Assessments.Single();
        var surface = assessment.Requirements.Single(requirement => requirement.RequirementId == "interactive-surfaces");
        surface.Outcome.Should().Be(surfaceOutcome);
        if (templates > 0) surface.Summary.Should().Contain("2 resource template(s)");
        assessment.Requirements.Single(requirement => requirement.RequirementId == "resource-resources-contract")
            .Outcome.Should().Be(contractOutcome);
    }

    [Fact]
    public void Evaluate_ShouldReplaceOnlyItsPriorInterpretationRevisionAndRemainIdempotent()
    {
        var result = CreateToolOnlyValidationResult();
        var baseline = new ValidationCoverageDeclaration
        {
            LayerId = "protocol", Scope = "recorded", Status = ValidationCoverageStatus.Covered
        };
        result.Evidence.Coverage.Add(baseline);
        result.Evidence.AppliedPacks.Add(new()
        {
            Key = new("client-profile-pack/built-in"), Kind = ValidationPackKind.ClientProfilePack,
            Revision = new("2026-04"), DisplayName = "Historical interpretation", Stability = ValidationStability.Stable
        });
        var neutralAssurance = result.Assurance;

        _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] });
        _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] });

        result.Evidence.AppliedPacks.Where(pack => pack.Key.Value == "client-profile-pack/built-in")
            .Should().ContainSingle().Which.Revision.Value.Should().Be(ClientProfileCatalog.InterpretationRevision);
        result.Evidence.Coverage.Should().Contain(baseline);
        result.Evidence.Coverage.Count(item => item.LayerId == "client-profiles").Should().Be(1);
        result.Assurance.Should().BeSameAs(neutralAssurance);
    }

    [Theory]
    [InlineData(TestStatus.Passed, true)]
    [InlineData(TestStatus.Skipped, false)]
    [InlineData(TestStatus.AuthRequired, false)]
    [InlineData(TestStatus.Error, false)]
    public void Evaluate_ShouldNotCreditUncompletedContractEvidence(TestStatus status, bool satisfied)
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.Status = status;
        result.PromptTesting = new()
        {
            PromptsDiscovered = 1, Status = status,
            PromptResults = [new() { PromptName = "review", Status = status, ExecutionSuccessful = satisfied }]
        };
        result.ResourceTesting = new()
        {
            ResourcesDiscovered = 1, Status = status,
            TemplateCatalogEnumeration = CompletedEmptyCatalog(),
            ResourceResults = [new() { ResourceUri = "file:///review", Status = status, AccessSuccessful = satisfied }]
        };

        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["claude-code"] })!.Assessments.Single();

        foreach (var id in new[] { "tool-tools-contract", "prompt-prompts-contract", "resource-resources-contract" })
            assessment.Requirements.Single(requirement => requirement.RequirementId == id).Outcome.Should().Be(
                satisfied ? ClientProfileRequirementOutcome.Satisfied : ClientProfileRequirementOutcome.Warning);
        if (!satisfied)
            assessment.Summary.Should().Contain("required compatibility check(s) remain unverified")
                .And.NotContain("Required compatibility checks passed");
    }

    [Fact]
    public void Evaluate_CountWithoutMetadata_ShouldNotAssertEveryToolIsDocumented()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation = new() { ToolsDiscovered = 2, Status = TestStatus.Passed };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["claude-code"] })!.Assessments.Single();

        foreach (var id in new[] { "tool-tools-contract", "tool-tool-descriptions", "tool-tool-titles", "tool-tool-annotations", "tool-destructive-hint", "tool-description-size" })
        {
            var requirement = assessment.Requirements.Single(requirement => requirement.RequirementId == id);
            requirement.Outcome.Should().Be(ClientProfileRequirementOutcome.Warning);
            requirement.ExampleComponents.Should().BeEmpty("unrecorded tool identities must not be invented");
        }
    }

    [Theory]
    [InlineData("author_search", false)]
    [InlineData("authenticate_user", true)]
    [InlineData("schema compliance helper", true)]
    [InlineData("Authentication Discovery", true)]
    public void Evaluate_ShouldNotDiscardObservedToolsBasedOnWordsInTheirNames(string name, bool catalogRecorded)
    {
        var result = CreateToolOnlyValidationResult();
        var tools = result.ToolValidation!;
        tools.ToolsDiscovered = 0;
        tools.DiscoveredToolNames.Clear();
        if (catalogRecorded) tools.DiscoveredToolNames.Add(name);
        tools.ToolResults.Single().ToolName = name;
        tools.ToolResults.Single().Description = null;

        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] })!.Assessments.Single();
        var description = assessment.Requirements.Single(requirement => requirement.RequirementId == "tool-tool-descriptions");

        description.Outcome.Should().Be(ClientProfileRequirementOutcome.Warning);
        description.ExampleComponents.Should().ContainSingle().Which.Should().Be(name);
    }

    [Fact]
    public void Evaluate_ShouldPreserveCaseDistinctToolIdentities()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation = new()
        {
            Status = TestStatus.Passed,
            DiscoveredToolNames = ["author_search", "AUTHOR_SEARCH"],
            ToolResults = [new() { ToolName = "author_search" }, new() { ToolName = "AUTHOR_SEARCH" }]
        };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] })!.Assessments.Single();
        var description = assessment.Requirements.Single(requirement => requirement.RequirementId == "tool-tool-descriptions");

        description.Outcome.Should().Be(ClientProfileRequirementOutcome.Warning);
        description.Summary.Should().StartWith("2/2");
        description.ExampleComponents.Should().Equal("author_search", "AUTHOR_SEARCH");

        foreach (var name in result.ToolValidation.DiscoveredToolNames)
            result.ToolValidation.Findings.Add(new()
            {
                RuleId = ValidationFindingRuleIds.ToolCallMissingContentArray, Component = name,
                Summary = "Recorded contract failure.", Severity = ValidationFindingSeverity.High
            });
        var failed = _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] })!.Assessments.Single()
            .Requirements.Single(requirement => requirement.RequirementId == "tool-tools-contract");
        failed.Outcome.Should().Be(ClientProfileRequirementOutcome.Failed);
        failed.ExampleComponents.Should().Equal("author_search", "AUTHOR_SEARCH");
    }

    [Fact]
    public void Evaluate_ShouldNotPromoteHistoricalAuthPlaceholdersIntoToolEvidence()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation = new()
        {
            ToolResults = [new() { ToolName = "Authentication Discovery", WwwAuthenticateHeader = "Bearer" }]
        };
        var assessment = _evaluator.Evaluate(result, new() { Profiles = ["github-copilot-cli"] })!.Assessments.Single();
        var description = assessment.Requirements.Single(requirement => requirement.RequirementId == "tool-tool-descriptions");
        description.Outcome.Should().Be(ClientProfileRequirementOutcome.Warning,
            "an authentication placeholder neither represents a tool nor proves an empty catalog");
        description.ExampleComponents.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_WithoutExplicitProfiles_ShouldDefaultToAllSupportedProfiles()
    {
        var result = CreateToolOnlyValidationResult();

        var compatibility = _evaluator.Evaluate(result, options: null);

        compatibility.Should().NotBeNull();
        compatibility!.Assessments.Select(assessment => assessment.ProfileId)
            .Should().Equal(ClientProfileCatalog.SupportedProfileIds);
    }

    [Fact]
    public void Evaluate_WithAllProfiles_ShouldReturnCatalogProfilesInOrder()
    {
        var result = CreateToolOnlyValidationResult();

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { ClientProfileCatalog.AllProfilesToken }
        });

        compatibility.Should().NotBeNull();
        compatibility!.Assessments.Select(assessment => assessment.ProfileId)
            .Should().Equal(ClientProfileCatalog.SupportedProfileIds);
        // Minimal fixture exposes one tool with no resources/prompts; the comprehensive
        // per-client checks will satisfy required gates but may surface advisory warnings
        // (e.g. extra surface absent, listChanged not advertised). No profile should be
        // Incompatible against this baseline fixture.
        compatibility.Assessments.Should().OnlyContain(assessment =>
            assessment.Status == ClientProfileCompatibilityStatus.Compatible ||
            assessment.Status == ClientProfileCompatibilityStatus.CompatibleWithWarnings);
        compatibility.Assessments.Should().OnlyContain(assessment => assessment.FailedRequirements == 0);
    }

    [Fact]
    public void Evaluate_ShouldRecordAppliedProfilePackAndCompatibilityLayer()
    {
        var result = CreateToolOnlyValidationResult();

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cli" }
        });

        compatibility.Should().NotBeNull();
        result.Evidence.AppliedPacks.Select(pack => pack.Key.Value).Should().Contain("client-profile-pack/built-in");
        result.Assessments.Layers.Should().ContainSingle(layer =>
            layer.LayerId == "client-profiles" &&
            layer.Status == TestStatus.Passed);
        result.Evidence.Coverage.Should().ContainSingle(coverage =>
            coverage.LayerId == "client-profiles" &&
            coverage.Status == ValidationCoverageStatus.Covered &&
            coverage.Lane == EvaluationLane.ClientCompatibility &&
            coverage.Origin == EvidenceOrigin.DeterministicAggregation);
    }

    [Fact]
    public void Evaluate_GitHubCopilotCloudAgent_ShouldWarnWhenPromptAndResourceSurfacesExist()
    {
        var result = CreateToolOnlyValidationResult();
        result.PromptTesting = new PromptTestResult
        {
            Status = TestStatus.Passed,
            PromptsDiscovered = 1,
            PromptResults = new List<IndividualPromptResult>
            {
                new() { PromptName = "triage_issue", Status = TestStatus.Passed }
            }
        };
        result.ResourceTesting = new ResourceTestResult
        {
            Status = TestStatus.Passed,
            ResourcesDiscovered = 1,
            ResourceResults = new List<IndividualResourceResult>
            {
                new() { ResourceUri = "file:///repo/README.md", ResourceName = "README", Status = TestStatus.Passed }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cloud-agent" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.CompatibleWithWarnings);
        assessment.Summary.Should().Be("Required compatibility checks passed; 1 advisory requirement still needs follow-up.");
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "surface-tools-only-surface" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldFailWhenPromptContractIsBroken()
    {
        var result = CreateToolOnlyValidationResult();
        result.PromptTesting = new PromptTestResult
        {
            Status = TestStatus.Failed,
            PromptsDiscovered = 1,
            Findings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = ValidationFindingRuleIds.PromptGetMissingMessagesArray,
                    Component = "triage_issue",
                    Severity = ValidationFindingSeverity.High,
                    Summary = "Prompt 'triage_issue' did not return messages[]."
                }
            },
            PromptResults = new List<IndividualPromptResult>
            {
                new() { PromptName = "triage_issue", Status = TestStatus.Failed }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.Incompatible);
        assessment.FailedRequirements.Should().BeGreaterThan(0);
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "prompt-prompts-contract" &&
            requirement.RuleIds.Contains(ValidationFindingRuleIds.PromptGetMissingMessagesArray));
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldWarnWhenPromptCategoryFailsWithoutPromptContractFinding()
    {
        var result = CreateToolOnlyValidationResult();
        result.PromptTesting = new PromptTestResult
        {
            Status = TestStatus.Failed,
            PromptsDiscovered = 1,
            Findings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = ValidationFindingRuleIds.PromptArgumentComplexityGuidanceMissing,
                    Component = "triage_issue",
                    Severity = ValidationFindingSeverity.Low,
                    Summary = "Prompt requires multiple inputs without enough caller guidance."
                }
            },
            PromptResults = new List<IndividualPromptResult>
            {
                new() { PromptName = "triage_issue", Status = TestStatus.Passed, ExecutionSuccessful = true }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.CompatibleWithWarnings);
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "prompt-prompts-contract" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldWarnWhenInitializeInstructionsAreMissing()
    {
        var result = CreateToolOnlyValidationResult();
        result.InitializationHandshake = new TransportResult<InitializeResult>
        {
            IsSuccessful = true,
            Payload = new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "Docs MCP", Version = "1.0.0" },
                Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities()
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.CompatibleWithWarnings);
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "initialize-server-instructions" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning &&
            requirement.EvidenceBasis == ClientProfileEvidenceBasis.Documented &&
            requirement.DocumentationUrl == "https://code.claude.com/docs/en/mcp#scale-with-mcp-tool-search");
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldRecognizeAnthropicMaxResultSizeAnnotation()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults[0].AnthropicMaxResultSizeChars = 200000;

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-output-budget" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied &&
            requirement.DocumentationUrl == "https://code.claude.com/docs/en/mcp#raise-the-limit-for-a-specific-tool");
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldRecognizeListChangedSupportAcrossSurfaces()
    {
        var result = CreateToolOnlyValidationResult();
        result.PromptTesting = new PromptTestResult
        {
            Status = TestStatus.Passed,
            PromptsDiscovered = 1,
            PromptResults = new List<IndividualPromptResult>
            {
                new() { PromptName = "triage_issue", Status = TestStatus.Passed, ExecutionSuccessful = true }
            }
        };
        result.ResourceTesting = new ResourceTestResult
        {
            Status = TestStatus.Passed,
            ResourcesDiscovered = 1,
            ResourceResults = new List<IndividualResourceResult>
            {
                new() { ResourceUri = "file:///repo/README.md", ResourceName = "README", Status = TestStatus.Passed }
            }
        };
        result.InitializationHandshake = new TransportResult<InitializeResult>
        {
            IsSuccessful = true,
            Payload = new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "Docs MCP", Version = "1.0.0" },
                Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = true },
                    Prompts = new PromptsCapability { ListChanged = true },
                    Resources = new ResourcesCapability { ListChanged = true }
                }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "list-changed" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied &&
            requirement.DocumentationUrl == "https://code.claude.com/docs/en/mcp#dynamic-tool-updates");
    }

    [Fact]
    public void Evaluate_GitHubCopilotCloudAgent_ShouldFailWhenRemoteOAuthIsObserved()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.AuthenticationSecurity = new AuthenticationSecurityResult
        {
            AuthMetadata = new AuthMetadata
            {
                Resource = "https://example.test/mcp",
                AuthorizationServers = new List<string> { "https://login.example.test" }
            },
            WwwAuthenticateHeader = "Bearer realm=\"example\""
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cloud-agent" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.Incompatible);
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "transport-remote-auth" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Failed &&
            requirement.DocumentationUrl == "https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp");
    }

    [Fact]
    public void Evaluate_GitHubCopilotCloudAgent_ShouldNotFlagBearerPatAsOAuth()
    {
        // GitHub Copilot Cloud Agent docs explicitly say the BUILT-IN GitHub MCP works.
        // That server uses static PAT bearer headers, which trigger a `Bearer realm=...`
        // 401 challenge but are NOT OAuth. The remote-auth gate must not fire on bare
        // bearer challenges — only on real OAuth-flow signals (RFC 9728 metadata,
        // resource_metadata/authorization_uri parameters, interactive login).
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.AuthenticationSecurity = new AuthenticationSecurityResult
        {
            // No AuthMetadata, no interactive flow, just a generic Bearer realm challenge.
            WwwAuthenticateHeader = "Bearer realm=\"github\""
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cloud-agent" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "transport-remote-auth" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied);
    }

    [Fact]
    public void Evaluate_GitHubCopilotCloudAgent_ShouldFlagOAuthDiscoveryHeaderAsOAuth()
    {
        // RFC 9728 §5.1 says OAuth-aware resource servers add resource_metadata=URL to
        // their WWW-Authenticate challenge. That IS an OAuth-flow signal even when the
        // protected resource metadata fetch hasn't been completed.
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.AuthenticationSecurity = new AuthenticationSecurityResult
        {
            WwwAuthenticateHeader =
                "Bearer realm=\"example\", resource_metadata=\"https://example.test/.well-known/oauth-protected-resource\""
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cloud-agent" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "transport-remote-auth" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Failed);
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldWarnWhenToolDescriptionExceeds2KB()
    {
        // Claude Code docs: "truncates tool descriptions and server instructions at 2KB each".
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults[0].Description = new string('x', 3000);

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-description-size" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning &&
            requirement.Summary.Contains("2KB", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldPassWhenDescriptionsAreUnder2KB()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults[0].Description = new string('x', 1024);

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-description-size" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied);
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldWarnWhenToolDescriptionMissing()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults[0].Description = null;

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-tool-descriptions" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldWarnWhenToolHasNoAnnotations()
    {
        var result = CreateToolOnlyValidationResult();
        var t = result.ToolValidation!.ToolResults[0];
        t.ReadOnlyHint = null;
        t.DestructiveHint = null;
        t.IdempotentHint = null;
        t.OpenWorldHint = null;

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-tool-annotations" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_GitHubCopilotCloudAgent_ShouldWarnWhenWriteToolMissesDestructiveHint()
    {
        // Cloud Agent calls tools autonomously without per-call approval, but GitHub
        // documents tool allowlisting rather than an explicit destructiveHint requirement.
        var result = CreateToolOnlyValidationResult();
        var t = result.ToolValidation!.ToolResults[0];
        t.ToolName = "delete_branch";
        t.ReadOnlyHint = false;
        t.DestructiveHint = null; // missing

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cloud-agent" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.CompatibleWithWarnings);
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-destructive-hint" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning &&
            requirement.Level == ClientProfileRequirementLevel.Recommended);
    }

    [Fact]
    public void Evaluate_VisualStudioCopilot_ShouldWarnWhenToolMissesDisplayTitle()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults[0].DisplayTitle = null;

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "visual-studio-copilot" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-tool-titles" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_ClaudeCode_RemoteOAuthDiscovery_ShouldBeSatisfiedWhenAuthMetadataPresent()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.AuthenticationSecurity = new AuthenticationSecurityResult
        {
            AuthenticationRequired = true,
            AuthMetadata = new AuthMetadata
            {
                Resource = "https://example.test/mcp",
                AuthorizationServers = new List<string> { "https://login.example.test" }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "transport-oauth-discovery" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied);
    }

    [Fact]
    public void Evaluate_VisualStudioCopilot_ShouldSurfaceSamplingSupportWhenDetected()
    {
        var result = CreateToolOnlyValidationResult();
        result.ProtocolCompliance = new ComplianceTestResult
        {
            Findings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = ValidationFindingRuleIds.OptionalCapabilitySamplingSupported,
                    Severity = ValidationFindingSeverity.Info,
                    Summary = "Server supports the optional sampling capability."
                }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "visual-studio-copilot" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "sampling-supported" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied &&
            requirement.RuleIds.Contains(ValidationFindingRuleIds.OptionalCapabilitySamplingSupported));
    }

    [Fact]
    public void Evaluate_VisualStudioCopilot_ShouldRecognizeToolListChangedSupport()
    {
        var result = CreateToolOnlyValidationResult();
        result.InitializationHandshake = new TransportResult<InitializeResult>
        {
            IsSuccessful = true,
            Payload = new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "Docs MCP", Version = "1.0.0" },
                Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = true }
                }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "visual-studio-copilot" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "list-changed" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied &&
            requirement.DocumentationUrl == "https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=vs-2022#tool-lifecycle");
    }

    [Fact]
    public void Evaluate_UnknownProfile_ShouldThrowArgumentException()
    {
        var action = () => _evaluator.Evaluate(CreateToolOnlyValidationResult(), new ClientProfileOptions
        {
            Profiles = new List<string> { "unknown-profile" }
        });

        action.Should().Throw<ArgumentException>()
            .WithMessage("*Supported profiles*");
    }

    private static ValidationResult CreateToolOnlyValidationResult()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            ToolValidation = new ToolTestResult
            {
                Status = TestStatus.Passed,
                ToolsDiscovered = 1,
                ToolResults = new List<IndividualToolResult>
                {
                    new()
                    {
                        ToolName = "search_docs",
                        Status = TestStatus.Passed,
                        DiscoveredCorrectly = true,
                        MetadataValid = true,
                        ExecutionSuccessful = true,
                        DisplayTitle = "Search Docs",
                        Description = "Search the documentation index for matching topics.",
                        ReadOnlyHint = true,
                        DestructiveHint = false,
                        OpenWorldHint = true,
                        IdempotentHint = true
                    }
                }
            }
        };
        // Provide a minimal handshake so server-identity / initialize-aware checks find evidence.
        result.InitializationHandshake = new TransportResult<InitializeResult>
        {
            IsSuccessful = true,
            Payload = new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "example-mcp", Version = "1.0.0" },
                Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities()
            }
        };
        return result;
    }
}