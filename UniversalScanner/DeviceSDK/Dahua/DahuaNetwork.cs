using System;
using System.Net;
using System.Threading.Tasks;
using UniversalScanner.DeviceSDK.Common;

namespace UniversalScanner.DeviceSDK.Dahua
{
    public static class DahuaNetwork
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
            statusCallback?.Invoke("Trying Dahua ONVIF network configuration...");

            bool ok = await GenericOnvifNetwork.ChangeNetworkAddressAsync(
                currentIp,
                username,
                password,
                newIp,
                subnetMask,
                gateway,
                enableDhcp,
                statusCallback);

            if (ok)
                return await VerifyChangeAsync(currentIp, newIp, statusCallback);

            statusCallback?.Invoke("Trying Dahua CGI network configuration...");

            ok = await Task.Run(() =>
                ChangeViaCgi(currentIp, username, password, newIp, subnetMask, gateway, enableDhcp, statusCallback));

            if (!ok)
                return false;

            return await VerifyChangeAsync(currentIp, newIp, statusCallback);
        }

        private static async Task<bool> VerifyChangeAsync(
            string oldIp,
            string newIp,
            Action<string> statusCallback)
        {
            if (string.IsNullOrWhiteSpace(newIp) ||
                string.Equals(oldIp, newIp, StringComparison.OrdinalIgnoreCase))
                return true;

            statusCallback?.Invoke("Verifying Dahua IP change...");

            for (int attempt = 1; attempt <= 10; attempt++)
            {
                await Task.Delay(2000);

                bool newReachable = await NetworkTools.IsDeviceReachableAsync(newIp, 900);
                if (newReachable)
                {
                    statusCallback?.Invoke("Dahua answered at the new IP address.");
                    return true;
                }

                bool oldReachable = await NetworkTools.IsDeviceReachableAsync(oldIp, 900);
                if (!oldReachable)
                {
                    statusCallback?.Invoke("Dahua stopped answering at the old IP address.");
                    return true;
                }

                statusCallback?.Invoke("Dahua still answers at old IP (" + attempt + "/10)...");
            }

            statusCallback?.Invoke("Dahua accepted the request, but still answers at the old IP. IP change was not confirmed.");
            return false;
        }

        private static bool ChangeViaCgi(
            string currentIp,
            string username,
            string password,
            string newIp,
            string subnetMask,
            string gateway,
            bool enableDhcp,
            Action<string> statusCallback)
        {
            try
            {
                string query =
                    "action=setConfig" +
                    "&Network.eth0.DhcpEnable=" + (enableDhcp ? "true" : "false");

                if (!enableDhcp)
                {
                    query +=
                        "&Network.eth0.IPAddress=" + Uri.EscapeDataString(newIp) +
                        "&Network.eth0.SubnetMask=" + Uri.EscapeDataString(subnetMask) +
                        "&Network.eth0.DefaultGateway=" + Uri.EscapeDataString(gateway);
                }

                string response;
                HttpStatusCode status;
                string error;
                if (!SendGet(
                        "http://" + currentIp + "/cgi-bin/configManager.cgi?" + query,
                        username,
                        password,
                        out response,
                        out status,
                        out error))
                {
                    statusCallback?.Invoke("Dahua CGI IP change failed: " + error);
                    return false;
                }

                if (!IsSuccess(status, response))
                {
                    statusCallback?.Invoke("Dahua CGI rejected IP change: " + Truncate(response, 160));
                    return false;
                }

                statusCallback?.Invoke("Rebooting Dahua device...");
                SendGet("http://" + currentIp + "/cgi-bin/magicBox.cgi?action=reboot", username, password, out response, out status, out error);
                return true;
            }
            catch (Exception ex)
            {
                statusCallback?.Invoke("Dahua CGI IP change failed: " + ex.Message);
                return false;
            }
        }

        private static bool SendGet(
            string url,
            string username,
            string password,
            out string responseBody,
            out HttpStatusCode statusCode,
            out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.UserAgent = "Mozilla/5.0";
                request.Credentials = new NetworkCredential(username ?? "admin", password ?? "");
                request.PreAuthenticate = false;

                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream()))
                    {
                        statusCode = response.StatusCode;
                        responseBody = reader.ReadToEnd();
                    }
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse)
                {
                    HttpWebResponse response = (HttpWebResponse)ex.Response;
                    statusCode = response.StatusCode;
                    try
                    {
                        using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream()))
                            responseBody = reader.ReadToEnd();
                    }
                    catch
                    {
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool IsSuccess(HttpStatusCode status, string response)
        {
            if ((int)status < 200 || (int)status >= 300)
                return false;

            if (string.IsNullOrWhiteSpace(response))
                return true;

            return response.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }
    }
}
