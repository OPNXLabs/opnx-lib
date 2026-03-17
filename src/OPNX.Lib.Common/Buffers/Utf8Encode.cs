using System.Buffers;
using System.Text;

namespace OPNX.Lib.Common.Buffers
{
    public static class Utf8Encode
    {
        public static (IMemoryOwner<byte> owner, int written) EncodePooled(string s)
        {
            if (string.IsNullOrEmpty(s))
                return (MemoryPool<byte>.Shared.Rent(1), 0);

            int max = Encoding.UTF8.GetMaxByteCount(s.Length);
            var owner = MemoryPool<byte>.Shared.Rent(max);
            int written = Encoding.UTF8.GetBytes(s.AsSpan(), owner.Memory.Span);
            return (owner, written);
        }
    }
}
