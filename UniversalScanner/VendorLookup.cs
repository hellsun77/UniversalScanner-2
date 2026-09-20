using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UniversalScanner
{
    public static class VendorLookup
    {
        private static readonly Dictionary<string, string> Vendors =
            new Dictionary<string, string>();

        private static bool loaded = false;

        public static void Load()
        {
            if (loaded)
                return;

            loaded = true;

            var assembly = Assembly.GetExecutingAssembly();

            using (Stream stream =
                assembly.GetManifestResourceStream("UniversalScanner.Resources.oui.csv"))
            {
                if (stream == null)
                    return;

                using (StreamReader reader = new StreamReader(stream))
                {
                    string line;
                    bool firstLine = true;

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (firstLine)
                        {
                            firstLine = false;
                            continue;
                        }

                        string[] parts = line.Split(new char[] { ',' }, 3);

                        if (parts.Length < 3)
                            continue;

                        string prefix = parts[1]
                            .Replace("-", "")
                            .Trim()
                            .ToUpper();

                        string vendor = parts[2].Trim();

                        if (vendor.StartsWith("\""))
                        {
                            int endQuote = vendor.IndexOf('"', 1);
                            if (endQuote > 1)
                                vendor = vendor.Substring(1, endQuote - 1);
                        }

                        if (prefix.Length == 6 && !Vendors.ContainsKey(prefix))
                            Vendors.Add(prefix, vendor);
                    }
                }
            }
        }

        public static string GetVendor(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
                return "";

            Load();

            string prefix = mac
                .Replace("-", "")
                .Replace(":", "")
                .Replace(".", "")
                .Trim()
                .ToUpper();

            if (prefix.Length < 6)
                return "";

            prefix = prefix.Substring(0, 6);

            if (Vendors.TryGetValue(prefix, out string vendor))
                return vendor;

            return "";
        }
    }
}