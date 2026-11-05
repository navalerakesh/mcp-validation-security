using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ValidationObservationMergerTests
{
    [Fact]
    public void AddDistinct_ShouldPreserveTypedEvidenceAndDisambiguateIds()
    {
        var target = new List<ValidationObservation>
        {
            new()
            {
                Id = "extension-observation",
                LayerId = "existing",
                Component = "existing",
                ObservationKind = "existing"
            }
        };
        var probe = new ProbeContext
        {
            ProbeId = "probe-1",
            Confidence = EvidenceConfidenceLevel.High
        };

        ValidationObservationMerger.AddDistinct(target,
        [
            new ValidationObservation
            {
                Id = "extension-observation",
                LayerId = "extension/apps",
                Component = "ui://demo/weather",
                ObservationKind = "apps-ui-resource-analysis",
                ScenarioId = "apps-linked-resource-security",
                Lane = EvaluationLane.ExtensionReadiness,
                Origin = EvidenceOrigin.DeterministicObservation,
                Metadata = new Dictionary<string, string> { ["contentSha256"] = new string('a', 64) },
                ProbeContexts = [probe]
            }
        ]);

        target.Should().HaveCount(2);
        target[1].Should().Match<ValidationObservation>(observation =>
            observation.Id == "extension-observation-2" &&
            observation.Lane == EvaluationLane.ExtensionReadiness &&
            observation.Origin == EvidenceOrigin.DeterministicObservation &&
            observation.ScenarioId == "apps-linked-resource-security");
        target[1].ProbeContexts.Should().ContainSingle().Which.Should().BeSameAs(probe);
    }
}