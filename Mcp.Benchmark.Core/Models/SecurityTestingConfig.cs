namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Configuration for security and penetration testing scenarios.
/// Includes various attack vectors and security validation tests.
/// </summary>
public class SecurityTestingConfig
{
    /// <summary>
    /// Gets or sets whether to perform input validation tests.
    /// </summary>
    public bool TestInputValidation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test for injection vulnerabilities.
    /// </summary>
    public bool TestInjectionAttacks { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test authentication bypass attempts.
    /// </summary>
    public bool TestAuthenticationBypass { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test for buffer overflow vulnerabilities.
    /// </summary>
    public bool TestBufferOverflow { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test malformed message handling.
    /// </summary>
    public bool TestMalformedMessages { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test resource exhaustion attacks.
    /// </summary>
    public bool TestResourceExhaustion { get; set; } = true;

    /// <summary>
    /// Gets or sets custom security test payloads.
    /// </summary>
    public List<SecurityTestPayload> CustomPayloads { get; set; } = new();

    /// <summary>
    /// Gets or sets the maximum time to spend on security testing in seconds.
    /// </summary>
    public int MaxTestDurationSeconds { get; set; } = 300;
}

/// <summary>
/// Represents a custom security test payload for targeted vulnerability testing.
/// </summary>
public class SecurityTestPayload
{
    /// <summary>
    /// Gets or sets the name/description of the security test.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target method or endpoint to test.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the payload data to send.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expected response pattern (for detecting vulnerabilities).
    /// </summary>
    public string? ExpectedResponse { get; set; }

    /// <summary>
    /// Gets or sets whether this test should expect the server to reject the payload.
    /// </summary>
    public bool ShouldReject { get; set; } = true;
}

/// <summary>
/// Configuration for performance and load testing scenarios.
/// </summary>
public class PerformanceTestingConfig
{
    /// <summary>
    /// Gets or sets whether to perform concurrent request testing.
    /// </summary>
    public bool TestConcurrentRequests { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test response time benchmarks.
    /// </summary>
    public bool TestResponseTimes { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test memory usage under load.
    /// </summary>
    public bool TestMemoryUsage { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test throughput limits.
    /// </summary>
    public bool TestThroughput { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of concurrent connections to test.
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 100;

    /// <summary>
    /// Gets or sets the duration of load testing in seconds.
    /// </summary>
    public int LoadTestDurationSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the acceptable response time threshold in milliseconds.
    /// </summary>
    public int ResponseTimeThresholdMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets custom performance test scenarios.
    /// </summary>
    public List<PerformanceTestScenario> CustomScenarios { get; set; } = new();
}

/// <summary>
/// Represents a custom performance test scenario.
/// </summary>
public class PerformanceTestScenario
{
    /// <summary>
    /// Gets or sets the name of the performance test.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of requests to send.
    /// </summary>
    public int RequestCount { get; set; } = 100;

    /// <summary>
    /// Gets or sets the number of concurrent users to simulate.
    /// </summary>
    public int ConcurrentUsers { get; set; } = 10;

    /// <summary>
    /// Gets or sets the target method or operation to test.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the test parameters to use.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Configuration for error handling and resilience testing.
/// </summary>
public class ErrorHandlingConfig
{
    /// <summary>
    /// Gets or sets whether to test invalid method calls.
    /// </summary>
    public bool TestInvalidMethods { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test malformed JSON handling.
    /// </summary>
    public bool TestMalformedJson { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test connection interruption recovery.
    /// </summary>
    public bool TestConnectionInterruption { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test timeout handling.
    /// </summary>
    public bool TestTimeoutHandling { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to test graceful degradation scenarios.
    /// </summary>
    public bool TestGracefulDegradation { get; set; } = true;

    /// <summary>
    /// Gets or sets custom error scenarios to test.
    /// </summary>
    public List<ErrorTestScenario> CustomErrorScenarios { get; set; } = new();
}

/// <summary>
/// Represents a custom error handling test scenario.
/// </summary>
public class ErrorTestScenario
{
    /// <summary>
    /// Gets or sets the name of the error test.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of error to simulate.
    /// </summary>
    public string ErrorType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the error condition to create.
    /// </summary>
    public string ErrorCondition { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expected error response code.
    /// </summary>
    public int? ExpectedErrorCode { get; set; }

    /// <summary>
    /// Gets or sets whether the server should recover from this error.
    /// </summary>
    public bool ShouldRecover { get; set; } = true;
}
