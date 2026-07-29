using OPNX.Lib.Common.Compression;
using System.Buffers;

namespace OPNX.Lib.Network.Protocol.Framing
{
    internal static class PacketProcessor
    {
        private static readonly ZstdCompressionProvider _zstd = new();

        public static Packet Process(PacketHeader header, ReadOnlySequence<byte> payload)
        {
            if (header.IsCompressed)
                return ProcessCompressedPacket(header, payload);

            return ProcessUncompressedPacket(header, payload);
        }

        private static Packet ProcessCompressedPacket(PacketHeader header, ReadOnlySequence<byte> payload)
        {
            var (owner, decompressedSize) = _zstd.Decompress(payload);

            try
            {
                var decompressedHeader = new PacketHeader(header.Flags & ~PacketFlags.Compressed, header.PacketType, header.PayloadType, checked((uint)decompressedSize), header.Version, header.Reserved);
                var packet = new Packet(decompressedHeader, owner, decompressedSize);
                owner = null;
                return packet;
            }
            finally
            {
                owner?.Dispose();
            }
        }

        private static Packet ProcessUncompressedPacket(PacketHeader header, ReadOnlySequence<byte> payload)
        {
            if (payload.IsSingleSegment)
                return new Packet(header, payload.First);

            int payloadSize = checked((int)payload.Length);
            var owner = MemoryPool<byte>.Shared.Rent(payloadSize);

            try
            {
                payload.CopyTo(owner.Memory.Span);
                var packet = new Packet(header, owner, payloadSize);
                owner = null;
                return packet;
            }
            finally
            {
                owner?.Dispose();
            }
        }
    }
}
