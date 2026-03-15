namespace Mcp.Benchmark.Core.Constants;

/// <summary>
/// JSON-RPC 2.0 specification constants and references.
/// Official JSON-RPC 2.0 Specification: https://www.jsonrpc.org/specification
/// </summary>
public static class JsonRpcConstants
{
    /// <summary>
    /// JSON-RPC 2.0 version identifier (MUST be exactly "2.0").
    /// Reference: https://www.jsonrpc.org/specification#request_object
    /// </summary>
    public const string Version = "2.0";

    /// <summary>
    /// Standard JSON-RPC 2.0 error codes as defined in the specification.
    /// Reference: https://www.jsonrpc.org/specification#error_object
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>
        /// Invalid JSON was received by the server.
        /// An error occurred on the server while parsing the JSON text.
        /// </summary>
        public const int ParseError = -32700;

        /// <summary>
        /// The JSON sent is not a valid Request object.
        /// Reference: https://www.jsonrpc.org/specification#request_object
        /// </summary>
        public const int InvalidRequest = -32600;

        /// <summary>
        /// The method does not exist / is not available.
        /// </summary>
        public const int MethodNotFound = -32601;

        /// <summary>
        /// Invalid method parameter(s).
        /// Reference: https://www.jsonrpc.org/specification#parameter_structures
        /// </summary>
        public const int InvalidParams = -32602;

        /// <summary>
        /// Internal JSON-RPC error.
        /// </summary>
        public const int InternalError = -32603;

        /// <summary>
        /// Reserved for implementation-defined server-errors.
        /// Range: -32000 to -32099
        /// </summary>
        public const int ServerErrorStart = -32099;
        
        /// <summary>
        /// End of reserved range for implementation-defined server-errors.
        /// </summary>
        public const int ServerErrorEnd = -32000;
    }

    /// <summary>
    /// Standard JSON-RPC 2.0 error messages corresponding to error codes.
    /// Reference: https://www.jsonrpc.org/specification#error_object
    /// </summary>
    public static class ErrorMessages
    {
        /// <summary>Parse error message for code -32700</summary>
        public const string ParseError = "Parse error";
        /// <summary>Invalid Request message for code -32600</summary>
        public const string InvalidRequest = "Invalid Request";
        /// <summary>Method not found message for code -32601</summary>
        public const string MethodNotFound = "Method not found";
        /// <summary>Invalid params message for code -32602</summary>
        public const string InvalidParams = "Invalid params";
        /// <summary>Internal error message for code -32603</summary>
        public const string InternalError = "Internal error";
    }

    /// <summary>
    /// Required fields in JSON-RPC 2.0 request objects.
    /// Reference: https://www.jsonrpc.org/specification#request_object
    /// </summary>
    public static class RequestFields
    {
        /// <summary>JSON-RPC version field (MUST be "2.0")</summary>
        public const string JsonRpc = "jsonrpc";
        /// <summary>Method name to be invoked</summary>
        public const string Method = "method";
        /// <summary>Parameter values to be used during invocation</summary>
        public const string Params = "params";
        /// <summary>Identifier established by the client</summary>
        public const string Id = "id";
    }

    /// <summary>
    /// Required fields in JSON-RPC 2.0 response objects.
    /// Reference: https://www.jsonrpc.org/specification#response_object
    /// </summary>
    public static class ResponseFields
    {
        /// <summary>JSON-RPC version field (MUST be "2.0")</summary>
        public const string JsonRpc = "jsonrpc";
        /// <summary>Result of the method invocation</summary>
        public const string Result = "result";
        /// <summary>Error object when method invocation fails</summary>
        public const string Error = "error";
        /// <summary>Identifier matching the request</summary>
        public const string Id = "id";
    }

    /// <summary>
    /// Required fields in JSON-RPC 2.0 error objects.
    /// Reference: https://www.jsonrpc.org/specification#error_object
    /// </summary>
    public static class ErrorFields
    {
        /// <summary>Numeric error code</summary>
        public const string Code = "code";
        /// <summary>Human-readable error message</summary>
        public const string Message = "message";
        /// <summary>Additional error information (optional)</summary>
        public const string Data = "data";
    }

    /// <summary>
    /// Validates if an error code is a standard JSON-RPC 2.0 error code.
    /// Reference: https://www.jsonrpc.org/specification#error_object
    /// </summary>
    /// <param name="errorCode">The error code to validate</param>
    /// <returns>True if the error code is a standard JSON-RPC 2.0 error code</returns>
    public static bool IsStandardErrorCode(int errorCode)
    {
        return errorCode switch
        {
            ErrorCodes.ParseError => true,
            ErrorCodes.InvalidRequest => true,
            ErrorCodes.MethodNotFound => true,
            ErrorCodes.InvalidParams => true,
            ErrorCodes.InternalError => true,
            _ when errorCode >= ErrorCodes.ServerErrorEnd && errorCode <= ErrorCodes.ServerErrorStart => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets the standard error message for a JSON-RPC 2.0 error code.
    /// Reference: https://www.jsonrpc.org/specification#error_object
    /// </summary>
    /// <param name="errorCode">The error code</param>
    /// <returns>The standard error message, or null if not a standard error code</returns>
    public static string? GetStandardErrorMessage(int errorCode)
    {
        return errorCode switch
        {
            ErrorCodes.ParseError => ErrorMessages.ParseError,
            ErrorCodes.InvalidRequest => ErrorMessages.InvalidRequest,
            ErrorCodes.MethodNotFound => ErrorMessages.MethodNotFound,
            ErrorCodes.InvalidParams => ErrorMessages.InvalidParams,
            ErrorCodes.InternalError => ErrorMessages.InternalError,
            _ => null
        };
    }
}
