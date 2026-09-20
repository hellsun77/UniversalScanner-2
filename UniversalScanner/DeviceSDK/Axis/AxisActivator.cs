using System;
using System.Net;
using System.Net.Http;

namespace UniversalScanner.DeviceSDK.Axis
{
    public static class AxisActivator
    {
        public static bool ActivateDevice(string ip, string username, string password, out string message)
        {
            if (string.IsNullOrWhiteSpace(username))
                username = "root";

            if (string.IsNullOrWhiteSpace(password))
            {
                message = "Axis password is required.";
                return false;
            }

            try
            {
                string httpHost = UniversalScanner.AxisLinkLocalRegistry.GetHttpHost(ip) ?? ip;
                string url = "http://" + httpHost + "/axis-cgi/pwdgrp.cgi" +
                    "?action=add" +
                    "&user=" + Uri.EscapeDataString(username) +
                    "&pwd=" + Uri.EscapeDataString(password) +
                    "&grp=" + Uri.EscapeDataString(username.Equals("root", StringComparison.OrdinalIgnoreCase) ? "root" : "users") +
                    "&sgrp=admin:operator:viewer:ptz" +
                    "&comment=UniversalScanner" +
                    "&strict_pwd=1";

                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    Credentials = new NetworkCredential(username, password)
                };

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(8);
                    var response = client.GetAsync(url).GetAwaiter().GetResult();
                    string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (response.IsSuccessStatusCode &&
                        (text.IndexOf("Created account", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("Modified account", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         text.IndexOf("Modified the account", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        message = "Axis device activated successfully.";
                        return true;
                    }

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        if (IsLoginAccepted(ip, username, password))
                        {
                            message = "Axis device is already active.";
                            return true;
                        }

                        message = "Axis activation failed: authentication was requested. The password may be incorrect, or the camera may already be active with another password.";
                        return false;
                    }

                    message = "Axis activation failed: device returned HTTP " + (int)response.StatusCode + ". " + text;
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = "Axis activation failed: " + ex.Message;
                return false;
            }
        }

        private static bool IsLoginAccepted(string ip, string username, string password)
        {
            try
            {
                string httpHost = UniversalScanner.AxisLinkLocalRegistry.GetHttpHost(ip) ?? ip;
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    Credentials = new NetworkCredential(username, password)
                };

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(6);
                    var response = client.GetAsync("http://" + httpHost + "/axis-cgi/param.cgi?action=list&group=Properties.System").GetAwaiter().GetResult();
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
