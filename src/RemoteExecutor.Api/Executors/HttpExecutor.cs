using System.Net.Http;
using System.Web;
using Microsoft.Extensions.Options;
using RemoteExecutor.Api.Configuration;

/// <summary>
/// HTTP executor that forwards requests with header filtering and query merging
/// </summary>
public class HttpExecutor : IExecutor
{
    public string Name => "http";
    private readonly HttpClient _http;
    private readonly ILogger<HttpExecutor> _logger;
    private readonly HttpExecutorConfiguration _config;
    private readonly int[] _transientStatusCodes;

    public HttpExecutor(
        ILogger<HttpExecutor> logger, 
        IOptions<HttpExecutorConfiguration> config,
        IOptions<RetryPolicyConfiguration> retryConfig) 
    { 
        _logger = logger;
        _config = config.Value;
        _transientStatusCodes = retryConfig.Value.TransientStatusCodes;
        
        // Create HttpClient with automatic decompression and timeout
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_config.DefaultTimeoutSeconds)
        };
        
        _logger.LogInformation("HttpExecutor initialized: MaxResponseBody={MaxKb}KB, Timeout={Timeout}s", 
            _config.MaxResponseBodyLengthKb, _config.DefaultTimeoutSeconds);
    }

    public async Task<ExecutionResult> ExecuteAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Forward-Base", out var baseUri))
            return ExecutionResult.FromError("BadConfiguration", "Missing X-Forward-Base header", false);

        try
        {
            // Combine base URL + path
            var baseUriObj = new Uri(baseUri);
            var path = $"{baseUriObj.AbsolutePath.TrimEnd('/')}/{request.Path.TrimStart('/')}";
            var builder = new UriBuilder(baseUri) { Path = path };

            // Merge query parameters (request.Query takes precedence)
            var q = HttpUtility.ParseQueryString(string.Empty);
            foreach (var kv in request.Query) q[kv.Key] = kv.Value;
            builder.Query = q.ToString();

            _logger.LogInformation("Forwarding HTTP request: Method={Method}, URL={Url}", request.Method, MaskSensitiveUrl(builder.Uri.ToString()));

            var msg = new HttpRequestMessage(new HttpMethod(request.Method), builder.Uri);

            // Filter and forward headers based on configuration
            var forwardedHeaders = new List<string>();
            foreach (var h in request.Headers)
            {
                if (ShouldFilterHeader(h.Key))
                {
                    _logger.LogDebug("Filtered header: {HeaderName}", h.Key);
                    continue;
                }

                if (msg.Headers.TryAddWithoutValidation(h.Key, h.Value))
                {
                    forwardedHeaders.Add(h.Key);
                }
            }
            _logger.LogDebug("Forwarded {Count} headers: {Headers}", forwardedHeaders.Count, string.Join(", ", forwardedHeaders));

            // Add body for applicable methods
            if (!string.IsNullOrEmpty(request.Body) && (request.Method == "POST" || request.Method == "PUT" || request.Method == "PATCH"))
            {
                msg.Content = new StringContent(request.Body, System.Text.Encoding.UTF8, "application/json");
            }

            // Execute request
            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(msg, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("HTTP request timed out: {Url}", MaskSensitiveUrl(builder.Uri.ToString()));
                return ExecutionResult.FromError("Timeout", "Operation timed out", true);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed: {Url}", MaskSensitiveUrl(builder.Uri.ToString()));
                return ExecutionResult.FromError("NetworkError", ex.Message, true);
            }

            // Read and truncate response body
            var body = resp.Content == null ? "" : await resp.Content.ReadAsStringAsync(cancellationToken);
            var maxBodyLength = _config.MaxResponseBodyLengthKb * 1024;
            var originalLength = body.Length;
            if (body.Length > maxBodyLength)
            {
                body = body[..maxBodyLength] + $"...[truncated from {originalLength} to {maxBodyLength} bytes]";
            }

            // Collect response headers
            var headers = new Dictionary<string, string>();
            foreach (var h in resp.Headers) 
                headers[h.Key] = string.Join(';', h.Value);
            if (resp.Content?.Headers != null)
                foreach (var h in resp.Content.Headers) 
                    headers[h.Key] = string.Join(';', h.Value);

            var statusCode = (int)resp.StatusCode;
            _logger.LogInformation("HTTP response received: Status={Status}, BodyLength={BodyLength}, Truncated={Truncated}", 
                statusCode, body.Length, originalLength > maxBodyLength);

            if (statusCode >= 400)
            {
                _logger.LogWarning("HTTP error response: Status={Status}, Body={Body}", statusCode, body.Length > 500 ? body[..500] : body);
            }

            return ExecutionResult.FromHttp(statusCode, headers, body, _transientStatusCodes);
        }
        catch (UriFormatException ex)
        {
            _logger.LogError(ex, "Invalid URI: {BaseUri}", baseUri);
            return ExecutionResult.FromError("InvalidUri", "Invalid base URI format", false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in HttpExecutor");
            return ExecutionResult.FromError("ExecutorError", ex.Message, false);
        }
    }

    /// <summary>
    /// Determines if a header should be filtered based on configuration
    /// </summary>
    private bool ShouldFilterHeader(string headerName)
    {
        // Always filter these sensitive headers
        if (_config.FilteredHeaders.Any(f => string.Equals(f, headerName, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Filter our custom X- headers
        if (headerName.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
            return true;

        // Filter host and security headers
        if (string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase))
            return true;
        
        if (headerName.StartsWith("sec-", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Masks sensitive data in URLs (API keys, tokens in query strings)
    /// </summary>
    private static string MaskSensitiveUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        
        // Mask common sensitive query parameters
        var sensitiveParams = new[] { "api_key", "apikey", "token", "secret", "password", "pwd" };
        foreach (var param in sensitiveParams)
        {
            var pattern = $"{param}=([^&]*)";
            url = System.Text.RegularExpressions.Regex.Replace(
                url, 
                pattern, 
                $"{param}=***MASKED***", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        return url;
    }
}
