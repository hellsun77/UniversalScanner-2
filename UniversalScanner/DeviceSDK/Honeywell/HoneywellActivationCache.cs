using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    /// <summary>
    /// Stores Honeywell devices that Universal Scanner successfully activated.
    ///
    /// Reason:
    /// Some Honeywell HC30/HC35 firmware can still expose activation-style
    /// unauthenticated endpoints after password creation. The only fully reliable
    /// event we control is a successful activation result from HoneywellActivator.
    /// </summary>
    public static class HoneywellActivationCache
    {
        private static readonly object Sync = new object();

        private static string CacheFilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "UniversalScanner");

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                return Path.Combine(dir, "honeywell_activated_devices.txt");
            }
        }

        public static void MarkActive(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            lock (Sync)
            {
                HashSet<string> ips = LoadIps();
                ips.Add(ip.Trim());
                SaveIps(ips);
            }
        }

        public static bool IsMarkedActive(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            lock (Sync)
            {
                HashSet<string> ips = LoadIps();
                return ips.Contains(ip.Trim());
            }
        }

        public static void Remove(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            lock (Sync)
            {
                HashSet<string> ips = LoadIps();
                if (ips.Remove(ip.Trim()))
                    SaveIps(ips);
            }
        }

        private static HashSet<string> LoadIps()
        {
            HashSet<string> ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!File.Exists(CacheFilePath))
                    return ips;

                string[] lines = File.ReadAllLines(CacheFilePath, Encoding.UTF8);

                foreach (string line in lines)
                {
                    string value = (line ?? "").Trim();

                    if (value.Length == 0)
                        continue;

                    if (value.StartsWith("#"))
                        continue;

                    ips.Add(value);
                }
            }
            catch
            {
                // Ignore cache read errors.
            }

            return ips;
        }

        private static void SaveIps(HashSet<string> ips)
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("# Honeywell devices successfully activated by Universal Scanner");
                lines.Add("# One IP per line");

                foreach (string ip in ips)
                    lines.Add(ip);

                File.WriteAllLines(CacheFilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch
            {
                // Ignore cache write errors.
            }
        }
    }
}
