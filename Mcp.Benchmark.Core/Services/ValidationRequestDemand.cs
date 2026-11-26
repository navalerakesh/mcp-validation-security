using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public sealed record RequestDemandStage(string Stage, int KnownMinimumAttempts, int? EstimatedMaximumAttempts, string Basis);
public sealed record RequestDemandEstimate(IReadOnlyList<RequestDemandStage> Stages, int EnforcedAttemptLimit)
{
    public int KnownMinimumAttempts => Stages.Sum(stage => stage.KnownMinimumAttempts);
    public bool HasUnknownExpansion => Stages.Any(stage => stage.EstimatedMaximumAttempts == null);
}

public static class ValidationRequestDemand
{
    public static RequestDemandEstimate Estimate(McpValidatorConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var policy = configuration.Execution ?? new ExecutionPolicy();
        var enforcedLimit = ValidationRequestBudget.NormalizeLimit(policy.MaxRequests);
        if (policy.DryRun) return new([], enforcedLimit);
        var categories = configuration.Validation.Categories;
        var stages = new List<RequestDemandStage>
        {
            new("bootstrap", 1, null, "At least one exchange for successful bootstrap; authentication, era fallback and retries depend on the target."),
            new("catalogs", 0, null, "Advertised catalogs, pages and cached bootstrap evidence determine expansion.")
        };
        var toolsAdmitted = McpOperationAdmission.IsAllowed(policy.Mode, policy.ConfirmElevatedRisk, Constants.ValidationConstants.Methods.ToolsCall);
        var promptsAdmitted = McpOperationAdmission.IsAllowed(policy.Mode, policy.ConfirmElevatedRisk, Constants.ValidationConstants.Methods.PromptsGet);
        if (toolsAdmitted && categories.ToolTesting.TestToolDiscovery && (categories.ToolTesting.TestToolExecution || categories.ToolTesting.TestParameterValidation))
        {
            var tools = ToolScenarioInputs.CaptureScenarios(categories.ToolTesting);
            var attempts = tools.Values.Sum(scenario => 1 + scenario.ClientInputRounds.Count);
            stages.Add(new("explicit-tools", attempts, attempts, "Admitted explicit plans only, excluding transport retries and other discovered tools."));
            stages.Add(new("discovered-tools", 0, null, "Catalog and execution admission determine other tool invocations."));
        }
        else if (categories.ToolTesting.CustomScenarios.Count > 0)
            stages.Add(new("explicit-tools-blocked", 0, 0, "Requested tool work is not admitted by the selected execution policy/category configuration."));
        if (promptsAdmitted && categories.PromptTesting.TestPromptDiscovery && categories.PromptTesting.TestPromptExecution)
        {
            var prompts = ToolScenarioInputs.CapturePromptScenarios(categories.PromptTesting);
            var attempts = prompts.Values.Sum(scenario => 1 + scenario.ClientInputRounds.Count);
            stages.Add(new("explicit-prompts", attempts, attempts, "Admitted explicit plans only, excluding retries and other discovered prompts."));
            stages.Add(new("discovered-prompts", 0, null, "Catalog arguments and policy determine other prompt invocations."));
        }
        else if (categories.PromptTesting.CustomScenarios.Count > 0)
            stages.Add(new("explicit-prompts-blocked", 0, 0, "Requested prompt work is not admitted by the selected execution policy/category configuration."));
        if (McpOperationAdmission.IsAllowed(policy.Mode, policy.ConfirmElevatedRisk, Constants.ValidationConstants.Methods.ResourcesRead) &&
            (categories.ResourceTesting.TestResourceReading || categories.ResourceTesting.TestSubscriptions))
            stages.Add(new("resources", 0, null, "Resource inventory, subscriptions and URI policy determine admitted work."));
        if (categories.ProtocolCompliance.TestJsonRpcCompliance)
            stages.Add(new("protocol", 0, null, "Revision-specific probes and negotiated optional features determine attempts."));
        if (categories.SecurityTesting.TestInputValidation)
            stages.Add(new("security", 0, null, "Authentication and admitted negative controls vary with target capabilities."));
        if (categories.PerformanceTesting.TestConcurrentRequests || categories.PerformanceTesting.TestResponseTimes || categories.PerformanceTesting.TestThroughput)
            stages.Add(new("performance", 0, null, "Duration/concurrency are workload settings, not a known request total."));
        stages.Add(new("retries-and-auxiliary", 0, null, "Retries, recovery, extension reads and auth discovery share the actual outbound limit."));
        stages.Add(new("signed-observer-import", 0, 0, "Import itself is offline; external collection costs are not estimated here."));
        return new(stages.AsReadOnly(), enforcedLimit);
    }
}
