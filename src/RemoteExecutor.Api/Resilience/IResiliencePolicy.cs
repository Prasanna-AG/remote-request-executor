namespace RemoteExecutor.Api.Resilience;

/// <summary>
/// Base interface for all resilience policies
/// Provides extension seam for future policies (circuit breaker, rate limiting, bulkhead, etc.)
/// </summary>
public interface IResiliencePolicy
{
    string Name { get; }
    Task<ExecutionResult> ExecuteAsync(
        Func<CancellationToken, Task<ExecutionResult>> action,
        string requestId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Circuit breaker policy interface (for future implementation)
/// 
/// DESIGN RATIONALE:
/// Circuit breaker prevents cascading failures by temporarily blocking requests
/// to a failing downstream service, giving it time to recover.
/// 
/// Typical states: Closed (normal), Open (blocking), Half-Open (testing recovery)
/// 
/// Configuration considerations:
/// - Failure threshold: Number/percentage of failures before opening circuit
/// - Break duration: How long to keep circuit open
/// - Success threshold: Number of successes in half-open state to close circuit
/// </summary>
public interface ICircuitBreakerPolicy : IResiliencePolicy
{
    CircuitState State { get; }
    TimeSpan BreakDuration { get; }
    int FailureThreshold { get; }
}

/// <summary>
/// Circuit breaker states
/// </summary>
public enum CircuitState
{
    Closed,   // Normal operation
    Open,     // Blocking requests
    HalfOpen  // Testing recovery
}

/// <summary>
/// Rate limiting policy interface (for future implementation)
/// 
/// DESIGN RATIONALE:
/// Rate limiting protects the service from overload and ensures fair resource distribution.
/// 
/// Strategies to consider:
/// - Fixed window: Simple, but allows bursts at window boundaries
/// - Sliding window: More accurate, higher memory cost
/// - Token bucket: Allows controlled bursts, smooth rate limiting
/// - Leaky bucket: Strict rate, no bursts
/// 
/// Scope considerations:
/// - Per-instance: Simple but doesn't protect cluster-wide
/// - Per-tenant: Fair resource distribution
/// - Global: Requires distributed cache (Redis)
/// </summary>
public interface IRateLimitPolicy : IResiliencePolicy
{
    int MaxRequestsPerWindow { get; }
    TimeSpan WindowDuration { get; }
    bool AllowRequest(string tenantId);
}

/// <summary>
/// Bulkhead isolation policy interface (for future implementation)
/// 
/// DESIGN RATIONALE:
/// Bulkhead isolation limits concurrent executions to prevent resource exhaustion.
/// Named after ship bulkheads that prevent one leak from sinking the entire ship.
/// 
/// Use cases:
/// - Limit concurrent PowerShell sessions
/// - Limit concurrent outbound HTTP requests
/// - Partition resources by executor type or tenant
/// 
/// Implementation options:
/// - Semaphore-based: Simple, local
/// - Queue-based: Better control, can add timeouts
/// - Distributed: Requires coordination (Redis, etc.)
/// </summary>
public interface IBulkheadPolicy : IResiliencePolicy
{
    int MaxConcurrency { get; }
    int QueuedRequestsLimit { get; }
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken);
    void Release();
}

/// <summary>
/// Policy chain builder for composing multiple resilience policies
/// 
/// Example usage:
/// var chain = new PolicyChain()
///     .AddTimeout(TimeSpan.FromSeconds(30))
///     .AddRetry(3, exponentialBackoff: true)
///     .AddCircuitBreaker(failureThreshold: 5, breakDuration: TimeSpan.FromSeconds(60))
///     .AddRateLimit(maxRequests: 100, window: TimeSpan.FromMinutes(1))
///     .AddBulkhead(maxConcurrency: 10);
/// 
/// var result = await chain.ExecuteAsync(requestId, async (ct) => 
/// {
///     return await executor.ExecuteAsync(envelope, ct);
/// });
/// </summary>
public interface IPolicyChain
{
    IPolicyChain AddPolicy(IResiliencePolicy policy);
    Task<ExecutionResult> ExecuteAsync(
        string requestId, 
        Func<CancellationToken, Task<ExecutionResult>> action,
        CancellationToken cancellationToken);
}

