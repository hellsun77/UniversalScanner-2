using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using JulienBlitte;

namespace UniversalScanner
{
    class Axis : ScanEngine
    {
        private mDNS dnsBroker;
        private readonly object directProbeLock = new object();
        private bool directProbeActive;
        private static readonly string[] domains =
        {
            "_vapix-http._tcp.local",
            "_vapix-https._tcp.local",
            "_axis-video._tcp.local",
            "_axis-nvr._tcp.local",
            "_axis-bwsc._tcp.local"
        };

        public override int color
        {
            get
            {
                return 0x806000;
            }
        }
        public override UInt16[] getUsedPort()
        {
            return dnsBroker.getUsedPort();
        }
        public override string name
        {
            get
            {
                return "Axis";
            }
        }

        public Axis()
        {
            dnsBroker = mDNS.getInstance();
        }

        public override void listen()
        {
            foreach (var d in domains)
            {
                dnsBroker.registerDomain(d, axisDeviceFound);
            }
        }

        public override void reciever(IPEndPoint from, byte[] data)
        {
            throw new NotImplementedException();
        }

        public override void scan()
        {
#if DEBUG
            dnsBroker.selfTest("Axis.selftest");
#endif
            foreach (var d in domains)
            {
                dnsBroker.scan(d, mDNSType.TYPE_PTR);
            }

            Task.Run((Action)DirectAxisMdnsProbe);
        }

        public override byte[] sender(IPEndPoint dest)
        {
            return (new byte[0]);
        }

        public void axisDeviceFound(string domainFilter, mDNSAnswer[] answers)
        {
            List<IPAddress> addresses;
            string deviceModel, serial;

            addresses = new List<IPAddress>();
            deviceModel = null;
            serial = null;
            foreach (var a in answers)
            {
                switch (a.Type)
                {
                    case mDNSType.TYPE_A:
                        addresses.Add(a.data.typeA);
                        break;
                    case mDNSType.TYPE_AAAA:
                        addresses.Add(a.data.typeAAAA);
                        break;
                    case mDNSType.TYPE_ANY:
                        break;
                    case mDNSType.TYPE_PTR:
                        if (deviceModel == null)
                        {
                            int splitter;

                            deviceModel = a.data.typePTR;
                            splitter = deviceModel.IndexOf('.');
                            if (splitter >= 0)
                            {
                                deviceModel = deviceModel.Substring(0, splitter);
                            }
                        }
                        break;
                    case mDNSType.TYPE_SRV:
                        break;
                    case mDNSType.TYPE_TXT:
                        if (serial == null && a.data.typeTXT != null && a.data.typeTXT.Length > 0)
                        {
                            int splitter;

                            serial = a.data.typeTXT[0];
                            splitter = serial.IndexOf('=');
                            if (splitter >= 0)
                            {
                                serial = serial.Substring(splitter + 1);
                            }
                        }
                        break;
                }
            }

            if (addresses.Count > 0)
            {
                if (deviceModel == null) deviceModel = "unknown";

                int modelMacSplitter = deviceModel.IndexOf(" - ");
                if (modelMacSplitter >= 0)
                {
                    if (serial == null)
                    {
                        serial = deviceModel.Substring(modelMacSplitter + 3).Trim();
                    }
                    deviceModel = deviceModel.Substring(0, modelMacSplitter).Trim();
                }

                if (serial == null) serial = "unknown";

                deviceFound(name, 1, addresses, deviceModel, serial);
            }
        }

        private void deviceFound(string protocol, int version, List<IPAddress> addresses, string deviceModel, string serial)
        {
            bool hasNonAutoIPv4, hasNonAutoIPv6;

            if (viewer == null)
                return;

            hasNonAutoIPv4 = false;
            hasNonAutoIPv6 = false;
            foreach (var ip in addresses)
            {
                if (!ip.isAutoConf())
                {
                    viewer.deviceFound(protocol, version, ip, deviceModel, serial);
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        hasNonAutoIPv4 = true;
                    }
                    if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        hasNonAutoIPv6 = true;
                    }
                }
            }

            // handle auto-conf addresses
            foreach (var ip in addresses)
            {
                if (ip.isAutoConf())
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (!hasNonAutoIPv4 || Config.getInstance().ForceZeroConf)
                        {
                            viewer.deviceFound(protocol, version, ip, deviceModel, serial);
                        }
                    }
                    if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        if (!hasNonAutoIPv6 || Config.getInstance().ForceLinkLocal)
                        {
                            viewer.deviceFound(protocol, version, ip, deviceModel, serial);
                        }
                    }
                }
            }
        }

        private void DirectAxisMdnsProbe()
        {
            lock (directProbeLock)
            {
                if (directProbeActive)
                {
                    return;
                }

                directProbeActive = true;
            }

            try
            {
                RunAxisMdnsProbeIPv6();

                var localAddresses = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address)
                    .Distinct()
                    .ToList();

                if (localAddresses.Count == 0 || viewer == null)
                {
                    return;
                }

                using (var udp = new UdpClient())
                {
                    udp.EnableBroadcast = true;
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
                    udp.Client.ReceiveTimeout = 500;

                    foreach (var localAddress in localAddresses)
                    {
                        try
                        {
                            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(IPAddress.Parse("224.0.0.251"), localAddress));
                        }
                        catch { }
                    }

                    var deadline = DateTime.UtcNow.AddSeconds(90);
                    var nextSend = DateTime.MinValue;
                    var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    while (DateTime.UtcNow < deadline)
                    {
                        if (DateTime.UtcNow >= nextSend)
                        {
                            foreach (var domain in domains)
                            {
                                byte[] query = BuildMdnsPtrQuery(domain);
                                foreach (var localAddress in localAddresses)
                                {
                                    try
                                    {
                                        int multicastInterface = BitConverter.ToInt32(localAddress.GetAddressBytes(), 0);
                                        udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, multicastInterface);
                                        udp.Send(query, query.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Warning: Axis direct mDNS send failed from " + localAddress + ": " + ex.Message);
                                    }
                                }
                            }

                            nextSend = DateTime.UtcNow.AddSeconds(3);
                        }

                        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        byte[] buffer;
                        try
                        {
                            buffer = udp.Receive(ref remoteEndPoint);
                        }
                        catch (SocketException ex)
                        {
                            if (ex.SocketErrorCode != SocketError.TimedOut && ex.SocketErrorCode != SocketError.WouldBlock)
                            {
                                Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Warning: Axis direct mDNS receive failed: " + ex.Message);
                            }

                            continue;
                        }

                        AxisMdnsDevice device;
                        if (TryParseAxisMdnsResponse(buffer, remoteEndPoint.Address, out device))
                        {
                            string key = (device.Serial ?? device.Ip.ToString()) + "|" + device.Ip;
                            if (reported.Add(key))
                            {
                                viewer.deviceFound(name, 1, device.Ip, device.Model ?? "unknown", device.Serial ?? "unknown");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Warning: Axis direct mDNS probe failed: " + ex.Message);
            }
            finally
            {
                lock (directProbeLock)
                {
                    directProbeActive = false;
                }
            }
        }

        private void RunAxisMdnsProbeIPv6()
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(n => new
                {
                    Network = n,
                    Properties = n.GetIPProperties()
                })
                .Where(n => n.Properties.UnicastAddresses.Any(a =>
                    a.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                    a.Address.IsIPv6LinkLocal))
                .Select(n => new
                {
                    n.Network.Name,
                    Index = n.Properties.GetIPv6Properties() != null ? n.Properties.GetIPv6Properties().Index : 0
                })
                .Where(n => n.Index > 0)
                .Distinct()
                .ToList();

            if (interfaces.Count == 0 || viewer == null)
            {
                return;
            }

            using (var udp = new UdpClient(AddressFamily.InterNetworkV6))
            {
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, 5353));
                udp.Client.ReceiveTimeout = 500;

                IPAddress multicastAddress = IPAddress.Parse("ff02::fb");
                foreach (var net in interfaces)
                {
                    try
                    {
                        udp.JoinMulticastGroup(net.Index, multicastAddress);
                    }
                    catch (Exception ex)
                    {
                        Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Warning: Axis IPv6 mDNS join failed on " + net.Name + ": " + ex.Message);
                    }
                }

                var deadline = DateTime.UtcNow.AddSeconds(45);
                var nextSend = DateTime.MinValue;
                var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (DateTime.UtcNow < deadline)
                {
                    if (DateTime.UtcNow >= nextSend)
                    {
                        foreach (var net in interfaces)
                        {
                            foreach (var domain in domains)
                            {
                                byte[] query = BuildMdnsPtrQuery(domain);
                                try
                                {
                                    IPAddress scopedMulticast = IPAddress.Parse("ff02::fb");
                                    scopedMulticast.ScopeId = net.Index;
                                    udp.Send(query, query.Length, new IPEndPoint(scopedMulticast, 5353));
                                }
                                catch (Exception ex)
                                {
                                    Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Warning: Axis IPv6 mDNS send failed on " + net.Name + ": " + ex.Message);
                                }
                            }
                        }

                        nextSend = DateTime.UtcNow.AddSeconds(3);
                    }

                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
                    byte[] buffer;
                    try
                    {
                        buffer = udp.Receive(ref remoteEndPoint);
                    }
                    catch (SocketException ex)
                    {
                        if (ex.SocketErrorCode != SocketError.TimedOut && ex.SocketErrorCode != SocketError.WouldBlock)
                        {
                            Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Warning: Axis IPv6 mDNS receive failed: " + ex.Message);
                        }

                        continue;
                    }

                    AxisMdnsDevice device;
                    if (TryParseAxisMdnsResponse(buffer, remoteEndPoint.Address, out device))
                    {
                        string key = (device.Serial ?? device.Ip.ToString()) + "|" + device.Ip;
                        if (reported.Add(key))
                        {
                            AxisLinkLocalRegistry.Register(device.Ip, remoteEndPoint.Address);
                            viewer.deviceFound(name, 1, device.Ip, device.Model ?? "unknown", device.Serial ?? "unknown");
                        }
                    }
                }
            }
        }

        private static byte[] BuildMdnsPtrQuery(string queryName)
        {
            var data = new List<byte>();
            data.AddRange(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
            foreach (var label in queryName.Split('.'))
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(label);
                data.Add((byte)bytes.Length);
                data.AddRange(bytes);
            }
            data.Add(0);
            data.AddRange(new byte[] { 0, 12, 0, 1 });
            return data.ToArray();
        }

        private static bool TryParseAxisMdnsResponse(byte[] data, IPAddress remoteAddress, out AxisMdnsDevice device)
        {
            device = null;
            if (data == null || data.Length < 12)
            {
                return false;
            }

            int questions = ReadUInt16(data, 4);
            int records = ReadUInt16(data, 6) + ReadUInt16(data, 8) + ReadUInt16(data, 10);
            int position = 12;
            for (int i = 0; i < questions; i++)
            {
                if (!SkipDnsName(data, ref position) || position + 4 > data.Length)
                {
                    return false;
                }
                position += 4;
            }

            bool axisSeen = false;
            string model = null;
            string serial = null;
            IPAddress ip = null;

            for (int i = 0; i < records && position < data.Length; i++)
            {
                string recordName;
                if (!TryReadDnsName(data, ref position, out recordName) || position + 10 > data.Length)
                {
                    break;
                }

                int type = ReadUInt16(data, position);
                position += 2;
                position += 2; // class
                position += 4; // ttl
                int dataLength = ReadUInt16(data, position);
                position += 2;

                if (position + dataLength > data.Length)
                {
                    break;
                }

                int dataPosition = position;
                if (IsAxisMdnsName(recordName))
                {
                    axisSeen = true;
                }

                if (type == 1 && dataLength == 4)
                {
                    var foundIp = new IPAddress(new byte[] { data[position], data[position + 1], data[position + 2], data[position + 3] });
                    if (axisSeen || IsAxisMdnsName(recordName))
                    {
                        ip = foundIp;
                    }
                }
                else if (type == 12)
                {
                    string ptrName;
                    int ptrPos = dataPosition;
                    if (TryReadDnsName(data, ref ptrPos, out ptrName) && IsAxisMdnsName(ptrName))
                    {
                        axisSeen = true;
                        ExtractAxisModelAndSerial(ptrName, ref model, ref serial);
                    }
                }
                else if (type == 16)
                {
                    ExtractAxisTxt(data, dataPosition, dataLength, ref model, ref serial);
                }
                else if (type == 33)
                {
                    string target;
                    int targetPos = dataPosition + 6;
                    if (targetPos < data.Length && TryReadDnsName(data, ref targetPos, out target) && IsAxisMdnsName(target))
                    {
                        axisSeen = true;
                    }
                }

                position += dataLength;
            }

            if (!axisSeen)
            {
                return false;
            }

            device = new AxisMdnsDevice
            {
                Ip = ip ?? remoteAddress,
                Model = model,
                Serial = serial
            };
            return device.Ip != null && device.Ip.AddressFamily == AddressFamily.InterNetwork;
        }

        private static bool IsAxisMdnsName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.IndexOf("_vapix-", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("_axis-", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("AXIS ", StringComparison.OrdinalIgnoreCase) >= 0
                || value.StartsWith("axis-", StringComparison.OrdinalIgnoreCase);
        }

        private static void ExtractAxisModelAndSerial(string ptrName, ref string model, ref string serial)
        {
            string instance = ptrName.Split('.')[0].Trim();
            int splitter = instance.IndexOf(" - ", StringComparison.Ordinal);
            if (splitter >= 0)
            {
                if (string.IsNullOrWhiteSpace(model))
                {
                    model = instance.Substring(0, splitter).Trim();
                }
                if (string.IsNullOrWhiteSpace(serial))
                {
                    serial = instance.Substring(splitter + 3).Trim();
                }
            }
            else if (string.IsNullOrWhiteSpace(model) && instance.StartsWith("AXIS ", StringComparison.OrdinalIgnoreCase))
            {
                model = instance;
            }
        }

        private static void ExtractAxisTxt(byte[] data, int position, int dataLength, ref string model, ref string serial)
        {
            int end = Math.Min(data.Length, position + dataLength);
            while (position < end)
            {
                int len = data[position++];
                if (len <= 0 || position + len > end)
                {
                    break;
                }

                string value = System.Text.Encoding.ASCII.GetString(data, position, len);
                position += len;
                int splitter = value.IndexOf('=');
                if (splitter <= 0)
                {
                    continue;
                }

                string key = value.Substring(0, splitter).Trim();
                string text = value.Substring(splitter + 1).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (string.Equals(key, "macaddress", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "serialnumber", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "serial", StringComparison.OrdinalIgnoreCase))
                {
                    serial = text;
                }
                else if (string.Equals(key, "model", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, "ty", StringComparison.OrdinalIgnoreCase))
                {
                    model = text;
                }
            }
        }

        private static int ReadUInt16(byte[] data, int position)
        {
            return (data[position] << 8) | data[position + 1];
        }

        private static bool SkipDnsName(byte[] data, ref int position)
        {
            string ignored;
            return TryReadDnsName(data, ref position, out ignored);
        }

        private static bool TryReadDnsName(byte[] data, ref int position, out string name, int depth = 0)
        {
            name = null;
            if (depth > 16)
            {
                return false;
            }

            var labels = new List<string>();
            int current = position;
            bool jumped = false;

            while (current < data.Length)
            {
                int len = data[current++];
                if (len == 0)
                {
                    if (!jumped)
                    {
                        position = current;
                    }
                    name = string.Join(".", labels);
                    return true;
                }

                if ((len & 0xC0) == 0xC0)
                {
                    if (current >= data.Length)
                    {
                        return false;
                    }

                    int pointer = ((len & 0x3F) << 8) | data[current++];
                    if (!jumped)
                    {
                        position = current;
                    }
                    current = pointer;
                    jumped = true;
                    depth++;
                    if (depth > 16)
                    {
                        return false;
                    }
                    continue;
                }

                if (current + len > data.Length)
                {
                    return false;
                }

                labels.Add(System.Text.Encoding.UTF8.GetString(data, current, len));
                current += len;
            }

            return false;
        }

        private class AxisMdnsDevice
        {
            public IPAddress Ip { get; set; }
            public string Model { get; set; }
            public string Serial { get; set; }
        }
    }

    public static class AxisLinkLocalRegistry
    {
        private static readonly object Locker = new object();
        private static readonly Dictionary<string, IPAddress> Routes = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);

        public static void Register(IPAddress ipv4Address, IPAddress ipv6LinkLocalAddress)
        {
            if (ipv4Address == null ||
                ipv4Address.AddressFamily != AddressFamily.InterNetwork ||
                ipv6LinkLocalAddress == null ||
                ipv6LinkLocalAddress.AddressFamily != AddressFamily.InterNetworkV6 ||
                !ipv6LinkLocalAddress.IsIPv6LinkLocal ||
                ipv6LinkLocalAddress.ScopeId <= 0)
            {
                return;
            }

            lock (Locker)
            {
                Routes[ipv4Address.ToString()] = ipv6LinkLocalAddress;
            }
        }

        public static void Move(string oldIpv4Address, string newIpv4Address)
        {
            if (string.IsNullOrWhiteSpace(oldIpv4Address) || string.IsNullOrWhiteSpace(newIpv4Address))
            {
                return;
            }

            IPAddress address;
            lock (Locker)
            {
                if (!Routes.TryGetValue(oldIpv4Address.Trim(), out address))
                {
                    return;
                }

                Routes[newIpv4Address.Trim()] = address;
            }
        }

        public static bool HasRoute(string ipv4Address)
        {
            return GetLinkLocalAddress(ipv4Address) != null;
        }

        public static string GetHttpHost(string ipv4Address)
        {
            if (string.IsNullOrWhiteSpace(ipv4Address))
            {
                return null;
            }

            IPAddress address;
            lock (Locker)
            {
                if (!Routes.TryGetValue(ipv4Address.Trim(), out address))
                {
                    return null;
                }
            }

            return "[" + address + "]";
        }

        public static string GetBrowserHost(string ipv4Address)
        {
            IPAddress address = GetLinkLocalAddress(ipv4Address);
            if (address == null)
            {
                return null;
            }

            string host = address.ToString();
            int zoneIndex = host.LastIndexOf('%');
            if (zoneIndex >= 0)
            {
                host = host.Substring(0, zoneIndex) + "%25" + host.Substring(zoneIndex + 1);
            }

            return "[" + host + "]";
        }

        public static IPAddress GetLinkLocalAddress(string ipv4Address)
        {
            if (string.IsNullOrWhiteSpace(ipv4Address))
            {
                return null;
            }

            IPAddress address;
            lock (Locker)
            {
                if (!Routes.TryGetValue(ipv4Address.Trim(), out address))
                {
                    return null;
                }
            }

            return address;
        }

        public static string GetAdapterAlias(string ipv4Address)
        {
            IPAddress address = GetLinkLocalAddress(ipv4Address);
            if (address == null || address.ScopeId <= 0)
            {
                return null;
            }

            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    IPv6InterfaceProperties ipv6 = adapter.GetIPProperties().GetIPv6Properties();
                    if (ipv6 != null && ipv6.Index == address.ScopeId)
                    {
                        return adapter.Name;
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
