using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UniversalScanner.DeviceSDK.Axis
{
    public static class AxisActivationStatusProbe
    {
        public static async Task<string> GetStatusAsync(string ip)
        {
            string httpHost = UniversalScanner.AxisLinkLocalRegistry.GetHttpHost(ip) ?? ip;

            try
            {
                string body = "{\"apiVersion\":\"1.1\",\"context\":\"UniversalScanner\",\"method\":\"systemready\",\"params\":{\"timeout\":1}}";
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(4);
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync("http://" + httpHost + "/axis-cgi/systemready.cgi", content).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        string needSetup = ExtractJsonString(text, "needsetup");
                        if (string.Equals(needSetup, "yes", StringComparison.OrdinalIgnoreCase))
                            return "Inactive - Password setup required";

                        if (string.Equals(needSetup, "no", StringComparison.OrdinalIgnoreCase))
                            return "Active";

                        if (text.IndexOf("\"systemready\"", StringComparison.OrdinalIgnoreCase) >= 0)
                            return "Unknown";
                    }
                }
            }
            catch
            {
                // Fall back to a normal authenticated endpoint probe below.
            }

            try
            {
                var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(4);
                    var response = await client.GetAsync("http://" + httpHost + "/axis-cgi/param.cgi?action=list&group=Properties.System").ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        return "Active";

                    if (response.IsSuccessStatusCode)
                        return "Active";
                }
            }
            catch
            {
                return "Unknown";
            }

            return "Unknown";
        }

        private static string ExtractJsonString(string json, string field)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(field))
                return "";

            Match match = Regex.Match(
                json,
                "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"([^\"]*)\"",
                RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
    }
}
