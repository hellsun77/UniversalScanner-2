using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace UniversalScanner.DeviceSDK.Axis
{
    public static class AxisMotionApp
    {
        private static readonly string[] MotionAppHints =
        {
            "axis video motion detection",
            "video motion detection",
            "motion detection",
            "vmd"
        };

        public static async Task<AxisMotionAppResult> StartMotionDetectionAsync(
            string ipAddress,
            string username,
            string password)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return AxisMotionAppResult.Failure("Axis motion app start failed: IP address is empty.");

            if (string.IsNullOrWhiteSpace(username))
                username = "root";

            if (string.IsNullOrWhiteSpace(password))
                return AxisMotionAppResult.Failure("Axis motion app start failed: password is empty.");

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                Credentials = new NetworkCredential(username, password),
                PreAuthenticate = true
            };

            using (var client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(12);

                var app = await FindMotionAppAsync(client, ipAddress).ConfigureAwait(false);
                if (app == null)
                {
                    return AxisMotionAppResult.Failure(
                        "Axis motion detection app was not found on " + ipAddress + ". It may not be installed on this firmware.");
                }

                if (IsRunning(app.Status))
                {
                    return AxisMotionAppResult.Ok(
                        "Axis motion detection app is already running on " + ipAddress + ".");
                }

                string controlUrl = "http://" + ipAddress +
                                    "/axis-cgi/applications/control.cgi?action=start&package=" +
                                    Uri.EscapeDataString(app.Name);

                using (var content = new StringContent(string.Empty))
                using (var response = await client.PostAsync(controlUrl, content).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        return AxisMotionAppResult.Failure("Axis motion app start failed: username or password was rejected.");

                    if (response.IsSuccessStatusCode && IsAxisOk(body))
                    {
                        return AxisMotionAppResult.Ok(
                            "Axis motion detection app started on " + ipAddress + ".");
                    }

                    if (body.IndexOf("Error: 6", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return AxisMotionAppResult.Ok(
                            "Axis motion detection app is already running on " + ipAddress + ".");
                    }

                    if (body.IndexOf("Error: 4", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return AxisMotionAppResult.Failure(
                            "Axis motion app start failed: the camera reported that the app package was not found.");
                    }

                    return AxisMotionAppResult.Failure(
                        "Axis motion app start failed: camera returned HTTP " +
                        (int)response.StatusCode + " " + response.ReasonPhrase +
                        FormatBody(body) + ".");
                }
            }
        }

        private static async Task<AxisApplicationInfo> FindMotionAppAsync(HttpClient client, string ipAddress)
        {
            string listUrl = "http://" + ipAddress + "/axis-cgi/applications/list.cgi";
            using (var content = new StringContent(string.Empty))
            using (var response = await client.PostAsync(listUrl, content).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new UnauthorizedAccessException("Axis application list failed: username or password was rejected.");

                if (!response.IsSuccessStatusCode)
                    throw new Exception("Axis application list failed: camera returned HTTP " + (int)response.StatusCode + ".");

                XDocument doc = XDocument.Parse(body);
                return doc.Descendants()
                    .Where(element => string.Equals(element.Name.LocalName, "application", StringComparison.OrdinalIgnoreCase))
                    .Select(element => new AxisApplicationInfo
                    {
                        Name = AttributeValue(element, "Name"),
                        NiceName = AttributeValue(element, "NiceName"),
                        Status = AttributeValue(element, "Status")
                    })
                    .Where(app => !string.IsNullOrWhiteSpace(app.Name))
                    .FirstOrDefault(IsMotionApp);
            }
        }

        private static bool IsMotionApp(AxisApplicationInfo app)
        {
            string combined = ((app.Name ?? "") + " " + (app.NiceName ?? "")).ToLowerInvariant();
            return MotionAppHints.Any(hint => combined.Contains(hint));
        }

        private static bool IsRunning(string status)
        {
            return string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAxisOk(string body)
        {
            return string.Equals((body ?? "").Trim(), "OK", StringComparison.OrdinalIgnoreCase);
        }

        private static string AttributeValue(XElement element, string name)
        {
            var attr = element.Attribute(name);
            return attr == null ? "" : attr.Value;
        }

        private static string FormatBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "";

            string singleLine = body.Replace("\r", " ").Replace("\n", " ").Trim();
            if (singleLine.Length > 160)
                singleLine = singleLine.Substring(0, 160) + "...";

            return ": " + singleLine;
        }

        private sealed class AxisApplicationInfo
        {
            public string Name { get; set; }
            public string NiceName { get; set; }
            public string Status { get; set; }
        }
    }

    public sealed class AxisMotionAppResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }

        public static AxisMotionAppResult Ok(string message)
        {
            return new AxisMotionAppResult { Success = true, Message = message };
        }

        public static AxisMotionAppResult Failure(string message)
        {
            return new AxisMotionAppResult { Success = false, Message = message };
        }
    }
}
