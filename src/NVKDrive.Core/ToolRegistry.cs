namespace NVKDrive.Core;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, object> registrations = new(StringComparer.Ordinal);

    public void Register<TCommand, TResult>(
        string operation,
        Func<IToolSession, ITool<TCommand, TResult>> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(factory);

        if (!registrations.TryAdd(operation, factory))
        {
            throw new InvalidOperationException($"Operation '{operation}' is already registered.");
        }
    }

    public ToolResult<Func<IToolSession, ITool<TCommand, TResult>>> Resolve<TCommand, TResult>(
        string operation)
    {
        if (!registrations.TryGetValue(operation, out var registration))
        {
            return new ToolResult<Func<IToolSession, ITool<TCommand, TResult>>>.Failure(
                new ToolError("UNKNOWN_OPERATION", $"Operation '{operation}' is not registered.", "operation"));
        }

        if (registration is not Func<IToolSession, ITool<TCommand, TResult>> factory)
        {
            throw new InvalidOperationException(
                $"Operation '{operation}' is registered with different command or result types.");
        }

        return new ToolResult<Func<IToolSession, ITool<TCommand, TResult>>>.Success(factory);
    }
}
