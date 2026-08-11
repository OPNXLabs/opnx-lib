using System.Buffers;

namespace OPNX.Lib.Network.Protocol.Framing
{
    public sealed class SharedPacketPayload : IDisposable
    {
        private sealed class Lease(SharedPacketPayload owner) : IMemoryOwner<byte>
        {
            private SharedPacketPayload? _owner = owner;

            public Memory<byte> Memory => _owner?.OwnedMemory ?? Memory<byte>.Empty;

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.Release();
            }
        }

        private readonly object _syncRoot = new();
        private IMemoryOwner<byte>? _owner;
        private int _referenceCount = 1;
        private bool _rootReleased;

        public SharedPacketPayload(IMemoryOwner<byte> owner, int length)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if ((uint)length > (uint)owner.Memory.Length)
                throw new ArgumentOutOfRangeException(nameof(length));

            _owner = owner;
            OwnedMemory = owner.Memory[..length];
        }

        private Memory<byte> OwnedMemory { get; }

        public ReadOnlyMemory<byte> Memory => OwnedMemory;

        public int Length => Memory.Length;

        public IMemoryOwner<byte> Rent()
        {
            lock (_syncRoot)
            {
                ObjectDisposedException.ThrowIf(_rootReleased || _owner is null, this);
                _referenceCount++;
                return new Lease(this);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_rootReleased)
                    return;

                _rootReleased = true;
            }

            Release();
        }

        private void Release()
        {
            IMemoryOwner<byte>? owner = null;

            lock (_syncRoot)
            {
                if (--_referenceCount == 0)
                {
                    owner = _owner;
                    _owner = null;
                }
            }

            owner?.Dispose();
        }
    }
}
