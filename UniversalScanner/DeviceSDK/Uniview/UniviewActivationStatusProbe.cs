using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace UniversalScanner.DeviceSDK.Uniview
{
    public static class UniviewActivationStatusProbe
    {
        private static readonly object CacheLock = new object();
        private static readonly System.Collections.Generic.Dictionary<string, CachedStatus> StatusCache =
            new System.Collections.Generic.Dictionary<string, CachedStatus>(StringComparer.OrdinalIgnoreCase);

        public static Task<string> GetStatusAsync(string ipAddress)
        {
            return Task.Run(() => GetStatus(ipAddress));
        }

        private static string GetStatus(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return "Unknown";

            string cached = TryGetCachedStatus(ipAddress);
            if (!string.IsNullOrWhiteSpace(cached))
                return cached;

            string[] endpoints =
            {
                "/LAPI/V1.0/System/DeviceInfo",
                "/LAPI/V1.0/System/Users/Users/0",
                "/LAPI/V1.0/Channel/0/System/DeviceInfo"
            };

            foreach (string endpoint in endpoints)
            {
                string url = "http://" + ipAddress + endpoint;

                string response;
                HttpStatusCode status;
                string error;

                if (!SendJson(url, "GET", "admin", "123456", out response, out status, out error))
                    continue;

                if (status == HttpStatusCode.Unauthorized || IsUnauthorized(response))
                    return CacheStatus(ipAddress, "Active", TimeSpan.FromMinutes(30));

                if (IsUserLocked(response))
                    return CacheStatus(ipAddress, "Active", TimeSpan.FromMinutes(10));

                if (status == HttpStatusCode.OK || status == HttpStatusCode.NoContent)
                {
                    if (RequiresInitialization(response) || IsLapiSuccess(response))
                        return CacheStatus(ipAddress, "Inactive", TimeSpan.FromMinutes(2));
                }
            }

            return "Unknown";
        }

        private static string TryGetCachedStatus(string ipAddress)
        {
            lock (CacheLock)
            {
                CachedStatus cached;
                if (!StatusCache.TryGetValue(ipAddress, out cached))
                    return null;

                if (cached.ExpiresAt <= DateTime.Now)
                {
                    StatusCache.Remove(ipAddress);
                    return null;
                }

                return cached.Status;
            }
        }

        public static void ClearCache(string ipAddress)
        {
            lock (CacheLock)
            {
                StatusCache.Remove(ipAddress);
            }
        }

        private static string CacheStatus(string ipAddress, string status, TimeSpan lifetime)
        {
            lock (CacheLock)
            {
                StatusCache[ipAddress] = new CachedStatus
                {
                    Status = status,
                    ExpiresAt = DateTime.Now.Add(lifetime)
                };
            }

            return status;
        }

        private static bool SendJson(
            string url,
            string method,
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
                request.Method = method;
                request.Timeout = 3500;
                request.ReadWriteTimeout = 3500;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.Accept = "application/json, text/plain, */*";
                request.UserAgent = "Mozilla/5.0";
                request.Credentials = new NetworkCredential(username, password);
                string basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes(username + ":" + password));
                request.Headers[HttpRequestHeader.Authorization] = "Basic " + basicToken;

                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        statusCode = response.StatusCode;
                        responseBody = reader.ReadToEnd();
                    }
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse)
                {
                    HttpWebResponse err = (HttpWebResponse)ex.Response;
                    statusCode = err.StatusCode;
                    try
                    {
                        using (StreamReader reader = new StreamReader(err.GetResponseStream()))
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
                error = ex.Message;
                return false;
            }
        }

        private static bool IsLapiSuccess(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            string compact = response.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
            return compact.IndexOf("\"ResponseCode\":0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   compact.IndexOf("\"StatusCode\":0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("Succeed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUnauthorized(string response)
        {
            return !string.IsNullOrWhiteSpace(response) &&
                   response.IndexOf("Not Authorized", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUserLocked(string response)
        {
            return !string.IsNullOrWhiteSpace(response) &&
                   response.IndexOf("UserLocked", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RequiresInitialization(string response)
        {
            return !string.IsNullOrWhiteSpace(response) &&
                   (response.IndexOf("privacy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    response.IndexOf("policy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    response.IndexOf("agreement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("change password", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private class CachedStatus
        {
            public string Status { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}
