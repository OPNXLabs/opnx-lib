namespace OPNX.Lib.Streaming.RTSP.Messages
{
    public class RtspRequestSetup : RtspRequest
    {
        public RtspRequestSetup()
        {
            Command = "SETUP * RTSP/1.0";
        }

        /// <summary>
        /// Gets the transports associate with the request.
        /// </summary>
        /// <value>The transport.</value>
        //public RtspTransport[] GetTransports()
        //{
        //    if (!Headers.TryGetValue(RtspHeaderNames.Transport, out string transportString) || transportString is null)
        //    {
        //        return new RtspTransport[] { new RtspTransport() };
        //        //return [new()];
        //    }

        //    return transportString.Split(',').Select(RtspTransport.Parse).ToArray();
        //}

        public RtspTransport[] GetTransports()
        {
            if (!Headers.TryGetValue(RtspHeaderNames.Transport, out var transportString) || string.IsNullOrEmpty(transportString))
            {
                return new[] { new RtspTransport() };
            }

            return transportString.Split(',').Select(RtspTransport.Parse).ToArray();
        }

        public void AddTransport(RtspTransport newTransport)
        {
            string actualTransport = string.Empty;
            if (Headers.TryGetValue(RtspHeaderNames.Transport, out string? value))
            {
                actualTransport = value + ",";
            }
            Headers[RtspHeaderNames.Transport] = actualTransport + newTransport;
        }
    }
}
