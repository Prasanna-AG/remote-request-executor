using Microsoft.Extensions.Options;
using RemoteExecutor.Api.Configuration;

public interface IRetryPolicyFactory 
{ 
    RetryPolicy CreateDefaultPolicy(); 
}

public class DefaultRetryPolicyFactory : IRetryPolicyFactory
{
    private readonly RetryPolicyConfiguration _config;
    private readonly ILogger<DefaultRetryPolicyFactory> _logger;

    public DefaultRetryPolicyFactory(IOptions<RetryPolicyConfiguration> config, ILogger<DefaultRetryPolicyFactory> logger)
    {
        _config = config.Value;
        _logger = logger;
        _logger.LogInformation("RetryPolicy initialized: MaxAttempts={MaxAttempts}, BaseDelay={BaseDelay}ms, MaxDelay={MaxDelay}ms, Jitter={Jitter}%, PerAttemptTimeout={Timeout}ms", 
            _config.MaxAttempts, _config.BaseDelayMs, _config.MaxDelayMs, _config.JitterPercent * 100, _config.PerAttemptTimeoutMs);
    }

    public RetryPolicy CreateDefaultPolicy() => new RetryPolicy(
        _config.MaxAttempts, 
        _config.BaseDelayMs, 
        _config.MaxDelayMs, 
        _config.PerAttemptTimeoutMs,
        _config.JitterPercent,
        _logger);
}

public class RetryPolicy
{
    private readonly int _maxAttempts;
    private readonly int _baseDelayMs;
    private readonly int _maxDelayMs;
    private readonly int _perAttemptTimeoutMs;
    private readonly double _jitterPercent;
    private readonly ILogger _logger;
    private readonly Random _rng = new();

    public RetryPolicy(int maxAttempts, int baseDelayMs, int maxDelayMs, int perAttemptTimeoutMs, double jitterPercent, ILogger logger)
    {
        _maxAttempts = maxAttempts;
        _baseDelayMs = baseDelayMs;
        _maxDelayMs = maxDelayMs;
        _perAttemptTimeoutMs = perAttemptTimeoutMs;
        _jitterPercent = jitterPercent;
        _logger = logger;
    }

    /// <summary>
    /// Executes action with retry logic and returns all attempt results
    /// </summary>
    public async Task<RetryResult> ExecuteAsync(Func<int, CancellationToken, Task<ExecutionResult>> action, string requestId)
    {
        var attempts = new List<ExecutionResult>();

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_perAttemptTimeoutMs));
            ExecutionResult r;
            
            try 
            { 
                r = await action(attempt, cts.Token); 
            }
            catch (OperationCanceledException) 
            { 
                _logger.LogWarning("Attempt {Attempt} timed out after {Timeout}ms for request {RequestId}", attempt, _perAttemptTimeoutMs, requestId);
                r = ExecutionResult.FromError("Timeout", "Per-attempt timeout", true); 
            }
            catch (Exception ex) 
            { 
                _logger.LogError(ex, "Attempt {Attempt} threw exception for request {RequestId}", attempt, requestId);
                r = ExecutionResult.FromError("ExecutorException", ex.Message, true); 
            }

            r.Attempt = attempt;
            attempts.Add(r);

            // Success or non-transient failure - stop retrying
            if (!r.IsTransientFailure)
            {
                _logger.LogInformation("Request {RequestId} completed after {Attempts} attempt(s): {Outcome}", 
                    requestId, attempt, r.IsSuccess ? "Success" : "Failure");
                return new RetryResult(attempts);
            }

            // Last attempt exhausted
            if (attempt == _maxAttempts)
            {
                _logger.LogWarning("Request {RequestId} exhausted all {MaxAttempts} retry attempts", requestId, _maxAttempts);
                return new RetryResult(attempts);
            }

            // Calculate backoff delay with exponential backoff and jitter
            // Formula: delay = min(maxDelay, base * 2^(attempt-1)) * (1 + random(0, jitter))
            var exponentialDelay = Math.Min(_maxDelayMs, _baseDelayMs * (int)Math.Pow(2, attempt - 1));
            var jitter = (int)(_rng.NextDouble() * exponentialDelay * _jitterPercent);
            var delay = exponentialDelay + jitter;
            
            _logger.LogInformation("Request {RequestId} retrying attempt {NextAttempt} after {Delay}ms (transient failure: {Error})", 
                requestId, attempt + 1, delay, r.ErrorMessage);
            
            await Task.Delay(delay);
        }

        return new RetryResult(attempts);
    }
}

/// <summary>
/// Result of retry execution including all attempts
/// </summary>
public class RetryResult
{
    public List<ExecutionResult> Attempts { get; }
    public ExecutionResult FinalResult => Attempts[^1];
    public int TotalAttempts => Attempts.Count;
    public bool IsSuccess => FinalResult.IsSuccess;

    public RetryResult(List<ExecutionResult> attempts)
    {
        Attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));
        if (attempts.Count == 0)
            throw new ArgumentException("At least one attempt required", nameof(attempts));
    }
}
