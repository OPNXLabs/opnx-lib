namespace OPNX.Lib.Network.Abstractions
{
    public enum DisconnectReason
    {
        Requested,   // 의도적 끊기(전역 EnableReconnect가 true면 재접속 허용)
        Broken,      // 네트워크 단절(재접속 허용)
        Error,       // 오류(재접속 허용/금지는 정책에 따라)
        Stopped      // 사용자/시스템이 "재접속 금지"로 중단
    }
}
