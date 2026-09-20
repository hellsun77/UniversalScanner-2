using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace UniversalScanner.DeviceSDK.Dahua
{
    public static class DahuaDiscoveryState
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> ActivationByIp =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ActivationByMac =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ActivationBySerial =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ModelByIp =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ModelByMac =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ModelBySerial =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Record(string ipAddress, string body)
        {
            if (string.IsNullOrWhiteSpace(ipAddress) || string.IsNullOrWhiteSpace(body))
                return;

            string status = ExtractActivationStatus(body);
            string model = ExtractJsonString(body, "DeviceType");
            string mac = NormalizeMac(ExtractJsonString(body, "mac"));
            string serial = ExtractJsonString(body, "SerialNo") ?? ExtractJsonString(body, "MachineName");

            lock (Sync)
            {
                if (!string.IsNullOrWhiteSpace(status))
                {
                    ActivationByIp[ipAddress.Trim()] = status;

                    if (!string.IsNullOrWhiteSpace(mac))
                        ActivationByMac[mac] = status;

                    if (!string.IsNullOrWhiteSpace(serial))
                        ActivationBySerial[serial.Trim()] = status;
                }

                if (!string.IsNullOrWhiteSpace(model))
                {
                    ModelByIp[ipAddress.Trim()] = model;

                    if (!string.IsNullOrWhiteSpace(mac))
                        ModelByMac[mac] = model;

                    if (!string.IsNullOrWhiteSpace(serial))
                        ModelBySerial[serial.Trim()] = model;
                }
            }

            WriteDiscoveryDebug(ipAddress, body, status, model);
        }

        public static string GetActivationStatus(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return null;

            lock (Sync)
            {
                string status;
                return ActivationByIp.TryGetValue(ipAddress.Trim(), out status) ? status : null;
            }
        }

        public static string GetActivationStatus(string ipAddress, string serialOrMac)
        {
            lock (Sync)
            {
                string status;
                string mac = NormalizeMac(serialOrMac);
                if (!string.IsNullOrWhiteSpace(mac) &&
                    ActivationByMac.TryGetValue(mac, out status))
                    return status;

                if (!string.IsNullOrWhiteSpace(serialOrMac) &&
                    ActivationBySerial.TryGetValue(serialOrMac.Trim(), out status))
                    return status;
            }

            return GetActivationStatus(ipAddress);
        }

        public static string GetDeviceModel(string ipAddress, string macAddress, string serial)
        {
            lock (Sync)
            {
                string model;

                string mac = NormalizeMac(macAddress);
                if (!string.IsNullOrWhiteSpace(mac) &&
                    ModelByMac.TryGetValue(mac, out model))
                    return model;

                if (!string.IsNullOrWhiteSpace(serial) &&
                    ModelBySerial.TryGetValue(serial.Trim(), out model))
                    return model;

                if (!string.IsNullOrWhiteSpace(ipAddress) &&
                    ModelByIp.TryGetValue(ipAddress.Trim(), out model))
                    return model;
            }

            return null;
        }

        private static string ExtractActivationStatus(string body)
        {
            string[] inactiveKeys =
            {
                "Uninitialized",
                "UnInit",
                "NotInit"
            };

            foreach (string key in inactiveKeys)
            {
                bool? value = ExtractJsonBool(body, key);
                if (value == true)
                    return "Uninitialized";
            }

            string[] initKeys =
            {
                "Init",
                "Initialized",
                "IsInit",
                "InitState",
                "Activation",
                "Activated"
            };

            foreach (string key in initKeys)
            {
                string text = ExtractJsonValue(body, key);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                string normalized = text.Trim().Trim('"').ToLowerInvariant();
                if (normalized == "false" || normalized == "0" || normalized == "uninitialized" || normalized == "inactive")
                    return "Uninitialized";

                int numeric;
                if (int.TryParse(normalized, out numeric))
                {
                    // Dahua discovery uses Init=1 while the first-run DevInit page is still open,
                    // then switches to a larger numeric value after DevInit.account/access succeeds.
                    if (string.Equals(key, "Init", StringComparison.OrdinalIgnoreCase))
                        return numeric > 1 ? "Active" : "Uninitialized";

                    return numeric > 0 ? "Active" : "Uninitialized";
                }

                if (normalized == "true" || normalized == "initialized" || normalized == "active")
                    return "Active";
            }

            if (body.IndexOf("uninitialized", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Uninitialized";

            return null;
        }

        private static bool? ExtractJsonBool(string body, string key)
        {
            string value = ExtractJsonValue(body, key);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            bool parsed;
            if (bool.TryParse(value.Trim().Trim('"'), out parsed))
                return parsed;

            return null;
        }

        private static string ExtractJsonValue(string body, string key)
        {
            Match m = Regex.Match(
                body,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\"[^\"]*\"|true|false|-?[0-9]+)",
                RegexOptions.IgnoreCase);

            return m.Success ? m.Groups[1].Value : null;
        }

        private static string ExtractJsonString(string body, string key)
        {
            string value = ExtractJsonValue(body, key);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
        }

        private static string NormalizeMac(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string hex = Regex.Replace(value, "[^0-9A-Fa-f]", "").ToUpperInvariant();
            return hex.Length == 12 ? hex : "";
        }

        private static void WriteDiscoveryDebug(string ipAddress, string body, string status, string model)
        {
            StringBuilder debug = new StringBuilder();
            debug.AppendLine("Dahua discovery debug");
            debug.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            debug.AppendLine("IP: " + ipAddress);
            debug.AppendLine("Parsed activation: " + (string.IsNullOrWhiteSpace(status) ? "not found" : status));
            debug.AppendLine("Parsed model: " + (string.IsNullOrWhiteSpace(model) ? "not found" : model));
            debug.AppendLine();
            debug.AppendLine("Discovery JSON:");
            debug.AppendLine(body);

            DahuaDebugLog.Write("", debug.ToString());
        }

    }
}
