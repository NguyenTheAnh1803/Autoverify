namespace AutoVerify.Api.Storage;

public sealed class StorageStreamResult : IAsyncDisposable, IDisposable
{
    public required Stream Stream { get; init; }
    public string? ContentType { get; init; }
    public long? ContentLength { get; init; }
    public string? ContentEncoding { get; init; }
    public IDisposable? LifetimeScope { get; init; }

    public void Dispose()
    {
        Stream.Dispose();
        LifetimeScope?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        if (LifetimeScope is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            LifetimeScope?.Dispose();
        }
    }
}
