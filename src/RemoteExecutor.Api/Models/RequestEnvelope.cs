public record RequestEnvelope
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = "";
    public Dictionary<string, string> Query { get; init; } = new();
    public Dictionary<string, string> Headers { get; init; } = new();
    public string? Body { get; init; }
}
