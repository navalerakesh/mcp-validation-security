using System.Text.Json;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Constants;
using System.Text;

namespace Mcp.Benchmark.Core.Models;

public sealed class ValidationRunRequest
{
    private readonly byte[] _configurationUtf8;

    private ValidationRunRequest(McpValidatorConfiguration configuration, string? requestId, string? conformanceEvidenceJson = null,
        ControlledIdentitySecurityRequirement? identityControls = null)
    {
        if (configuration.Validation?.Categories?.ToolTesting is { } tools)
            ToolScenarioInputs.CaptureScenarios(tools);
        if (configuration.Validation?.Categories?.PromptTesting is { } prompts)
            ToolScenarioInputs.CapturePromptScenarios(prompts);
        if (conformanceEvidenceJson != null && (conformanceEvidenceJson.Length > OfficialConformanceContract.MaximumEvidenceBytes ||
            Encoding.UTF8.GetByteCount(conformanceEvidenceJson) > OfficialConformanceContract.MaximumEvidenceBytes))
            throw new ArgumentException("Conformance evidence exceeds the admitted byte limit.", nameof(conformanceEvidenceJson));
        ConformanceEvidenceJson = conformanceEvidenceJson;
        if (identityControls != null)
        {
            if (identityControls.Contract?.Cases is not { Count: > 0 and <= ControlledIdentityMatrixContract.MaximumCases } ||
                identityControls.SignedEvidenceJson == null || identityControls.TrustedObserverPublicKeyPem == null ||
                identityControls.SignedEvidenceJson.Length > ExecutionPolicyDefaults.DefaultMaxResponseBytes ||
                Encoding.UTF8.GetByteCount(identityControls.SignedEvidenceJson) > ExecutionPolicyDefaults.DefaultMaxResponseBytes ||
                identityControls.TrustedObserverPublicKeyPem.Length > 16_384)
                throw new ArgumentException("Identity baseline exceeds the admitted bounds.", nameof(identityControls));
            IdentityControls = identityControls with
            {
                Contract = identityControls.Contract with { Cases = Array.AsReadOnly(identityControls.Contract.Cases.ToArray()) }
            };
        }
        RequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        Target = configuration.Server.Endpoint ?? string.Empty;
        TargetIdentitySha256 = ValidationTargetIdentity.ComputeDigest(configuration.Server);
        Transport = configuration.Server.Transport;
        OperationPolicy = OperationPolicySnapshot.From(configuration.Execution ?? new ExecutionPolicy());
        _configurationUtf8 = JsonSerializer.SerializeToUtf8Bytes(configuration);
    }

    public string RequestId { get; }

    public string Target { get; }

    public string TargetIdentitySha256 { get; }

    public string Transport { get; }

    public OperationPolicySnapshot OperationPolicy { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? ConformanceEvidenceJson { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ControlledIdentitySecurityRequirement? IdentityControls { get; }

    public ValidationRunRequest WithIdentityControls(ControlledIdentitySecurityRequirement identityControls)
    {
        ArgumentNullException.ThrowIfNull(identityControls);
        return new ValidationRunRequest(CreateConfiguration(), RequestId, ConformanceEvidenceJson, identityControls);
    }

    public static ValidationRunRequest CaptureWithConformance(McpValidatorConfiguration configuration, string conformanceEvidenceJson, string? requestId = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Server);
        ArgumentNullException.ThrowIfNull(conformanceEvidenceJson);
        return new ValidationRunRequest(configuration, requestId, conformanceEvidenceJson);
    }

    public static ValidationRunRequest Capture(McpValidatorConfiguration configuration, string? requestId = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Server);
        return new ValidationRunRequest(configuration, requestId);
    }

    public McpValidatorConfiguration CreateConfiguration()
    {
        return JsonSerializer.Deserialize<McpValidatorConfiguration>(_configurationUtf8)
            ?? throw new InvalidOperationException("The captured validation configuration could not be materialized.");
    }
}