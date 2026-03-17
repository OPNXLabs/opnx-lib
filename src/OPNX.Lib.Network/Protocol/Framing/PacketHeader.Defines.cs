namespace OPNX.Lib.Network.Protocol.Framing
{
    public enum PacketType : byte
    {
        None = 0x00,

        /// <summary>서버로 요청</summary>
        Request = 0x01,

        /// <summary>서버에서 응답</summary>
        Response = 0x02,

        /// <summary>알림 / 이벤트</summary>
        Notice = 0x03,
    }

    [Flags]
    public enum PacketFlags : byte
    {
        None = 0,

        Compressed = 1 << 0,   // 0x01        
        Encrypted = 1 << 1,   // 0x02 (future)
    }
}
