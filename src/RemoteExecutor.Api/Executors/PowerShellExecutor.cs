public class PowerShellExecutor : IExecutor
{
    public string Name => "powershell";
    private readonly ILogger<PowerShellExecutor> _logger;
    private readonly HashSet<string> _allowlist = new() { "Get-Mailbox", "Get-User", "Get-Recipient" };

    public PowerShellExecutor(ILogger<PowerShellExecutor> logger) => _logger = logger;

    public async Task<ExecutionResult> ExecuteAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-PS-Command", out var cmd))
            return ExecutionResult.FromError("MissingCommand", "X-PS-Command header required", false);

        if (!_allowlist.Contains(cmd))
            return ExecutionResult.FromError("CommandNotAllowed", "This PowerShell command is not allowed", false);

        // In this skeleton we simulate the connect->run->disconnect lifecycle.
        try
        {
            var started = DateTime.UtcNow;
            // Simulated delay to mimic work (do not use Thread.Sleep in production). Keep small.
            await Task.Yield();

            var stdout = new List<string> { $"Simulated output: {cmd} result line 1", $"Simulated output: {cmd} result line 2" };
            var stderr = new List<string>();

            var res = ExecutionResult.FromPowerShell(cmd, stdout, stderr);
            res.StartedAt = started;
            res.CompletedAt = DateTime.UtcNow;
            return res;
        }
        catch (Exception ex)
        {
            return ExecutionResult.FromError("PSFailure", ex.Message, true);
        }
    }
}
