using System;
using System.Collections.Generic;
using Mcp.Benchmark.Core.Abstractions;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Captures the negotiated state required to run validator pipelines.
/// </summary>
public sealed class ValidationSessionContext
{
    public ValidationSessionContext(McpValidatorConfiguration originalConfiguration, McpServerConfig effectiveServer)
    {
        OriginalConfiguration = originalConfiguration ?? throw new ArgumentNullException(nameof(originalConfiguration));
        EffectiveServer = effectiveServer ?? throw new ArgumentNullException(nameof(effectiveServer));
    }

    /// <summary>
    /// The original configuration supplied by the caller. This object should be treated as read-only.
    /// </summary>
    public McpValidatorConfiguration OriginalConfiguration { get; }

    /// <summary>
    /// The effective server configuration used by downstream validators (includes negotiated protocol version, auth changes, etc.).
    /// </summary>
    public McpServerConfig EffectiveServer { get; }

    /// <summary>
    /// Initialization handshake transport details captured during session creation.
    /// </summary>
    public TransportResult<InitializeResult>? InitializationHandshake { get; set; }

    /// <summary>
    /// Calibrated bootstrap health outcome captured before validators run.
    /// </summary>
    public HealthCheckResult? BootstrapHealth { get; set; }

    /// <summary>
    /// Capability snapshot captured up-front so tool/resource/prompt validators can share the same data.
    /// </summary>
    public TransportResult<CapabilitySummary>? CapabilitySnapshot { get; set; }

    /// <summary>
    /// The negotiated MCP protocol version, if the server provided one.
    /// </summary>
    public string? ProtocolVersion { get; set; }

    /// <summary>
    /// Authentication discovery metadata gathered before validation begins.
    /// </summary>
    public AuthDiscoveryInfo? AuthDiscovery { get; set; }

    /// <summary>
    /// Session-level log entries (warnings, diagnostics) generated while building the session.
    /// These are appended to the final ValidationResult for transparency.
    /// </summary>
    public List<ValidationLogEntry> SessionLogs { get; } = new();

    /// <summary>
    /// The profile applied to this validation session.
    /// </summary>
    public McpServerProfile ServerProfile { get; set; } = McpServerProfile.Unspecified;

    /// <summary>
    /// Indicates how the server profile was determined.
    /// </summary>
    public ServerProfileSource ServerProfileSource { get; set; } = ServerProfileSource.Unknown;
}
