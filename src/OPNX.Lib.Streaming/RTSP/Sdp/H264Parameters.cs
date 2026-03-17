namespace OPNX.Lib.Streaming.RTSP.Sdp
{
    public class H264Parameters : ParametersBase, IDictionary<string, string>
    {
        private const string HeaderName = "sprop-parameter-sets";

        public IList<byte[]> SpropParameterSets =>
            TryGetValue(HeaderName, out var value)
                ? value.Split(',').Select(Convert.FromBase64String).ToList()
                : Array.Empty<byte[]>();

        public static H264Parameters Parse(string parameterString) => Parse<H264Parameters>(parameterString);
    }
}
