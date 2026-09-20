using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    /// <summary>
    /// Honeywell-specific activation status probe. Honeywell HC30/HC35 cameras can
    /// expose Honeywell-branded pages and unauthenticated ISAPI-style endpoints even
    /// after password setup, so this probe is intentionally conservative:
    ///   - Active is shown when Universal Scanner activated the camera, or when
    ///     the camera exposes the normal Honeywell login page.
    ///   - Inactive is shown only when the camera exposes the first-time password
    ///     setup handshake and explicit password-setup markers.
    ///   - Everything else is Unknown.
    /// </summary>
    public static class HoneywellActivationStatusProbe
    {
        public static async Task<string> GetStatusAsync(string ip)
        {
            return await Task.Run(() => GetStatus(ip));
        }

        public static async Task<bool> HasActivationEndpointAsync(string ip)
        {
            return await Task.Run(() => HasActivationEndpoint(ip));
        }

        public static bool HasActivationEndpoint(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            SecurityProtocolType prevProtocol = ServicePointManager.SecurityProtocol;
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls;

            System.Net.Security.RemoteCertificateValidationCallback prevCallback =
                ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            try
            {
                string jsonState = TryGetState(ip);
                return HasPasswordSetupHandshake(jsonState);
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = prevCallback;
                ServicePointManager.SecurityProtocol = prevProtocol;
            }
        }

        public static string GetStatus(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return "Unknown";

            if (HoneywellActivationCache.IsMarkedActive(ip))
                return "Active";

            SecurityProtocolType prevProtocol = ServicePointManager.SecurityProtocol;
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls;

            System.Net.Security.RemoteCertificateValidationCallback prevCallback =
                ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            try
            {
                string jsonState = TryGetState(ip);

                if (IsConfirmedFirstTimePasswordSetup(jsonState))
                    return "Inactive";

                string loginPage = TryGetLoginPage(ip);

                if (IsNormalLoginPage(loginPage))
                    return "Active";
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = prevCallback;
                ServicePointManager.SecurityProtocol = prevProtocol;
            }

            return "Unknown";
        }

        private static string TryGetLoginPage(string ip)
        {
            string[] urls = new string[]
            {
                "https://" + ip + "/",
                "http://"  + ip + "/"
            };

            foreach (string url in urls)
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "GET";
                    request.Timeout = 4000;
                    request.ReadWriteTimeout = 4000;
                    request.AllowAutoRedirect = true;
                    request.KeepAlive = false;
                    request.UserAgent = "Mozilla/5.0";
                    request.ServicePoint.Expect100Continue = false;

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        return reader.ReadToEnd();
                }
                catch (WebException ex)
                {
                    HttpWebResponse errResponse = ex.Response as HttpWebResponse;
                    if (errResponse == null)
                        continue;

                    try
                    {
                        using (StreamReader reader = new StreamReader(errResponse.GetResponseStream()))
                            return reader.ReadToEnd();
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }
                catch
                {
                    // Try the next transport.
                }
            }

            return null;
        }

        /// <summary>
        /// POSTs the proprietary {"cmd":"getState"} probe to the camera's
        /// WEB_JsonAjaxAuth endpoint. Returns the response body on any HTTP
        /// reply (2xx or 4xx) or null if the endpoint is unreachable.
        /// </summary>
        private static string TryGetState(string ip)
        {
            byte[] body = Encoding.UTF8.GetBytes("{\"cmd\":\"getState\"}");

            string[] urls = new string[]
            {
                "https://" + ip + "/action/WEB_JsonAjaxAuth",
                "http://"  + ip + "/action/WEB_JsonAjaxAuth"
            };

            foreach (string url in urls)
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "POST";
                    request.ContentType = "application/xml";
                    request.Accept = "application/json, text/javascript, */*; q=0.01";
                    request.ContentLength = body.Length;
                    request.Timeout = 3000;
                    request.ReadWriteTimeout = 3000;
                    request.AllowAutoRedirect = false;
                    request.KeepAlive = false;
                    request.UserAgent = "Mozilla/5.0";
                    request.Headers["Origin"] = url.Substring(0, url.IndexOf("/action/"));
                    request.Headers["X-Requested-With"] = "XMLHttpRequest";
                    request.ServicePoint.Expect100Continue = false;

                    using (Stream stream = request.GetRequestStream())
                        stream.Write(body, 0, body.Length);

                    try
                    {
                        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                            return reader.ReadToEnd();
                    }
                    catch (WebException ex)
                    {
                        HttpWebResponse errResponse = ex.Response as HttpWebResponse;
                        if (errResponse == null)
                            continue;

                        try
                        {
                            using (StreamReader reader = new StreamReader(errResponse.GetResponseStream()))
                                return reader.ReadToEnd();
                        }
                        catch
                        {
                            return string.Empty;
                        }
                    }
                }
                catch
                {
                    // Try the next transport.
                }
            }

            return null;
        }

        private static bool IsConfirmedFirstTimePasswordSetup(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            if (!HasPasswordSetupHandshake(body))
                return false;

            // On some activated HC30/HC35 firmware, getState can still return
            // handshake fields. Do not call that Inactive unless the response also
            // names the first-time password creation/setup flow.
            return body.IndexOf("createPassword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("passwordSetup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("setPassword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("initialization", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("firstTime", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasPasswordSetupHandshake(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            return body.IndexOf("PublicKey", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   body.IndexOf("Nonce", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeHoneywellPage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            return body.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("HC30", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("HC35", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("Network Camera", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeFirstTimeSetup(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            return body.IndexOf("createPassword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("passwordSetup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("setPassword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("initialization", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("firstTime", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("new password", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsNormalLoginPage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            bool honeywellPage =
                body.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0 &&
                body.IndexOf("Network Camera", StringComparison.OrdinalIgnoreCase) >= 0;

            bool loginForm =
                body.IndexOf("User name", StringComparison.OrdinalIgnoreCase) >= 0 &&
                body.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0 &&
                body.IndexOf("LOGIN", StringComparison.OrdinalIgnoreCase) >= 0;

            bool setupPage =
                body.IndexOf("createPassword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("passwordSetup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("setPassword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("initialization", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("firstTime", StringComparison.OrdinalIgnoreCase) >= 0;

            return honeywellPage && loginForm && !setupPage;
        }
    }
}
