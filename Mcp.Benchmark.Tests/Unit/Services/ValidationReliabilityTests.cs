using System.Net;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ValidationReliabilityTests
{
    [Fact]
    public void HealthClassification_ShouldIgnoreAuthenticationAndTransientWordsWithoutTypedStatus()
    {
        var targetText = new TransportResult<InitializeResult>
        {
            IsSuccessful = false,
            Error = "HTTP 400 body says 401 unauthorized, timeout, and 503 service unavailable",
            Transport = new TransportMetadata { StatusCode = 400 }
        };
        var health = new HealthCheckResult
        {
            Disposition = HealthCheckDisposition.Unhealthy,
            ErrorMessage = "401 unauthorized",
            InitializationDetails = targetText
        };

        ValidationReliability.ClassifyHealthCheck(targetText).Should().Be(HealthCheckDisposition.Unhealthy);
        ValidationReliability.IsAuthenticationFailure(health).Should().BeFalse();
        ValidationReliability.IsAuthenticationFailure(new InvalidOperationException("401 unauthorized")).Should().BeFalse();
    }

    [Fact]
    public void HealthClassification_ShouldUseTypedHttpStatus()
    {
        ValidationReliability.ClassifyHealthCheck(
            new HttpRequestException("arbitrary", null, HttpStatusCode.Unauthorized),
            CancellationToken.None).Should().Be(HealthCheckDisposition.Protected);
        ValidationReliability.ClassifyHealthCheck(
            new HttpRequestException("arbitrary", null, HttpStatusCode.ServiceUnavailable),
            CancellationToken.None).Should().Be(HealthCheckDisposition.TransientFailure);
    }

    [Fact]
    public void RetryDescription_ShouldNeverEchoTargetErrorOrRawBody()
    {
        var response = new Mcp.Benchmark.Core.Models.JsonRpcResponse
        {
            StatusCode = 503,
            Error = "secret-error-canary",
            RawJson = "secret-body-canary"
        };

        ValidationReliability.DescribeRetryableResponse(response).Should().Be("HTTP 503");
    }

    [Fact]
    public void StatuslessRetry_ShouldRequireTypedTransientClassification()
    {
        ValidationReliability.ShouldRetryRpcResponse(new Mcp.Benchmark.Core.Models.JsonRpcResponse
        {
            StatusCode = -1,
            ProbeContext = new ProbeContext { ResponseClassification = ProbeResponseClassification.ProtocolError }
        }).Should().BeFalse();
        ValidationReliability.ShouldRetryRpcResponse(new Mcp.Benchmark.Core.Models.JsonRpcResponse
        {
            StatusCode = -1,
            ProbeContext = new ProbeContext { ResponseClassification = ProbeResponseClassification.Timeout }
        }).Should().BeTrue();
    }
}