using OPNX.Lib.Streaming.RTSP.Messages;

namespace OPNX.Lib.Streaming.RTSP
{
    public static class RTSPMessageAuthExtension
    {
        /// <summary>
        /// An helper method to add the Authorization header if required.
        /// </summary>
        /// <param name="message">Message to add to.</param>
        /// <param name="authentication">Authentication value</param>
        /// <param name="uri">Uri to connect to</param>
        /// <param name="commandCounter">A counter for authorization info.</param>
        public static void AddAuthorization(this RtspMessage message, Authentication authentication, Uri uri, uint commandCounter)
        {
            if (authentication is null)
            {
                return;
            }


            string method = string.Empty;
            if (message is RtspRequest rtspRequest)
                method = rtspRequest.RequestTyped.ToString();

            //string authorization = authentication.GetResponse(commandCounter, uri.AbsoluteUri, message.Method, []);
            //string authorization = authentication.GetResponse(commandCounter, uri.AbsoluteUri, message.Method, Array.Empty<byte>());
            string authorization = authentication.GetResponse(commandCounter, uri.AbsoluteUri, method, Array.Empty<byte>());
            // remove if already one...
            message.Headers.Remove(RtspHeaderNames.Authorization);
            message.Headers.Add(RtspHeaderNames.Authorization, authorization);
        }
    }
}
