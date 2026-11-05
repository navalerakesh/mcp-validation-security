using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services;

internal static class ValidationObservationMerger
{
    public static void AddDistinct(
        ICollection<ValidationObservation> target,
        IEnumerable<ValidationObservation> observations)
    {
        foreach (var observation in observations)
        {
            var effectiveId = observation.Id;
            var suffix = 2;
            while (target.Any(existing => string.Equals(existing.Id, effectiveId, StringComparison.Ordinal)))
            {
                effectiveId = $"{observation.Id}-{suffix++}";
            }

            target.Add(new ValidationObservation
            {
                Id = effectiveId,
                LayerId = observation.LayerId,
                Component = observation.Component,
                ObservationKind = observation.ObservationKind,
                ScenarioId = observation.ScenarioId,
                Lane = observation.Lane,
                Origin = observation.Origin,
                RedactedPayloadPreview = observation.RedactedPayloadPreview,
                Metadata = observation.Metadata,
                ProbeContexts = observation.ProbeContexts
            });
        }
    }
}