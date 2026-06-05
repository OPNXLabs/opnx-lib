using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OPNX.Lib.Common.Network
{
    public static class NetworkingAddress
    {
        public static bool IsLocalIPAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress) ||
                !IPAddress.TryParse(ipAddress, out var ip) ||
                ip.AddressFamily != AddressFamily.InterNetwork)
                return false;

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.Equals(ip))
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public static string GetLocalIPAddress()
        {
            var list = GetLocalIPAddressList();
            return list.Count > 0 ? list[0] : string.Empty;
        }

        public static List<string> GetLocalIPAddressList()
        {
            var result = new List<string>();

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    // 필요하면 Loopback/터널 제외
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            result.Add(ua.Address.ToString());
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }
    }
}



