using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace UniversalScanner.DeviceSDK.Hikvision
{
    public static class HikvisionActivationStatusProbe
    {
        public static async Task<string> GetStatusAsync(string ip)
        {
            return await Task.Run(() => GetStatus(ip));
        }

        public static string GetStatus(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return "Unknown";

            /*
             * Important:
             * Do NOT return Active immediately on 401.
             * Some inactive Hikvision/Honeywell devices still return 401 on ISAPI endpoints.
             * We first check web pages that expose the activation/password setup UI.
             * Only if no activation page is detected, 401/403 is treated as Active.
             *
             * Honeywell HC30/HC35 cameras typically redirect HTTP→HTTPS (self-signed cert).
             * We probe both HTTP and HTTPS, with SSL validation bypassed for cameras.
             */

            bool sawUnauthorized = false;
            bool sawForbidden    = false;
            bool sawActiveLogin  = false;

            string[] urls = new string[]
            {
                // HTTPS first — Honeywell / newer cameras redirect HTTP→HTTPS
                "https://" + ip + "/",
                "https://" + ip + "/ISAPI/Security/userCheck",
                "https://" + ip + "/ISAPI/System/deviceInfo",
                // Plain HTTP — older Hikvision cameras
                "http://"  + ip + "/",
                "http://"  + ip + "/doc/page/login.asp",
                "http://"  + ip + "/doc/page/config.asp",
                "http://"  + ip + "/ISAPI/Security/userCheck",
                "http://"  + ip + "/ISAPI/System/deviceInfo",
            };

            // Bypass SSL cert validation — cameras use self-signed certs
            System.Net.Security.RemoteCertificateValidationCallback prevCallback =
                ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            try
            {
                foreach (string url in urls)
                {
                    try
                    {
                        ProbeResponse response = ReadUrl(url);

                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            sawUnauthorized = true;
                            continue;
                        }

                        if (response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            sawForbidden = true;
                            continue;
                        }

                        // HTTP redirect (301/302): device is alive, likely redirecting to HTTPS
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        {
                            sawUnauthorized = true; // treat as "alive = probably active"
                            continue;
                        }

                        string body = response.Body ?? "";

                        if (LooksInactive(body))
                            return "Inactive";

                        if (LooksActive(body))
                            sawActiveLogin = true;
                    }
                    catch
                    {
                        // Unreachable endpoint — try next
                    }
                }
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = prevCallback;
            }

            if (sawActiveLogin)
                return "Active";

            if (sawUnauthorized || sawForbidden)
                return "Active";

            return "Unknown";
        }

        private static bool LooksInactive(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            return
                body.IndexOf("activate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("activation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("activated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("inactive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("not active", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("not activated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("set password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("new password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("confirm password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("password strength", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("security code", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksActive(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            return
                body.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("userCheck", StringComparison.OrdinalIgnoreCase) >= 0 ||
                body.IndexOf("Hikvision", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ProbeResponse ReadUrl(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 1200;
            request.ReadWriteTimeout = 1200;
            request.AllowAutoRedirect = false;
            request.UserAgent = "UniversalScanner";

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    return new ProbeResponse
                    {
                        StatusCode = response.StatusCode,
                        Body = reader.ReadToEnd()
                    };
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;

                if (response != null)
                {
                    string body = "";

                    try
                    {
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            body = reader.ReadToEnd();
                        }
                    }
                    catch
                    {
                    }

                    return new ProbeResponse
                    {
                        StatusCode = response.StatusCode,
                        Body = body
                    };
                }

                throw;
            }
        }

        private class ProbeResponse
        {
            public HttpStatusCode StatusCode { get; set; }
            public string Body { get; set; }
        }
    }
}
