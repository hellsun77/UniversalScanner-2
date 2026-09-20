using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    public static class HoneywellDebugProbe
    {
        public static Task<string> BuildReportAsync(string ipAddress)
        {
            return Task.Run(() => BuildReport(ipAddress));
        }

        private static string BuildReport(string ipAddress)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("Honeywell read-only debug report");
            report.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("IP: " + (ipAddress ?? ""));
            report.AppendLine("Note: this report only sends HTTP GET requests and read-only activation/status probes.");
            report.AppendLine();

            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                report.AppendLine("IP address is empty.");
                return report.ToString();
            }

            SecurityProtocolType prevProtocol = ServicePointManager.SecurityProtocol;
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls;

            System.Net.Security.RemoteCertificateValidationCallback prevCallback =
                ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            bool prevExpect100 = ServicePointManager.Expect100Continue;
            ServicePointManager.Expect100Continue = false;

            try
            {
                AppendGet(report, "HTTP root", "http://" + ipAddress + "/");
                AppendGet(report, "HTTPS root", "https://" + ipAddress + "/");
                AppendGet(report, "HTTP ISAPI deviceInfo", "http://" + ipAddress + "/ISAPI/System/deviceInfo");
                AppendGet(report, "HTTPS ISAPI deviceInfo", "https://" + ipAddress + "/ISAPI/System/deviceInfo");
                AppendGet(report, "HTTP Dahua magicBox systemInfo", "http://" + ipAddress + "/cgi-bin/magicBox.cgi?action=getSystemInfo");
                AppendPost(report, "HTTP Honeywell getState", "http://" + ipAddress + "/action/WEB_JsonAjaxAuth", "{\"cmd\":\"getState\"}");
                AppendPost(report, "HTTPS Honeywell getState", "https://" + ipAddress + "/action/WEB_JsonAjaxAuth", "{\"cmd\":\"getState\"}");
                AppendDahuaPost(report, "HTTP Dahua DevInit.getStatus", "http://" + ipAddress + "/OutsideCmd", "{\"method\":\"DevInit.getStatus\",\"params\":null,\"id\":1,\"session\":0}");
                AppendDahuaPost(report, "HTTPS Dahua DevInit.getStatus", "https://" + ipAddress + "/OutsideCmd", "{\"method\":\"DevInit.getStatus\",\"params\":null,\"id\":2,\"session\":0}");
                AppendDahuaPost(report, "HTTP Dahua Security.getEncryptInfo", "http://" + ipAddress + "/OutsideCmd", "{\"method\":\"Security.getEncryptInfo\",\"params\":null,\"id\":3,\"session\":0}");
                AppendDahuaPost(report, "HTTPS Dahua Security.getEncryptInfo", "https://" + ipAddress + "/OutsideCmd", "{\"method\":\"Security.getEncryptInfo\",\"params\":null,\"id\":4,\"session\":0}");
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = prevCallback;
                ServicePointManager.SecurityProtocol = prevProtocol;
                ServicePointManager.Expect100Continue = prevExpect100;
            }

            return report.ToString();
        }

        private static void AppendGet(StringBuilder report, string label, string url)
        {
            AppendRequest(report, label, url, "GET", null);
        }

        private static void AppendPost(StringBuilder report, string label, string url, string body)
        {
            AppendRequest(report, label, url, "POST", body, "application/xml");
        }

        private static void AppendDahuaPost(StringBuilder report, string label, string url, string body)
        {
            AppendRequest(report, label, url, "POST", body, "application/x-www-form-urlencoded; charset=UTF-8");
        }

        private static void AppendRequest(StringBuilder report, string label, string url, string method, string requestBody)
        {
            AppendRequest(report, label, url, method, requestBody, null);
        }

        private static void AppendRequest(StringBuilder report, string label, string url, string method, string requestBody, string contentType)
        {
            report.AppendLine("==== " + label + " ====");
            report.AppendLine(method + " " + url);

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = method;
                request.Timeout = 5000;
                request.ReadWriteTimeout = 5000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.UserAgent = "Mozilla/5.0";
                request.ServicePoint.Expect100Continue = false;

                if (requestBody != null)
                {
                    byte[] bodyBytes = Encoding.UTF8.GetBytes(requestBody);
                    request.ContentType = contentType ?? "text/plain";
                    request.Accept = "application/json, text/javascript, */*; q=0.01";
                    request.ContentLength = bodyBytes.Length;
                    Uri uri = new Uri(url);
                    string origin = uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString());
                    request.Headers["Origin"] = origin;
                    request.Referer = origin + "/";
                    request.Headers["X-Requested-With"] = "XMLHttpRequest";

                    using (Stream stream = request.GetRequestStream())
                        stream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (HttpWebResponse response = GetResponseIncludingErrors(request))
                {
                    report.AppendLine("Status: HTTP " + (int)response.StatusCode + " " + response.StatusDescription);
                    report.AppendLine("Headers:");
                    report.AppendLine(response.Headers.ToString().TrimEnd());
                    report.AppendLine("Body:");
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        report.AppendLine(Truncate(reader.ReadToEnd(), 12000));
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("Error: " + ex.GetType().Name + ": " + ex.Message);
            }

            report.AppendLine();
        }

        private static HttpWebResponse GetResponseIncludingErrors(HttpWebRequest request)
        {
            try
            {
                return (HttpWebResponse)request.GetResponse();
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse)
            {
                return (HttpWebResponse)ex.Response;
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? "";

            return value.Substring(0, maxLength) + "\r\n[truncated]";
        }
    }
}
