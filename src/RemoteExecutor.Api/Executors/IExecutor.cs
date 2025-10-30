public interface IExecutor
{
    string Name { get; }
    Task<ExecutionResult> ExecuteAsync(RequestEnvelope request, CancellationToken cancellationToken);
}
