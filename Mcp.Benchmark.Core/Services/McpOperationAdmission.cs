using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class McpOperationAdmission
{
    public static bool IsAllowed(OperationPolicySnapshot policy, string? method)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Mode switch
        {
            ExecutionMode.Safe => ValidationReliability.IsDiscoveryRpcMethod(method) || method is
                ValidationConstants.Methods.Initialize or ValidationConstants.Methods.Ping or
                McpSpecConstants.InitializedNotification or McpSpecConstants.CancelledNotification,
            ExecutionMode.Standard => true,
            ExecutionMode.Elevated => policy.ConfirmElevatedRisk,
            _ => false
        };
    }
}