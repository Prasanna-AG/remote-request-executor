using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/{**path}")]
public class ProxyController : ControllerBase
{
    private readonly IEnumerable<IExecutor> _executors;
    private readonly IRetryPolicyFactory _retryFactory;
    private readonly IMetricsCollector _metrics;
    private readonly ILogger<ProxyController> _logger;
    private readonly ISystemClock _clock;

    public ProxyController(IEnumerable<IExecutor> executors, IRetryPolicyFactory retryFactory, IMetricsCollector metrics, ILogger<ProxyController> logger, ISystemClock clock)
    {
        _executors = executors;
        _retryFactory = retryFactory;
        _metrics = metrics;
        _logger = logger;
        _clock = clock;
    }

    [HttpGet, HttpPost, HttpPut, HttpPatch, HttpDelete]
    public async Task<IActionResult> CatchAll([FromRoute] string? path)
    {
        // build envelope
        var reqId = Request.Headers.ContainsKey("X-Request-Id") ? Request.Headers["X-Request-Id"].ToString() : Guid.NewGuid().ToString();
        var corr = Request.Headers.ContainsKey("X-Correlation-Id") ? Request.Headers["X-Correlation-Id"].ToString() : null;

        string body = null;
        if (Request.ContentLength > 0 && Request.ContentType?.Contains("application/json") == true)
        {
            using var sr = new StreamReader(Request.Body);
            body = await sr.ReadToEndAsync();
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

        var validation = RequestValidator.Validate(envelope);
        if (!validation.IsValid)
        {
            _metrics.Increment("requests.invalid");
            return BadRequest(new { code = "InvalidRequest", message = validation.Message, requestId = reqId });
        }

        var executorType = envelope.Headers.TryGetValue("X-Executor-Type", out var et) ? et : "http";
        var executor = _executors.FirstOrDefault(e => e.Name.Equals(executorType, StringComparison.OrdinalIgnoreCase));
        if (executor == null)
        {
            _metrics.Increment("requests.badexecutor");
            return BadRequest(new { code = "UnsupportedExecutor", message = $"Executor '{executorType}' not found", requestId = reqId });
        }

        var policy = _retryFactory.CreateDefaultPolicy();
        var start = _clock.UtcNow;
        ExecutionResult result = await policy.ExecuteAsync(async (attempt, token) =>
        {
            _logger.LogInformation("Executing {executor} attempt {attempt} requestId={reqId}", executor.Name, attempt, envelope.RequestId);
            var er = await executor.ExecuteAsync(envelope, token);
            er.Attempt = attempt;
            return er;
        });
        var end = _clock.UtcNow;

        // Build response envelope
        var resp = new ResponseEnvelope
        {
            RequestId = envelope.RequestId,
            CorrelationId = envelope.CorrelationId,
            ExecutorType = executor.Name,
            StartedAt = result.StartedAt == default ? start : result.StartedAt,
            CompletedAt = result.CompletedAt == default ? end : result.CompletedAt,
            OverallStatus = result.IsSuccess ? "Success" : "Failure",
            Attempts = result.Attempt,
            AttemptSummaries = new List<AttemptSummary> { new AttemptSummary(result.Attempt, result.IsSuccess ? "Success" : "Failure", result.ErrorMessage) },
            ExecutorResult = result.IsSuccess ? (object)new { httpStatus = result.StatusCode, headers = result.ResponseHeaders, body = result.ResponseBody, psCommand = result.PsCommand, psStdout = result.PsStdout, psStderr = result.PsStderr } :
                                                   new { errorCode = result.ErrorCode, error = result.ErrorMessage }
        };

        // Add response headers for traceability
        Response.Headers["X-Request-Id"] = envelope.RequestId;
        Response.Headers["X-Executor"] = executor.Name;
        Response.Headers["X-Attempts"] = result.Attempt.ToString();

        _metrics.Increment("requests.total");
        if (result.IsSuccess) _metrics.Increment("requests.success"); else _metrics.Increment("requests.failed");

        return StatusCode(result.StatusCode ?? 200, resp);
    }
}
