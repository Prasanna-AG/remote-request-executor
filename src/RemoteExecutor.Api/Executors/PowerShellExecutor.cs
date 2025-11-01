using Microsoft.Extensions.Options;
using RemoteExecutor.Api.Configuration;
using System.Collections;

/// <summary>
/// PowerShell executor with session lifecycle management (connect → execute → disconnect)
/// 
/// ISOLATION STRATEGY DOCUMENTATION:
/// ===================================
/// This executor implements a PER-REQUEST SESSION approach for security and isolation:
/// 
/// 1. SESSION CREATION: Each request creates a fresh PowerShell session
///    - Ensures zero credential leakage between tenants/requests
///    - Prevents state pollution from previous commands
///    - Eliminates risks of stale or corrupted sessions
/// 
/// 2. COMMAND ALLOWLIST: Only explicitly permitted commands are executed
///    - Prevents arbitrary code execution attacks
///    - Configured via appsettings.json for flexibility
///    - Future: Can extend to parameter validation/sanitization
/// 
/// 3. SESSION DISPOSAL: Sessions are always disposed after execution
///    - Guaranteed cleanup via try-finally blocks
///    - Prevents resource leaks and connection exhaustion
///    - Clear audit trail with connect/disconnect logging
/// 
/// TRADE-OFFS:
/// - Higher Latency: Session setup adds ~500-2000ms per request
/// - Better Security: Complete isolation between requests
/// - Simpler Error Handling: No stale session recovery logic needed
/// 
/// ALTERNATIVE APPROACHES CONSIDERED:
/// - Session Pooling: Rejected due to tenant isolation complexity
/// - Long-lived Sessions: Rejected due to credential lifetime management
/// - Keyed Session Cache: Could be added for same-tenant requests with proper auth validation
/// </summary>
public class PowerShellExecutor : IExecutor
{
    public string Name => "powershell";
    private readonly ILogger<PowerShellExecutor> _logger;
    private readonly PowerShellExecutorConfiguration _config;
    private readonly HashSet<string> _allowlist;

    public PowerShellExecutor(
        ILogger<PowerShellExecutor> logger,
        IOptions<PowerShellExecutorConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
        _allowlist = new HashSet<string>(_config.AllowedCommands, StringComparer.OrdinalIgnoreCase);
        
        _logger.LogInformation("PowerShellExecutor initialized: AllowedCommands=[{Commands}], SessionReuse={SessionReuse}", 
            string.Join(", ", _config.AllowedCommands), _config.SessionReuseEnabled);
    }

    public async Task<ExecutionResult> ExecuteAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        // Extract command and parameters
        if (!request.Headers.TryGetValue("X-PS-Command", out var command))
            return ExecutionResult.FromError("MissingCommand", "X-PS-Command header required", false);

        // Validate against allowlist
        if (!_allowlist.Contains(command))
        {
            _logger.LogWarning("Attempted to execute disallowed PowerShell command: {Command}", command);
            return ExecutionResult.FromError("CommandNotAllowed", 
                $"Command '{command}' is not in the allowlist. Allowed: {string.Join(", ", _config.AllowedCommands)}", 
                false);
        }

        // Extract optional parameters (e.g., for filtering/paging)
        var maxResults = request.Headers.TryGetValue("X-PS-MaxResults", out var maxStr) && int.TryParse(maxStr, out var max) ? max : 100;
        var filter = request.Headers.TryGetValue("X-PS-Filter", out var filterVal) ? filterVal : null;
        var resultSize = request.Headers.TryGetValue("X-PS-ResultSize", out var sizeVal) ? sizeVal : "100";

        _logger.LogInformation("Executing PowerShell command: {Command}, Filter={Filter}, MaxResults={MaxResults}", 
            command, filter ?? "none", maxResults);

        IPowerShellSession? session = null;
        try
        {
            // PHASE 1: CONNECT - Establish remote session
            _logger.LogInformation("Connecting PowerShell session for request {RequestId}", request.RequestId);
            session = await ConnectSessionAsync(request, cancellationToken);

            // PHASE 2: EXECUTE - Run command with parameters
            _logger.LogInformation("Executing command in session: {Command}", command);
            var result = await ExecuteCommandAsync(session, command, filter, resultSize, maxResults, cancellationToken);

            // PHASE 3: DISCONNECT - Clean up session
            _logger.LogInformation("Disconnecting PowerShell session for request {RequestId}", request.RequestId);
            await DisconnectSessionAsync(session, cancellationToken);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("PowerShell execution cancelled for request {RequestId}", request.RequestId);
            return ExecutionResult.FromError("Timeout", "PowerShell execution timed out", true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PowerShell execution failed for command {Command}", command);
            return ExecutionResult.FromError("PSFailure", ex.Message, DetermineIfTransient(ex));
        }
        finally
        {
            // Ensure session cleanup even on error
            if (session != null)
            {
                try
                {
                    await DisconnectSessionAsync(session, CancellationToken.None);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "Failed to cleanup PowerShell session");
                }
            }
        }
    }

    /// <summary>
    /// Connects a new PowerShell session (simulated for now)
    /// In production, this would establish a remote PSSession to Exchange/AD
    /// </summary>
    private async Task<IPowerShellSession> ConnectSessionAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        // Simulate connection delay
        // Rationale: 50ms approximates typical remote PowerShell session establishment time
        // (TCP handshake + authentication + session negotiation). Used for realistic simulation
        // of production behavior without requiring actual remote connections in development.
        await Task.Delay(50, cancellationToken); // Simulate connection time
        
        _logger.LogInformation("PowerShell session connected (simulated)");
        
        // In production, you would:
        // var session = await PSSessionFactory.CreateRemoteSessionAsync(
        //     uri: _config.ConnectionUri,
        //     credential: GetCredentialFromRequest(request),
        //     cancellationToken: cancellationToken);
        
        return new SimulatedPowerShellSession();
    }

    /// <summary>
    /// Executes command in the session with filtering and paging support
    /// </summary>
    private async Task<ExecutionResult> ExecuteCommandAsync(
        IPowerShellSession session, 
        string command, 
        string? filter, 
        string resultSize,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        
        // Build command with parameters
        var fullCommand = BuildCommandWithParameters(command, filter, resultSize);
        _logger.LogDebug("Executing: {FullCommand}", fullCommand);

        // Execute (simulated)
        // Rationale: 100ms approximates typical PowerShell cmdlet execution time for read operations
        // (Get-Mailbox, Get-User typically take 50-200ms depending on result size and network).
        // Used for realistic simulation without requiring actual Exchange/AD connectivity.
        await Task.Delay(100, cancellationToken); // Simulate execution time

        // Generate simulated results
        var stdout = new List<string>();
        var objects = new List<object>();

        // Simulate realistic output based on command
        if (command.Equals("Get-Mailbox", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < Math.Min(5, maxResults); i++)
            {
                var mailbox = new
                {
                    DisplayName = $"User {i + 1}",
                    PrimarySmtpAddress = $"user{i + 1}@contoso.com",
                    MailboxType = "UserMailbox",
                    DatabaseName = "DB01"
                };
                objects.Add(mailbox);
                stdout.Add($"DisplayName: {mailbox.DisplayName}, PrimarySmtpAddress: {mailbox.PrimarySmtpAddress}");
            }
        }
        else if (command.Equals("Get-User", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < Math.Min(3, maxResults); i++)
            {
                var user = new
                {
                    Name = $"User {i + 1}",
                    UserPrincipalName = $"user{i + 1}@contoso.com",
                    Department = "IT"
                };
                objects.Add(user);
                stdout.Add($"Name: {user.Name}, UPN: {user.UserPrincipalName}");
            }
        }
        else
        {
            stdout.Add($"Simulated output for: {command}");
            stdout.Add($"Filter applied: {filter ?? "none"}");
            stdout.Add($"ResultSize: {resultSize}");
        }

        // Ensure a recognizable simulated marker for integrations
        stdout.Add("Simulated output");

        var stderr = new List<string>();
        var completed = DateTime.UtcNow;

        _logger.LogInformation("PowerShell command completed: {Command}, ResultCount={Count}, Duration={Duration}ms", 
            command, objects.Count, (completed - started).TotalMilliseconds);

        var result = ExecutionResult.FromPowerShell(fullCommand, stdout, stderr, objects);
        result.StartedAt = started;
        result.CompletedAt = completed;
        return result;
    }

    /// <summary>
    /// Disconnects and disposes the PowerShell session
    /// </summary>
    private async Task DisconnectSessionAsync(IPowerShellSession session, CancellationToken cancellationToken)
    {
        // Rationale: 20ms approximates session cleanup and disconnection time.
        // Disconnect is typically faster than connect since it's mostly cleanup.
        // Used for realistic simulation of session lifecycle without actual connections.
        await Task.Delay(20, cancellationToken); // Simulate disconnect time
        session.Dispose();
        _logger.LogInformation("PowerShell session disconnected and disposed");
    }

    /// <summary>
    /// Builds command string with filtering and paging parameters
    /// </summary>
    private static string BuildCommandWithParameters(string command, string? filter, string resultSize)
    {
        var parts = new List<string> { command };
        
        if (!string.IsNullOrEmpty(filter))
            parts.Add($"-Filter \"{filter}\"");
        
        parts.Add($"-ResultSize {resultSize}");
        
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Determines if exception represents a transient failure
    /// </summary>
    private static bool DetermineIfTransient(Exception ex)
    {
        // Network timeouts, connection failures are typically transient
        if (ex is TimeoutException or OperationCanceledException)
            return true;
        
        // Some PowerShell errors are transient (throttling, busy server)
        if (ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            return true;
        
        return false;
    }
}

/// <summary>
/// Interface for PowerShell session (enables testing and future remote implementation)
/// </summary>
public interface IPowerShellSession : IDisposable
{
    string SessionId { get; }
}

/// <summary>
/// Simulated PowerShell session for demonstration
/// In production, this would wrap a real PSSession to Exchange Online or AD
/// </summary>
public class SimulatedPowerShellSession : IPowerShellSession
{
    public string SessionId { get; } = Guid.NewGuid().ToString();
    
    public void Dispose()
    {
        // In production: Close and dispose remote PSSession
    }
}
