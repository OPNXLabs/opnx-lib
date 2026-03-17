using System.Buffers;

namespace OPNX.Lib.Streaming.RTSP.RTP
{
    // This class handles the G711 Payload
    // It has methods to process the RTP Payload

    public class G711Payload(MemoryPool<byte> memoryPool = null) : RawPayload(memoryPool)
    {

    }
}
