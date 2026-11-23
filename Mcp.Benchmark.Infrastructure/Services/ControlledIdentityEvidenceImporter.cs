using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;
using Mcp.Benchmark.Infrastructure.Scenarios;

namespace Mcp.Benchmark.Infrastructure.Services;

public sealed class ControlledIdentityEvidenceImporter
{
    public ControlledIdentityScenarioPack ImportPack(string json, ControlledIdentityMatrixContract admittedContract,
        ControlledIdentityAdmission admission, string trustedObserverPublicKeyPem, DateTimeOffset assessedAt, CancellationToken cancellationToken = default) =>
        new(Import(json, admittedContract, admission, trustedObserverPublicKeyPem, assessedAt, cancellationToken), admission);

    public ControlledIdentityMatrixAssessment Import(string json, ControlledIdentityMatrixContract admittedContract,
        ControlledIdentityAdmission admission, string trustedObserverPublicKeyPem, DateTimeOffset assessedAt, CancellationToken cancellationToken = default)
        => ControlledIdentityEvidenceVerifier.Verify(json, admittedContract, admission, trustedObserverPublicKeyPem, assessedAt, cancellationToken);
}