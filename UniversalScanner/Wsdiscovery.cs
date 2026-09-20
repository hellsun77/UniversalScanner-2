using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace UniversalScanner
{
    internal class UniviewDiscoveryInfo
    {
        public string IPAddress { get; set; }
        public string Model { get; set; }
        public string Serial { get; set; }
        public string Mac { get; set; }
        public string Firmware { get; set; }
        public string OnvifProfiles { get; set; }
        public string LocalInterfaceIp { get; set; }
        public int LocalInterfaceIndex { get; set; }
    }

    internal static class UniviewDiscoveryCache
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, UniviewDiscoveryInfo> ByIp =
            new Dictionary<string, UniviewDiscoveryInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, UniviewDiscoveryInfo> ByMac =
            new Dictionary<string, UniviewDiscoveryInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, UniviewDiscoveryInfo> BySerial =
            new Dictionary<string, UniviewDiscoveryInfo>(StringComparer.OrdinalIgnoreCase);

        public static void Store(UniviewDiscoveryInfo info)
        {
            if (info == null)
                return;

            lock (Sync)
            {
                if (!string.IsNullOrWhiteSpace(info.IPAddress))
                    ByIp[info.IPAddress.Trim()] = info;
                if (!string.IsNullOrWhiteSpace(info.Mac))
                    ByMac[NormalizeMac(info.Mac)] = info;
                if (!string.IsNullOrWhiteSpace(info.Serial))
                    BySerial[info.Serial.Trim()] = info;
            }
        }

        public static bool TryGet(string ip, string mac, string serial, out UniviewDiscoveryInfo info)
        {
            lock (Sync)
            {
                if (!string.IsNullOrWhiteSpace(mac) && ByMac.TryGetValue(NormalizeMac(mac), out info))
                    return true;
                if (!string.IsNullOrWhiteSpace(serial) && BySerial.TryGetValue(serial.Trim(), out info))
                    return true;
                if (!string.IsNullOrWhiteSpace(ip) && ByIp.TryGetValue(ip.Trim(), out info))
                    return true;
            }

            info = null;
            return false;
        }

        private static string NormalizeMac(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
                return "";

            string clean = mac.Trim().ToUpperInvariant().Replace(":", "-");
            if (Regex.IsMatch(clean, @"^[0-9A-F]{12}$"))
                clean = Regex.Replace(clean, "(.{2})(?!$)", "$1-");

            return clean;
        }
    }

    class Wsdiscovery : ScanEngine
    {
        private const int port = 3702;
        private const int univiewPreferredSourcePort = 3705;
        private static readonly string multicastIP = "239.255.255.250";

        private readonly List<UdpClient> univiewSockets = new List<UdpClient>();
        private readonly List<Thread> univiewThreads = new List<Thread>();
        private readonly object univiewSocketLock = new object();

        private Guid message_uuidg;
        private string announce = "<s:Envelope"
            + " xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\""
            + " xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\""
            + " xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\""
            + " xmlns:w=\"http://schemas.xmlsoap.org/ws/2006/02/devprof\""
            + " xmlns:o=\"http://www.onvif.org/ver10/device/wsdl\""
            + ">"
            + "<s:Header>"
            + "<a:Action s:mustUnderstand=\"1\">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>"
            + "<a:MessageID>urn:uuid:{0}</a:MessageID>"
            + "<a:To s:mustUnderstand=\"1\">urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To>"
            + "<a:ReplyTo>"
            + "<a:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</a:Address>"
            + "</a:ReplyTo>"
            + "</s:Header>"
            + "<s:Body>"
            + "<d:Probe>"
            + "<d:Types>w:Device o:Device</d:Types>"
            + "</d:Probe>"
            + "</s:Body>"
            + "</s:Envelope>";

        private static readonly string verbatim = "<s:Envelope"
            + " xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\""
            + " xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\""
            + ">"
            + "<s:Header>"
            + "<a:Action s:mustUnderstand=\"1\">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>"
            + "<a:MessageID>uuid:f686768c-3e60-4f9c-a344-0769929d665c</a:MessageID>"
            + "<a:ReplyTo><a:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</a:Address></a:ReplyTo>"
            + "<a:To s:mustUnderstand=\"1\">urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To>"
            + "</s:Header><s:Body><Probe xmlns=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\">"
            + "<d:Types xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" xmlns:dp0=\"http://www.onvif.org/ver10/network/wsdl\">dp0:NetworkVideoTransmitter</d:Types>"
            + "</Probe>"
            + "</s:Body>"
            + "</s:Envelope>";

        public override int color
        {
            get { return 0x404040; }
        }

        public override UInt16[] getUsedPort()
        {
            return new UInt16[] { port, univiewPreferredSourcePort };
        }

        public override string name
        {
            get { return "WSDiscovery"; }
        }

        public Wsdiscovery()
        {
            if (Config.getInstance().OnvifVerbatim)
            {
                announce = verbatim;
                Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Using WSDiscovery ONVIF verbatim payload");
            }
        }

        public override void listen()
        {
            listenMulticast(IPAddress.Parse(multicastIP), port);
            listenUdpInterfaces();
            StartUniviewDiscoverySockets();
        }

        public override void reciever(IPEndPoint from, byte[] data)
        {
            HandleDiscoveryResponse(from, data, false);
        }

        public override void scan()
        {
#if DEBUG
            selfTest();
#endif
            sendMulticast(IPAddress.Parse(multicastIP), port);
            SendUniviewDiscoveryProbes();
        }

        public override byte[] sender(IPEndPoint dest)
        {
            return Encoding.ASCII.GetBytes(CreateStandardProbeXml());
        }

        private void StartUniviewDiscoverySockets()
        {
            foreach (NetworkInterface iface in listActiveInterface())
            {
                foreach (IPAddress localIp in listInterfaceAddresses(iface, AddressFamily.InterNetwork))
                {
                    UdpClient udp = CreateUniviewSocket(localIp, univiewPreferredSourcePort);
                    if (udp == null)
                        udp = CreateUniviewSocket(localIp, 0);

                    if (udp == null)
                        continue;

                    lock (univiewSocketLock)
                    {
                        univiewSockets.Add(udp);
                    }

                    IPEndPoint localEndPoint = (IPEndPoint)udp.Client.LocalEndPoint;
                    AppendDebug("Listening for Uniview discovery responses on " + localEndPoint);
                    Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "Listening for Uniview WS-Discovery responses on " + localEndPoint);

                    Thread thread = new Thread(() => UniviewResponseReceiver(udp));
                    thread.IsBackground = true;
                    univiewThreads.Add(thread);
                    thread.Start();
                }
            }
        }

        private UdpClient CreateUniviewSocket(IPAddress localIp, int localPort)
        {
            try
            {
                UdpClient udp = new UdpClient(AddressFamily.InterNetwork);
                udp.ExclusiveAddressUse = false;
                udp.EnableBroadcast = true;
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.ReceiveTimeout = 1000;
                udp.Client.Bind(new IPEndPoint(localIp, localPort));

                return udp;
            }
            catch (Exception ex)
            {
                string portText = localPort == 0 ? "ephemeral port" : "UDP port " + localPort;
                AppendDebug("Unable to bind Uniview discovery socket on " + localIp + " " + portText + ": " + ex.Message);
                Logger.getInstance().WriteLine(Logger.DebugLevel.Warn,
                    "Unable to bind Uniview discovery socket on " + localIp + " " + portText + ": " + ex.Message);
                return null;
            }
        }

        private void UniviewResponseReceiver(UdpClient udp)
        {
            while (!closing)
            {
                try
                {
                    IPEndPoint source = null;
                    byte[] data = udp.Receive(ref source);
                    IPEndPoint local = (IPEndPoint)udp.Client.LocalEndPoint;

                    AppendDebug("Received Uniview discovery response from " + source + " on " + local);
                    HandleDiscoveryResponse(source, data, true, local);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.TimedOut &&
                        ex.SocketErrorCode != SocketError.Interrupted)
                    {
                        AppendDebug("Uniview discovery receive error: " + ex.Message);
                    }
                }
                catch
                {
                }
            }
        }

        private void SendUniviewDiscoveryProbes()
        {
            string standardXml = CreateStandardProbeXml();
            string univiewXml = CreateUniviewProbeXml(standardXml);
            byte[] standardBytes = Encoding.UTF8.GetBytes(standardXml);
            byte[] univiewBytes = Encoding.UTF8.GetBytes(univiewXml);
            IPEndPoint multicastEndpoint = new IPEndPoint(IPAddress.Parse(multicastIP), port);
            IPEndPoint broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, port);

            lock (univiewSocketLock)
            {
                foreach (UdpClient udp in univiewSockets.ToArray())
                {
                    try
                    {
                        IPEndPoint local = (IPEndPoint)udp.Client.LocalEndPoint;
                        AppendDebug("Local interface IP used: " + local.Address);
                        AppendDebug("Local port used: " + local.Port);

                        udp.Send(standardBytes, standardBytes.Length, multicastEndpoint);
                        AppendDebug("Sent multicast Probe to " + multicastEndpoint + Environment.NewLine + standardXml);

                        udp.Send(univiewBytes, univiewBytes.Length, broadcastEndpoint);
                        AppendDebug("Sent UniviewProbe broadcast to " + broadcastEndpoint + Environment.NewLine + univiewXml);
                    }
                    catch (Exception ex)
                    {
                        AppendDebug("Failed to send Uniview discovery probe: " + ex.Message);
                    }
                }
            }
        }

        private void HandleDiscoveryResponse(IPEndPoint from, byte[] data, bool fromUniviewSocket)
        {
            HandleDiscoveryResponse(from, data, fromUniviewSocket, null);
        }

        private void HandleDiscoveryResponse(IPEndPoint from, byte[] data, bool fromUniviewSocket, IPEndPoint localEndPoint)
        {
            string xml;

            try
            {
                xml = Encoding.UTF8.GetString(data);
            }
            catch
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Wsdiscovery.reciever(): Error: Unable to read text");
                return;
            }

            if (from.Address.ToString() == "127.0.0.1")
                return;

            if (!xml.Contains("ProbeMatch") &&
                !xml.Contains("ProbeMatches") &&
                !xml.Contains("UniviewProbeMatch") &&
                !xml.Contains("UniviewProbeMatches"))
            {
                return;
            }

            ParsedDiscovery parsed = ParseDiscoveryXml(xml, from);
            if (parsed == null)
                return;

            AppendDebug(
                "Response source: " + from +
                Environment.NewLine + "Parsed model: " + parsed.Model +
                Environment.NewLine + "Parsed serial: " + parsed.Serial +
                Environment.NewLine + "Parsed MAC: " + parsed.Mac +
                Environment.NewLine + "Parsed firmware: " + parsed.Firmware +
                Environment.NewLine + "Parsed ONVIF profiles: " + parsed.OnvifProfiles +
                Environment.NewLine + "Received XML:" + Environment.NewLine + xml);

            if (parsed.IsUniview)
            {
                UniviewDiscoveryCache.Store(new UniviewDiscoveryInfo
                {
                    IPAddress = parsed.IPAddress.ToString(),
                    Model = parsed.Model,
                    Serial = parsed.Serial,
                    Mac = parsed.Mac,
                    Firmware = parsed.Firmware,
                    OnvifProfiles = parsed.OnvifProfiles,
                    LocalInterfaceIp = localEndPoint == null ? null : localEndPoint.Address.ToString(),
                    LocalInterfaceIndex = GetInterfaceIndexForAddress(localEndPoint == null ? null : localEndPoint.Address)
                });

                viewer?.deviceFound("Uniview", 0, parsed.IPAddress, FirstNonEmpty(parsed.Model, "Uniview"), FirstNonEmpty(parsed.Mac, parsed.Serial));
                return;
            }

            if (parsed.IsAxis)
            {
                viewer?.deviceFound("Axis", 0, parsed.IPAddress, FirstNonEmpty(parsed.Model, "Axis"), FirstNonEmpty(parsed.Mac, parsed.Serial));
                return;
            }

            if (viewer != null && !string.IsNullOrWhiteSpace(parsed.Model) && !string.IsNullOrWhiteSpace(parsed.Serial))
                viewer.deviceFound(name, 0, parsed.IPAddress, parsed.Model, parsed.Serial);
        }

        private static int GetInterfaceIndexForAddress(IPAddress address)
        {
            if (address == null)
                return 0;

            try
            {
                foreach (NetworkInterface iface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    IPInterfaceProperties props = iface.GetIPProperties();
                    foreach (UnicastIPAddressInformation unicast in props.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                            unicast.Address.Equals(address))
                        {
                            IPv4InterfaceProperties ipv4 = props.GetIPv4Properties();
                            return ipv4 == null ? 0 : ipv4.Index;
                        }
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private ParsedDiscovery ParseDiscoveryXml(string xml, IPEndPoint from)
        {
            string scopesText = MatchValue(xml, "[<:]Scopes[^>]*>([^<>]*)</");
            string model = removeNameSpace(MatchValue(xml, "[<:]Types[^>]*>([^<>]+)</"));
            string serial = extractUUID(MatchValue(xml, "[<:]Address[^>]*>([^<>]+)</"));
            string xaddrs = MatchValue(xml, "[<:]XAddrs[^>]*>([^<>]+)</");
            IPAddress ip = ExtractIpFromXAddrs(xaddrs) ?? from.Address;

            if (!string.IsNullOrWhiteSpace(scopesText))
            {
                string scopeModel = FirstNonEmpty(
                    extractOnvifScopeValue(scopesText, "hardware"),
                    extractOnvifScopeValue(scopesText, "name"));
                if (!string.IsNullOrWhiteSpace(scopeModel))
                    model = scopeModel;

                string scopeSerial = FirstNonEmpty(
                    extractOnvifScopeValue(scopesText, "serial"),
                    extractOnvifScopeValue(scopesText, "serialNumber"),
                    extractOnvifScopeValue(scopesText, "SerialNumber"));
                if (!string.IsNullOrWhiteSpace(scopeSerial))
                    serial = scopeSerial;
            }

            string mac = ExtractScopeMac(scopesText);
            string firmware = FirstNonEmpty(
                extractOnvifScopeValue(scopesText, "version"),
                extractOnvifScopeValue(scopesText, "firmware"),
                extractOnvifScopeValue(scopesText, "firmwareVersion"));
            string profiles = ExtractOnvifProfiles(scopesText);

            bool isUniview =
                xml.IndexOf("UniviewProbeMatch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                isUniviewScopes(scopesText) ||
                model.IndexOf("Uniview", StringComparison.OrdinalIgnoreCase) >= 0;

            bool isAxis =
                isAxisScopes(scopesText) ||
                model.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                xml.IndexOf("axis.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                xml.IndexOf("Axis Communications", StringComparison.OrdinalIgnoreCase) >= 0;

            if (string.IsNullOrWhiteSpace(model) && !isUniview && !isAxis)
                return null;

            return new ParsedDiscovery
            {
                IPAddress = ip,
                Model = model,
                Serial = serial,
                Mac = mac,
                Firmware = firmware,
                OnvifProfiles = profiles,
                IsUniview = isUniview,
                IsAxis = isAxis
            };
        }

        private string CreateStandardProbeXml()
        {
            message_uuidg = Guid.NewGuid();
            return string.Format(announce, message_uuidg);
        }

        private string CreateUniviewProbeXml(string standardXml)
        {
            string xml = standardXml
                .Replace("<d:Probe>", "<d:UniviewProbe>")
                .Replace("</d:Probe>", "</d:UniviewProbe>")
                .Replace("<Probe xmlns=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\">", "<UniviewProbe xmlns=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\">")
                .Replace("</Probe>", "</UniviewProbe>");

            if (xml.IndexOf("NetworkVideoTransmitter", StringComparison.OrdinalIgnoreCase) < 0)
            {
                xml = xml.Replace(
                    "<d:Types>w:Device o:Device</d:Types>",
                    "<d:Types>dn:NetworkVideoTransmitter</d:Types>");
            }

            if (xml.IndexOf("xmlns:dn=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                xml = xml.Replace(
                    " xmlns:o=\"http://www.onvif.org/ver10/device/wsdl\"",
                    " xmlns:o=\"http://www.onvif.org/ver10/device/wsdl\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\"");
            }

            return xml;
        }

        private IPAddress ExtractIpFromXAddrs(string xaddrs)
        {
            if (string.IsNullOrWhiteSpace(xaddrs))
                return null;

            foreach (string part in xaddrs.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    Uri uri = new Uri(part);
                    IPAddress ip;
                    if (IPAddress.TryParse(uri.Host, out ip))
                        return ip;
                }
                catch
                {
                }
            }

            return null;
        }

        private string ExtractScopeMac(string scopes)
        {
            string mac = FirstNonEmpty(
                extractOnvifScopeValue(scopes, "macaddr"),
                extractOnvifScopeValue(scopes, "mac"),
                extractOnvifScopeValue(scopes, "MAC"));

            if (string.IsNullOrWhiteSpace(mac))
                return "";

            mac = mac.Trim().ToUpperInvariant().Replace(":", "-");
            if (Regex.IsMatch(mac, @"^[0-9A-F]{12}$"))
                mac = Regex.Replace(mac, "(.{2})(?!$)", "$1-");

            return mac;
        }

        private string ExtractOnvifProfiles(string scopes)
        {
            if (string.IsNullOrWhiteSpace(scopes))
                return "Unknown";

            SortedSet<string> profiles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            Regex profileRegex = new Regex(@"(?:^|[/\s])Profile[/_-]?([A-Z0-9]+)", RegexOptions.IgnoreCase);

            foreach (Match match in profileRegex.Matches(scopes))
            {
                string profile = match.Groups[1].Value.Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(profile))
                    profiles.Add("Profile " + profile);
            }

            return profiles.Count == 0 ? "Unknown" : string.Join(", ", profiles.ToArray());
        }

        private string MatchValue(string text, string pattern)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            Match match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private string extractUUID(string USN)
        {
            if (string.IsNullOrWhiteSpace(USN))
                return "";

            if (USN.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
                USN = USN.Substring(4);

            if (USN.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
                USN = USN.Substring(5);

            return USN.Trim();
        }

        private string removeNameSpace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            Regex xmlns = new Regex("[a-zA-Z_][a-zA-Z_0-9-]*:");
            return xmlns.Replace(value, "").Trim();
        }

        private bool isUniviewScopes(string scopes)
        {
            return !string.IsNullOrWhiteSpace(scopes) &&
                   (scopes.IndexOf("/manufacturer/UNIVIEW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    scopes.IndexOf("/manufacturer/Uniview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    scopes.IndexOf("uniview", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool isAxisScopes(string scopes)
        {
            return !string.IsNullOrWhiteSpace(scopes) &&
                   (scopes.IndexOf("axis.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    scopes.IndexOf("/manufacturer/Axis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    scopes.IndexOf("Axis Communications", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string extractOnvifScopeValue(string scopes, string key)
        {
            if (string.IsNullOrWhiteSpace(scopes) || string.IsNullOrWhiteSpace(key))
                return "";

            Regex scopeValue = new Regex(
                @"onvif://[^\s<>""']*/" + Regex.Escape(key) + @"/([^\s<>""']+)",
                RegexOptions.IgnoreCase);

            Match match = scopeValue.Match(scopes);
            if (!match.Success)
                return "";

            string value = match.Groups[1].Value;

            try
            {
                value = Uri.UnescapeDataString(value);
            }
            catch
            {
            }

            return value.Trim();
        }

        private string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private bool IsDiagnosticsEnabled()
        {
#if DEBUG
            return true;
#else
            return Config.getInstance().DebugMode;
#endif
        }

        private void AppendDebug(string text)
        {
            if (!IsDiagnosticsEnabled()) return;

            try
            {
                string debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Debug");
                Directory.CreateDirectory(debugDir);
                string path = Path.Combine(debugDir, "UniviewDiscoveryDebug.txt");
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                    Environment.NewLine +
                    text +
                    Environment.NewLine +
                    "==============================" +
                    Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        private class ParsedDiscovery
        {
            public IPAddress IPAddress { get; set; }
            public string Model { get; set; }
            public string Serial { get; set; }
            public string Mac { get; set; }
            public string Firmware { get; set; }
            public string OnvifProfiles { get; set; }
            public bool IsUniview { get; set; }
            public bool IsAxis { get; set; }
        }
    }
}
