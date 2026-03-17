using System.Buffers;
using System.Runtime.CompilerServices;

namespace OPNX.Lib.Network.Protocol.Framing
{
    public static class PacketFramer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadFrame(
            ref ReadOnlySequence<byte> buffer,
            out PacketHeader header,
            out ReadOnlySequence<byte> payload)
        {
            header = default;
            payload = default;

            if (buffer.Length < PacketHeader.Size)
                return false;

            Span<byte> headerBytes = stackalloc byte[PacketHeader.Size];
            buffer.Slice(0, PacketHeader.Size).CopyTo(headerBytes);

            if (!headerBytes.TryReadPacketHeader(out header))
                return false;

            if (!PacketHeaderExtensions.TryGetFrameSize(header.PayloadLength, out int frameSize))
                return false;

            if (buffer.Length < frameSize)
                return false;


            payload = buffer.Slice(PacketHeader.Size, header.PayloadLength);

            buffer = buffer.Slice(frameSize);
            return true;
        }
    }
}
