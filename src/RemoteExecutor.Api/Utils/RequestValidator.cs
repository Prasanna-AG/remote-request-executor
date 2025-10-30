public static class RequestValidator
{
    public static (bool IsValid, string? Message) Validate(RequestEnvelope r)
    {
        if (r == null) return (false, "Request is null");
        if (string.IsNullOrEmpty(r.RequestId)) return (false, "RequestId required");
        if (r.Headers != null && r.Headers.TryGetValue("X-Executor-Type", out var et) && et == "powershell")
        {
            if (!r.Headers.ContainsKey("X-PS-Command")) return (false, "X-PS-Command header required for PowerShell executor");
        }
        if (r.Body != null && r.Body.Length > 1_000_000) return (false, "Body too large");
        return (true, null);
    }
}
