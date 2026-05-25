using OPNX.Lib.Streaming.WebRTC.Events;

namespace OPNX.Lib.Streaming.WebRTC.Abstractions
{
    public interface IWebRtcSignalServer : IDisposable
    {
        int Port { get; }

        event EventHandler<WebRtcClientOpenedEventArgs>? ClientOpened;
        event EventHandler<WebRtcClientClosedEventArgs>? ClientClosed;

        void SendRtpVideoData(int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit);
        void SendRtpAudioData(int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit);
        void SendRtpVideoData(Guid connectionID, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit);
        void SendRtpAudioData(Guid connectionID, int payloadType, ReadOnlySpan<byte> payload, uint timeStamp, int markerBit);
        void CloseConnection(Guid connectionID);
    }
}


