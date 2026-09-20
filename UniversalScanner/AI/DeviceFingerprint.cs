using System;
using System.Collections.Generic;

namespace UniversalScanner.AI
{
    public sealed class DeviceFingerprint
    {
        private readonly Dictionary<string, string> signals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public string Vendor { get; set; }
        public string Model { get; set; }
        public string Protocol { get; set; }
        public string FirmwareVersion { get; set; }
        public string OnvifProfile { get; set; }
        public DateTime CapturedAtUtc { get; private set; }

        public DeviceFingerprint()
        {
            CapturedAtUtc = DateTime.UtcNow;
        }

        public IReadOnlyDictionary<string, string> Signals
        {
            get { return signals; }
        }

        public void AddSignal(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                return;

            signals[name.Trim()] = value.Trim();
        }
    }
}
