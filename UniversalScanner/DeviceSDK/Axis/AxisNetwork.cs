using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace UniversalScanner.DeviceSDK.Axis
{
    public static class AxisNetwork
    {
        public static async Task<bool> ChangeNetworkAddressAsync(
            string currentIp,
            string username,
            string password,
            string newIp,
            string subnetMask,
            string gateway,
            bool enableDhcp,
            Action<string> statusCallback = null)
        {
            if (string.IsNullOrWhiteSpace(username))
                username = "root";

            string httpHost = UniversalScanner.AxisLinkLocalRegistry.GetHttpHost(currentIp) ?? currentIp;

            statusCallback?.Invoke("Checking Axis target IP...");
            if (!string.Equals(currentIp, newIp, StringComparison.OrdinalIgnoreCase))
            {
                NetworkTools.FlushArpEntry(newIp);
                bool targetResponds = await NetworkTools.IsOnlineAsync(newIp, 1000).ConfigureAwait(false);
                string targetMac = await NetworkTools.GetMacAsync(newIp, 2, 250).ConfigureAwait(false);
                string currentMac = await NetworkTools.GetMacAsync(currentIp, 2, 250).ConfigureAwait(false);

                if ((targetResponds || !string.IsNullOrWhiteSpace(targetMac)) &&
                    !MacEquals(targetMac, currentMac))
                {
                    throw new Exception("IP conflict detected. Requested IP " + newIp + " is already in use by " + (string.IsNullOrWhiteSpace(targetMac) ? "Unknown" : targetMac) + ".");
                }
            }

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                Credentials = new NetworkCredential(username, password),
                PreAuthenticate = true
            };

            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(10);

                string parameters = enableDhcp
                    ? "Network.BootProto=dhcp"
                    : "Network.BootProto=none" +
                      "&Network.IPAddress=" + Uri.EscapeDataString(newIp) +
                      "&Network.SubnetMask=" + Uri.EscapeDataString(subnetMask) +
                      "&Network.DefaultRouter=" + Uri.EscapeDataString(gateway);

                statusCallback?.Invoke("Applying Axis VAPIX network settings...");
                HttpResponseMessage updateResponse;
                try
                {
                    updateResponse = await client.GetAsync("http://" + httpHost + "/axis-cgi/param.cgi?action=update&" + parameters).ConfigureAwait(false);
                }
                catch (TaskCanceledException ex)
                {
                    throw new Exception("Axis IP change failed: network settings request timed out before the camera accepted it.", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception("Axis IP change failed: could not reach the camera to send network settings.", ex);
                }

                if (updateResponse.StatusCode == HttpStatusCode.Unauthorized)
                    throw new Exception("Axis IP change failed: authentication rejected.");

                if (!updateResponse.IsSuccessStatusCode)
                    throw new Exception("Axis IP change failed: camera returned HTTP " + (int)updateResponse.StatusCode + ".");

                string updateBody = await updateResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (AxisResponseHasError(updateBody))
                    throw new Exception("Axis IP change failed: " + SummarizeAxisResponse(updateBody));

                if (!string.Equals(currentIp, newIp, StringComparison.OrdinalIgnoreCase))
                    UniversalScanner.AxisLinkLocalRegistry.Move(currentIp, newIp);

                statusCallback?.Invoke("Restarting Axis camera to apply network settings...");
                try
                {
                    await client.GetAsync("http://" + httpHost + "/axis-cgi/admin/restart.cgi").ConfigureAwait(false);
                }
                catch
                {
                    try
                    {
                        await client.GetAsync("http://" + httpHost + "/axis-cgi/restart.cgi").ConfigureAwait(false);
                    }
                    catch
                    {
                        // The connection may drop during restart.
                    }
                }
            }

            statusCallback?.Invoke("Axis network settings accepted. Waiting for camera to come back online...");
            return true;
        }

        private static bool MacEquals(string a, string b)
        {
            string cleanA = NormalizeMac(a);
            string cleanB = NormalizeMac(b);
            return !string.IsNullOrWhiteSpace(cleanA) &&
                   !string.IsNullOrWhiteSpace(cleanB) &&
                   cleanA == cleanB;
        }

        private static string NormalizeMac(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value.Replace("-", "").Replace(":", "").Trim().ToUpperInvariant();
        }

        private static bool AxisResponseHasError(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SummarizeAxisResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "camera returned an empty response.";

            string oneLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length > 180 ? oneLine.Substring(0, 180) + "..." : oneLine;
        }
    }
}
