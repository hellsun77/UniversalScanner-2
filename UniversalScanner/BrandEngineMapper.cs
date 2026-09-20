using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalScanner
{
    public static class BrandEngineMapper
    {
        // Internal mapping of Display Brand -> Required Registered Engine Names
        private static readonly Dictionary<string, List<string>> brandToEngines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Advantech", new List<string> { "Advantech" } },
            { "Arecont", new List<string> { "Arecont" } },
            { "Axis", new List<string> { "Axis" } },
            { "Bosch", new List<string> { "Bosch" } },
            { "Dahua", new List<string> { "Dahua" } },
            { "Eaton", new List<string> { "Eaton" } },
            { "FLIR", new List<string> { "WSDiscovery" } },
            { "Hanwha", new List<string> { "Hanwha" } },
            { "Hikvision", new List<string> { "Hikvision" } },
            { "Honeywell", new List<string> { "Honeywell", "Honeywell (ONVIF Mode)" } },
            { "Mobotix", new List<string> { "mDNS", "WSDiscovery" } },
            { "NiceVision", new List<string> { "NiceVision" } },
            { "Panasonic", new List<string> { "Panasonic" } },
            { "Siqura", new List<string> { "WSDiscovery" } },
            { "Sony", new List<string> { "Sony" } },
            { "Tiandy", new List<string> { "Tiandy (Experimental Fallback)" } },
            { "Ubiquiti", new List<string> { "Ubiquiti" } },
            { "Uniview", new List<string> { "WSDiscovery" } },
            { "Vivotek", new List<string> { "Vivotek" } },
            { "360Vision", new List<string> { "360Vision" } },
            { "Sunell", new List<string> { "WSDiscovery" } }
        };

        public static IEnumerable<string> GetSupportedBrands()
        {
            return brandToEngines.Keys.OrderBy(k => k);
        }

        public static HashSet<string> GetRequiredEngines(IEnumerable<string> selectedBrands)
        {
            var requiredEngines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (selectedBrands == null) return requiredEngines;

            foreach (var brand in selectedBrands)
            {
                if (brandToEngines.TryGetValue(brand.Trim(), out var engines))
                {
                    foreach (var engine in engines)
                    {
                        requiredEngines.Add(engine);
                    }
                }
            }

            return requiredEngines;
        }

        public static string NormalizeVendor(string rawVendor)
        {
            if (string.IsNullOrWhiteSpace(rawVendor))
                return "";

            string v = rawVendor.Trim();

            // Uniview aliases
            if (v.IndexOf("Uniview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                v.Equals("UNV", StringComparison.OrdinalIgnoreCase))
                return "Uniview";

            // Hikvision aliases
            if (v.IndexOf("Hikvision", StringComparison.OrdinalIgnoreCase) >= 0 ||
                v.IndexOf("Hangzhou Hikvision", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Hikvision";

            // Dahua aliases
            if (v.IndexOf("Dahua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                v.IndexOf("Zhejiang Dahua", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Dahua";

            // Hanwha aliases
            if (v.IndexOf("Hanwha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                v.IndexOf("Hanwha Techwin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                v.IndexOf("Samsung", StringComparison.OrdinalIgnoreCase) >= 0) // Samsung often used by Hanwha
                return "Hanwha";

            // FLIR aliases
            if (v.IndexOf("FLIR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                v.IndexOf("FLIR Systems", StringComparison.OrdinalIgnoreCase) >= 0)
                return "FLIR";

            // Bosch aliases
            if (v.IndexOf("Bosch", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Bosch";

            // Axis aliases
            if (v.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Axis";

            // Honeywell aliases
            if (v.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Honeywell";

            // Siqura aliases
            if (v.IndexOf("Siqura", StringComparison.OrdinalIgnoreCase) >= 0 ||
                v.IndexOf("TKH", StringComparison.OrdinalIgnoreCase) >= 0) // TKH Security / Siqura
                return "Siqura";

            // Mobotix aliases
            if (v.IndexOf("Mobotix", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Mobotix";

            // Panasonic aliases
            if (v.IndexOf("Panasonic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                v.IndexOf("i-PRO", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Panasonic";

            // Vivotek aliases
            if (v.IndexOf("Vivotek", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Vivotek";
                
            // Tiandy aliases
            if (v.IndexOf("Tiandy", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Tiandy";

            // Arecont aliases
            if (v.IndexOf("Arecont", StringComparison.OrdinalIgnoreCase) >= 0 ||
                v.IndexOf("Arecont Vision", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Arecont";

            // 360Vision aliases
            if (v.IndexOf("360Vision", StringComparison.OrdinalIgnoreCase) >= 0 ||
                v.IndexOf("360 Vision", StringComparison.OrdinalIgnoreCase) >= 0)
                return "360Vision";

            return v;
        }
    }
}
