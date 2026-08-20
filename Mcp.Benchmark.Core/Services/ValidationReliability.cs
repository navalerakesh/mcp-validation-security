using System.Net;
using Mcp.Benchmark.Core.Models;
using ModelContextProtocol.Protocol;
using ValidatorJsonRpcResponse = Mcp.Benchmark.Core.Models.JsonRpcResponse;

namespace Mcp.Benchmark.Core.Services;

/// <summary>
/// Centralized reliability helpers for authentication failure detection,
/// transient retry classification, and retry backoff decisions.
/// </summary>
public static class ValidationReliability
{
    public const int DefaultRpcMaxAttempts = 4;
    public const int DefaultPerformanceCalibrationAttempts = 3;

    public static bool IsAuthenticationStatusCode(int statusCode)
    {
        return statusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden;
    }

    public static bool IsAuthenticationFailure(HealthCheckResult result)
    {
        return result.Disposition == HealthCheckDisposition.Protected ||
               result.InitializationDetails?.Transport.StatusCode is int statusCode && IsAuthenticationStatusCode(statusCode);
    }

    public static bool IsAuthenticationFailure(Exception exception)
    {
        var current = exception;
        while (current != null)
        {
            if (current is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    public static bool IsSoftHealthFailure(string? errorMessage)
    {
        return false;
    }

    public static HealthCheckDisposition ClassifyHealthCheck(TransportResult<InitializeResult>? result)
    {
        if (result == null)
        {
            return HealthCheckDisposition.Unknown;
        }

        if (result.IsSuccessful)
        {
            return HealthCheckDisposition.Healthy;
        }

        if (result.Transport.StatusCode is int statusCode)
        {
            if (IsAuthenticationStatusCode(statusCode))
            {
                return HealthCheckDisposition.Protected;
            }

            if (IsTransientHealthStatusCode(statusCode))
            {
                return HealthCheckDisposition.TransientFailure;
            }

            if (statusCode == (int)HttpStatusCode.MethodNotAllowed)
            {
                return HealthCheckDisposition.Inconclusive;
            }
        }

        return HealthCheckDisposition.Unhealthy;
    }

    public static HealthCheckDisposition ClassifyHealthCheck(Exception exception, CancellationToken cancellationToken)
    {
        if (IsAuthenticationFailure(exception))
        {
            return HealthCheckDisposition.Protected;
        }

        if (exception is OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? HealthCheckDisposition.TransientFailure
                : HealthCheckDisposition.TransientFailure;
        }

        if (exception is HttpRequestException { StatusCode: HttpStatusCode statusCode } && IsTransientHealthStatusCode((int)statusCode))
        {
            return HealthCheckDisposition.TransientFailure;
        }

        if (exception is HttpRequestException { StatusCode: null }) return HealthCheckDisposition.TransientFailure;

        return HealthCheckDisposition.Unhealthy;
    }

    public static bool ShouldAllowDeferredValidation(HealthCheckResult result)
    {
        return result.AllowsDeferredValidation;
    }

    public static bool ShouldRetryRpcResponse(ValidatorJsonRpcResponse? response)
    {
        if (response == null)
        {
            return false;
        }

        if (response.StatusCode <= 0)
        {
            return response.ProbeContext?.ResponseClassification is
                ProbeResponseClassification.TransientFailure or
                ProbeResponseClassification.TransportFailure or
                ProbeResponseClassification.Timeout or
                ProbeResponseClassification.NoResponse;
        }

        return IsRetryableHttpStatusCode(response.StatusCode);
    }

    public static string DescribeRetryableResponse(ValidatorJsonRpcResponse? response, string fallback = "Transient transport failure")
    {
        if (response == null)
        {
            return fallback;
        }

        if (response.StatusCode > 0)
        {
            return $"HTTP {response.StatusCode}";
        }

        if (response.ProbeContext?.ResponseClassification is { } classification and not ProbeResponseClassification.Unknown)
        {
            return classification.ToString();
        }

        return fallback;
    }

    public static bool ShouldRetryException(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception switch
        {
            OperationCanceledException => true,
            HttpRequestException => true,
            _ => false
        };
    }

    public static bool IsRetryableHttpStatusCode(int statusCode)
    {
        return statusCode is
            (int)HttpStatusCode.RequestTimeout or
            425 or
            (int)HttpStatusCode.TooManyRequests or
            (int)HttpStatusCode.BadGateway or
            (int)HttpStatusCode.ServiceUnavailable or
            (int)HttpStatusCode.GatewayTimeout;
    }

    public static bool IsTransientHealthStatusCode(int statusCode)
    {
        return statusCode is
            (int)HttpStatusCode.RequestTimeout or
            425 or
            (int)HttpStatusCode.TooManyRequests or
            (int)HttpStatusCode.BadGateway or
            (int)HttpStatusCode.ServiceUnavailable or
            (int)HttpStatusCode.GatewayTimeout;
    }

    public static bool IsRetryableTransportError(string? errorMessage)
    {
        return false;
    }

    public static TimeSpan GetRetryDelay(int attemptNumber, IReadOnlyDictionary<string, string>? headers = null)
    {
        if (attemptNumber < 1)
        {
            attemptNumber = 1;
        }

        if (headers != null)
        {
            foreach (var headerName in new[] { "Retry-After", "retry-after" })
            {
                if (!headers.TryGetValue(headerName, out var headerValue) || string.IsNullOrWhiteSpace(headerValue))
                {
                    continue;
                }

                if (int.TryParse(headerValue, out var seconds) && seconds >= 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }

                if (DateTimeOffset.TryParse(headerValue, out var retryAt))
                {
                    var delay = retryAt - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero)
                    {
                        return delay;
                    }
                }
            }
        }

        var exponentialSeconds = Math.Min(8, Math.Pow(2, attemptNumber - 1));
        return TimeSpan.FromSeconds(exponentialSeconds);
    }

    public static bool ShouldRetryPerformanceCalibration(
        int attemptNumber,
        int maxAttempts,
        int totalRequests,
        int successfulRequests,
        int rateLimitedRequests,
        int transientFailures)
    {
        if (attemptNumber >= maxAttempts || totalRequests <= 0)
        {
            return false;
        }

        var transientRatio = (double)(rateLimitedRequests + transientFailures) / totalRequests;
        if (rateLimitedRequests > 0 && successfulRequests > 0)
        {
            return true;
        }

        return successfulRequests == 0 && transientRatio >= 0.5;
    }

    public static int GetReducedConcurrency(int currentConcurrency)
    {
        if (currentConcurrency <= 1)
        {
            return 1;
        }

        return Math.Max(1, currentConcurrency / 2);
    }

}