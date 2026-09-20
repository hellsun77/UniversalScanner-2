using System;
using System.IO;
using System.Net;
using System.Text;

namespace UniversalScanner.DeviceSDK.Dahua
{
    public static class DahuaActivationStatusProbe
    {
        public static string GetStatus(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return "Unknown";

            StringBuilder debug = new StringBuilder();
            debug.AppendLine("Dahua activation status debug");
            debug.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            debug.AppendLine("IP: " + ipAddress);
            debug.AppendLine();

            string body;
            HttpStatusCode status;
            string error;
            if (!TryGet("http://" + ipAddress + "/", out body, out status, out error))
                return Finish(ipAddress, debug, "Unknown (Dahua HTTP: " + ShortError(error) + ")", "root GET", status, body, error);

            AppendDebug(debug, "root GET", "http://" + ipAddress + "/", status, body, null);

            if (ContainsRegionSetupMarker(body))
                return Finish(ipAddress, debug, "Setup Required (region)", null, 0, null, null);

            if (ContainsInactiveMarker(body))
                return Finish(ipAddress, debug, "Inactive (Dahua web init; root HTTP " + (int)status + ")", null, 0, null, null);

            string cgiBody;
            HttpStatusCode cgiStatus;
            string cgiError;
            if (TryGet("http://" + ipAddress + "/cgi-bin/magicBox.cgi?action=getSystemInfo", out cgiBody, out cgiStatus, out cgiError))
            {
                AppendDebug(debug, "CGI getSystemInfo", "http://" + ipAddress + "/cgi-bin/magicBox.cgi?action=getSystemInfo", cgiStatus, cgiBody, null);

                if (cgiStatus == HttpStatusCode.Unauthorized || cgiStatus == HttpStatusCode.Forbidden)
                    return Finish(ipAddress, debug, "Active", null, 0, null, null);

                if (cgiStatus == HttpStatusCode.OK && !ContainsInactiveMarker(cgiBody) && !ContainsRejectedMarker(cgiBody))
                    return Finish(ipAddress, debug, "Active", null, 0, null, null);

                if (ContainsInactiveMarker(cgiBody))
                    return Finish(ipAddress, debug, "Inactive (Dahua CGI init)", null, 0, null, null);

                return Finish(ipAddress, debug, "Unknown (root HTTP " + (int)status + "; CGI HTTP " + (int)cgiStatus + "; " + BodySnippet(cgiBody) + ")", null, 0, null, null);
            }

            AppendDebug(debug, "CGI getSystemInfo", "http://" + ipAddress + "/cgi-bin/magicBox.cgi?action=getSystemInfo", cgiStatus, cgiBody, cgiError);

            if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
                return Finish(ipAddress, debug, "Active", null, 0, null, null);

            if (body != null &&
                (body.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 body.IndexOf("Dahua", StringComparison.OrdinalIgnoreCase) >= 0))
                return Finish(ipAddress, debug, "Unknown (root HTTP " + (int)status + " login; CGI " + ShortError(cgiError) + ")", null, 0, null, null);

            return Finish(ipAddress, debug, "Unknown (root HTTP " + (int)status + "; " + BodySnippet(body) + ")", null, 0, null, null);
        }

        private static bool TryGet(string url, out string body, out HttpStatusCode status, out string error)
        {
            body = "";
            status = 0;
            error = "";

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 3500;
                request.ReadWriteTimeout = 3500;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.UserAgent = "Mozilla/5.0";

                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        status = response.StatusCode;
                        body = reader.ReadToEnd();
                    }
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse)
                {
                    HttpWebResponse response = (HttpWebResponse)ex.Response;
                    status = response.StatusCode;
                    try
                    {
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                            body = reader.ReadToEnd();
                    }
                    catch
                    {
                    }
                }

                return true;
            }
            catch (WebException ex)
            {
                error = ex.Status + ": " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool ContainsInactiveMarker(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            return body.IndexOf("DeviceInit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("InitPwd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("init password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("initialize", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("activation", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsRegionSetupMarker(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            return body.IndexOf("Country/Region", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("Country\\/Region", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("Region Setting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("Video Standard", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsRejectedMarker(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            return body.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("Forbidden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("Bad Request", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ShortError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return "no response";

            if (error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                return "timeout";

            if (error.Length <= 32)
                return error;

            return error.Substring(0, 32);
        }

        private static string BodySnippet(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "empty body";

            string compact = body.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (compact.IndexOf("  ", StringComparison.Ordinal) >= 0)
                compact = compact.Replace("  ", " ");

            compact = compact.Trim();
            if (compact.Length <= 80)
                return compact;

            return compact.Substring(0, 80);
        }

        private static string Finish(
            string ipAddress,
            StringBuilder debug,
            string result,
            string label,
            HttpStatusCode status,
            string body,
            string error)
        {
            if (!string.IsNullOrWhiteSpace(label))
                AppendDebug(debug, label, "", status, body, error);

            debug.AppendLine();
            debug.AppendLine("Result: " + result);
            DahuaDebugLog.WriteStatus(ipAddress, debug.ToString());
            return result;
        }

        private static void AppendDebug(
            StringBuilder debug,
            string label,
            string url,
            HttpStatusCode status,
            string body,
            string error)
        {
            if (debug == null)
                return;

            debug.Append(label);
            if (!string.IsNullOrWhiteSpace(url))
                debug.Append(" ").Append(url);

            debug.Append(" -> ");

            if ((int)status != 0)
                debug.Append("HTTP ").Append((int)status);
            else
                debug.Append("no HTTP status");

            if (!string.IsNullOrWhiteSpace(error))
                debug.Append(", error=").Append(CompactLine(error, 220));

            if (!string.IsNullOrWhiteSpace(body))
                debug.Append(", body=").Append(CompactLine(body, 700));

            debug.AppendLine();
        }

        private static string CompactLine(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            string compact = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (compact.IndexOf("  ", StringComparison.Ordinal) >= 0)
                compact = compact.Replace("  ", " ");

            compact = compact.Trim();
            if (compact.Length <= max)
                return compact;

            return compact.Substring(0, max) + "...";
        }
    }
}
