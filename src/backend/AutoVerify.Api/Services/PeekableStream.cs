namespace AutoVerify.Api.Services;

/// <summary>
/// Stream-safe peek/pushback wrapper that reads a small prefix buffer from non-seekable
/// or seekable streams (e.g. HTTP response streams, SeaweedFS) without losing bytes
/// or seeking backwards.
/// </summary>
public sealed class PeekableStream : Stream
{
    private readonly Stream _underlyingStream;
    private readonly byte[] _peekBuffer;
    private readonly int _peekCount;
    private int _peekOffset;
    private readonly bool _leaveOpen;
    private bool _disposed;

    public PeekableStream(Stream underlyingStream, byte[] peekBuffer, int peekCount, bool leaveOpen = false)
    {
        _underlyingStream = underlyingStream ?? throw new ArgumentNullException(nameof(underlyingStream));
        _peekBuffer = peekBuffer ?? throw new ArgumentNullException(nameof(peekBuffer));
        _peekCount = Math.Clamp(peekCount, 0, peekBuffer.Length);
        _peekOffset = 0;
        _leaveOpen = leaveOpen;
    }

    public static async Task<PeekableStream> CreateAsync(
        Stream stream,
        int maxPeekBytes = 2048,
        bool leaveOpen = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = new byte[maxPeekBytes];
        var totalRead = 0;
        while (totalRead < maxPeekBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, maxPeekBytes - totalRead), ct);
            if (read == 0) break;
            totalRead += read;
        }

        return new PeekableStream(stream, buffer, totalRead, leaveOpen);
    }

    public static PeekableStream Create(
        Stream stream,
        int maxPeekBytes = 2048,
        bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = new byte[maxPeekBytes];
        var totalRead = 0;
        while (totalRead < maxPeekBytes)
        {
            var read = stream.Read(buffer, totalRead, maxPeekBytes - totalRead);
            if (read == 0) break;
            totalRead += read;
        }

        return new PeekableStream(stream, buffer, totalRead, leaveOpen);
    }

    public ReadOnlyMemory<byte> PeekedMemory => _peekBuffer.AsMemory(0, _peekCount);
    public ReadOnlySpan<byte> PeekedSpan => _peekBuffer.AsSpan(0, _peekCount);
    public int PeekedCount => _peekCount;

    public override bool CanRead => !_disposed && _underlyingStream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _underlyingStream.CanSeek ? _underlyingStream.Length : throw new NotSupportedException("PeekableStream is non-seekable.");
    public override long Position
    {
        get => throw new NotSupportedException("PeekableStream does not support direct Position access.");
        set => throw new NotSupportedException("PeekableStream does not support setting Position.");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length) throw new ArgumentException("Offset and count exceed destination buffer size.");

        var bytesFromPeek = 0;
        if (_peekOffset < _peekCount)
        {
            bytesFromPeek = Math.Min(count, _peekCount - _peekOffset);
            Array.Copy(_peekBuffer, _peekOffset, buffer, offset, bytesFromPeek);
            _peekOffset += bytesFromPeek;

            if (bytesFromPeek == count)
            {
                return bytesFromPeek;
            }
        }

        var bytesFromUnderlying = _underlyingStream.Read(buffer, offset + bytesFromPeek, count - bytesFromPeek);
        return bytesFromPeek + bytesFromUnderlying;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bytesFromPeek = 0;
        if (_peekOffset < _peekCount)
        {
            bytesFromPeek = Math.Min(destination.Length, _peekCount - _peekOffset);
            _peekBuffer.AsSpan(_peekOffset, bytesFromPeek).CopyTo(destination.Span);
            _peekOffset += bytesFromPeek;

            if (bytesFromPeek == destination.Length)
            {
                return bytesFromPeek;
            }
        }

        var remaining = destination[bytesFromPeek..];
        var bytesFromUnderlying = await _underlyingStream.ReadAsync(remaining, cancellationToken);
        return bytesFromPeek + bytesFromUnderlying;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override void Flush() => _underlyingStream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _underlyingStream.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("PeekableStream does not support seeking.");
    public override void SetLength(long value) => throw new NotSupportedException("PeekableStream is read-only.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("PeekableStream is read-only.");

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing && !_leaveOpen)
            {
                _underlyingStream.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (!_leaveOpen)
            {
                await _underlyingStream.DisposeAsync();
            }
        }
        await base.DisposeAsync();
    }
}
