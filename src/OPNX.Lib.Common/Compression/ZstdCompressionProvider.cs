using System.Buffers;
using ZstdSharp;

namespace OPNX.Lib.Common.Compression
{
    public sealed class ZstdCompressionProvider(int level = Compressor.DefaultCompressionLevel) : ICompressionProvider
    {
        private readonly int _level = level;

        public (IMemoryOwner<byte> owner, int size) Compress(ReadOnlySpan<byte> data)
        {
            using var compressor = new Compressor(_level);

            int maxSize = Compressor.GetCompressBound(data.Length);

            IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(maxSize);

            try
            {
                int written = compressor.Wrap(data, owner.Memory.Span);

                if (written <= 0)
                    throw new InvalidDataException("Zstd compression failed.");

                return (owner, written);
            }
            catch
            {
                owner.Dispose();
                throw;
            }
        }

        public (IMemoryOwner<byte> owner, int size) Decompress(ReadOnlySequence<byte> data)
        {
            ReadOnlySpan<byte> span;
            byte[]? temp = null;

            try
            {
                if (data.IsSingleSegment)
                {
                    span = data.FirstSpan;
                }
                else
                {
                    temp = ArrayPool<byte>.Shared.Rent((int)data.Length);
                    data.CopyTo(temp);
                    span = temp.AsSpan(0, (int)data.Length);
                }

                using var decompressor = new Decompressor();

                ulong declared = Decompressor.GetDecompressedSize(span);

                if (declared > 0 && declared != ulong.MaxValue)
                {
                    int outSize = (int)Math.Min(declared, int.MaxValue);
                    IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(outSize);

                    try
                    {
                        int written = decompressor.Unwrap(span, owner.Memory.Span);
                        return (owner, written);
                    }
                    catch
                    {
                        owner.Dispose();
                        throw;
                    }
                }
                else
                {
                    Span<byte> decompressed = decompressor.Unwrap(span);
                    var owner = MemoryPool<byte>.Shared.Rent(decompressed.Length);
                    decompressed.CopyTo(owner.Memory.Span);
                    return (owner, decompressed.Length);
                }
            }
            finally
            {
                if (temp != null)
                    ArrayPool<byte>.Shared.Return(temp);
            }
        }
    }
}
