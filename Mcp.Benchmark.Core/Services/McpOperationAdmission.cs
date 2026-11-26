using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class McpOperationAdmission
{
    public static bool IsAllowed(OperationPolicySnapshot policy, string? method)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return IsAllowed(policy.Mode, policy.ConfirmElevatedRisk, method);
    }

    public static bool IsAllowed(ExecutionMode mode, bool confirmElevatedRisk, string? method)
    {
        return mode switch
        {
            ExecutionMode.Safe => ValidationReliability.IsDiscoveryRpcMethod(method) || method is
                ValidationConstants.Methods.Initialize or ValidationConstants.Methods.Ping or
                McpSpecConstants.InitializedNotification or McpSpecConstants.CancelledNotification,
            ExecutionMode.Standard => true,
            ExecutionMode.Elevated => confirmElevatedRisk,
            _ => false
        };
    }
}