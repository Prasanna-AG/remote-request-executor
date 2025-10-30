public interface IRetryPolicyFactory { RetryPolicy CreateDefaultPolicy(); }

public class DefaultRetryPolicyFactory : IRetryPolicyFactory
{
    public RetryPolicy CreateDefaultPolicy() => new RetryPolicy(maxAttempts: 3, baseDelayMs: 200, maxDelayMs: 2000, perAttemptTimeoutMs: 10000);
}

public class RetryPolicy
{
    private readonly int _maxAttempts;
    private readonly int _baseDelayMs;
    private readonly int _maxDelayMs;
    private readonly int _perAttemptTimeoutMs;
    private readonly Random _rng = new();

    public RetryPolicy(int maxAttempts, int baseDelayMs, int maxDelayMs, int perAttemptTimeoutMs)
    {
        _maxAttempts = maxAttempts;
        _baseDelayMs = baseDelayMs;
        _maxDelayMs = maxDelayMs;
        _perAttemptTimeoutMs = perAttemptTimeoutMs;
    }

    // Executes the delegate and retries on transient failures
    public async Task<ExecutionResult> ExecuteAsync(Func<int, CancellationToken, Task<ExecutionResult>> action)
    {
        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_perAttemptTimeoutMs));
            ExecutionResult r;
            try { r = await action(attempt, cts.Token); }
            catch (OperationCanceledException) { r = ExecutionResult.FromError("Timeout", "Per-attempt timeout", true); }
            catch (Exception ex) { r = ExecutionResult.FromError("ExecutorException", ex.Message, true); }

            if (!r.IsTransientFailure) { r.Attempt = attempt; return r; }
            if (attempt == _maxAttempts) { r.Attempt = attempt; return r; }

            // exponential backoff with jitter: delay = min(maxDelay, base * 2^(attempt-1)) + jitter(0..30%)
            var baseDelay = Math.Min(_maxDelayMs, _baseDelayMs * (int)Math.Pow(2, attempt - 1));
            var jitter = (int)(_rng.NextDouble() * baseDelay * 0.3);
            var delay = baseDelay + jitter;
            await Task.Delay(delay);
        }

        return ExecutionResult.FromError("RetryExhausted", "Retries exhausted", false);
    }
}
