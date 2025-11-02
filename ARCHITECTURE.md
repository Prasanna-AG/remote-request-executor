# Remote Request Executor - Architecture & Design Documentation

## Table of Contents
1. [Overview](#overview)
2. [System Architecture](#system-architecture)
3. [Resilience Strategy](#resilience-strategy)
4. [Isolation Strategy](#isolation-strategy)
5. [Extensibility Design](#extensibility-design)
6. [Configuration](#configuration)
7. [Security Considerations](#security-considerations)
8. [Observability](#observability)
9. [Testing Strategy](#testing-strategy)

---

## Overview

The Remote Request Executor is a resilient proxy API designed to forward HTTP requests and execute PowerShell commands with built-in retry logic, structured logging, metrics collection, and comprehensive error handling.

### Key Features
- **Dual Execution Modes**: HTTP forwarding and PowerShell remote execution
- **Automatic Retry Logic**: Exponential backoff with jitter for transient failures
- **Request Traceability**: Request/Correlation IDs tracked across all layers
- **Structured Logging**: JSON-formatted logs with sensitive data masking
- **Metrics Collection**: Latency tracking, success/failure rates, retry counts
- **Configurable Limits**: Body size, timeouts, retry behavior via appsettings
- **Security**: Command allowlisting, input validation, header filtering

---

## System Architecture

### High-Level Components

```
┌─────────────────────────────────────────────────────────────┐
│                      API Surface                             │
│  ┌────────────┬─────────────────┬────────────────┐          │
│  │ /ping      │ /api/{**path}   │ /metrics       │          │
│  │ (Health)   │ (Catch-all)     │ (Observability)│          │
│  └────────────┴─────────────────┴────────────────┘          │
└─────────────────────┬───────────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────────┐
│                 ProxyController                              │
│  • Request validation                                        │
│  • Executor selection                                        │
│  • Retry orchestration                                       │
│  • Response envelope construction                            │
│  • Metrics/logging                                           │
└─────────────────┬───────────────────────────────────────────┘
                  │
        ┌─────────┴─────────┐
        ▼                   ▼
┌────────────────┐  ┌────────────────────────┐
│ HttpExecutor   │  │ PowerShellExecutor     │
│                │  │                        │
│ • Forward HTTP │  │ • Connect session      │
│ • Filter headers│ │ • Execute command      │
│ • Merge queries│  │ • Apply filters/paging │
│ • Classify     │  │ • Disconnect session   │
│   errors       │  │ • Command allowlist    │
└────────────────┘  └────────────────────────┘
```

### Request Flow

1. **Request Ingress**
   - Client sends request to `/api/{**path}`
   - Controller extracts/generates Request ID and Correlation ID
   - Request envelope is constructed

2. **Validation**
   - RequestValidator checks for:
     - Required fields (RequestId, Headers)
     - Body size limits
     - Executor-specific requirements
     - HTTP method validity
   - Returns structured error codes on failure

3. **Executor Selection**
   - `X-Executor-Type` header determines executor (`http` or `powershell`)
   - Default: `http`
   - Returns error if executor not found

4. **Retry Execution**
   - RetryPolicy wraps executor call
   - Tracks all attempts with timestamps
   - Applies exponential backoff + jitter for transient failures
   - Enforces per-attempt timeout

5. **Response Construction**
   - ResponseEnvelope contains:
     - Request/Correlation IDs
     - All attempt summaries
     - Final execution result
     - Timestamps and metrics
   - Response headers added for traceability

---

## Resilience Strategy

### Retry Logic

**Algorithm**: Exponential Backoff with Jitter

**Formula**:
```
delay = min(maxDelay, baseDelay * 2^(attempt-1)) * (1 + random(0, jitterPercent))
```

**Example** (base=200ms, max=5000ms, jitter=25%):
- Attempt 1 → immediate
- Attempt 2 → 200ms + jitter(0-50ms) = 200-250ms
- Attempt 3 → 400ms + jitter(0-100ms) = 400-500ms
- Attempt 4 → 800ms + jitter(0-200ms) = 800-1000ms

**Rationale**:
1. **Exponential Backoff**: Gives downstream services time to recover
2. **Jitter**: Prevents thundering herd when multiple clients retry simultaneously
3. **Max Delay Cap**: Prevents excessive wait times (user experience)
4. **Configurable**: Tunable per environment (dev vs prod)

### Transient vs Non-Transient Classification

**Transient Failures** (retryable):
- HTTP Status: 408 (Timeout), 429 (Rate Limit), 500, 502, 503, 504
- Network exceptions: `HttpRequestException`, `OperationCanceledException`
- PowerShell: Timeout, busy server, unavailable

**Non-Transient Failures** (not retryable):
- HTTP Status: 400 (Bad Request), 401 (Unauthorized), 403 (Forbidden), 404 (Not Found)
- Validation errors
- Configuration errors
- Command not allowlisted

**Justification**:
- Retrying auth failures (401) wastes resources and risks account lockout
- Bad requests (400) won't succeed on retry without client changes
- Transient network issues often resolve quickly (< 5 seconds)

### Per-Attempt Timeout

Default: 10 seconds (configurable)

**Purpose**:
- Prevents indefinite hangs
- Allows retry attempts within reasonable total time
- Protects against slow downstream services

**Example**:
- 3 attempts × 10s timeout = max 30s blocked (plus retry delays)
- Without timeout: Could hang indefinitely on one attempt

### Extension Points for Future Policies

**Circuit Breaker** (see `ICircuitBreakerPolicy`):
```csharp
// Prevents cascading failures by temporarily blocking requests
// States: Closed → Open → Half-Open
// Use case: Protect against sustained downstream outage
```

**Rate Limiting** (see `IRateLimitPolicy`):
```csharp
// Protects service from overload
// Strategies: Fixed window, sliding window, token bucket
// Scope: Per-instance, per-tenant, or global (with Redis)
```

**Bulkhead Isolation** (see `IBulkheadPolicy`):
```csharp
// Limits concurrent executions
// Use case: Prevent resource exhaustion (e.g., max PowerShell sessions)
```

---

## Isolation Strategy

### PowerShell Executor: Per-Request Session Approach

**Design Decision**: Create fresh PowerShell session for each request

**Implementation**:
```
┌─────────────────────────────────────────────────────────┐
│  Request N                  Request N+1                  │
│                                                          │
│  1. Connect                 1. Connect                   │
│  2. Execute Command         2. Execute Command           │
│  3. Disconnect              3. Disconnect                │
│                                                          │
│  ✓ Isolated credentials     ✓ Isolated credentials      │
│  ✓ Clean state              ✓ Clean state               │
└─────────────────────────────────────────────────────────┘
```

**Benefits**:
1. **Credential Isolation**: No leakage between tenants
2. **State Cleanliness**: No pollution from previous commands
3. **Simplified Error Handling**: No stale session recovery logic
4. **Audit Trail**: Clear connect/execute/disconnect logging

**Trade-offs**:
- ❌ Higher Latency: Session setup adds ~500-2000ms per request
- ✅ Better Security: Complete isolation
- ✅ Simpler Code: No session pooling complexity

**Alternative Approaches Considered**:

| Approach | Pros | Cons | Decision |
|----------|------|------|----------|
| **Long-lived sessions** | Lower latency | Credential lifetime management complexity | ❌ Rejected |
| **Session pool** | Balance latency/isolation | Complex tenant isolation, stale session handling | ❌ Rejected |
| **Keyed session cache** | Good for same-tenant requests | Auth validation complexity | 💡 Future enhancement |
| **Per-request (chosen)** | Simple, secure | Higher latency | ✅ Selected |

**Future Enhancement**:
If latency becomes critical, implement keyed session cache with:
- Key: Hash of (tenant ID + credential + session configuration)
- Expiry: Configurable TTL (e.g., 5 minutes)
- Validation: Re-auth check before reuse
- Disposal: Aggressive cleanup on error

### HTTP Executor: Header Filtering

**Security Approach**: Deny-list with explicit filtering

**Filtered Headers**:
- `Authorization`, `Proxy-Authorization` (prevent credential forwarding)
- `Cookie` (prevent session hijacking)
- `X-*` custom headers (internal control headers)
- `Host` (set by HttpClient)
- `sec-*` (browser security headers)

**Rationale**:
- Prevents accidentally forwarding sensitive tokens
- Protects downstream services from malicious headers
- Configurable via `appsettings.json`

---

## Extensibility Design

### Adding a New Executor

**Steps**:
1. Implement `IExecutor` interface
2. Register in `Program.cs` DI container
3. Add configuration section in `appsettings.json`
4. No changes to controller or retry logic required

**Example**:
```csharp
public class DatabaseExecutor : IExecutor
{
    public string Name => "database";
    
    public async Task<ExecutionResult> ExecuteAsync(
        RequestEnvelope request, 
        CancellationToken cancellationToken)
    {
        // Execute SQL query with parameters
        // Apply result set paging
        // Return structured results
    }
}

// Program.cs
builder.Services.AddSingleton<IExecutor, DatabaseExecutor>();
```

### Adding a New Resilience Policy

**Steps**:
1. Implement `IResiliencePolicy` interface
2. Register in DI container
3. Compose policies via `IPolicyChain`

**Example**:
```csharp
public class CircuitBreakerPolicy : ICircuitBreakerPolicy
{
    public CircuitState State { get; private set; }
    
    public async Task<ExecutionResult> ExecuteAsync(...)
    {
        if (State == CircuitState.Open)
            return ExecutionResult.FromError("CircuitOpen", ...);
        
        var result = await action(cancellationToken);
        UpdateState(result);
        return result;
    }
}
```

### Abstraction Layers

1. **IExecutor**: Abstract execution strategy
2. **IResiliencePolicy**: Abstract resilience patterns
3. **IMetricsCollector**: Abstract metrics backend
4. **ISystemClock**: Abstract time (enables testing)
5. **RequestValidator**: Centralized validation logic

---

## Configuration

All configuration is externalized via `appsettings.json` and environment variables.

### Configuration Hierarchy
```
Environment Variables (highest priority)
          ↓
appsettings.{Environment}.json
          ↓
appsettings.json (lowest priority)
```

### Key Configuration Sections

**Service**:
```json
{
  "Service": {
    "InstanceId": "remote-executor-01",
    "MaxRequestBodySizeKb": 1024,
    "DefaultTimeoutSeconds": 30
  }
}
```

**RetryPolicy**:
```json
{
  "RetryPolicy": {
    "MaxAttempts": 3,
    "BaseDelayMs": 200,
    "MaxDelayMs": 5000,
    "JitterPercent": 0.25,
    "TransientStatusCodes": [408, 429, 500, 502, 503, 504],
    "PerAttemptTimeoutMs": 10000
  }
}
```

**Environment Variable Override Examples**:
```bash
export Service__InstanceId=prod-instance-01
export RetryPolicy__MaxAttempts=5
export Executors__Http__MaxResponseBodyLengthKb=1024
```

---

## Security Considerations

### Input Validation
- Request body size limits (configurable, default 1MB)
- HTTP method allowlist
- Executor-specific validation (e.g., PowerShell command allowlist)

### Sensitive Data Masking
- URL parameters: `api_key`, `token`, `password` masked in logs
- Headers: `Authorization`, `Cookie` never logged
- Regex-based masking: `token=value` → `token=***MASKED***`

### PowerShell Command Allowlist
- Only explicitly permitted commands execute
- Configured in `appsettings.json`
- Prevents arbitrary code execution
- Future: Add parameter validation/sanitization

### Container Security
- Non-root user (`appuser`) in Dockerfile
- Minimal base image (aspnet:9.0)
- No unnecessary dependencies

---

## Observability

### Structured Logging

**Format**: JSON (console)

**Fields**:
- `Timestamp`: ISO 8601 UTC
- `Level`: Information, Warning, Error
- `Message`: Human-readable message
- `RequestId`: Unique request identifier
- `CorrelationId`: Cross-service tracing
- `Scopes`: Nested context (request details)

**Example**:
```json
{
  "timestamp": "2025-10-30T12:34:56.789Z",
  "level": "Information",
  "message": "Request completed: Status=Success, Attempts=2, Latency=350ms",
  "requestId": "abc-123",
  "correlationId": "xyz-789",
  "executor": "http"
}
```

### Metrics Endpoint

**GET /metrics**

**Response**:
```json
{
  "timestamp": "2025-10-30T12:34:56.789Z",
  "instance": "remote-executor-01",
  "metrics": {
    "total": 1250,
    "success": 1180,
    "failed": 70,
    "retried": 45,
    "avgLatencyMs": 234.5,
    "p95LatencyMs": 890.2
  }
}
```

**Metrics Computation**:
- **Average Latency**: Arithmetic mean of all request latencies
- **P95 Latency**: 95th percentile (approximate via sorted array)
  - Formula: `sorted[floor(0.95 * (n - 1))]`
  - Rationale: P95 shows "typical worst case" (excludes outliers)
- **In-memory storage**: Up to 10,000 samples (circular buffer)

### Response Headers for Traceability

- `X-Request-Id`: Forwarded or generated request ID
- `X-Correlation-Id`: Optional cross-service tracing ID
- `X-Instance-Id`: Identifies which instance handled request
- `X-Executor`: Which executor processed request
- `X-Attempts`: Number of retry attempts

**Rationale**:
- Enables end-to-end request tracking
- Facilitates debugging in distributed environments
- Supports distributed tracing integration (future)

---

## Testing Strategy

### Unit Tests

**Coverage Areas**:
1. **Validation Logic**: All error codes, edge cases
2. **Retry Policy**: Backoff calculation, transient classification, timeout handling
3. **Executor Selection**: Valid/invalid executor types
4. **Response Envelope**: Attempt summaries, timestamp accuracy
5. **Metrics Calculation**: P95, average, counters

**Testing Approach**:
- **Deterministic**: No sleeps (use fake clock for time-based tests)
- **Isolated**: Mock dependencies via interfaces
- **Fast**: Unit tests run in < 100ms each

### Integration Tests

**Coverage Areas**:
1. **HTTP Execution**: End-to-end with real HTTP calls (test server)
2. **PowerShell Execution**: Command allowlist enforcement
3. **Validation Errors**: Structured error responses
4. **Response Headers**: Traceability headers present
5. **Metrics Endpoint**: Returns valid snapshot

**Test Strategy**:
- Use `WebApplicationFactory<Program>` for in-process testing
- External HTTP calls use public test APIs (jsonplaceholder.typicode.com)
- PowerShell executor uses simulated sessions (no external dependencies)

### Test Organization

```
RemoteExecutor.Tests/
├── HttpExecutorTests.cs          (Unit)
├── PowerShellExecutorTests.cs    (Unit)
├── RetryPolicyTests.cs           (Unit)
├── RequestValidatorTests.cs      (Unit)
├── MetricsCollectorTests.cs      (Unit)
└── IntegrationTests.cs           (Integration)
```

---

## Design Decisions Summary

### Why These Choices?

1. **ASP.NET Core 9.0**: Modern, performant, excellent DI/logging support
2. **Catch-all Route**: Flexible proxy pattern, forwards arbitrary paths
3. **Per-Request Sessions**: Security over performance for PowerShell
4. **Exponential Backoff + Jitter**: Industry standard, prevents thundering herd
5. **Structured JSON Logs**: Machine-readable, integrates with log aggregators
6. **Interface-Based Design**: Testability, extensibility, dependency injection
7. **Configuration-Driven**: Environment-specific tuning without code changes
8. **Multi-Stage Dockerfile**: Small runtime image, fast builds (layer caching)

### Future Enhancements

1. **Circuit Breaker**: Implement `ICircuitBreakerPolicy`
2. **Rate Limiting**: Per-tenant or global rate limiting
3. **Distributed Tracing**: OpenTelemetry integration
4. **Real PowerShell Remoting**: Exchange Online / Active Directory
5. **Health Checks**: Deeper health probes (downstream service availability)
6. **Caching**: Response caching for idempotent requests
7. **Authentication**: API key or OAuth 2.0 for inbound requests
8. **Async Processing**: Queue-based execution for long-running commands

---

## Performance Considerations

### Latency Budget

**HTTP Executor** (per request):
- Validation: < 1ms
- Retry logic overhead: < 5ms
- Downstream HTTP call: Variable (user-controlled)
- Response envelope construction: < 2ms
- Total overhead: ~8-10ms + downstream latency

**PowerShell Executor** (per request):
- Session connect: ~500-2000ms (simulated, real may vary)
- Command execution: Variable (command-dependent)
- Session disconnect: ~20ms
- Total overhead: ~500-2020ms + command execution

### Scalability

**Bottlenecks**:
1. In-memory metrics (10K sample limit)
2. PowerShell session creation (per-request model)
3. HttpClient pool exhaustion (mitigated by singleton HttpClient)

**Mitigations**:
1. Metrics: Use external aggregator (Prometheus, Azure Monitor)
2. PowerShell: Implement session pooling if latency critical
3. HttpClient: Singleton pattern already applied

---

## Conclusion

The Remote Request Executor demonstrates production-ready API design with:
- ✅ Comprehensive resilience patterns
- ✅ Security-first isolation strategy
- ✅ Extensible architecture
- ✅ Full observability
- ✅ Thorough testing
- ✅ Clear documentation

The system is ready for deployment, monitoring, and iterative enhancement based on production telemetry.



