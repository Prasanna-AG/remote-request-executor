using System.Net.Http;
using System.Web;

public class HttpExecutor : IExecutor
{
    public string Name => "http";
    private readonly HttpClient _http;
    private readonly ILogger<HttpExecutor> _logger;

    public HttpExecutor(ILogger<HttpExecutor> logger) 
    { 
        _logger = logger;
        
        // Create HttpClient with automatic decompression
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        _http = new HttpClient(handler);
    }

    public async Task<ExecutionResult> ExecuteAsync(RequestEnvelope request, CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Forward-Base", out var baseUri))
            return ExecutionResult.FromError("BadConfiguration", "Missing X-Forward-Base header", false);

        _logger.LogInformation("HttpExecutor: baseUri={BaseUri}, path={Path}", baseUri, request.Path);

        // combine base + path
        var baseUriObj = new Uri(baseUri);
        var path = $"{baseUriObj.AbsolutePath.TrimEnd('/')}/{request.Path.TrimStart('/')}";
        var builder = new UriBuilder(baseUri) { Path = path };

        _logger.LogInformation("HttpExecutor: Final URL={Url}", builder.Uri);

        // merge query: request.Query wins
        var q = HttpUtility.ParseQueryString(string.Empty);
        foreach (var kv in request.Query) q[kv.Key] = kv.Value;
        builder.Query = q.ToString();

        var msg = new HttpRequestMessage(new HttpMethod(request.Method), builder.Uri);

        // copy headers, except blocked ones
        var forwardedHeaders = new List<string>();
        foreach (var h in request.Headers)
        {
            // Skip headers that shouldn't be forwarded
            if (string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) continue;
            if (h.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase)) continue; // Skip our custom headers
            if (string.Equals(h.Key, "Host", StringComparison.OrdinalIgnoreCase)) continue;
            if (h.Key.StartsWith("sec-", StringComparison.OrdinalIgnoreCase)) continue; // Skip browser security headers

            if (msg.Headers.TryAddWithoutValidation(h.Key, h.Value))
            {
                forwardedHeaders.Add(h.Key);
            }
        }
        _logger.LogInformation("HttpExecutor: Forwarded headers: {Headers}", string.Join(", ", forwardedHeaders));

        if (!string.IsNullOrEmpty(request.Body) && (request.Method == "POST" || request.Method == "PUT" || request.Method == "PATCH"))
        {
            msg.Content = new StringContent(request.Body, System.Text.Encoding.UTF8, "application/json");
        }

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(msg, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ExecutionResult.FromError("Timeout", "Operation timed out", true);
        }
        catch (HttpRequestException ex)
        {
            return ExecutionResult.FromError("NetworkError", ex.Message, true);
        }

        var body = resp.Content == null ? "" : await resp.Content.ReadAsStringAsync();
        if (body.Length > 4096) body = body[..4096] + "...[truncated]";

        var headers = new Dictionary<string, string>();
        foreach (var h in resp.Headers) headers[h.Key] = string.Join(';', h.Value);
        if (resp.Content?.Headers != null)
            foreach (var h in resp.Content.Headers) headers[h.Key] = string.Join(';', h.Value);

        _logger.LogInformation("HttpExecutor: Response status={Status}, bodyLength={BodyLength}", (int)resp.StatusCode, body.Length);

        // Log error responses for debugging
        if ((int)resp.StatusCode >= 400)
        {
            _logger.LogWarning("HttpExecutor: Error response body: {Body}", body);
        }

        return ExecutionResult.FromHttp((int)resp.StatusCode, headers, body);
    }

    private static string MaskIfSensitive(string v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        if (v.Length <= 8) return v[..1] + "..." + v[^1..];
        return v[..3] + "..." + v[^3..];
    }
}
