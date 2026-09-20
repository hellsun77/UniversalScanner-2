using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class SSDP : ScanEngine
    {
        private readonly string multicastIP = "239.255.255.250";
        private const int port = 1900;

        public override int color
        {
            get
            {
                return Color.DarkGreen.ToArgb();
            }
        }
        public override UInt16[] getUsedPort()
        {
            return new UInt16[] { port };
        }
        public override string name
        {
            get
            {
                return "SSDP";
            }
        }

        public SSDP()
        {

        }
        public override void listen()
        {
            listenMulticast(IPAddress.Parse(multicastIP), port);
            listenUdpInterfaces();
        }

        public override void scan()
        {
#if DEBUG
            selfTest();
#endif
            sendMulticast(IPAddress.Parse(multicastIP), port);
            sendBroadcast(port);
        }

        public override byte[] sender(IPEndPoint dest)
        {
            return Encoding.UTF8.GetBytes(string.Format("M-SEARCH * HTTP/1.1\r\nHost: {0}:{1}\r\nST:upnp:rootdevice\r\nMan:\"ssdp:discover\"\r\nMX:2\r\n\r\n",
                    dest.Address, dest.Port));
        }

        public override void reciever(IPEndPoint from, byte[] data)
        {

            
            string deviceType, deviceID;
            string server;
            string[] serverDetails;
            string body;

            body = Encoding.UTF8.GetString(data);
            var localIPs =
    System.Net.NetworkInformation.NetworkInterface
        .GetAllNetworkInterfaces()
        .SelectMany(i => i.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily ==
                    System.Net.Sockets.AddressFamily.InterNetwork)
        .Select(a => a.Address.ToString())
        .ToHashSet();

            if (localIPs.Contains(from.Address.ToString()))
                return;

            server = extractHttpVar(body, "SERVER");
            serverDetails = splitServerDetails(server);
            
            if (serverDetails.Length > 0)
            {
                deviceType = serverDetails[serverDetails.Length - 1];
            }
            else
            {
                deviceType = "anonymous";
            }

            deviceID = extractHttpVar(body, "USN");

            string location = extractHttpVar(body, "LOCATION");
            string description = TryGetDeviceDescription(location);
            string identityText = server + "\n" + deviceType + "\n" + deviceID + "\n" + description;
            if (LooksLikeHoneywellDevice(identityText))
            {
                string model = FirstNonEmpty(
                    ExtractXmlValue(description, "modelName"),
                    ExtractXmlValue(description, "modelNumber"),
                    ExtractXmlValue(description, "friendlyName"),
                    deviceType);

                if (LooksLikeGenericUpnpText(model))
                    model = identityText.IndexOf("NVR", StringComparison.OrdinalIgnoreCase) >= 0 ? "Honeywell NVR" : "Honeywell Device";

                viewer?.deviceFound("Honeywell", 0, from.Address, model, extractUUID(deviceID));
                return;
            }

            if (viewer != null && deviceID != "" )
                if (server.IndexOf("uTorrent", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;

            if (from.Address.ToString() == "192.168.1.254")
                return;

            viewer.deviceFound(name, 0, from.Address, deviceType, extractUUID(deviceID));
        }

        private string TryGetDeviceDescription(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return "";

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(location);
                request.Method = "GET";
                request.Timeout = 1200;
                request.ReadWriteTimeout = 1200;
                request.AllowAutoRedirect = true;
                request.KeepAlive = false;
                request.UserAgent = "Universal Scanner";
                request.ServicePoint.Expect100Continue = false;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    return reader.ReadToEnd();
            }
            catch
            {
                return "";
            }
        }

        private bool LooksLikeHoneywellDevice(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Honeywell Security", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool LooksLikeGenericUpnpText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            return value.IndexOf("UPnP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("anonymous", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ExtractXmlValue(string xml, string elementName)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(elementName))
                return "";

            Match match = Regex.Match(
                xml,
                "<\\s*" + Regex.Escape(elementName) + "\\s*>\\s*([^<]+)\\s*<\\s*/\\s*" + Regex.Escape(elementName) + "\\s*>",
                RegexOptions.IgnoreCase);

            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : "";
        }

        private string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return "";

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
        

        private string[] splitServerDetails(string device)
        {
            Regex reg;
            string[] result;

            // values can be separated by a comma or a space
            // values are generally this format: name/version
            // "/version" can be ommited when comma are used as separator
            reg = new Regex("[^,/]+(/[^ ,]+)?");
            var foundComp = reg.Matches(device);

            result = new string[foundComp.Count];
            for(int i = 0; i< foundComp.Count; i++)
            {
                result[i] = foundComp[i].Value.Trim();
            }

            return result;
        }

        private string extractUUID(string USN)
        {
            int s = USN.IndexOf("::");
            if (s >= 0)
            {
                USN = USN.Substring(0, s);
            }
            if (USN.StartsWith("uuid:"))
            {
                USN = USN.Substring(5);
            }

            return USN;
        }

        private string extractHttpVar(string data, string variable)
        {
            string[] lines = data.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length < 2)
                return "";

            for(int i=1; i < lines.Length; i++)
            {
                int s = lines[i].IndexOf(":");
                if (s >= 0)
                {
                    string a = lines[i].Substring(0, s);
                    string b = lines[i].Substring(s + 1);

                    if (a.Trim().ToUpper() == variable.ToUpper())
                    {
                        return b.Trim();
                    }
                }
            }

            return "";
        }

    }
}
