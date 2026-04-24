using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Registries;

public sealed class ProtocolFeatureResolver : IProtocolFeatureResolver
{
    private readonly IValidationPackRegistry<IProtocolFeaturePack> _packRegistry;

    public ProtocolFeatureResolver(IValidationPackRegistry<IProtocolFeaturePack> packRegistry)
    {
        _packRegistry = packRegistry ?? throw new ArgumentNullException(nameof(packRegistry));
    }

    public ProtocolFeatureSet Resolve(ValidationApplicabilityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var pack = _packRegistry.Resolve(context).FirstOrDefault();
        if (pack == null)
        {
            throw new InvalidOperationException($"No protocol feature pack matched protocol version '{context.NegotiatedProtocolVersion}'.");
        }

        return pack.BuildFeatureSet(context);
    }
}