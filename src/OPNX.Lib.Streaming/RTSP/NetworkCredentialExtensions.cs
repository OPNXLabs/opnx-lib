using System.Net;

namespace OPNX.Lib.Streaming.RTSP
{
    static class NetworkCredentialExtensions
    {
        //extension(NetworkCredential networkCredential)
        //{
        //    public bool IsEmpty()
        //    {
        //        return string.IsNullOrEmpty(networkCredential.UserName) || networkCredential.Password == null;
        //    }
        //}

        public static bool IsEmpty(this NetworkCredential networkCredential)
        {
            return string.IsNullOrEmpty(networkCredential.UserName) || networkCredential.Password == null;
        }
    }
}
