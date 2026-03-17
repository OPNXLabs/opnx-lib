using OPNX.Lib.Common.Logging;
using System.Net;
using System.Net.NetworkInformation;

namespace OPNX.Lib.Streaming.RTSP
{
    public class UdpPortsWatcherSingleton
    {
        private readonly HashSet<int> portsHashSet = [];
        private readonly Lock portsSetLock = new();
        //private readonly ILogger _logger = Log.ForContext<UdpPortsWatcherSingleton>();// LogManager.GetLogger("UdpPortsWatcherSingleton");

        static UdpPortsWatcherSingleton()
        {
            Instance = new UdpPortsWatcherSingleton();
        }

        public static UdpPortsWatcherSingleton Instance { get; private set; }


        public bool TryReservePort(int startPort, int endPort, out int dataPortNumber, out int controlPortNumber)
        {
            using (portsSetLock.EnterScope())
            {
                if (GetAvailableUdpPorts(startPort, endPort, out dataPortNumber, out controlPortNumber))
                {
                    portsHashSet.Add(dataPortNumber);
                    LogManager.Verbose($"UdpClient ports {dataPortNumber}-{controlPortNumber} reserved");

                    return true;
                }

                return false;

            }
        }

        public bool TryReleasePort(int dataPortNumber, int controlPortNumber)
        {
            using (portsSetLock.EnterScope())
            {
                bool portRemoved = portsHashSet.Remove(dataPortNumber);
                LogManager.Verbose($"UdpClient ports {dataPortNumber}-{controlPortNumber} released");
                return portRemoved;
            }

        }

        public bool GetAvailableUdpPorts(int startPort, int endPort, out int dataPortNumber, out int controlPortNumber)
        {
            LogManager.Verbose($"UdpClient GetAvailableUdpPorts for ports {startPort}-{endPort}");

            IPEndPoint[] endPoints;
            List<int> portArray = [];

            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();

            //getting active connections
            TcpConnectionInformation[] connections = properties.GetActiveTcpConnections();
            portArray.AddRange(from n in connections
                               select n.LocalEndPoint.Port);

            //getting active tcp listners - WCF service listening in tcp
            endPoints = properties.GetActiveTcpListeners();
            portArray.AddRange(from n in endPoints
                               select n.Port);

            //getting active udp listeners
            endPoints = properties.GetActiveUdpListeners();
            portArray.AddRange(from n in endPoints
                               select n.Port);

            portArray.Sort();

            dataPortNumber = -1;
            controlPortNumber = -1;
            for (int i = startPort; i < endPort; i++)
            {
                if (!portArray.Contains(i) && !portArray.Contains(i + 1) && !portsHashSet.Contains(i))
                {
                    dataPortNumber = i;
                    controlPortNumber = i + 1;
                    LogManager.Verbose($"UdpClient GetAvailableUdpPorts for ports {startPort}-{endPort} finished : {dataPortNumber}-{controlPortNumber} portArray.Count={portArray.Count}, portsHashSet.Count={portsHashSet.Count}");
                    return true;
                }
            }

            LogManager.Verbose($"UdpClient GetAvailableUdpPorts for ports {startPort}-{endPort} finished : {dataPortNumber}-{controlPortNumber} portArray.Count={portArray.Count}, portsHashSet.Count={portsHashSet.Count}");
            return false;

        }
    }
}
