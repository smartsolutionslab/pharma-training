using System.Text;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Exports;

/// <summary>
/// A blob whose connection is lost while it is read: readable forward only, its first read yields the start of a PDF
/// (<c>%PDF-1.7</c> and a line break), and every later read, synchronous or not, throws an <see cref="IOException"/>.
/// </summary>
internal sealed class FailingReadStream : Stream
{
    private static readonly byte[] Start = Encoding.ASCII.GetBytes("%PDF-1.7\n");

    private bool started;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (started)
        {
            throw new IOException("The blob's connection was lost.");
        }

        started = true;
        var length = Math.Min(Start.Length, buffer.Length);
        Start.AsSpan(0, length).CopyTo(buffer);
        return length;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
