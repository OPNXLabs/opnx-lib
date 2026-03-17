using System.Buffers;
using System.IO.Compression;

namespace OPNX.Lib.Common.Compression
{
    public sealed class GZipCompressionProvider : ICompressionProvider
    {
        public (IMemoryOwner<byte> owner, int size) Compress(ReadOnlySpan<byte> data)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                gz.Write(data);
            }

            byte[] compressed = ms.ToArray(); // 안전하게 소유권 확보
            var owner = MemoryPool<byte>.Shared.Rent(compressed.Length);
            compressed.CopyTo(owner.Memory.Span);
            return (owner, compressed.Length);
        }

        public (IMemoryOwner<byte> owner, int size) Decompress(ReadOnlySequence<byte> data)
        {
            byte[] compressed = data.IsSingleSegment ? data.First.ToArray() : data.ToArray();

            using var input = new MemoryStream(compressed, writable: false);
            using var gz = new GZipStream(input, CompressionMode.Decompress);

            using var output = new MemoryStream();
            gz.CopyTo(output);

            // output의 유효 바이트만 owner로 복사
            int size = (int)output.Length;
            var owner = MemoryPool<byte>.Shared.Rent(size);
            output.GetBuffer().AsSpan(0, size).CopyTo(owner.Memory.Span);

            return (owner, size);
        }
    }
}
