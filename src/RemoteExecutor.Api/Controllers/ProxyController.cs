using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using RemoteExecutor.Api.Configuration;

[ApiController]
[Route("api/{**path}")]
public class ProxyController : ControllerBase
{
    private readonly IEnumerable<IExecutor> _executors;
    private readonly IRetryPolicyFactory _retryFactory;
    private readonly IMetricsCollector _metrics;
    private readonly ILogger<ProxyController> _logger;
    private readonly ISystemClock _clock;
    private readonly RequestValidator _validator;
    private readonly string _instanceId;
    private readonly int _maxRequestBodyBytes;

    public ProxyController(
        IEnumerable<IExecutor> executors, 
        IRetryPolicyFactory retryFactory, 
        IMetricsCollector metrics, 
        ILogger<ProxyController> logger, 
        ISystemClock clock,
        RequestValidator validator,
        IOptions<ServiceConfiguration> serviceConfig)
    {
        _executors = executors;
        _retryFactory = retryFactory;
        _metrics = metrics;
        _logger = logger;
        _clock = clock;
        _validator = validator;
        _instanceId = serviceConfig.Value.InstanceId;
        _maxRequestBodyBytes = serviceConfig.Value.MaxRequestBodySizeKb * 1024;
    }

    [HttpGet, HttpPost, HttpPut, HttpPatch, HttpDelete]
    public async Task<IActionResult> CatchAll([FromRoute] string? path)
    {
        var start = _clock.UtcNow;
        
        // Enforce request body size limit early using Content-Length when available
        if (Request.ContentLength.HasValue && Request.ContentLength.Value > _maxRequestBodyBytes)
        {
            _logger.LogWarning("Request body too large: ContentLength={ContentLength} > Max={Max}", Request.ContentLength.Value, _maxRequestBodyBytes);
            return BadRequest(new
            {
                code = "InvalidRequest",
                message = $"Request body exceeds maximum size of {_maxRequestBodyBytes / 1024}KB",
                requestId = Request.Headers.ContainsKey("X-Request-Id") ? Request.Headers["X-Request-Id"].ToString() : Guid.NewGuid().ToString(),
                timestamp = _clock.UtcNow
            });
        }

        // Build envelope with request/correlation IDs
        var reqId = Request.Headers.ContainsKey("X-Request-Id") ? Request.Headers["X-Request-Id"].ToString() : Guid.NewGuid().ToString();
        var corr = Request.Headers.ContainsKey("X-Correlation-Id") ? Request.Headers["X-Correlation-Id"].ToString() : null;

        // Structured logging with request context
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = reqId,
            ["CorrelationId"] = corr ?? "none",
            ["Method"] = Request.Method,
            ["Path"] = path ?? ""
        }))
        {
            _logger.LogInformation("Processing request: Method={Method}, Path={Path}", Request.Method, path);

            string? body = null;
            if (string.Equals(Request.Method, "POST", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Request.Method, "PUT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Request.Method, "PATCH", StringComparison.OrdinalIgnoreCase) ||
                (Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true))
            {
                // Ensure we can read the body stream
                Request.EnableBuffering();
                if (Request.Body.CanSeek)
                {
                    Request.Body.Position = 0;
                }

                // Read body with guard to enforce max size even when Content-Length is absent
                using var sr = new StreamReader(Request.Body);
                var sb = new System.Text.StringBuilder();
                // Buffer size: 8192 chars (16KB for UTF-16) balances I/O efficiency with memory usage.
                // Rationale: Optimal buffer size for network I/O, derived from common OS page sizes (4KB-8KB).
                // Smaller buffers increase read syscalls; larger buffers waste memory for small requests.
                var buffer = new char[8192];
                int read;
                var limit = _maxRequestBodyBytes + 1; // detection threshold
                while ((read = await sr.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    sb.Append(buffer, 0, read);
                    if (sb.Length > _maxRequestBodyBytes)
                    {
                        _logger.LogWarning("Request body too large (stream): Read={Read} > Max={Max}", sb.Length, _maxRequestBodyBytes);
                        return BadRequest(new
                        {
                            code = "InvalidRequest",
                            message = $"Request body exceeds maximum size of {_maxRequestBodyBytes / 1024}KB",
                            requestId = Request.Headers.ContainsKey("X-Request-Id") ? Request.Headers["X-Request-Id"].ToString() : Guid.NewGuid().ToString(),
                            timestamp = _clock.UtcNow
                        });
                    }
                }
                body = sb.ToString();
            }

            // URL decode the path to handle encoded slashes
            var decodedPath = path != null ? System.Web.HttpUtility.UrlDecode(path) : "";
            
            var envelope = new RequestEnvelope
            {
                RequestId = reqId,
                CorrelationId = corr,
                Method = Request.Method,
                Path = decodedPath,
                Query = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                Headers = Request.Headers.ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                Body = body
            };

            // Validation layer with structured error responses
            var validation = _validator.Validate(envelope);
            if (!validation.IsValid)
            {
                _metrics.Increment("requests.invalid");
                _logger.LogWarning("Request validation failed: {ErrorCode} - {Message}", validation.ErrorCode, validation.Message);
                return BadRequest(new 
                { 
                    code = "InvalidRequest", 
                    message = validation.Message, 
                    requestId = reqId,
                    timestamp = _clock.UtcNow
                });
            }

            // Executor selection
            var executorType = envelope.Headers.TryGetValue("X-Executor-Type", out var et) ? et : "http";
            var executor = _executors.FirstOrDefault(e => e.Name.Equals(executorType, StringComparison.OrdinalIgnoreCase));
            if (executor == null)
            {
                _metrics.Increment("requests.badexecutor");
                _logger.LogWarning("Unsupported executor type: {ExecutorType}", executorType);
                return BadRequest(new 
                { 
                    code = "UnsupportedExecutor", 
                    message = $"Executor '{executorType}' not found. Supported: http, powershell", 
                    requestId = reqId,
                    timestamp = _clock.UtcNow
                });
            }

            _logger.LogInformation("Selected executor: {ExecutorType}", executor.Name);

            // Execute with retry policy
            var policy = _retryFactory.CreateDefaultPolicy();
            var retryResult = await policy.ExecuteAsync(async (attempt, token) =>
            {
                var attemptStart = _clock.UtcNow;
                _logger.LogInformation("Executing attempt {Attempt} with executor {Executor}", attempt, executor.Name);
                
                var er = await executor.ExecuteAsync(envelope, token);
                er.StartedAt = attemptStart;
                er.CompletedAt = _clock.UtcNow;
                
                return er;
            }, envelope.RequestId);
            
            var end = _clock.UtcNow;
            var latency = end - start;
            _metrics.AddLatency(latency);

            // Track retry metrics
            if (retryResult.TotalAttempts > 1)
            {
                _metrics.Increment("requests.retried");
            }

            // Build response envelope with all attempt summaries
            var attemptSummaries = retryResult.Attempts.Select(a => new AttemptSummary(
                a.Attempt,
                a.IsSuccess ? "Success" : (a.IsTransientFailure ? "TransientFailure" : "PermanentFailure"),
                a.ErrorMessage ?? a.ErrorCode
            )).ToList();

            var finalResult = retryResult.FinalResult;
            var resp = new ResponseEnvelope
            {
                RequestId = envelope.RequestId,
                CorrelationId = envelope.CorrelationId,
                ExecutorType = executor.Name,
                StartedAt = start,
                CompletedAt = end,
                OverallStatus = finalResult.IsSuccess ? "Success" : "Failure",
                Attempts = retryResult.TotalAttempts,
                AttemptSummaries = attemptSummaries,
                ExecutorResult = BuildExecutorResult(finalResult)
            };

            // Add response headers for traceability
            Response.Headers["X-Request-Id"] = envelope.RequestId;
            Response.Headers["X-Instance-Id"] = _instanceId;
            Response.Headers["X-Executor"] = executor.Name;
            Response.Headers["X-Attempts"] = retryResult.TotalAttempts.ToString();
            if (corr != null)
                Response.Headers["X-Correlation-Id"] = corr;

            // Update metrics
            _metrics.Increment("requests.total");
            if (finalResult.IsSuccess) 
                _metrics.Increment("requests.success"); 
            else 
                _metrics.Increment("requests.failed");

            _logger.LogInformation("Request completed: Status={Status}, Attempts={Attempts}, Latency={LatencyMs}ms", 
                resp.OverallStatus, retryResult.TotalAttempts, latency.TotalMilliseconds);

            return StatusCode(finalResult.StatusCode ?? 200, resp);
        }
    }

    private static object BuildExecutorResult(ExecutionResult result)
    {
        if (result.IsSuccess)
        {
            // HTTP executor result
            if (result.StatusCode.HasValue)
            {
                return new 
                { 
                    httpStatus = result.StatusCode, 
                    headers = result.ResponseHeaders, 
                    body = result.ResponseBody 
                };
            }
            
            // PowerShell executor result
            if (result.PsCommand != null)
            {
                return new 
                { 
                    psCommand = result.PsCommand, 
                    psStdout = result.PsStdout, 
                    psStderr = result.PsStderr,
                    psObjects = result.PsObjects
                };
            }
        }
        
        // Error result
        return new 
        { 
            errorCode = result.ErrorCode, 
            error = result.ErrorMessage,
            isTransient = result.IsTransientFailure
        };
    }
}
