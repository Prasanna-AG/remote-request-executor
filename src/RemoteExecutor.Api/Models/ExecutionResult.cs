/// <summary>
/// Result of a single execution attempt
/// </summary>
public class ExecutionResult
{
    public bool IsSuccess { get; init; }
    public bool IsTransientFailure { get; init; } = false;
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int Attempt { get; set; }

    // HTTP fields
    public int? StatusCode { get; init; }
    public Dictionary<string, string>? ResponseHeaders { get; init; }
    public string? ResponseBody { get; init; }

    // PowerShell fields
    public string? PsCommand { get; init; }
    public List<string>? PsStdout { get; init; }
    public List<string>? PsStderr { get; init; }
    public List<object>? PsObjects { get; init; }

    // Factory methods
    public static ExecutionResult FromHttp(int status, Dictionary<string, string> headers, string body, int[]? transientStatusCodes = null)
    {
        var isTransient = IsTransientHttpStatus(status, transientStatusCodes ?? new[] { 408, 429, 500, 502, 503, 504 });
        // Success range: 200-299 (per RFC 7231 Section 6.3)
        // Rationale: HTTP status codes 2xx indicate successful request processing.
        // This includes 200 (OK), 201 (Created), 204 (No Content), etc.
        return new() 
        { 
            IsSuccess = status >= 200 && status < 300, 
            IsTransientFailure = isTransient, 
            StatusCode = status, 
            ResponseHeaders = headers, 
            ResponseBody = body 
        };
    }

    public static ExecutionResult FromError(string code, string message, bool isTransient) =>
        new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message, IsTransientFailure = isTransient };

    public static ExecutionResult FromPowerShell(string cmd, List<string> stdout, List<string> stderr, List<object>? objects = null) =>
        new() { IsSuccess = true, PsCommand = cmd, PsStdout = stdout, PsStderr = stderr, PsObjects = objects };

    private static bool IsTransientHttpStatus(int status, int[] transientCodes)
    {
        return transientCodes.Contains(status);
    }
}
