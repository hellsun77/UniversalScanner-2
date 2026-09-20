using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UniversalScanner.DeviceSDK.Common
{
    public static class OnvifProfileProbe
    {
        public static async Task<string> GetProfilesAsync(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return "Unknown";

            string[] paths =
            {
                "/onvif/device_service",
                "/onvif/Device",
                "/onvif/device"
            };

            HashSet<string> profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                string url = "http://" + ip.Trim() + path;

                string services = await PostSoapAsync(url, GetServicesBody());
                AddProfilesFromText(services, profiles);

                string capabilities = await PostSoapAsync(url, GetCapabilitiesBody());
                AddProfilesFromText(capabilities, profiles);

                string mediaProfiles = await PostSoapAsync(url, GetProfilesBody());
                AddProfilesFromText(mediaProfiles, profiles);

                if (profiles.Count > 0)
                    break;
            }

            if (profiles.Count == 0)
                return "Unknown";

            List<string> ordered = new List<string>();
            AddIfPresent(profiles, ordered, "S");
            AddIfPresent(profiles, ordered, "T");
            AddIfPresent(profiles, ordered, "G");
            AddIfPresent(profiles, ordered, "M");
            AddIfPresent(profiles, ordered, "C");
            AddIfPresent(profiles, ordered, "Q");
            AddIfPresent(profiles, ordered, "A");

            foreach (string profile in profiles)
            {
                if (!ordered.Contains(profile))
                    ordered.Add(profile);
            }

            return "Profile " + string.Join(", ", ordered.ToArray());
        }

        private static async Task<string> PostSoapAsync(string url, string bodyXml)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMilliseconds(2500);
                    using (StringContent content = new StringContent(WrapSoap(bodyXml), Encoding.UTF8, "application/soap+xml"))
                    {
                        HttpResponseMessage response = await client.PostAsync(url, content);
                        return await response.Content.ReadAsStringAsync();
                    }
                }
            }
            catch
            {
                return "";
            }
        }

        private static string WrapSoap(string bodyXml)
        {
            return
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\">" +
                "<s:Body>" + bodyXml + "</s:Body>" +
                "</s:Envelope>";
        }

        private static string GetServicesBody()
        {
            return
                "<GetServices xmlns=\"http://www.onvif.org/ver10/device/wsdl\">" +
                "<IncludeCapability>true</IncludeCapability>" +
                "</GetServices>";
        }

        private static string GetCapabilitiesBody()
        {
            return
                "<GetCapabilities xmlns=\"http://www.onvif.org/ver10/device/wsdl\">" +
                "<Category>All</Category>" +
                "</GetCapabilities>";
        }

        private static string GetProfilesBody()
        {
            return "<GetProfiles xmlns=\"http://www.onvif.org/ver10/media/wsdl\"/>";
        }

        private static void AddProfilesFromText(string text, HashSet<string> profiles)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            AddIfContains(text, profiles, "profile/s", "S");
            AddIfContains(text, profiles, "profile/t", "T");
            AddIfContains(text, profiles, "profile/g", "G");
            AddIfContains(text, profiles, "profile/m", "M");
            AddIfContains(text, profiles, "profile/c", "C");
            AddIfContains(text, profiles, "profile/q", "Q");
            AddIfContains(text, profiles, "profile/a", "A");

            if (Regex.IsMatch(text, "ver10/(media|device|events)/wsdl", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, "trt:GetProfilesResponse|trt:Profiles|GetProfilesResponse", RegexOptions.IgnoreCase))
            {
                profiles.Add("S");
            }

            if (Regex.IsMatch(text, "ver20/media/wsdl|tr2:GetProfilesResponse|tr2:Profiles", RegexOptions.IgnoreCase))
                profiles.Add("T");

            if (Regex.IsMatch(text, "recording/wsdl|replay/wsdl|search/wsdl", RegexOptions.IgnoreCase))
                profiles.Add("G");

            if (Regex.IsMatch(text, "analytics/wsdl|ver20/analytics", RegexOptions.IgnoreCase))
                profiles.Add("M");
        }

        private static void AddIfContains(string text, HashSet<string> profiles, string needle, string profile)
        {
            if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                profiles.Add(profile);
        }

        private static void AddIfPresent(HashSet<string> profiles, List<string> ordered, string profile)
        {
            if (profiles.Contains(profile))
                ordered.Add(profile);
        }
    }
}
