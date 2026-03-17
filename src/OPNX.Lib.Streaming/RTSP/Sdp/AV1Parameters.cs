namespace OPNX.Lib.Streaming.RTSP.Sdp
{
    public class AV1Parameters : ParametersBase, IDictionary<string, string>
    {
        public static AV1Parameters Parse(string parameterString) => Parse<AV1Parameters>(parameterString);
    }
}
