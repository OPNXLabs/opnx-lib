// Parse 'fmtp' attribute in SDP
// Extract H265 fields
// By Roger Hardiman, RJH Technical Consultancy Ltd

namespace OPNX.Lib.Streaming.RTSP.Sdp
{
    public class H265Parameters : ParametersBase, IDictionary<string, string>
    {
        public IList<byte[]> SpropParameterSets
        {
            get
            {
                List<byte[]> result = new();

                if (ContainsKey("sprop-vps") && this["sprop-vps"] != null)
                {
                    result.AddRange(this["sprop-vps"].Split(',').Select(x => Convert.FromBase64String(x)));
                }

                if (ContainsKey("sprop-sps") && this["sprop-sps"] != null)
                {
                    result.AddRange(this["sprop-sps"].Split(',').Select(x => Convert.FromBase64String(x)));
                }

                if (ContainsKey("sprop-pps") && this["sprop-pps"] != null)
                {
                    result.AddRange(this["sprop-pps"].Split(',').Select(x => Convert.FromBase64String(x)));
                }

                return result;
            }
        }

        public IList<byte[]> VideoParameterSet => ParameterListFromBase64String("sprop-vps");
        public IList<byte[]> SequenceParameterSet => ParameterListFromBase64String("sprop-sps");
        public IList<byte[]> PictureParameterSet => ParameterListFromBase64String("sprop-pps");
        public IList<byte[]> SEIMessages => ParameterListFromBase64String("sprop-sei");


        public static H265Parameters Parse(string parameterString) => Parse<H265Parameters>(parameterString);
    }
}
