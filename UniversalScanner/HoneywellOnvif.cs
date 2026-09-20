using System;
using System.Drawing;
using System.Net;
using System.Text;
using JulienBlitte; // Standard framework engine utilities

namespace UniversalScanner
{
    class HoneywellOnvif : ScanEngine
    {
        // Verified global port for WS-Discovery utilized by ONVIF compliance stacks
        protected const int port = 3702; 

        // Official global IPv4 multicast address group assigned for WS-Discovery
        private static readonly string multicastIP = "239.255.255.250";

        public override string name
        {
            get { return "Honeywell (ONVIF Mode)"; }
        }

        public override UInt16[] getUsedPort()
        {
            return new UInt16[] { port };
        }

        public override int color
        {
            get { return Color.Gold.ToArgb(); }
        }

        public HoneywellOnvif()
        {
            // Framework constructor template
        }

        public override void listen()
        {
            // Binds to the official WS-Discovery multicast zone and local interfaces
            listenMulticast(IPAddress.Parse(multicastIP), port);
            listenUdpInterfaces();
        }

        public override void scan()
        {
#if DEBUG
            selfTest();
#endif
            // Transmit over targeted multicast as standard broadcast is unnecessary for WS-Discovery
            sendMulticast(IPAddress.Parse(multicastIP), port);
        }

        public override byte[] sender(IPEndPoint dest)
        {
            // Dynamic UUID optimization: Generates a completely unique MessageID for every individual scan
            // to prevent smart switches or device firewalls from dropping probe packets as duplicate traffic.
            string uniqueMessageId = Guid.NewGuid().ToString();

            string probePayload = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                                  "<Envelope xmlns=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\">" +
                                  "<Header>" +
                                  "<a:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>" +
                                  "<a:MessageID>urn:uuid:" + uniqueMessageId + "</a:MessageID>" +
                                  "<a:To>urn:schemas-xmlsoap-org:ws:2004:08:addressing:role:anonymous</a:To>" +
                                  "</Header>" +
                                  "<Body>" +
                                  "<Probe xmlns=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\">" +
                                  "<Types xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\">dn:NetworkVideoTransmitter</Types>" +
                                  "</Probe>" +
                                  "</Body>" +
                                  "</Envelope>";

            return Encoding.UTF8.GetBytes(probePayload);
        }

        public override void reciever(IPEndPoint from, byte[] data)
        {
            if (data == null || data.Length < 10)
                return;

            string payloadText = Encoding.UTF8.GetString(data);

            // Strict check: Ignore non-video appliances on the network loop
            if (payloadText.IndexOf("NetworkVideoTransmitter", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            string deviceModel = "Honeywell IP Camera";
            string deviceSerial = "Unknown";

            // Advanced parsing sequence: Locate the hardware definition block case-insensitively first
            int hwIdx = payloadText.IndexOf("hardware/", StringComparison.OrdinalIgnoreCase);
            if (hwIdx >= 0)
            {
                int start = hwIdx + 9; // Offset past "hardware/"
                int end = payloadText.IndexOfAny(new char[] { ';', ',', '\r', '\n', '|', ' ', '<', '"' }, start);
                
                if (end > start)
                    deviceModel = payloadText.Substring(start, end - start).Trim();
                else
                    deviceModel = payloadText.Substring(start).Trim();
            }

            // Normalizing extracted text strings using ToLowerInvariant to eliminate language/culture bugs
            string lowerModel = deviceModel.ToLowerInvariant();
            string lowerPayload = payloadText.ToLowerInvariant();

            // Set validation target flag to false initially
            bool isHoneywell = false;

            // Layer 1: Global Corporate Signature Check
            if (lowerPayload.Contains("honeywell") || lowerPayload.Contains("equip"))
            {
                isHoneywell = true;
            }
            // Layer 2: Dedicated Model Naming Prefixes
            else if (lowerModel.StartsWith("hc30") || lowerModel.StartsWith("hc35") || lowerModel.StartsWith("hc60") ||
                     lowerModel.StartsWith("hn30") || lowerModel.StartsWith("hn35") || lowerModel.StartsWith("hn60"))
            {
                isHoneywell = true;
            }
            // Layer 3: Comprehensive Commercial IP Line Formats (e.g., H4W4PER2, HBW2PER1, H3W2GR1)
            else if (lowerModel.StartsWith("h2w") || 
                     lowerModel.StartsWith("h3w") || 
                     lowerModel.StartsWith("h4w") || 
                     lowerModel.StartsWith("h5w") ||
                     lowerModel.StartsWith("hbd") || // Legacy Performance Series Bullet
                     lowerModel.StartsWith("h4d") || // Legacy Performance Series Dome
                     lowerModel.StartsWith("hcd"))   // Legacy Commercial Domes
            {
                isHoneywell = true;
            }

            // Drop if it completely fails to match any known Honeywell identifier layers
            if (!isHoneywell)
            {
                return;
            }

            // Extract UUID descriptor to use as the hardware identifier token
            if (lowerPayload.Contains("uuid:"))
            {
                deviceSerial = ExtractValue(payloadText, "uuid:");
            }

            viewer.deviceFound(name, 1, from.Address, deviceModel, deviceSerial);
        }

        // Defensive case-insensitive string extraction helper with explicit null/missing key index checking
        private string ExtractValue(string text, string key)
        {
            try
            {
                int index = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return "Unknown";

                int start = index + key.Length;
                int end = text.IndexOfAny(new char[] { ';', ',', '\r', '\n', '|', ' ', '<', '"' }, start);

                if (end > start)
                    return text.Substring(start, end - start).Trim();

                return text.Substring(start).Trim();
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}