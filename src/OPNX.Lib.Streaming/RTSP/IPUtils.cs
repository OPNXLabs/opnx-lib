using System.Net;
using System.Net.Sockets;

namespace OPNX.Lib.Streaming.RTSP
{
    public static class IPUtils
    {

        public static IPAddress GetIPAddressFromString(string address, out Exception? exception)
        {
            IPAddress? result;
            exception = null;

            if (string.IsNullOrEmpty(address) ||
                false == System.Net.IPAddress.TryParse(address, out result))
            {
                try
                {
                    result = System.Net.Dns.GetHostEntry(address).AddressList.Last(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                }
                catch (Exception ex)
                {
                    exception = ex;

                    result = IPAddress.Any;
                }
            }

            return result;
        }

        public static string GetLocalIPAddress()
        {
            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                string ClientIP = string.Empty;
                for (int i = 0; i < host.AddressList.Length; i++)
                {
                    if (host.AddressList[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        return host.AddressList[i].ToString();
                    }
                }
            }
            catch (Exception)
            {
            }

            return string.Empty;
        }
    }
}
