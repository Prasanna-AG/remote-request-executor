public record AttemptSummary(int Attempt, string Outcome, string? Message);

public record ResponseEnvelope
{
    public string RequestId { get; init; } = "";
    public string? CorrelationId { get; init; }
    public string ExecutorType { get; init; } = "";
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; init; }
    public string OverallStatus { get; init; } = "Failure";
    public int Attempts { get; init; }
    public List<AttemptSummary> AttemptSummaries { get; init; } = new();
    public object? ExecutorResult { get; init; }
}
