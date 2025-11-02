namespace RemoteExecutor.Api.Configuration;

/// <summary>
/// Main service configuration loaded from appsettings.json
/// </summary>
public class ServiceConfiguration
{
    public const string SectionName = "Service";
    
    public string InstanceId { get; set; } = "remote-executor-01";
    
    /// <summary>
    /// Maximum request body size in kilobytes
    /// Rationale: 1000KB (1MB) limits memory usage and prevents DoS via large payloads.
    /// Typical API requests are &lt;100KB; 1MB accommodates most legitimate use cases
    /// while preventing excessive memory allocation attacks.
    /// </summary>
    public int MaxRequestBodySizeKb { get; set; } = 1000;
    
    /// <summary>
    /// Default timeout for request processing in seconds
    /// Rationale: 30 seconds balances user experience with resource protection.
    /// Allows sufficient time for downstream operations (HTTP, PowerShell) while
    /// preventing indefinite request hangs that could exhaust thread pool resources.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Retry policy configuration with documented rationale
/// </summary>
public class RetryPolicyConfiguration
{
    public const string SectionName = "RetryPolicy";
    
    /// <summary>
    /// Maximum number of retry attempts (including initial attempt)
    /// Rationale: 3 attempts balances resilience with response time (initial + 2 retries)
    /// </summary>
    public int MaxAttempts { get; set; } = 3;
    
    /// <summary>
    /// Base delay in milliseconds for exponential backoff
    /// Rationale: 200ms provides quick retry for transient network blips
    /// </summary>
    public int BaseDelayMs { get; set; } = 200;
    
    /// <summary>
    /// Maximum delay cap in milliseconds
    /// Rationale: 5000ms prevents excessive wait times while allowing downstream recovery
    /// </summary>
    public int MaxDelayMs { get; set; } = 5000;
    
    /// <summary>
    /// Jitter percentage (0.0-1.0) added to backoff delay
    /// Rationale: 25% jitter prevents thundering herd when multiple clients retry simultaneously
    /// Math: delay = min(maxDelay, base * 2^(attempt-1)) * (1 + random(0, jitter))
    /// Example: attempt 2 with base=200ms: 200*2=400ms + jitter(0-100ms) = 400-500ms
    /// </summary>
    public double JitterPercent { get; set; } = 0.25;
    
    /// <summary>
    /// HTTP status codes classified as transient (retryable)
    /// Rationale: 
    /// - 408 Request Timeout: network/server overload
    /// - 429 Too Many Requests: rate limiting, retry with backoff
    /// - 5xx: server errors, typically transient
    /// </summary>
    public int[] TransientStatusCodes { get; set; } = new[] { 408, 429, 500, 502, 503, 504 };
    
    /// <summary>
    /// Timeout per individual attempt in milliseconds
    /// Rationale: 10s allows reasonable time for operations while preventing indefinite hangs
    /// </summary>
    public int PerAttemptTimeoutMs { get; set; } = 10000;
}

/// <summary>
/// HTTP executor configuration
/// </summary>
public class HttpExecutorConfiguration
{
    public const string SectionName = "Executors:Http";
    
    public string[] AllowedHeaderPrefixes { get; set; } = new[] { "Accept", "Content-Type", "User-Agent" };
    public string[] FilteredHeaders { get; set; } = new[] { "Authorization", "Proxy-Authorization", "Cookie" };
    
    /// <summary>
    /// Maximum response body length to return in kilobytes
    /// Rationale: 512KB limits memory usage from large downstream responses.
    /// Most API responses are &lt;10KB; 512KB accommodates paginated results and
    /// data exports while preventing memory exhaustion from malicious or misconfigured endpoints.
    /// Responses exceeding this limit are truncated with a truncation notice.
    /// </summary>
    public int MaxResponseBodyLengthKb { get; set; } = 512;
    
    /// <summary>
    /// Default timeout for HTTP requests in seconds
    /// Rationale: 15 seconds balances responsiveness with downstream operation time.
    /// HTTP operations typically complete in 1-5 seconds; 15s allows for network latency
    /// and slow downstream services while preventing thread pool exhaustion from hanging requests.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 15;
    
    /// <summary>
    /// When true in Development mode, uses a mock HttpClient that returns test responses
    /// without making real external network calls. Useful for local testing with Postman or cURL.
    /// </summary>
    public bool UseMockHttpClient { get; set; } = false;
}

/// <summary>
/// PowerShell executor configuration with isolation strategy
/// </summary>
public class PowerShellExecutorConfiguration
{
    public const string SectionName = "Executors:PowerShell";
    
    /// <summary>
    /// Commands allowed for execution (allowlist approach)
    /// Rationale: Explicit allowlist prevents arbitrary command execution
    /// </summary>
    public string[] AllowedCommands { get; set; } = new[] { "Get-Mailbox", "Get-User", "Get-DistributionGroup" };
    
    /// <summary>
    /// Whether to reuse PowerShell sessions across requests
    /// Rationale: False by default for tenant isolation - each request gets fresh session
    /// ISOLATION STRATEGY: Per-request session creation/disposal ensures:
    /// 1. No credential leakage between tenants
    /// 2. No state pollution from previous commands
    /// 3. Simplified error recovery (no stale session handling)
    /// Trade-off: Higher latency (session setup cost) vs security/isolation
    /// </summary>
    public bool SessionReuseEnabled { get; set; } = false;
    
    /// <summary>
    /// PowerShell session timeout in seconds
    /// Rationale: 60 seconds allows sufficient time for remote PowerShell command execution
    /// (typical Exchange/AD cmdlets take 5-30 seconds) while preventing indefinite session hangs
    /// that could exhaust connection pool resources. Aligns with common Exchange Online session defaults.
    /// </summary>
    public int SessionTimeoutSeconds { get; set; } = 60;
    
    public PowerShellAuthConfiguration Authentication { get; set; } = new();
}

public class PowerShellAuthConfiguration
{
    public string Type { get; set; } = "Basic";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Observability configuration
/// </summary>
public class ObservabilityConfiguration
{
    public const string SectionName = "Observability";
    
    public bool EnableStructuredLogging { get; set; } = true;
    public MetricsConfiguration Metrics { get; set; } = new();
}

public class MetricsConfiguration
{
    /// <summary>
    /// Metrics publishing interval in seconds
    /// Rationale: 30 seconds balances metrics freshness with collection overhead.
    /// Provides timely visibility into system health without excessive aggregation work.
    /// Common monitoring systems poll at 30-60s intervals; this aligns with that cadence.
    /// </summary>
    public int PublishIntervalSeconds { get; set; } = 30;
    
    /// <summary>
    /// Latency percentiles to track (p50, p95, p99)
    /// Rationale: [50, 95, 99] provides standard SLO monitoring coverage.
    /// - p50 (median): Typical response time
    /// - p95: Most users experience this or better (SLO target)
    /// - p99: Worst-case tail latency (outlier detection)
    /// Aligns with industry-standard observability practices.
    /// </summary>
    public int[] TrackLatencyPercentiles { get; set; } = new[] { 50, 95, 99 };
}

