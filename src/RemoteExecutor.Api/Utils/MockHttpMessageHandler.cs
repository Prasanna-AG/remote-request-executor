using System.Net;
using System.Net.Http;

/// <summary>
/// Mock HTTP message handler for testing without external network calls.
/// Returns successful responses with test data based on request path.
/// Only used when UseMockHttpClient is enabled in Development mode.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Simulate different responses based on URL path
        var path = request.RequestUri?.AbsolutePath ?? "";
        var query = request.RequestUri?.Query ?? "";
        var method = request.Method.Method;
        var statusCode = HttpStatusCode.OK;
        var body = "{\"id\":1,\"name\":\"test-response\",\"status\":\"ok\",\"method\":\"" + method + "\",\"path\":\"" + path + "\",\"url\":\"" + 
                   request.RequestUri?.ToString() + "\"}";

        // Simulate error responses for certain paths
        if (path.Contains("/error") || path.Contains("/404") || path.Contains("/notfound"))
        {
            statusCode = HttpStatusCode.NotFound;
            body = "{\"error\":\"Not Found\",\"path\":\"" + path + "\"}";
        }
        else if (path.Contains("/500") || path.Contains("/error5xx") || path.Contains("/servererror"))
        {
            statusCode = HttpStatusCode.InternalServerError;
            body = "{\"error\":\"Internal Server Error\",\"path\":\"" + path + "\"}";
        }
        else if (path.Contains("/503") || path.Contains("/unavailable"))
        {
            statusCode = HttpStatusCode.ServiceUnavailable;
            body = "{\"error\":\"Service Unavailable\",\"path\":\"" + path + "\"}";
        }
        else if (path.Contains("/400") || path.Contains("/badrequest"))
        {
            statusCode = HttpStatusCode.BadRequest;
            body = "{\"error\":\"Bad Request\",\"path\":\"" + path + "\"}";
        }
        else if (path.Contains("/timeout") || query.Contains("timeout=true"))
        {
            // Simulate timeout by delaying (for retry testing)
            // Use async delay instead of blocking Thread.Sleep to avoid test flakiness
            await Task.Delay(2000, cancellationToken);
            statusCode = HttpStatusCode.RequestTimeout;
            body = "{\"error\":\"Request Timeout\",\"path\":\"" + path + "\"}";
        }
        else
        {
            // Default success response with dynamic content
            var id = ExtractIdFromPath(path) ?? 1;
            body = "{\"id\":" + id + ",\"name\":\"Test User " + id + "\",\"email\":\"user" + id + "@test.com\",\"status\":\"active\",\"path\":\"" + path + "\",\"method\":\"" + method + "\"}";
        }

        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        
        // Add standard headers
        response.Headers.Add("X-Mock-Response", "true");
        response.Headers.Add("X-Requested-Url", request.RequestUri?.ToString() ?? "");
        
        return response;
    }

    private static int? ExtractIdFromPath(string path)
    {
        // Try to extract numeric ID from path (e.g., /users/123 -> 123)
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (int.TryParse(segment, out var id))
            {
                return id;
            }
        }
        return null;
    }
}

