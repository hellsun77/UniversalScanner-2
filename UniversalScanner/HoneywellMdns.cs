using JulienBlitte;
using System;
using System.Drawing;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace UniversalScanner
{
    class HoneywellMdns : ScanEngine
    {
        protected const int port = 5353;
        private static readonly string multicastIP = "224.0.0.251";

        public override string name
        {
            get { return "Honeywell"; }
        }

        public override UInt16[] getUsedPort()
        {
            return new UInt16[] { port };
        }

        public override int color
        {
            get { return Color.Black.ToArgb(); }
        }

        public HoneywellMdns()
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
        }

        public override byte[] sender(IPEndPoint dest)
        {
            return BuildMdnsPtrQuery("_whistler._tcp.local");
        }
        public override void reciever(IPEndPoint from, byte[] data)
        {
            if (data == null || data.Length < 20)
                return;

            string payload = Encoding.ASCII.GetString(data);

            if (from.Address.ToString() == "127.0.0.1")
                return;

            if (!payload.Contains("_HON"))
                return;

            string deviceModel = "Honeywell";
            string deviceSerial = "Unknown";

            var match = System.Text.RegularExpressions.Regex.Match(
                payload,
                @"(HC\d+[A-Z0-9]*)_([0-9A-F]+)_([\d\.]+)_"
            );

            if (match.Success)
            {
                deviceModel = match.Groups[1].Value;
                deviceSerial = match.Groups[2].Value;
            }

            viewer.deviceFound(
                name,
                1,
                from.Address,
                deviceModel,
                deviceSerial);
        }

        private byte[] BuildMdnsPtrQuery(string serviceName)
        {
            byte[] header = new byte[]
            {
                0x00,0x00,
                0x00,0x00,
                0x00,0x01,
                0x00,0x00,
                0x00,0x00,
                0x00,0x00
            };

            byte[] qname = EncodeDnsName(serviceName);

            byte[] qtype = new byte[]
            {
                0x00,0x0C,
                0x00,0x01
            };

            byte[] packet =
                new byte[header.Length + qname.Length + qtype.Length];

            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            Buffer.BlockCopy(qname, 0, packet, header.Length, qname.Length);
            Buffer.BlockCopy(qtype, 0, packet,
                header.Length + qname.Length,
                qtype.Length);

            return packet;
        }

        private byte[] EncodeDnsName(string name)
        {
            string[] labels = name.Split('.');

            byte[] buffer = new byte[256];
            int offset = 0;

            foreach (string label in labels)
            {
                byte[] labelBytes = Encoding.ASCII.GetBytes(label);

                buffer[offset++] = (byte)labelBytes.Length;

                Buffer.BlockCopy(
                    labelBytes,
                    0,
                    buffer,
                    offset,
                    labelBytes.Length);

                offset += labelBytes.Length;
            }

            buffer[offset++] = 0x00;

            byte[] result = new byte[offset];

            Buffer.BlockCopy(buffer, 0, result, 0, offset);

            return result;
        }
    }
}