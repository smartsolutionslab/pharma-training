namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Exports;

/// <summary>
/// A response body as Kestrel gives it: written forward only, never sought or read, and refusing every synchronous write
/// or flush. What was written is kept in <see cref="Written"/>.
/// </summary>
internal sealed class AsyncOnlyStream : Stream
{
    private readonly MemoryStream written = new();

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public byte[] Written => written.ToArray();

    public override void Flush() => throw new InvalidOperationException("Synchronous operations are disallowed.");

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous operations are disallowed.");

    public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Synchronous operations are disallowed.");

    public override void WriteByte(byte value) => throw new InvalidOperationException("Synchronous operations are disallowed.");

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        written.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        written.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            written.Dispose();
        }

        base.Dispose(disposing);
    }
}
