using System.Buffers;

namespace OPNX.Lib.Common.Compression
{
    public enum CompressionLevel
    {
        Default = 0,
        Min,
        Max
    }


    public interface ICompressionProvider
    {
        (IMemoryOwner<byte> owner, int size) Compress(ReadOnlySpan<byte> data);
        (IMemoryOwner<byte> owner, int size) Decompress(ReadOnlySequence<byte> data);
    }
}
