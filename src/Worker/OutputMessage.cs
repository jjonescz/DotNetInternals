using System.Text.Json.Serialization;

namespace DotNetInternals;

[JsonDerivedType(typeof(Ready), nameof(Ready))]
[JsonDerivedType(typeof(Empty), nameof(Empty))]
[JsonDerivedType(typeof(Success), nameof(Success))]
[JsonDerivedType(typeof(Failure), nameof(Failure))]
public abstract record WorkerOutputMessage
{
    public required int Id { get; init; }

    public sealed record Ready : WorkerOutputMessage;

    public sealed record Empty : WorkerOutputMessage;

    public sealed record Success(object? Result) : WorkerOutputMessage;

    public sealed record Failure(string Message) : WorkerOutputMessage;
}
