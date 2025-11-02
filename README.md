# Remote Request Executor API

A .NET 9.0 API service that executes remote HTTP and PowerShell commands with automatic retry, structured logging, and comprehensive observability. Designed for reliability, security, and operational visibility.

## Running Locally

### Using .NET CLI

```bash
cd src/RemoteExecutor.Api
dotnet run --launch-profile http
```

Access: `http://localhost:5072`

### Using Docker

```bash
# Development (with mock HTTP client)
docker-compose up -d

# Production (real HTTP calls)
docker-compose -f docker-compose.yml -f docker-compose.production.yml up -d
```

Access: `http://localhost:5072`

### Sample Requests

**HTTP Executor (GET)**:
```bash
curl -X GET "http://localhost:5072/api/users/1" \
  -H "X-Forward-Base: https://jsonplaceholder.typicode.com" \
  -H "X-Executor-Type: http" \
  -H "X-Request-Id: req-001"
```

**PowerShell Executor**:
```bash
curl -X POST "http://localhost:5072/api/mailbox/list" \
  -H "X-Executor-Type: powershell" \
  -H "X-PS-Command: Get-Mailbox" \
  -H "X-Request-Id: ps-001"
```

**Health Check**:
```bash
curl http://localhost:5072/ping
```

**Metrics**:
```bash
curl http://localhost:5072/metrics
```

### Example Responses

See `src/examples/` for complete request/response JSON examples:
- `http-response-example.json` - HTTP success response
- `powershell-response-example.json` - PowerShell success response  
- `retry-response-example.json` - Response after retries
- `error-response-example.json` - Error scenarios

## Overview & Architecture

```
Client Request
     ↓
Validation (structured errors)
     ↓
Executor Selection (http/powershell)
     ↓
Retry Logic (exponential backoff + jitter)
     ↓
Execution (with per-attempt timeout)
     ↓
Response Envelope (all attempts tracked)
     ↓
Structured Response + Traceability Headers
```

The system acts as a **proxy gateway** with executor abstraction, enabling transparent HTTP forwarding or remote PowerShell execution with unified retry logic, metrics, and structured logging.

## Design Decisions & Trade-offs

### Executor Pattern
**Decision**: Abstract `IExecutor` interface with pluggable implementations  
**Rationale**: Enables adding new executor types (SSH, gRPC) without modifying core retry/validation logic  
**Trade-off**: Slight abstraction overhead vs. flexibility for future extensions

### Per-Request PowerShell Sessions
**Decision**: Create/dispose PowerShell session per request (no pooling)  
**Rationale**: Complete tenant isolation, zero credential leakage, simplified error recovery  
**Trade-off**: Higher latency (~500-2000ms session setup) vs. better security

### Exponential Backoff with Jitter
**Decision**: `delay = min(maxDelay, base × 2^(attempt-1)) + random(0, jitter%)`  
**Rationale**: Prevents thundering herd when multiple clients retry simultaneously  
**Trade-off**: More complex than fixed delay, but reduces cascading failures

### Structured JSON Logging
**Decision**: JSON console logging with scopes and UTC timestamps  
**Rationale**: Machine-parseable logs for aggregation (ELK, Splunk), better observability  
**Trade-off**: Less human-readable in development, requires log processing tools

### Request Size Limits
**Decision**: 1MB request body limit, 512KB response truncation  
**Rationale**: Prevents DoS via large payloads, limits memory usage  
**Trade-off**: Rejects legitimate large requests; configurable for different environments

## Resilience Strategy

### Retry Classification

**Transient Failures** (retryable):
- HTTP: 408 (Timeout), 429 (Rate Limit), 5xx (Server Errors)
- Network errors (connection failures, timeouts)
- PowerShell: Session timeouts, connection failures

**Permanent Failures** (no retry):
- HTTP: 4xx client errors (400, 401, 403, 404)
- Configuration errors (missing headers, invalid URLs)
- PowerShell: Command not allowed, validation errors

### Backoff Formula

```
exponentialDelay = min(maxDelay, baseDelay × 2^(attempt-1))
jitter = random(0, exponentialDelay × jitterPercent)
finalDelay = exponentialDelay + jitter
```

**Default Values**:
- Base delay: 200ms
- Max delay: 5000ms
- Jitter: 25%
- Max attempts: 3 (initial + 2 retries)

**Example**: Attempt 2 failure → 200×2 = 400ms + 0-100ms jitter = 400-500ms delay

### Per-Attempt Timeout
Each attempt has a 10-second timeout to prevent indefinite hangs. Timeout errors are classified as transient and trigger retries.

## Security Considerations & Limitations

### Implemented
- **Command Allowlist**: PowerShell only executes allowlisted commands (`Get-Mailbox`, `Get-User`, `Get-DistributionGroup`)
- **Header Filtering**: Sensitive headers (Authorization, Cookie) filtered before forwarding
      URLs logged with sensitive parameters masked:
      - `https://api.com/users?token=secret123` → `https://api.com/users?token=***MASKED***`
      - Patterns: `api_key`, `token`, `password`, `secret`, `pwd`
      - **Request Size Limits**: 1MB body limit prevents DoS attacks
- **Per-Request Isolation**: PowerShell sessions prevent credential leakage

### Limitations
- **No Authentication**: Assumes deployment behind API Gateway
- **No Rate Limiting**: Relies on external proxies
- **PowerShell Simulation**: Uses simulated sessions (not real remote)
- **Input Validation**: PowerShell parameters not validated beyond allowlist

**Production Recommendations**: Deploy behind API Gateway (auth/rate limiting), use reverse proxy for SSL termination, add parameter sanitization

## Testing Approach & Notable Gaps

### Test Coverage
- **Unit Tests**: Validator, retry policy, executors, metrics (xUnit)
- **Integration Tests**: End-to-end flows with `WebApplicationFactory`
- **Deterministic**: No external dependencies; uses mocks

### Notable Gaps
- **Circuit Breaker**: Interface exists, not implemented
- **Load Testing**: No concurrent request stress tests
- **Real PowerShell**: Uses simulated sessions
- **Security Testing**: No injection/prevention tests
- **Container Tests**: No Docker integration tests

## Troubleshooting

### Common Issues

**Issue**: `Missing X-Forward-Base header`
- **Solution**: Add `-H "X-Forward-Base: https://your-api.com"` to your request

**Issue**: `Command not in allowlist`
- **Solution**: Update `appsettings.json` → `Executors:PowerShell:AllowedCommands`

**Issue**: `Body too large`
- **Solution**: Increase `Service:MaxRequestBodySizeKb` in configuration

**Issue**: `Request timeout`
- **Solution**: Increase `RetryPolicy:PerAttemptTimeoutMs` or reduce data size

## Additional Resources

- **Testing Documentation**: `src/TESTING.md`
- **Architecture Details**: See inline code documentation for design rationale

## If I Had More Time…

**Circuit Breaker**: The `IResiliencePolicy` interface exists but isn't implemented. Would add half-open state logic to prevent cascading failures when downstream services are degraded.

**PowerShell Session Pooling**: Current per-request isolation prioritizes security but adds latency. Would implement tenant-scoped session pools with automatic cleanup to balance performance and isolation.

**Rate Limiting**: Currently relies on external proxies. Would add per-client/per-endpoint rate limiting with sliding window algorithm and configurable limits.

**Authentication & Authorization**: Would integrate with OAuth2/JWT for API authentication and implement role-based access control for PowerShell commands.

**Security Hardening**: Would add PowerShell parameter sanitization, SQL injection prevention tests, and request signing/validation for production deployments.

**Enhanced Observability**: Would add distributed tracing (OpenTelemetry), metrics export (Prometheus), and structured error categorization for better incident response.
