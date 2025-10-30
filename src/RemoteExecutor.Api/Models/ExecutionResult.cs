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

    // helpers
    public static ExecutionResult FromHttp(int status, Dictionary<string, string> headers, string body) =>
        new() { IsSuccess = status >= 200 && status < 300, IsTransientFailure = status >= 500, StatusCode = status, ResponseHeaders = headers, ResponseBody = body };

    public static ExecutionResult FromError(string code, string message, bool isTransient) =>
        new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message, IsTransientFailure = isTransient };

    public static ExecutionResult FromPowerShell(string cmd, List<string> stdout, List<string> stderr) =>
        new() { IsSuccess = true, PsCommand = cmd, PsStdout = stdout, PsStderr = stderr };
}
