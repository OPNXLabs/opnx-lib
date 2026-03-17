using OPNX.Lib.Streaming.RTSP.Sys;

namespace OPNX.Lib.Streaming.RTSP.RTP
{
    public record class RTPHeaderExtension(int Id, string Uri)
    {
        public RTPHeaderExtensionUri.Type? Type => RTPHeaderExtensionUri.GetType(Uri);
    }

    public class RTPHeaderExtensionUri
    {
        public enum Type
        {
            Unknown,
            AbsCaptureTime
        }

        private static Dictionary<string, Type> Types { get; } = new Dictionary<string, Type>() { { "http://www.webrtc.org/experiments/rtp-hdrext/abs-capture-time", Type.AbsCaptureTime } };

        public static Type? GetType(string uri)
        {
            return Types.TryGetValue(uri, out var type)
                ? type
                : Type.Unknown;
        }
    }

    public enum RTPHeaderExtensionType
    {
        OneByte,
        TwoByte
    }

    public record class RTPHeaderExtensionData(int Id, byte[] Data, RTPHeaderExtensionType Type)
    {
        public RTPHeaderExtensionUri.Type? GetUriType(Dictionary<int, RTPHeaderExtension> map)
        {
            return map.TryGetValue(Id, out var ext)
              ? ext.Type
              : null;
        }


        public ulong? GetNtpTimestamp(Dictionary<int, RTPHeaderExtension> extensions)
        {
            var extensionType = GetUriType(extensions);
            if (extensionType != RTPHeaderExtensionUri.Type.AbsCaptureTime)
            {
                return null;
            }

            return GetUlong(0);
        }

        public ulong? GetUlong(int offset)
        {
            if (offset + sizeof(ulong) - 1 >= Data.Length)
            {
                return null;
            }

            return BitConverter.IsLittleEndian ?
                NetConvert.DoReverseEndian(BitConverter.ToUInt64(Data, offset)) :
                BitConverter.ToUInt64(Data, offset);
        }
    }
}
