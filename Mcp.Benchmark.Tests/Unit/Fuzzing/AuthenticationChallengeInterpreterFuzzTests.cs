using FsCheck;
using FsCheck.Xunit;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Fuzzing;

public class AuthenticationChallengeInterpreterFuzzTests
{
    [Property(MaxTest = 100)]
    public void TryGetHeaderValue_ShouldFindHeadersIgnoringCase(NonEmptyString headerName, NonEmptyString headerValue)
    {
        var normalizedName = ToToken(headerName.Get);
        var normalizedValue = ToHeaderValue(headerValue.Get);
        var headers = new Dictionary<string, string>
        {
            [normalizedName.ToUpperInvariant()] = normalizedValue
        };

        var result = AuthenticationChallengeInterpreter.TryGetHeaderValue(headers, normalizedName.ToLowerInvariant());

        result.Should().Be(normalizedValue);
    }

    [Property(MaxTest = 100)]
    public void ExtractQuotedParameter_ShouldRoundTripSanitizedBearerParameters(NonEmptyString parameterName, NonEmptyString parameterValue)
    {
        var normalizedName = ToToken(parameterName.Get);
        var normalizedValue = ToHeaderValue(parameterValue.Get);
        var header = $"Bearer {normalizedName}=\"{normalizedValue}\", realm=\"mcp\"";

        var result = AuthenticationChallengeInterpreter.ExtractQuotedParameter(header, normalizedName);

        result.Should().Be(normalizedValue);
    }

    [Property(MaxTest = 100)]
    public void CalculateCoverageRatio_ShouldStayWithinExpectedBounds(NonNegativeInt affectedComponents, NonNegativeInt totalComponents)
    {
        var affected = affectedComponents.Get;
        var total = totalComponents.Get;

        var result = ValidationFindingAggregator.CalculateCoverageRatio(affected, total);

        result.Should().BeInRange(0.0, 1.0);

        if (affected == 0)
        {
            result.Should().Be(0.0);
        }
        else if (total == 0)
        {
            result.Should().Be(1.0);
        }
    }

    private static string ToToken(string value)
    {
        var tokenChars = value
            .Where(char.IsLetterOrDigit)
            .Take(32)
            .ToArray();

        return tokenChars.Length == 0 ? "resource_metadata" : new string(tokenChars);
    }

    private static string ToHeaderValue(string value)
    {
        var sanitizedValue = value
            .Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();

        return sanitizedValue.Length == 0 ? "https://example.test/resource" : sanitizedValue;
    }
}
