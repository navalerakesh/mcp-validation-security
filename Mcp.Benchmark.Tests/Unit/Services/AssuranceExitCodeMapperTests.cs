using FluentAssertions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class AssuranceExitCodeMapperTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComprehensivePreflight_ShouldNotTreatRequiredChecksAsOptionalStressOrMemoryScope(bool security)
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Execution!.Mode = ExecutionMode.Standard;
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true).Should().BeEmpty();
        if (security) configuration.Validation.Categories.SecurityTesting.TestInputValidation = false;
        else configuration.Validation.Categories.PerformanceTesting.TestConcurrentRequests = false;
        var errors = ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true);
        errors.Should().ContainSingle().Which.Should().Contain(security
            ? "SecurityTesting.TestInputValidation" : "PerformanceTesting.TestConcurrentRequests");
    }

    [Theory]
    [InlineData("positive", true)]
    [InlineData("pattern", false)]
    [InlineData("expected-error", false)]
    [InlineData("unavailable", false)]
    public void ComprehensivePreflight_ShouldAdmitOnlySupportedExplicitPositiveArguments(string variant, bool admitted)
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Execution!.Mode = ExecutionMode.Standard;
        var scenario = new ToolTestScenario { ToolName = "owned-tool", Parameters = new() { ["value"] = "test" } };
        if (variant == "pattern") scenario.ExpectedResult = "";
        if (variant == "expected-error") scenario.ExpectError = true;
        if (variant == "unavailable") scenario.Parameters = null;
        configuration.Validation.Categories.ToolTesting.CustomScenarios.Add(scenario);
        var errors = ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true);
        if (admitted) errors.Should().BeEmpty();
        else errors.Should().ContainSingle().Which.Should().Contain("ToolTesting.CustomScenarios");
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("categories")]
    [InlineData("ProtocolCompliance")]
    [InlineData("ToolTesting")]
    [InlineData("ResourceTesting")]
    [InlineData("PromptTesting")]
    [InlineData("SecurityTesting")]
    [InlineData("PerformanceTesting")]
    [InlineData("ErrorHandling")]
    public void ComprehensivePreflight_ShouldIdentifyMissingCategoryWithoutMaskingIt(string missing)
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Execution!.Mode = ExecutionMode.Standard;
        var categories = configuration.Validation.Categories;
        switch (missing)
        {
            case "validation": configuration.Validation = null!; break;
            case "categories": configuration.Validation.Categories = null!; break;
            case "ProtocolCompliance": categories.ProtocolCompliance = null!; break;
            case "ToolTesting": categories.ToolTesting = null!; break;
            case "ResourceTesting": categories.ResourceTesting = null!; break;
            case "PromptTesting": categories.PromptTesting = null!; break;
            case "SecurityTesting": categories.SecurityTesting = null!; break;
            case "PerformanceTesting": categories.PerformanceTesting = null!; break;
            case "ErrorHandling": categories.ErrorHandling = null!; break;
        }
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true).Should().ContainSingle().Which
            .Should().Contain(missing is "validation" or "categories" ? "all validation categories" : $"category {missing}");
    }

    [Theory]
    [InlineData(ExecutionMode.Safe, false)]
    [InlineData(ExecutionMode.Standard, true)]
    [InlineData(ExecutionMode.Elevated, true)]
    [InlineData((ExecutionMode)999, false)]
    public void ComprehensivePreflight_ShouldApplyExecutionAdmissionIndependently(ExecutionMode mode, bool admitted)
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Execution!.Mode = mode;
        var errors = ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true);
        if (admitted) errors.Should().BeEmpty();
        else errors.Should().ContainSingle().Which.Should().Contain("explicit standard or elevated execution admission");
        configuration.Execution.Mode.Should().Be(mode);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("ci")]
    [InlineData("provider")]
    public void ComprehensivePreflight_ShouldNotApplyToExplicitOtherProfiles(string profile)
    {
        var configuration = new McpValidatorConfiguration { Execution = null!, Validation = null! };
        configuration.Assurance.Profile = profile;
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, false).Should().BeEmpty();
        configuration.Assurance.Profile = "comprehensive";
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, false, planningOnly: true).Should().BeEmpty();
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, false).Should().HaveCount(3);
    }

    [Fact]
    public void ComprehensivePreflight_ShouldRetainEachUnsupportedRequestReason()
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Execution!.Mode = ExecutionMode.Standard;
        var categories = configuration.Validation.Categories;
        categories.ToolTesting.CustomScenarios.Add(new());
        categories.PerformanceTesting.CustomScenarios.Add(new());
        categories.PerformanceTesting.TestMemoryUsage = true;
        categories.SecurityTesting.TestBufferOverflow = true;
        categories.SecurityTesting.TestResourceExhaustion = true;
        var errors = ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true);
        errors.Should().HaveCount(5).And.OnlyHaveUniqueItems();
        errors.Should().Contain(error => error.Contains("ToolTesting.CustomScenarios") && error.Contains("cannot be executed"));
        errors.Should().Contain(error => error.Contains("PerformanceTesting.CustomScenarios") && error.Contains("cannot be executed"));
        errors.Should().Contain(error => error.Contains("Target-memory") && error.Contains("operator-owned observer"));
        errors.Should().Contain(error => error.Contains("Buffer-overflow") && error.Contains("isolated stress harness"));
        errors.Should().Contain(error => error.Contains("Resource-exhaustion") && error.Contains("isolated stress harness"));
    }

    [Fact]
    public void PublicAssuranceEntryPoints_ShouldRejectNullInputs()
    {
        var preflight = () => ComprehensiveAssessmentRequirements.GetPreflightErrors(null!, true);
        var mapResult = () => AssuranceExitCodeMapper.Map((ValidationResult)null!);
        var mapEvaluation = () => AssuranceExitCodeMapper.Map(null!, null);
        var project = () => AssuranceClaimProjector.Project(null!);
        var verify = () => ControlledIdentitySecurityBaseline.VerifyRetainedRequirement(null!);
        preflight.Should().Throw<ArgumentNullException>();
        mapResult.Should().Throw<ArgumentNullException>();
        mapEvaluation.Should().Throw<ArgumentNullException>();
        project.Should().Throw<ArgumentNullException>();
        verify.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComprehensivePreflight_ShouldRejectUnsupportedCustomRequests(bool tools)
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Execution!.Mode = ExecutionMode.Standard;
        if (tools) configuration.Validation.Categories.ToolTesting.CustomScenarios.Add(new());
        else configuration.Validation.Categories.PerformanceTesting.CustomScenarios.Add(new());

        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true)
            .Should().ContainSingle(error => error.Contains("CustomScenarios", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComprehensiveStressScope_ShouldRejectExplicitRequirementsWithoutAnIsolatedHarness(bool bufferOverflow)
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Execution!.Mode = ExecutionMode.Elevated;
        var security = configuration.Validation.Categories.SecurityTesting;
        security.TestBufferOverflow.Should().BeFalse();
        security.TestResourceExhaustion.Should().BeFalse();
        if (bufferOverflow) security.TestBufferOverflow = true;
        else security.TestResourceExhaustion = true;

        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true)
            .Should().ContainSingle(error => error.Contains("isolated stress harness"));
    }

    [Fact]
    public void ComprehensivePerformanceScope_ShouldRequireAnObserverOnlyWhenTargetMemoryIsSelected()
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Execution!.Mode = ExecutionMode.Standard;
        configuration.Validation.Categories.PerformanceTesting.TestMemoryUsage.Should().BeFalse();
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true).Should().BeEmpty();
        configuration.Validation.Categories.PerformanceTesting.TestMemoryUsage = true;
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true)
            .Should().ContainSingle(error => error.Contains("operator-owned observer"));
    }

    [Fact]
    public void ComprehensiveDefault_ShouldHaveIndependentRevisionAndRejectUndefinedExecutionMode()
    {
        var configuration = new McpValidatorConfiguration();
        AssuranceProfileCatalog.Resolve(configuration.Assurance.Profile).Should().BeSameAs(AssuranceProfileCatalog.Comprehensive);
        AssuranceProfileCatalog.Comprehensive.Revision.Should().Be(AssuranceProfileCatalog.ComprehensiveRevision);
        AssuranceProfileCatalog.Inventory.Revision.Should().Be(AssuranceProfileCatalog.Revision);
        configuration.Execution!.Mode = (ExecutionMode)int.MaxValue;
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true).Should().Contain(error => error.Contains("execution admission"));
    }

    [Fact]
    public void ComprehensivePreflight_ShouldRequireAdmissionAndEvidenceWithoutChangingExecution()
    {
        var configuration = new McpValidatorConfiguration { Assurance = new AssurancePolicyConfig { Profile = "comprehensive" } };
        var errors = ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, false);
        errors.Should().Contain(error => error.Contains("execution admission"));
        errors.Should().Contain(error => error.Contains("conformance evidence"));
        configuration.Execution!.Mode.Should().Be(ExecutionMode.Safe);
    }

    [Fact]
    public void ComprehensivePreflight_ShouldRejectDisabledChecksButPermitPlanning()
    {
        var configuration = new McpValidatorConfiguration { Assurance = new AssurancePolicyConfig { Profile = "comprehensive" } };
        configuration.Execution!.Mode = ExecutionMode.Standard;
        configuration.Validation.Categories.ToolTesting.TestToolExecution = false;
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true).Should().Contain(error => error.Contains("TestToolExecution"));
        configuration.Execution.DryRun = true;
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, false, planningOnly: true).Should().BeEmpty();
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true).Should().Contain(error => error.Contains("execution API"));
    }

    [Fact]
    public void Map_ComprehensiveProfile_ShouldNotAcceptAnEmptySuccessfulRun()
    {
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Comprehensive, CertificateEligibilityStatus.Ineligible);
        AssuranceExitCodeMapper.Map(assurance, new ValidationPolicyOutcome { Passed = true, RecommendedExitCode = 0 })
            .Should().Be(AssuranceExitCodeMapper.Incomplete);
    }

    [Fact]
    public void Map_InventoryProfile_ShouldUseSelectedPolicyWithoutTreatingCertificateIneligibilityAsFailure()
    {
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Inventory, CertificateEligibilityStatus.Ineligible);

        AssuranceExitCodeMapper.Map(assurance, new ValidationPolicyOutcome { Passed = true, RecommendedExitCode = 0 })
            .Should().Be(AssuranceExitCodeMapper.Satisfied);
    }

    [Fact]
    public void Map_CiProfile_ShouldDistinguishIncompleteEvidenceFromFailure()
    {
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Ci, CertificateEligibilityStatus.Ineligible);

        AssuranceExitCodeMapper.Map(assurance, new ValidationPolicyOutcome { Passed = true, RecommendedExitCode = 0 })
            .Should().Be(AssuranceExitCodeMapper.Incomplete);
    }

    [Theory]
    [InlineData(CertificateEligibilityStatus.Eligible, 0)]
    [InlineData(CertificateEligibilityStatus.Ineligible, 1)]
    [InlineData(CertificateEligibilityStatus.ReviewRequired, 2)]
    public void Map_ProviderProfile_ShouldFollowCertificateEligibility(CertificateEligibilityStatus status, int expected)
    {
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Provider, status);

        AssuranceExitCodeMapper.Map(assurance, null).Should().Be(expected);
    }

    [Fact]
    public void Map_WholeTargetUnavailable_ShouldReturnSeventyFive()
    {
        var result = new ValidationResult
        {
            Assurance = CreateEvaluation(AssuranceProfileCatalog.Ci, CertificateEligibilityStatus.Ineligible)
        };
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "protocol-core",
            Scope = "target",
            Status = ValidationCoverageStatus.Unavailable,
            ObservedOutcome = ValidationOutcome.Unavailable,
            Lane = EvaluationLane.Baseline
        });

        AssuranceExitCodeMapper.Map(result).Should().Be(AssuranceExitCodeMapper.TargetUnavailable);
    }

    [Fact]
    public void Map_FrameworkIntegrityError_ShouldReturnSeventy()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Error,
            Assurance = CreateEvaluation(AssuranceProfileCatalog.Ci, CertificateEligibilityStatus.Ineligible),
            CriticalErrors = ["Internal validator failure."]
        };

        AssuranceExitCodeMapper.Map(result).Should().Be(AssuranceExitCodeMapper.RuntimeIntegrityFailure);
    }

    [Fact]
    public void Map_CiIncompleteAssessment_ShouldReturnTwo()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.PartiallyCompleted,
            Assurance = CreateEvaluation(AssuranceProfileCatalog.Ci, CertificateEligibilityStatus.Ineligible),
            PolicyOutcome = new ValidationPolicyOutcome { Passed = true }
        };

        AssuranceExitCodeMapper.Map(result).Should().Be(AssuranceExitCodeMapper.Incomplete);
    }

    internal static AssuranceEvaluation CreateEvaluation(
        AssuranceProfileManifest profile, AssuranceClaimOutcome outcome, AssuranceRequirementLevel requirement, bool extensionGap = false, string? omittedBaseline = null,
        IReadOnlyList<AssuranceClaim>? coverageClaims = null, EvidenceCoverageSummary? coverageSummary = null)
    {
        var baseline = new ValidationResult();
        ControlledIdentityEvaluatorTests.AddSecurityBaseline(baseline);
        return new AssuranceEvaluation
        {
            Profile = profile,
            RequiredIdentityControls = omittedBaseline == "selection" ? null : baseline.Assurance.RequiredIdentityControls,
            CertificateEligibility = new CertificateEligibilityAssessment { ProfileId = profile.ProfileId, Status = CertificateEligibilityStatus.Ineligible },
            RequiredExtensionIds = extensionGap ? ["selected.extension"] : [],
            EvidenceSummary = coverageSummary ?? new EvidenceCoverageSummary { TotalDeclarations = 1, ApplicableDeclarations = 1, Covered = 1, EvidenceCoverageRatio = 1 },
            Assessments = new AssuranceAssessmentSet
            {
                CoreConformance = new CoreConformanceAssessment { Status = CoreConformanceStatus.Conformant },
                SecurityAssurance = new SecurityAssuranceAssessment { Status = SecurityAssuranceStatus.Verified },
                OperationalReadiness = new OperationalReadinessAssessment { Status = OperationalReadinessStatus.Ready },
                ExternalConformance = new ExternalConformanceAssuranceAssessment { Status = ExternalConformanceStatus.Verified, VerificationStatus = OfficialConformanceVerificationStatus.Verified },
                ExtensionReadiness = extensionGap ? [new ExtensionAssuranceAssessment
                {
                    ExtensionId = "selected.extension", SpecificationRevision = "fixture-v1", SpecificationSha256 = new string('a', 64), Status = ExtensionReadinessStatus.Incomplete
                }] : []
            },
            Claims = [.. coverageClaims ?? [], .. baseline.Assurance.Claims.Where(claim => ControlledIdentitySecurityBaseline.RequiredClaimIds.Contains(claim.ClaimId) && claim.Subject != omittedBaseline), new AssuranceClaim
            {
                ClaimId = "official:fixture", Subject = "fixture", Dimension = AssuranceDimension.ExternalConformance,
                RequirementLevel = requirement, Authority = AssuranceAuthority.OfficialConformance,
                Source = new AssuranceSourceReference { Revision = "fixture-v1" }, Outcome = outcome, Summary = "Fixture requirement"
            }]
        };
    }

    [Theory]
    [InlineData(AssuranceClaimOutcome.Satisfied, AssuranceRequirementLevel.Must, false, 0)]
    [InlineData(AssuranceClaimOutcome.ReviewRequired, AssuranceRequirementLevel.Must, false, 2)]
    [InlineData(AssuranceClaimOutcome.Incomplete, AssuranceRequirementLevel.Must, false, 2)]
    [InlineData(AssuranceClaimOutcome.Violated, AssuranceRequirementLevel.Must, false, 1)]
    [InlineData(AssuranceClaimOutcome.Incomplete, AssuranceRequirementLevel.May, false, 0)]
    [InlineData(AssuranceClaimOutcome.Violated, AssuranceRequirementLevel.May, false, 0)]
    [InlineData(AssuranceClaimOutcome.Satisfied, AssuranceRequirementLevel.Must, true, 2)]
    public void ComprehensiveAcceptance_ShouldDistinguishRequiredDebtFromOptionalEvidence(
        AssuranceClaimOutcome outcome, AssuranceRequirementLevel requirement, bool extensionGap, int expected)
    {
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Comprehensive, outcome, requirement, extensionGap);
        AssuranceExitCodeMapper.Map(assurance, new ValidationPolicyOutcome { Passed = true, RecommendedExitCode = 0 }).Should().Be(expected);
        AssuranceAssessmentEngine.DetermineValidationStatus(assurance).Should().Be(expected switch
        {
            0 => ValidationStatus.Passed,
            1 => ValidationStatus.Failed,
            _ => ValidationStatus.PartiallyCompleted
        });
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("actor-binding")]
    [InlineData("scope-enforcement")]
    [InlineData("actor-action-object-authorization")]
    public void ComprehensiveAcceptance_ShouldRejectMissingBaselineDespiteReadySummaries(string omitted)
    {
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Comprehensive, AssuranceClaimOutcome.Satisfied,
            AssuranceRequirementLevel.Must, omittedBaseline: omitted);
        AssuranceExitCodeMapper.Map(assurance, null).Should().Be(AssuranceExitCodeMapper.Incomplete);
    }

    [Theory]
    [InlineData(ValidationCoverageStatus.Covered, ValidationOutcome.Failed, 1)]
    [InlineData(ValidationCoverageStatus.Unavailable, ValidationOutcome.Unavailable, 2)]
    public void ComprehensiveAcceptance_ShouldRequireToolCoverageOutcomes(ValidationCoverageStatus status, ValidationOutcome outcome, int expected)
    {
        var result = new ValidationResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "capabilities", Scope = "tools", Lane = EvaluationLane.Baseline,
            Status = status, ObservedOutcome = outcome
        });
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Comprehensive, AssuranceClaimOutcome.Satisfied,
            AssuranceRequirementLevel.Must, coverageClaims: AssuranceClaimProjector.Project(result));

        AssuranceExitCodeMapper.Map(assurance, null).Should().Be(expected);
    }

    [Fact]
    public void ComprehensiveAcceptance_ShouldNotUseRoundedCoverageForAdmission()
    {
        var assurance = CreateEvaluation(AssuranceProfileCatalog.Comprehensive, AssuranceClaimOutcome.Satisfied,
            AssuranceRequirementLevel.Must, coverageSummary: new EvidenceCoverageSummary
            {
                TotalDeclarations = 20001, ApplicableDeclarations = 20001, Covered = 20000, Unavailable = 1,
                EvidenceCoverageRatio = Math.Round(20000d / 20001, 4)
            });

        assurance.EvidenceSummary.EvidenceCoverageRatio.Should().Be(1);
        AssuranceExitCodeMapper.Map(assurance, null).Should().Be(AssuranceExitCodeMapper.Incomplete);
    }

    private static AssuranceEvaluation CreateEvaluation(
        AssuranceProfileManifest profile,
        CertificateEligibilityStatus certificateStatus)
    {
        return new AssuranceEvaluation
        {
            Profile = profile,
            CertificateEligibility = new CertificateEligibilityAssessment
            {
                ProfileId = profile.ProfileId,
                Status = certificateStatus
            }
        };
    }
}