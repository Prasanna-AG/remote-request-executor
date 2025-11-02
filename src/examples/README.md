# Examples & Testing Resources

This directory contains example requests, responses, and testing scripts for the Remote Request Executor API.

## 📁 Files in This Directory

### Example JSON Files
- `http-request-example.json` - Sample HTTP executor GET request with headers
- `http-response-example.json` - Sample HTTP executor success response
- `http-post-request-example.json` - Sample HTTP executor POST request with body
- `powershell-request-example.json` - Sample PowerShell executor request with Get-Mailbox command
- `powershell-response-example.json` - Sample PowerShell executor success response with simulated output
- `powershell-error-response-example.json` - Sample PowerShell error response for disallowed commands
- `error-response-example.json` - Sample error response with common error codes
- `retry-response-example.json` - Sample response after automatic retries with attempt summaries

> **Note**: These JSON files demonstrate the request structure (headers, query params, body) and the response envelope format. Use them as references when integrating with the API.

### Test Scripts
- `test-endpoints.sh` - Bash script for testing all endpoints (Linux/Mac)
- `test-endpoints.ps1` - PowerShell script for testing all endpoints (Windows)
- `QUICK-TEST-GUIDE.md` - Step-by-step testing guide

### Postman Collection
- `../postman/Remote-Request-Executor.json` - Complete Postman collection with all endpoints (configured for HTTP)

## 🚀 Quick Start

### Option 1: Automated Test Scripts

**Windows (PowerShell):**
```powershell
cd examples
.\test-endpoints.ps1
```

**Linux/Mac (Bash):**
```bash
cd examples
chmod +x test-endpoints.sh
./test-endpoints.sh
```

### Option 2: Manual cURL
```bash
# Test HTTP executor
curl -X GET "http://localhost:5072/api/users/1" \
  -H "X-Forward-Base: https://jsonplaceholder.typicode.com" \
  -H "X-Executor-Type: http"

# Test PowerShell executor
curl -X POST "http://localhost:5072/api/mailbox/list" \
  -H "X-Executor-Type: powershell" \
  -H "X-PS-Command: Get-Mailbox"
```

## 📋 Required Headers

### HTTP Executor
| Header | Required | Description | Example |
|--------|----------|-------------|---------|
| X-Forward-Base | **Yes** | Base URL to forward to | `https://jsonplaceholder.typicode.com` |
| X-Executor-Type | No | Executor to use (default: http) | `http` |
| X-Request-Id | No | Custom request ID | `my-request-123` |
| X-Correlation-Id | No | Correlation ID for tracing | `corr-456` |

### PowerShell Executor
| Header | Required | Description | Example |
|--------|----------|-------------|---------|
| X-Executor-Type | **Yes** | Must be "powershell" | `powershell` |
| X-PS-Command | **Yes** | PowerShell command to run | `Get-Mailbox` |
| X-PS-Filter | No | Filter expression for command | `Department -eq 'IT'` |
| X-PS-ResultSize | No | Result size limit | `100` |
| X-PS-MaxResults | No | Maximum number of results | `10` |
| X-Request-Id | No | Custom request ID | `ps-req-123` |
| X-Correlation-Id | No | Correlation ID for tracing | `corr-789` |

## ✅ Allowed PowerShell Commands

Only these commands are permitted (allowlist):
- `Get-Mailbox`
- `Get-User`
- `Get-DistributionGroup`

Any other command will be rejected with `CommandNotAllowed` error.

## 🎯 Test Scenarios

See `QUICK-TEST-GUIDE.md` for detailed testing instructions and expected results.

Quick checklist:
- ✅ HTTP request with valid forward base → Success
- ✅ HTTP request without forward base → BadConfiguration error
- ✅ PowerShell with allowed command → Success with simulated output
- ✅ PowerShell with disallowed command → CommandNotAllowed error
- ✅ Unknown executor type → UnsupportedExecutor error
- ✅ Health check at /ping → Pong response
- ✅ Metrics at /metrics → Metrics snapshot

## 📖 Assignment Compliance

These examples demonstrate:
1. ✅ Both HTTP and PowerShell executors
2. ✅ Structured request/response envelopes
3. ✅ Request ID and Correlation ID propagation
4. ✅ Executor-specific headers
5. ✅ Error responses with classification
6. ✅ Timestamps and attempt tracking
7. ✅ Response headers for traceability

## 🔗 Related Documentation

- **Full Test Matrix**: See `../TESTING.md`
- **Architecture**: See `../README.md` (when created)
- **API Reference**: Use the test scripts or cURL commands to test the API

## 📄 Using Example JSON Files

The example JSON files in this directory show:
- **Request Examples**: How to structure HTTP requests (headers, query params, body)
- **Response Examples**: The actual response envelope format returned by the API
- **Error Examples**: Common error scenarios and their response structures
- **Retry Examples**: How retry attempts are reflected in the response

### Viewing Examples
```bash
# Pretty print JSON files
cat http-request-example.json | jq '.'
# or on Windows PowerShell
Get-Content http-request-example.json | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

### Using Examples in Testing
You can use these examples as templates for your API requests. For example:
```bash
# HTTP GET request based on http-request-example.json
curl -X GET "http://localhost:5072/api/users/1" \
  -H "X-Forward-Base: https://jsonplaceholder.typicode.com" \
  -H "X-Executor-Type: http" \
  -H "X-Request-Id: req-http-001"

# HTTP POST request based on http-post-request-example.json
curl -X POST "http://localhost:5072/api/posts" \
  -H "X-Forward-Base: https://jsonplaceholder.typicode.com" \
  -H "X-Executor-Type: http" \
  -H "Content-Type: application/json" \
  -d '{"title":"My New Post","body":"Content","userId":1}'
```

## 💡 Tips

1. **Running the Application**: The app runs on HTTP by default. Use `dotnet run --launch-profile http` in the `RemoteExecutor.Api` directory. See `QUICK-TEST-GUIDE.md` section "0. Running the Application" for details.
2. **JSON Formatting**: Pipe curl output through `jq` for pretty printing: `curl ... | jq '.'`
3. **Testing APIs**: Use JSONPlaceholder (https://jsonplaceholder.typicode.com) or HTTPBin (https://httpbin.org) as test targets
4. **Debugging**: Check `/metrics` endpoint to see request counts and latencies
5. **Example Files**: Reference the example JSON files to understand request/response structures
6. **Production HTTPS**: For production deployments, use a reverse proxy (nginx/traefik) for SSL termination in front of the HTTP application.


