using Mcp.Benchmark.Core;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Validators;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ValidationRunBudgetTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CatalogBudget_ShouldRejectNonpositiveLimitsWithoutReplacingAnActiveScope(long limit)
    {
        using var parent = ValidationCatalogBudget.BeginRun(3);
        FluentActions.Invoking(() => ValidationCatalogBudget.BeginRun(limit)).Should().Throw<ArgumentOutOfRangeException>();
        ValidationCatalogBudget.TryReserve(3).Should().BeTrue();
        parent.Reserved.Should().Be(3);
    }

    [Fact]
    public void CatalogBudget_ShouldRestoreNestedScopeAndMakeDisposalIdempotent()
    {
        using var parent = ValidationCatalogBudget.BeginRun(5);
        ValidationCatalogBudget.TryReserve(2).Should().BeTrue();
        var child = ValidationCatalogBudget.BeginRun(10);
        ValidationCatalogBudget.TryReserve(10).Should().BeTrue();
        child.Dispose();
        ValidationCatalogBudget.TryReserve(3).Should().BeTrue();
        parent.Reserved.Should().Be(5);
        using var next = ValidationCatalogBudget.BeginRun(7);
        child.Dispose();
        ValidationCatalogBudget.TryReserve(7).Should().BeTrue();
        next.Reserved.Should().Be(7);
        ValidationCatalogBudget.TryReserve(1).Should().BeFalse();
    }

    [Fact]
    public void CatalogBudget_ShouldRejectNegativeReservationsEvenWithoutRunScope()
    {
        FluentActions.Invoking(() => ValidationCatalogBudget.TryReserve(-1)).Should().Throw<ArgumentOutOfRangeException>();
        ValidationCatalogBudget.TryReserve(1).Should().BeTrue();
        using var budget = ValidationCatalogBudget.BeginRun(1);
        FluentActions.Invoking(() => ValidationCatalogBudget.TryReserve(-1)).Should().Throw<ArgumentOutOfRangeException>();
        budget.Reserved.Should().Be(0);
    }

    [Fact]
    public async Task CatalogBudget_ShouldRejectLateWorkUsingACapturedDisposedScope()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var budget = ValidationCatalogBudget.BeginRun(10);
        var late = Task.Run(async () =>
        {
            entered.SetResult();
            await release.Task;
            FluentActions.Invoking(() => ValidationCatalogBudget.TryReserve(1))
                .Should().Throw<InvalidOperationException>().WithMessage("*disposed*");
        });
        await entered.Task;
        budget.Dispose();
        release.SetResult();
        await late;
        budget.Reserved.Should().Be(0);
    }

    [Fact]
    public void DemandEstimate_ShouldDistinguishFiniteUnknownAndEmptyStages()
    {
        new RequestDemandEstimate([], 10).HasUnknownExpansion.Should().BeFalse();
        var finite = new RequestDemandEstimate([new("first", 2, 2, "Explicit"), new("second", 3, 3, "Explicit")], 10);
        finite.HasUnknownExpansion.Should().BeFalse();
        finite.KnownMinimumAttempts.Should().Be(5);
        new RequestDemandEstimate([new("unknown", 0, null, "Target dependent")], 10).HasUnknownExpansion.Should().BeTrue();
        FluentActions.Invoking(() => ValidationRequestDemand.Estimate(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RequestDemand_ShouldPreserveTheCompleteStageContractWithoutInventedWork()
    {
        var configuration = MinimalDemandConfiguration();
        var estimate = ValidationRequestDemand.Estimate(configuration);
        estimate.Stages.Select(stage => (stage.Stage, stage.KnownMinimumAttempts, stage.EstimatedMaximumAttempts))
            .Should().Equal(("bootstrap", 1, null), ("catalogs", 0, null), ("retries-and-auxiliary", 0, null), ("signed-observer-import", 0, 0));
        estimate.Stages.Should().OnlyContain(stage => !string.IsNullOrWhiteSpace(stage.Basis));
        estimate.EnforcedAttemptLimit.Should().Be(19);
        configuration.Execution!.DryRun = true;
        var planned = ValidationRequestDemand.Estimate(configuration);
        planned.Stages.Should().BeEmpty();
        planned.EnforcedAttemptLimit.Should().Be(19);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, false)]
    public void RequestDemand_ShouldRetainExplicitToolAdmissionAndItsUnknownDiscoveredWork(bool discovery, bool execution, bool parameters, bool admitted)
    {
        var configuration = MinimalDemandConfiguration();
        var tools = configuration.Validation.Categories.ToolTesting;
        tools.TestToolDiscovery = discovery;
        tools.TestToolExecution = execution;
        tools.TestParameterValidation = parameters;
        tools.CustomScenarios = [new() { ToolName = "owned" }];
        var estimate = ValidationRequestDemand.Estimate(configuration);
        var stage = estimate.Stages.Single(item => item.Stage == (admitted ? "explicit-tools" : "explicit-tools-blocked"));
        stage.KnownMinimumAttempts.Should().Be(admitted ? 1 : 0);
        stage.EstimatedMaximumAttempts.Should().Be(admitted ? 1 : 0);
        estimate.Stages.Any(item => item.Stage == "discovered-tools").Should().Be(admitted);
        if (admitted) estimate.Stages.Single(item => item.Stage == "discovered-tools").EstimatedMaximumAttempts.Should().BeNull();
        estimate.Stages.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Basis));
        tools.CustomScenarios.Clear();
        ValidationRequestDemand.Estimate(configuration).Stages.Should().NotContain(item => item.Stage == "explicit-tools-blocked");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void RequestDemand_ShouldRetainExplicitPromptAdmissionAndUnknownDiscoveredWork(bool discovery, bool execution)
    {
        var configuration = MinimalDemandConfiguration();
        var prompts = configuration.Validation.Categories.PromptTesting;
        prompts.TestPromptDiscovery = discovery;
        prompts.TestPromptExecution = execution;
        prompts.CustomScenarios = [new() { PromptName = "owned" }];
        var admitted = discovery && execution;
        var estimate = ValidationRequestDemand.Estimate(configuration);
        var stage = estimate.Stages.Single(item => item.Stage == (admitted ? "explicit-prompts" : "explicit-prompts-blocked"));
        stage.KnownMinimumAttempts.Should().Be(admitted ? 1 : 0);
        stage.EstimatedMaximumAttempts.Should().Be(admitted ? 1 : 0);
        estimate.Stages.Any(item => item.Stage == "discovered-prompts").Should().Be(admitted);
        if (admitted) estimate.Stages.Single(item => item.Stage == "discovered-prompts").EstimatedMaximumAttempts.Should().BeNull();
        estimate.Stages.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Basis));
        prompts.CustomScenarios.Clear();
        ValidationRequestDemand.Estimate(configuration).Stages.Should().NotContain(item => item.Stage == "explicit-prompts-blocked");
    }

    [Theory]
    [InlineData(ExecutionMode.Standard, false, false, false)]
    [InlineData(ExecutionMode.Standard, true, false, true)]
    [InlineData(ExecutionMode.Standard, false, true, true)]
    [InlineData(ExecutionMode.Safe, true, true, false)]
    public void RequestDemand_ShouldDeclareOnlyAdmittedResourceWork(ExecutionMode mode, bool reading, bool subscriptions, bool expected)
    {
        var configuration = MinimalDemandConfiguration();
        configuration.Execution!.Mode = mode;
        configuration.Validation.Categories.ResourceTesting.TestResourceReading = reading;
        configuration.Validation.Categories.ResourceTesting.TestSubscriptions = subscriptions;
        var resourceStages = ValidationRequestDemand.Estimate(configuration).Stages.Where(stage => stage.Stage == "resources").ToArray();
        resourceStages.Should().HaveCount(expected ? 1 : 0);
        if (expected)
        {
            resourceStages[0].KnownMinimumAttempts.Should().Be(0);
            resourceStages[0].EstimatedMaximumAttempts.Should().BeNull();
            resourceStages[0].Basis.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("security")]
    [InlineData("concurrent")]
    [InlineData("response-times")]
    [InlineData("throughput")]
    public void RequestDemand_ShouldDeclareEachConfiguredUnknownExpansionIndependently(string selected)
    {
        var configuration = MinimalDemandConfiguration();
        var categories = configuration.Validation.Categories;
        categories.ProtocolCompliance.TestJsonRpcCompliance = selected == "protocol";
        categories.SecurityTesting.TestInputValidation = selected == "security";
        categories.PerformanceTesting.TestConcurrentRequests = selected == "concurrent";
        categories.PerformanceTesting.TestResponseTimes = selected == "response-times";
        categories.PerformanceTesting.TestThroughput = selected == "throughput";
        var name = selected is "protocol" or "security" ? selected : "performance";
        var estimate = ValidationRequestDemand.Estimate(configuration);
        estimate.Stages.Select(stage => stage.Stage).Should().Equal("bootstrap", "catalogs", name, "retries-and-auxiliary", "signed-observer-import");
        var stage = estimate.Stages.Single(item => item.Stage == name);
        stage.KnownMinimumAttempts.Should().Be(0);
        stage.EstimatedMaximumAttempts.Should().BeNull();
        estimate.Stages.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Basis));
    }

    private static McpValidatorConfiguration MinimalDemandConfiguration() => new()
    {
        Execution = new() { Mode = ExecutionMode.Standard, MaxRequests = 19 },
        Validation = new() { Categories = new()
        {
            ToolTesting = new() { TestToolDiscovery = false, TestToolExecution = false, TestParameterValidation = false },
            PromptTesting = new() { TestPromptDiscovery = false, TestPromptExecution = false },
            ResourceTesting = new() { TestResourceReading = false, TestSubscriptions = false },
            ProtocolCompliance = new() { TestJsonRpcCompliance = false },
            SecurityTesting = new() { TestInputValidation = false },
            PerformanceTesting = new() { TestConcurrentRequests = false, TestResponseTimes = false, TestThroughput = false }
        }}
    };

    [Fact]
    public async Task CatalogBudget_ShouldBeSharedAcrossConcurrentCollectorsWithoutOverReservation()
    {
        using var budget = ValidationCatalogBudget.BeginRun(100);
        var admissions = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => ValidationCatalogBudget.TryReserve(10))));
        admissions.Count(admitted => admitted).Should().Be(10);
        budget.Reserved.Should().Be(100);
        ValidationCatalogBudget.TryReserve(1).Should().BeFalse();
        budget.Reserved.Should().Be(100);
    }

    [Fact]
    public async Task CatalogBudget_ShouldIsolateRunsAndRestoreParentScope()
    {
        using var parent = ValidationCatalogBudget.BeginRun(5);
        ValidationCatalogBudget.TryReserve(2).Should().BeTrue();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            using var child = ValidationCatalogBudget.BeginRun(20);
            ValidationCatalogBudget.TryReserve(20).Should().BeTrue();
            child.Reserved.Should().Be(20);
        })));
        parent.Reserved.Should().Be(2);
        ValidationCatalogBudget.TryReserve(3).Should().BeTrue();
        ValidationCatalogBudget.TryReserve(1).Should().BeFalse();
    }

    [Fact]
    public async Task CatalogReader_ShouldRetainEarlierPagesAndDeclareSharedBudgetExhaustion()
    {
        var first = new JsonRpcResponse { IsSuccess = true, StatusCode = 200, RawJson = """{"result":{"tools":[{"name":"first"}],"nextCursor":"next"}}""" };
        var second = new JsonRpcResponse { IsSuccess = true, StatusCode = 200, RawJson = """{"result":{"tools":[{"name":"second"}]}}""" };
        using var budget = ValidationCatalogBudget.BeginRun(first.RawJson.Length);
        var evidence = new CatalogEnumerationEvidence();
        var issues = new List<string>();
        var names = new List<string>();
        await foreach (var item in BoundedCatalogReader.ReadAsync(first, "tools", 10, evidence, issues,
            (_, _) => Task.FromResult(second), CancellationToken.None))
            names.Add(item.GetProperty("name").GetString()!);
        names.Should().Equal("first");
        evidence.Termination.Should().Be(CatalogEnumerationTermination.BudgetExceeded);
        evidence.IsComplete.Should().BeFalse();
        issues.Should().Contain(message => message.Contains("Run-wide", StringComparison.Ordinal));
        budget.Reserved.Should().Be(first.RawJson.Length);
    }

    [Fact]
    public void RequestDemand_ShouldSeparateKnownExplicitWorkFromUnknownExpansionAndHardLimit()
    {
        var configuration = new McpValidatorConfiguration { Execution = new() { Mode = ExecutionMode.Standard, MaxRequests = 4 } };
        configuration.Validation.Categories.ToolTesting.CustomScenarios = [new() { ToolName = "first" }, new() { ToolName = "second" }];
        configuration.Validation.Categories.PromptTesting.CustomScenarios = [new() { PromptName = "prompt" }];
        var estimate = ValidationRequestDemand.Estimate(configuration);
        estimate.KnownMinimumAttempts.Should().Be(4);
        estimate.HasUnknownExpansion.Should().BeTrue();
        estimate.EnforcedAttemptLimit.Should().Be(4);
        estimate.Stages.Single(stage => stage.Stage == "explicit-tools").EstimatedMaximumAttempts.Should().Be(2);
        estimate.Stages.Single(stage => stage.Stage == "catalogs").EstimatedMaximumAttempts.Should().BeNull();
        configuration.Execution.DryRun = true;
        var dryRun = ValidationRequestDemand.Estimate(configuration);
        dryRun.KnownMinimumAttempts.Should().Be(0);
        dryRun.HasUnknownExpansion.Should().BeFalse();
    }

    [Theory]
    [InlineData(ExecutionMode.Safe, false, 1)]
    [InlineData(ExecutionMode.Standard, false, 3)]
    [InlineData(ExecutionMode.Elevated, false, 1)]
    [InlineData(ExecutionMode.Elevated, true, 3)]
    public void RequestDemand_ShouldCountOnlyAdmittedExplicitOperations(ExecutionMode mode, bool confirmed, int minimum)
    {
        var configuration = new McpValidatorConfiguration { Execution = new() { Mode = mode, ConfirmElevatedRisk = confirmed } };
        configuration.Validation.Categories.ToolTesting.CustomScenarios = [new() { ToolName = "tool" }];
        configuration.Validation.Categories.PromptTesting.CustomScenarios = [new() { PromptName = "prompt" }];
        ValidationRequestDemand.Estimate(configuration).KnownMinimumAttempts.Should().Be(minimum);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    public void InvalidRequestedLimit_ShouldRemainRejectedWithoutCorruptingTheAuditEstimate(int requested, bool dryRun)
    {
        var configuration = new McpValidatorConfiguration
        { Execution = new() { MaxRequests = requested, DryRun = dryRun }, Server = new() { Endpoint = "https://example.invalid/mcp" } };
        var governance = new Mcp.Benchmark.CLI.Services.ExecutionGovernanceService();
        var plan = governance.BuildValidationPlan(new Mcp.Benchmark.CLI.Utilities.CliSessionContext("invalid-budget"), configuration, null);
        plan.IsValid.Should().BeFalse();
        plan.RequestDemand!.EnforcedAttemptLimit.Should().Be(plan.MaxRequests);
        var audit = governance.BuildAuditManifest(plan, null, [], null);
        audit.RequestDemand!.EnforcedAttemptLimit.Should().BeGreaterThan(0);
        audit.ExecutedChecks.Should().BeEmpty();
    }
}
