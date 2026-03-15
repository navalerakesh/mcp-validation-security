using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Mcp.Benchmark.Core.Resources;

/// <summary>
/// Centralized message resource manager for validation messages and localization.
/// Provides type-safe access to validation messages, status indicators, and user-facing text.
/// </summary>
public static class ValidationMessages
{
    /// <summary>
    /// Validation status messages with appropriate emoji indicators.
    /// </summary>
    public static class Status
    {
        public static string Passed => Strings.Status_Passed;
        public static string Failed => Strings.Status_Failed;
        public static string Warning => Strings.Status_Warning;
        public static string Error => Strings.Status_Error;
        public static string Success => Strings.Status_Success;
        public static string InProgress => Strings.Status_InProgress;
        public static string Skipped => Strings.Status_Skipped;
        public static string Timeout => Strings.Status_Timeout;
        public static string Unknown => Strings.Status_Unknown;
    }

    /// <summary>
    /// Professional validation titles for different test categories.
    /// </summary>
    public static class Titles
    {
        public static string ProtocolCompliance => Strings.Title_ProtocolCompliance;
        public static string JsonRpcCompliance => Strings.Title_JsonRpcCompliance;
        public static string ToolValidation => Strings.Title_ToolValidation;
        public static string SecurityValidation => Strings.Title_SecurityValidation;
        public static string PerformanceValidation => Strings.Title_PerformanceValidation;
        public static string HealthCheck => Strings.Title_HealthCheck;
        public static string CapabilityDiscovery => Strings.Title_CapabilityDiscovery;
    }

    /// <summary>
    /// Validation result categories for reporting.
    /// </summary>
    public static class Categories
    {
        public static string Critical => Strings.Category_Critical;
        public static string Major => Strings.Category_Major;
        public static string Minor => Strings.Category_Minor;
        public static string Informational => Strings.Category_Informational;
        public static string BestPractice => Strings.Category_BestPractice;
    }

    /// <summary>
    /// User-facing progress messages for long-running operations.
    /// </summary>
    public static class Progress
    {
        public static string Initializing => Strings.Progress_Initializing;
        public static string ConnectingToServer => Strings.Progress_ConnectingToServer;
        public static string PerformingHealthCheck => Strings.Progress_PerformingHealthCheck;
        public static string ValidatingProtocol => Strings.Progress_ValidatingProtocol;
        public static string TestingTools => Strings.Progress_TestingTools;
        public static string CheckingSecurity => Strings.Progress_CheckingSecurity;
        public static string MeasuringPerformance => Strings.Progress_MeasuringPerformance;
        public static string GeneratingReport => Strings.Progress_GeneratingReport;
        public static string Complete => Strings.Progress_Complete;
    }

    /// <summary>
    /// Standard error messages for validation failures.
    /// </summary>
    public static class Errors
    {
        public static string ConnectionFailed => Strings.Error_ConnectionFailed;
        public static string TimeoutOccurred => Strings.Error_TimeoutOccurred;
        public static string InvalidConfiguration => Strings.Error_InvalidConfiguration;
        public static string AuthenticationFailed => Strings.Error_AuthenticationFailed;
        public static string UnexpectedError => Strings.Error_UnexpectedError;
        public static string ServerNotResponding => Strings.Error_ServerNotResponding;
        public static string InvalidResponse => Strings.Error_InvalidResponse;
        public static string ProtocolViolation => Strings.Error_ProtocolViolation;
    }

    /// <summary>
    /// Detailed compliance check failure messages.
    /// </summary>
    public static class Compliance
    {
        public static string JsonRpcVersionMissing => Strings.Compliance_JsonRpcVersionMissing;
        public static string InvalidJsonRpcVersion => Strings.Compliance_InvalidJsonRpcVersion;
        public static string MissingRequiredField => Strings.Compliance_MissingRequiredField;
        public static string InvalidFieldType => Strings.Compliance_InvalidFieldType;
        public static string InvalidMethodName => Strings.Compliance_InvalidMethodName;
        public static string InvalidParameterStructure => Strings.Compliance_InvalidParameterStructure;
        public static string ResponseIdMismatch => Strings.Compliance_ResponseIdMismatch;
        public static string UnrecognizedErrorCode => Strings.Compliance_UnrecognizedErrorCode;
        public static string InvalidContentType => Strings.Compliance_InvalidContentType;
        public static string MissingCapability => Strings.Compliance_MissingCapability;
        public static string ProtocolVersionMismatch => Strings.Compliance_ProtocolVersionMismatch;
    }

    /// <summary>
    /// Report section headers.
    /// </summary>
    public static class Report
    {
        public static string ExecutiveSummary => Strings.Report_ExecutiveSummary;
        public static string ValidationResults => Strings.Report_ValidationResults;
        public static string ComplianceScore => Strings.Report_ComplianceScore;
        public static string DetailedFindings => Strings.Report_DetailedFindings;
        public static string Recommendations => Strings.Report_Recommendations;
        public static string TechnicalDetails => Strings.Report_TechnicalDetails;
        public static string Appendices => Strings.Report_Appendices;
    }

    /// <summary>
    /// HTTP header constants.
    /// </summary>
    public static class Headers
    {
        public static string UserAgentPrefix => Strings.Header_UserAgentPrefix;
        public static string AuthorizationBearer => Strings.Header_AuthorizationBearer;
        public static string McpVersionHeader => Strings.Header_McpVersionHeader;
    }

    /// <summary>
    /// Configuration loading messages.
    /// </summary>
    public static class Configuration
    {
        public static string LoadingConfiguration => Strings.Config_LoadingConfiguration;
        public static string ConfigurationLoaded => Strings.Config_ConfigurationLoaded;
        public static string UsingDefaultConfiguration => Strings.Config_UsingDefaultConfiguration;
        public static string InvalidConfigurationFile => Strings.Config_InvalidConfigurationFile;
        public static string ConfigurationValidationFailed => Strings.Config_ConfigurationValidationFailed;
    }

    /// <summary>
    /// Security validation specific messages.
    /// </summary>
    public static class Security
    {
        public static string Assessment => Strings.Security_Assessment;
        public static string InputValidation => Strings.Security_InputValidation;
        public static string AttackSimulation => Strings.Security_AttackSimulation;
        public static string AuthenticationIssue => Strings.Security_AuthenticationIssue;
        public static string RemediationAuth => Strings.Security_Remediation_Auth;
        public static string SmartAuthCompleted => Strings.Security_SmartAuthCompleted;
        public static string ExecutingAdvancedVectors => Strings.Security_ExecutingAdvancedVectors;
        public static string AssessmentCompleted => Strings.Security_AssessmentCompleted;
        
        public static class Defense
        {
            public static string Auth => Strings.Security_Defense_Auth;
            public static string InputValidation => Strings.Security_Defense_InputValidation;
            public static string ServerError => Strings.Security_Defense_ServerError;
            public static string AppLayer => Strings.Security_Defense_AppLayer;
            public static string Network => Strings.Security_Defense_Network;
            public static string Unknown => Strings.Security_Defense_Unknown;
        }

        public static class Recommendation
        {
            public static string InputValidation => Strings.Security_Rec_InputValidation;
            public static string HighSeverity => Strings.Security_Rec_HighSeverity;
            public static string Controls => Strings.Security_Rec_Controls;
            public static string Defense => Strings.Security_Rec_Defense;
            public static string Injection => Strings.Security_Rec_Injection;
        }
    }

    /// <summary>
    /// Protocol validation specific messages.
    /// </summary>
    public static class Protocol
    {
        public static string Compliance => Strings.Protocol_Compliance;
        public static string JsonRpcCompliance => Strings.Protocol_JsonRpcCompliance;
        public static string McpCompliance => Strings.Protocol_McpCompliance;
        public static string ValidationCompleted => Strings.Protocol_ValidationCompleted;
    }
}
