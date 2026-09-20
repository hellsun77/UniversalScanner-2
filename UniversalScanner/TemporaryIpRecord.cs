using System;

namespace UniversalScanner
{
    public class TemporaryIpRecord
    {
        public string AdapterName { get; set; }
        public string AdapterId { get; set; }
        public string TemporaryIp { get; set; }
        public string SubnetMask { get; set; }
        public int PrefixLength { get; set; }
        public string CameraIp { get; set; }
        public DateTime CreatedTime { get; set; }
        public string Status { get; set; }
        public bool CreatedByUniversalScanner { get; set; }
        public bool RouteAdded { get; set; }
        public bool WasDhcpBefore { get; set; }
        public string OriginalDhcpIp { get; set; }
        public string OriginalDhcpMask { get; set; }
        public string OriginalDhcpGateway { get; set; }
        public string OriginalDhcpDns { get; set; }
    }
}
