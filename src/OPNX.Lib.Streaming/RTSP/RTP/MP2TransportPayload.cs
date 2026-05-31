using System.Buffers;

namespace OPNX.Lib.Streaming.RTSP.RTP
{
    public class MP2TransportPayload(MemoryPool<byte>? memoryPool = null) : RawPayload(memoryPool)
    {

    }
}
