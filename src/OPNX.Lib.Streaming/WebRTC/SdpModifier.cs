using System.Text.RegularExpressions;

namespace OPNX.Lib.Streaming.WebRTC
{
    public static partial class SdpModifier
    {
        // a=candidate:<foundation> <component> <transport> <priority> <ip> <port> typ <type> ...
        // 위에서 <ip>만 치환
        [GeneratedRegex(
            @"(?<=^a=candidate:\d+\s+\d+\s+\w+\s+\d+\s+)(\d{1,3}(?:\.\d{1,3}){3})",
            RegexOptions.Multiline)]
        private static partial Regex CandidateIpRegex();

        [GeneratedRegex(@"^b=AS:\d+", RegexOptions.Multiline)]
        private static partial Regex BandwidthLineRegex();

        // m=video 라인 바로 아래에 b=AS 삽입
        // 줄 끝은 SDP가 \r\n 또는 \n 둘 다 올 수 있으니 \r?\n 처리
        [GeneratedRegex(@"^(m=video.*?$)\r?\n?", RegexOptions.Multiline)]
        private static partial Regex VideoMLIneRegex();

        // b=AS:1234 에서 1234만 교체
        [GeneratedRegex(@"^(b=AS:)\d+", RegexOptions.Multiline)]
        private static partial Regex BandwidthValueRegex();

        public static string ModifySdpCandidateIP(string sdp, string newIp)
        {
            if (string.IsNullOrEmpty(sdp))
                return sdp;

            if (string.IsNullOrWhiteSpace(newIp))
                return sdp;

            return CandidateIpRegex().Replace(sdp, newIp);
        }

        public static string ModifySdpBitrate(string sdp, int newBitrateKbps)
        {
            if (string.IsNullOrEmpty(sdp))
                return sdp;

            if (newBitrateKbps <= 0)
                return sdp;

            return BandwidthLineRegex().IsMatch(sdp)
                ? UpdateBitrateInSdp(sdp, newBitrateKbps)
                : AddBitrateToSdp(sdp, newBitrateKbps);
        }

        private static string AddBitrateToSdp(string sdp, int bitrateKbps)
        {
            // m=video 라인이 있으면 그 바로 아래에 b=AS 삽입
            // 없으면 SDP 끝에 추가(최소한의 fallback)
            if (VideoMLIneRegex().IsMatch(sdp))
            {
                return VideoMLIneRegex().Replace(sdp, $"$1\r\nb=AS:{bitrateKbps}\r\n", 1);
            }

            // video m-line이 없는 SDP라도 방어적으로 추가
            return sdp.EndsWith('\n')
                ? sdp + $"b=AS:{bitrateKbps}\r\n"
                : sdp + $"\r\nb=AS:{bitrateKbps}\r\n";
        }

        private static string UpdateBitrateInSdp(string sdp, int bitrateKbps)
        {
            return BandwidthValueRegex().Replace(sdp, $"$1{bitrateKbps}");
        }
    }
}
