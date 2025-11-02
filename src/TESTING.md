# Testing Documentation

## Overview

This document outlines the testing strategy for the Remote Request Executor service, including unit tests, integration tests, and manual testing procedures.

## Test Coverage

### Unit Tests

1. **RequestValidatorTests** - Validates request envelope validation logic
2. **RetryPolicyTests** - Tests retry logic with transient and permanent failures
3. **HttpExecutorTests** - Tests HTTP executor configuration and error handling
4. **PowerShellExecutorTests** - Tests PowerShell executor with allowlist and validation
5. **MetricsCollectorTests** - Tests metrics collection and aggregation

### Integration Tests

**IntegrationTests** - End-to-end API tests covering:
- Health and metrics endpoints
- HTTP executor flow
- PowerShell executor flow
- Error scenarios
- Response envelope structure
- Request/correlation ID tracing
- Input validation at API boundary

## Test Matrix

| Scenario | Executor | Expected Outcome | Transient? | Retries Used | Key Assertions |
|----------|----------|------------------|------------|--------------|----------------|
| **Valid HTTP request with forward base** | HTTP | Success (200) | No | 1 | Response contains data, requestId present, executor=http |
| **HTTP request missing X-Forward-Base** | HTTP | Error (BadConfiguration) | No | 1 | ErrorCode=BadConfiguration, IsTransient=false |
| **HTTP request with invalid URL** | HTTP | Error (NetworkError or parsing) | Varies | 1-3 | Error returned, may retry if network-related |
| **PowerShell with allowlisted command** | PowerShell | Success (200) | No | 1 | PsStdout populated, command echoed, timestamps set |
| **PowerShell with disallowed command** | PowerShell | Error (CommandNotAllowed) | No | 1 | ErrorCode=CommandNotAllowed, IsTransient=false |
| **PowerShell missing X-PS-Command header** | PowerShell | Error (MissingCommand) | No | 1 | ErrorCode=MissingCommand, validation fails |
| **Request with unsupported executor type** | N/A | BadRequest (UnsupportedExecutor) | No | 0 | 400 status, no executor selected |
| **Request with oversized body (>1MB)** | N/A | BadRequest (InvalidRequest) | No | 0 | Validation fails, 400 status |
| **Request with custom Request-Id** | Any | Success/Failure | Varies | Varies | RequestId echoed in response & headers |
| **Request with Correlation-Id** | Any | Success/Failure | Varies | Varies | CorrelationId propagated to response |
| **Transient HTTP failure (simulated 503)** | HTTP | Retry then Success/Failure | Yes | 1-3 | Retries attempted, attempt count in response |
| **Non-transient HTTP failure (400)** | HTTP | Immediate failure | No | 1 | No retry, attempt=1 |
| **Timeout (per-attempt)** | Any | Error (Timeout) | Yes | Up to maxAttempts | Timeout error, potentially retried |
| **Health endpoint (/ping)** | N/A | Success (pong) | No | 0 | Status 200, body contains "pong" |
| **Metrics endpoint (/metrics)** | N/A | Success (metrics snapshot) | No | 0 | JSON with total, success, failed, latencies |

## Running Tests

### Run All Tests
```bash
cd src/RemoteExecutor.Tests
dotnet test
```

### Run Specific Test Class
```bash
dotnet test --filter "FullyQualifiedName~RetryPolicyTests"
```

### Run with Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Run in Verbose Mode
```bash
dotnet test --logger "console;verbosity=detailed"
```

## Manual Testing

### Using cURL

#### HTTP Executor Test
```bash
curl -X GET "http://localhost:5072/api/users/1" \
  -H "X-Forward-Base: https://jsonplaceholder.typicode.com" \
  -H "X-Executor-Type: http" \
  -H "X-Request-Id: curl-test-1"
```

#### PowerShell Executor Test
```bash
curl -X POST "http://localhost:5072/api/mailbox/list" \
  -H "X-Executor-Type: powershell" \
  -H "X-PS-Command: Get-Mailbox" \
  -H "X-Request-Id: ps-curl-1"
```

#### Health Check
```bash
curl http://localhost:5072/ping
```

#### Metrics
```bash
curl http://localhost:5072/metrics
```

### Using PowerShell (Windows)
```powershell
# HTTP Executor
$headers = @{
    "X-Forward-Base" = "https://jsonplaceholder.typicode.com"
    "X-Executor-Type" = "http"
    "X-Request-Id" = "ps-test-1"
}
Invoke-RestMethod -Uri "http://localhost:5072/api/users/1" -Headers $headers

# PowerShell Executor
$psHeaders = @{
    "X-Executor-Type" = "powershell"
    "X-PS-Command" = "Get-Mailbox"
    "X-Request-Id" = "ps-test-2"
}
Invoke-RestMethod -Uri "http://localhost:5072/api/mailbox/list" -Method Post -Headers $psHeaders
```

## Test Scenarios by Category

### Validation Tests
- ✅ Null request rejection
- ✅ Missing RequestId rejection
- ✅ Oversized body rejection (>1MB)
- ✅ PowerShell without command header
- ✅ Valid requests pass through

### Executor Selection Tests
- ✅ Default to HTTP executor
- ✅ Select PowerShell via header
- ✅ Reject unknown executor types
- ✅ Executor name exposed in response

### Retry & Resilience Tests
- ✅ Success after transient failures
- ✅ No retry on permanent failures
- ✅ Retry count tracking
- ✅ Exponential backoff with jitter
- ✅ Per-attempt timeout enforcement
- ✅ Max attempts respected

### HTTP Executor Tests
- ✅ Missing X-Forward-Base error
- ✅ Invalid URL handling
- ✅ Header forwarding (filtered)
- ✅ Query parameter merging
- ✅ Response body truncation (>4KB)
- ✅ Status code classification (transient vs permanent)

### PowerShell Executor Tests
- ✅ Command allowlist enforcement
- ✅ Disallowed command rejection
- ✅ Missing command header error
- ✅ Simulated session lifecycle
- ✅ Stdout/stderr capture
- ✅ Timestamp tracking

### Observability Tests
- ✅ Metrics increment (total, success, failed)
- ✅ Latency tracking (avg, p95)
- ✅ RequestId generation and propagation
- ✅ CorrelationId forwarding
- ✅ Response headers (X-Request-Id, X-Executor, X-Attempts)
- ✅ Structured response envelope

### Integration Tests
- ✅ End-to-end HTTP flow
- ✅ End-to-end PowerShell flow
- ✅ Health endpoint
- ✅ Metrics endpoint
- ✅ Error response format
- ✅ Request/response envelope structure

## Deterministic Testing

All tests are designed to be deterministic:

- **No external network dependencies** in unit tests
- **Controlled timing** via abstractions (ISystemClock)
- **Mocked randomness** where jitter affects outcomes
- **In-memory test server** for integration tests (WebApplicationFactory)
- **No Thread.Sleep** or real delays in test assertions

## Known Test Gaps

1. **Circuit breaker testing** - Not implemented (stretch feature)
2. **Actual remote PowerShell session** - Currently simulated
3. **Real HTTP timeout scenarios** - Difficult to test deterministically without more sophisticated mocking
4. **Concurrent request handling** - Load/stress testing not included
5. **Container-based integration tests** - Requires Docker test infrastructure
6. **Security testing** - Token masking and injection prevention (manual verification needed)

## Extending Tests

To add tests for new executors:

1. Create `NewExecutorTests.cs` in `RemoteExecutor.Tests`
2. Follow pattern from `HttpExecutorTests` or `PowerShellExecutorTests`
3. Test: name, error cases, success cases, timing
4. Add integration test scenario in `IntegrationTests.cs`
5. Update test matrix in this document

## Continuous Integration

Recommended CI pipeline:
```yaml
- Restore dependencies
- Build solution
- Run unit tests
- Run integration tests
- Generate coverage report
- Fail on <80% coverage (aspirational)
```


