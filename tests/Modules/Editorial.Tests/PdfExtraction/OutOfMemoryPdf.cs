using System.Buffers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.PdfExtraction;

/// <summary>
/// A PDF's bytes whose reading runs out of memory, as a parse that inflates a stream beyond the container's memory
/// does: every way to reach the bytes throws <see cref="OutOfMemoryException"/>.
/// </summary>
internal sealed class OutOfMemoryPdf : MemoryManager<byte>
{
    public override Memory<byte> Memory => CreateMemory(1024);

    public override Span<byte> GetSpan() => throw new OutOfMemoryException();

    public override MemoryHandle Pin(int elementIndex = 0) => throw new OutOfMemoryException();

    public override void Unpin()
    {
    }

    protected override bool TryGetArray(out ArraySegment<byte> segment) => throw new OutOfMemoryException();

    protected override void Dispose(bool disposing)
    {
    }
}
