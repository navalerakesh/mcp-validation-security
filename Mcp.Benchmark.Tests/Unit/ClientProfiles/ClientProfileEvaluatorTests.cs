using Mcp.Benchmark.ClientProfiles;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit.ClientProfiles;

public class ClientProfileEvaluatorTests
{
    private readonly ClientProfileEvaluator _evaluator = new();

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
        compatibility.Assessments.Should().OnlyContain(assessment => assessment.Status == ClientProfileCompatibilityStatus.Compatible);
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
        return new ValidationResult
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
                        ReadOnlyHint = true,
                        DestructiveHint = false,
                        OpenWorldHint = true,
                        IdempotentHint = true
                    }
                }
            }
        };
    }
}