namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>
/// A caller's request body as Kestrel hands it over for a body of unknown length: read once, front to back, never
/// sought. It counts what was read, and throws <see cref="failure"/> instead of returning more once it is set.
/// </summary>
public sealed class ReadCountingStream(int length, Exception? failure = null) : Stream
{
    public long BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (failure is not null)
        {
            throw failure;
        }

        var read = (int)Math.Min(buffer.Length, length - BytesRead);
        buffer[..read].Fill((byte)'a');
        BytesRead += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
