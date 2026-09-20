using System;
using System.Drawing;
using System.Net;
using System.Text;
using JulienBlitte; // Standard framework engine utilities

namespace UniversalScanner
{
    class Tiandy : ScanEngine
    {
        // Default target port used by legacy Tiandy search utility platforms
        protected const int port = 3000; 

        public override string name
        {
            get { return "Tiandy (Experimental Fallback)"; }
        }

        public override UInt16[] getUsedPort()
        {
            return new UInt16[] { port };
        }

        public override int color
        {
            get { return Color.Chocolate.ToArgb(); }
        }

        public Tiandy()
        {
            // Framework constructor template
        }

        public override void listen()
        {
            listenUdpGlobal(port);
            listenUdpInterfaces();
        }

        public override void scan()
        {
#if DEBUG
            selfTest();
#endif
            sendBroadcast(port);
        }

        public override byte[] sender(IPEndPoint dest)
        {
            // WARNING: This payload remains an unverified placeholder string.
            // For production-grade discovery of modern Tiandy cameras, rely primarily 
            // on the generic ONVIF scanning methods utilizing WS-Discovery on port 3702.
            string discoveryPayload = "Search Device\r\n"; 
            return Encoding.UTF8.GetBytes(discoveryPayload);
        }

        public override void reciever(IPEndPoint from, byte[] data)
        {
            if (data == null || data.Length < 10)
                return;

            string payloadText = Encoding.UTF8.GetString(data);
            
            // Convert to lowercase invariant to ensure case-insensitive safety across firmware versions
            string lowerPayload = payloadText.ToLowerInvariant();

            // Case-Insensitive Filter Block: Strictly guards against false-positive device listings
            if (!lowerPayload.Contains("tiandy") &&
                !payloadText.Contains("TC-") && // Kept case-sensitive as Tiandy models strictly use uppercase 'TC-'
                !lowerPayload.Contains("model=") &&
                !lowerPayload.Contains("serial=") &&
                !lowerPayload.Contains("mac="))
            {
                return; 
            }

            string deviceModel = "Tiandy Device";
            string deviceSerial = "Unknown";

            // Parsing engine using normalized lowercase keys to find indexes, but extracting from raw payload
            if (lowerPayload.Contains("model="))
            {
                deviceModel = ExtractValue(payloadText, "model=");
            }
            else if (payloadText.Contains("TC-"))
            {
                // Dynamic boundary parsing to safely extract long or complex model strings without clipping
                int idx = payloadText.IndexOf("TC-");
                int end = payloadText.IndexOfAny(new char[] { ';', ',', '\r', '\n', '|', ' ' }, idx);
                
                if (end > idx)
                    deviceModel = payloadText.Substring(idx, end - idx).Trim();
                else
                    deviceModel = payloadText.Substring(idx).Trim();
            }

            if (lowerPayload.Contains("serial="))
            {
                deviceSerial = ExtractValue(payloadText, "serial=");
            }
            else if (lowerPayload.Contains("mac="))
            {
                deviceSerial = ExtractValue(payloadText, "mac=");
            }

            viewer.deviceFound(name, 1, from.Address, deviceModel, deviceSerial);
        }

        // Case-insensitive extraction helper with robust index checking to prevent crashes
        private string ExtractValue(string text, string key)
        {
            try
            {
                int index = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return "Unknown";

                int start = index + key.Length;
                int end = text.IndexOfAny(new char[] { ';', ',', '\r', '\n', '|' }, start);

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