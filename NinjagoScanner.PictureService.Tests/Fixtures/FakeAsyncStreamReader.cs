using Grpc.Core;

namespace NinjagoScanner.PictureService.Tests.Fixtures;

/// <summary>
/// Feeds a pre-built sequence of messages to a client-streaming RPC handler under test, standing
/// in for the real gRPC client stream.
/// </summary>
internal sealed class FakeAsyncStreamReader<T>(IEnumerable<T> messages) : IAsyncStreamReader<T>
    where T : class
{
    private readonly IEnumerator<T> enumerator = messages.GetEnumerator();

    public T Current { get; private set; } = null!;

    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (enumerator.MoveNext())
        {
            Current = enumerator.Current;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}
