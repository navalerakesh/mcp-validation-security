using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

internal static class ControlledEvidenceAdmission
{
    internal static bool IsValidBinding(ControlledOperationBinding? binding) => binding != null &&
        new[] { binding.RunId, binding.OperationId, binding.AttemptId, binding.RequestId, binding.BoundaryId, binding.ActorAlias, binding.ObjectAlias }
            .All(IsValidIdentifier) &&
        new[] { binding.TargetIdentitySha256, binding.RouteIdentitySha256, binding.ArgumentsSha256 }.All(IsSha256);

    internal static bool IsValidIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= ControlledStateScenarioContract.MaximumIdentifierCharacters &&
        value.All(character => !char.IsControl(character));

    internal static bool IsSha256(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}