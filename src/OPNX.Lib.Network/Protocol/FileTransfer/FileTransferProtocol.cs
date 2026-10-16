using OPNX.Lib.Network.Protocol.Framing;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace OPNX.Lib.Network.Protocol.FileTransfer
{
    internal enum FileTransferMessageType : byte
    {
        None = 0x00,
        Prepare = 0x40,
        Start = 0x41,
        Continue = 0x42,
        Cancel = 0x43,
        FileBegin = 0x44,
        FileChunk = 0x45,
        FileEnd = 0x46,
        TransferEnd = 0x47
    }

    internal static class FileTransferProtocol
    {
        public const int ChunkHeaderSize = 44;

        public static bool IsFileTransferMessage(byte payloadType)
            => payloadType is >= (byte)FileTransferMessageType.Prepare and <= (byte)FileTransferMessageType.TransferEnd;

        public static PacketHeader CreateHeader(PacketType packetType, FileTransferMessageType messageType)
            => new(PacketFlags.None, packetType, (byte)messageType);

        public static (IMemoryOwner<byte> Owner, int Length) Write(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                write(writer);
            int length = checked((int)stream.Length);
            IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(Math.Max(1, length));
            stream.GetBuffer().AsSpan(0, length).CopyTo(owner.Memory.Span);
            return (owner, length);
        }

        public static BinaryReader CreateReader(ReadOnlyMemory<byte> payload)
            => new(new MemoryStream(payload.ToArray(), writable: false), Encoding.UTF8, leaveOpen: false);

        public static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        public static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || (uint)length > PacketHeaderExtensions.MaxPayloadLength)
                throw new InvalidDataException("Invalid string length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        public static (IMemoryOwner<byte> Owner, int Length) WriteChunk(Guid transferID, Guid fileID, long offset, ReadOnlySpan<byte> data)
        {
            int length = checked(ChunkHeaderSize + data.Length);
            IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(length);
            Span<byte> span = owner.Memory.Span[..length];
            transferID.TryWriteBytes(span[..16]);
            fileID.TryWriteBytes(span.Slice(16, 16));
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(32, 8), offset);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(40, 4), data.Length);
            data.CopyTo(span[ChunkHeaderSize..]);
            return (owner, length);
        }

        public static bool TryReadChunk(ReadOnlySpan<byte> payload, out Guid transferID, out Guid fileID, out long offset, out ReadOnlySpan<byte> data)
        {
            transferID = Guid.Empty;
            fileID = Guid.Empty;
            offset = 0;
            data = default;
            if (payload.Length < ChunkHeaderSize)
                return false;
            transferID = new Guid(payload[..16]);
            fileID = new Guid(payload.Slice(16, 16));
            offset = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(32, 8));
            int dataLength = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(40, 4));
            if (dataLength < 0 || payload.Length != ChunkHeaderSize + dataLength)
                return false;
            data = payload[ChunkHeaderSize..];
            return true;
        }
    }
}
