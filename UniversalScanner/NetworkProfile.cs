using System.Collections.Generic;

namespace UniversalScanner
{
    public class NetworkProfile
    {
        public string ProfileName { get; set; }
        public string InterfaceAlias { get; set; }
        public bool DhcpEnabled { get; set; }
        public List<NetworkProfileAddress> Addresses { get; set; }
        public string Gateway { get; set; }
        public List<string> DnsServers { get; set; }
        public int? InterfaceMetric { get; set; }

        public NetworkProfile()
        {
            Addresses = new List<NetworkProfileAddress>();
            DnsServers = new List<string>();
        }
    }
}
