using System.Buffers;

namespace OPNX.Lib.Common.Buffers
{

    internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private IMemoryOwner<byte> _owner;
        private int _written;

        public PooledBufferWriter(int initialSize = 4096)
        {
            if (initialSize < 1) initialSize = 1;
            _owner = MemoryPool<byte>.Shared.Rent(initialSize);
            _written = 0;
        }

        public int WrittenCount => _written;
        public ReadOnlyMemory<byte> WrittenMemory => _owner.Memory[.._written];

        public void Advance(int count) => _written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _owner.Memory.Slice(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _owner.Memory.Span.Slice(_written);
        }

        private void Ensure(int sizeHint)
        {
            if (sizeHint <= 0) sizeHint = 1;

            int available = _owner.Memory.Length - _written;
            if (available >= sizeHint) return;

            int newSize = Math.Max(_owner.Memory.Length * 2, _written + sizeHint);
            var newOwner = MemoryPool<byte>.Shared.Rent(newSize);

            _owner.Memory[.._written].CopyTo(newOwner.Memory);

            _owner.Dispose();
            _owner = newOwner;
        }

        /// <summary>소유권을 호출자에게 넘김(호출자는 반드시 Dispose 해야 함)</summary>
        public (IMemoryOwner<byte> owner, int written) Detach()
        {
            var owner = _owner;
            var written = _written;

            // Detach 이후 Dispose 안전용 더미 owner로 교체
            _owner = MemoryPool<byte>.Shared.Rent(1);
            _written = 0;

            return (owner, written);
        }

        public void Dispose() => _owner.Dispose();
    }
}
