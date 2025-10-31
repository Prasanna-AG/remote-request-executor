using Microsoft.Extensions.Options;
using RemoteExecutor.Api.Configuration;

/// <summary>
/// Validation result with structured error codes
/// </summary>
public record ValidationResult(bool IsValid, string? ErrorCode, string? Message)
{
    public static ValidationResult Success() => new(true, null, null);
    public static ValidationResult Fail(string errorCode, string message) => new(false, errorCode, message);
}

/// <summary>
/// Request validation with configurable limits and structured error responses
/// </summary>
public class RequestValidator
{
    private readonly int _maxBodySizeBytes;

    public RequestValidator(IOptions<ServiceConfiguration> config)
    {
        _maxBodySizeBytes = config.Value.MaxRequestBodySizeKb * 1024;
    }

    public ValidationResult Validate(RequestEnvelope? r)
    {
        if (r == null) 
            return ValidationResult.Fail("NullRequest", "Request envelope is null");
        
        if (string.IsNullOrEmpty(r.RequestId)) 
            return ValidationResult.Fail("MissingRequestId", "RequestId is required");
        
        // Validate PowerShell-specific requirements
        if (r.Headers != null && r.Headers.TryGetValue("X-Executor-Type", out var et) && et == "powershell")
        {
            if (!r.Headers.ContainsKey("X-PS-Command")) 
                return ValidationResult.Fail("MissingPsCommand", "X-PS-Command header required for PowerShell executor");
        }
        
        // Validate HTTP-specific requirements
        if (r.Headers != null && r.Headers.TryGetValue("X-Executor-Type", out var et2) && et2 == "http")
        {
            if (!r.Headers.ContainsKey("X-Forward-Base")) 
                return ValidationResult.Fail("MissingForwardBase", "X-Forward-Base header required for HTTP executor");
        }
        
        // Validate body size using Content-Length header if available
        if (r.Headers != null && r.Headers.TryGetValue("Content-Length", out var contentLengthValue)
            && long.TryParse(contentLengthValue, out var contentLengthBytes)
            && contentLengthBytes > _maxBodySizeBytes)
        {
            return ValidationResult.Fail("BodyTooLarge", $"Request body exceeds maximum size of {_maxBodySizeBytes / 1024}KB");
        }

        // Validate body size by measured string length as a fallback
        if (r.Body != null && r.Body.Length > _maxBodySizeBytes)
            return ValidationResult.Fail("BodyTooLarge", $"Request body exceeds maximum size of {_maxBodySizeBytes / 1024}KB");
        
        // Validate HTTP method
        var validMethods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
        if (!validMethods.Contains(r.Method, StringComparer.OrdinalIgnoreCase))
            return ValidationResult.Fail("InvalidHttpMethod", $"HTTP method '{r.Method}' is not supported");
        
        return ValidationResult.Success();
    }
}
