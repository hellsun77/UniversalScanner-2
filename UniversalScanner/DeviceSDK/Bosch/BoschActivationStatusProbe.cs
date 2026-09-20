using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace UniversalScanner.DeviceSDK.Bosch
{
    public static class BoschActivationStatusProbe
    {
        public static async Task<string> GetStatusAsync(string ip)
        {
            try
            {
                var httpHandler = new HttpClientHandler { AllowAutoRedirect = false };
                using (HttpClient client = new HttpClient(httpHandler))
                {
                    client.Timeout = TimeSpan.FromSeconds(3);

                    try
                    {
                        string status = await GetStatusFromHttpAsync(client, "http://" + ip + "/");
                        if (!string.IsNullOrWhiteSpace(status))
                            return status;
                    }
                    catch
                    {
                        // Try HTTPS below.
                    }

                    var httpsHandler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                        AllowAutoRedirect = false
                    };

                    using (HttpClient httpsClient = new HttpClient(httpsHandler))
                    {
                        httpsClient.Timeout = TimeSpan.FromSeconds(3);
                        string status = await GetStatusFromHttpAsync(httpsClient, "https://" + ip + "/");
                        if (!string.IsNullOrWhiteSpace(status))
                            return status;
                    }
                }
            }
            catch
            {
                return "Unknown";
            }

            return "Unknown";
        }

        private static async Task<string> GetStatusFromHttpAsync(HttpClient client, string url)
        {
            var response = await client.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                response.StatusCode == System.Net.HttpStatusCode.SeeOther ||
                response.StatusCode == System.Net.HttpStatusCode.RedirectMethod ||
                response.StatusCode == System.Net.HttpStatusCode.Found ||
                response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
            {
                if (response.Headers.Location != null &&
                    response.Headers.Location.OriginalString.IndexOf("setpwd.htm", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Inactive - Password setup required";
                }
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return "Active";

            if (response.IsSuccessStatusCode)
            {
                string html = await response.Content.ReadAsStringAsync();
                if (html.IndexOf("nopwd_header", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    html.IndexOf("id=\"setpwd\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    html.IndexOf("setpwd.htm", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Inactive - Password setup required";
                }

                return "Active";
            }

            return "";
        }
    }
}
