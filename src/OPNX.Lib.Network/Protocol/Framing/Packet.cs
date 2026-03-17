using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace OPNX.Lib.Network.Protocol.Framing
{
    public sealed class Packet : IDisposable
    {
        #region Fields
        private IMemoryOwner<byte>? _payloadOwner;
        private bool _disposed;
        #endregion

        #region Properties
        public PacketHeader Header { get; }
        public ReadOnlyMemory<byte> Payload { get; private set; }
        #endregion

        #region Constructors
        // owner 기반 생성자 (소유권 이전)
        public Packet(PacketHeader header, IMemoryOwner<byte> payloadOwner, int payloadSize)
        {
            ArgumentNullException.ThrowIfNull(payloadOwner);
            //if (payloadOwner is null) throw new ArgumentNullException(nameof(payloadOwner));
            if ((uint)payloadSize > (uint)payloadOwner.Memory.Length)
                throw new ArgumentOutOfRangeException(nameof(payloadSize));

            Header = header;

            _payloadOwner = payloadOwner;
            //Payload = payloadOwner.Memory.Slice(0, payloadSize);
            Payload = payloadOwner.Memory[..payloadSize];

            if (header.PayloadLength != (uint)payloadSize)
                throw new ArgumentException("Header.PayloadLength must match payloadSize.", nameof(header));
        }

        // ReadOnlyMemory 기반 생성자 → 내부에서 복사해서 소유
        public Packet(PacketHeader header, ReadOnlyMemory<byte> payload)
        {
            Header = header;

            var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
            payload.CopyTo(owner.Memory);

            _payloadOwner = owner;
            //Payload = owner.Memory.Slice(0, payload.Length);
            Payload = owner.Memory[..payload.Length];

            if (header.PayloadLength != (uint)payload.Length)
                throw new ArgumentException("Header.PayloadLength must match payload.Length.", nameof(header));
        }
        #endregion

        #region Public Methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTo(PipeWriter writer)
        {
            ObjectDisposedException.ThrowIf(_disposed, typeof(Packet));

            ArgumentNullException.ThrowIfNull(writer);

            //if (_disposed) throw new ObjectDisposedException(nameof(Packet));
            //if (writer is null) throw new ArgumentNullException(nameof(writer));

            // Header
            writer.WriteHeaderTo(Header);

            // Payload
            writer.Write(Payload.Span);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _payloadOwner?.Dispose();
            _payloadOwner = null;
            Payload = default;
        }
        #endregion
    }
}
