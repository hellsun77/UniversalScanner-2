using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UniversalScanner.DeviceSDK.Honeywell;
using UniversalScanner.DeviceSDK.Sunell;
using UniversalScanner.DeviceSDK.Tiandy;

namespace UniversalScanner.DeviceSDK.Common
{
    /// <summary>
    /// Extracts firmware version and release date directly from devices.
    /// No internet connections — data comes only from the device itself.
    /// Each vendor path reuses the same endpoints and auth mechanisms already
    /// used by the activation status probes.
    /// </summary>
    public static class DeviceFirmwareProbe
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, FirmwareResult> ResultCache =
            new Dictionary<string, FirmwareResult>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CredentialInfo> CredentialCache =
            new Dictionary<string, CredentialInfo>(StringComparer.OrdinalIgnoreCase);

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        public class FirmwareResult
        {
            public string FirmwareVersion { get; set; } = "";
            public string ReleaseDate { get; set; } = "";
            public string RawFirmwareString { get; set; } = "";
            public string RawReleaseDate { get; set; } = "";
        }

        public static void RegisterCredentials(string ip, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return;

            lock (CacheLock)
            {
                CredentialCache[ip.Trim()] = new CredentialInfo
                {
                    Username = username.Trim(),
                    Password = password
                };
            }
        }

        /// <summary>
        /// Try ONVIF GetDeviceInformation first (works for most ONVIF-compliant cameras).
        /// Requires username/password. If credentials are not known, ONVIF is skipped
        /// and we fall through to unauthenticated vendor endpoints.
        /// </summary>
        public static async Task<FirmwareResult> GetFirmwareAsync(
            string ip,
            string vendorIdentity,
            string username = null,
            string password = null)
        {
            FirmwareResult result = new FirmwareResult();

            if (string.IsNullOrWhiteSpace(ip))
                return result;

            CredentialInfo knownCredentials = TryGetRegisteredCredentials(ip);
            if (knownCredentials != null)
            {
                username = knownCredentials.Username;
                password = knownCredentials.Password;
            }
            else if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                string universalUsername;
                string universalPassword;
                UniversalLoginManager.GetUniversalCredentials(out universalUsername, out universalPassword);
                if (!string.IsNullOrWhiteSpace(universalUsername) && !string.IsNullOrWhiteSpace(universalPassword))
                {
                    username = universalUsername;
                    password = universalPassword;
                }
            }

            if (string.IsNullOrWhiteSpace(username))
                username = "admin";
            if (string.IsNullOrWhiteSpace(password))
                password = "admin";

            string cacheKey = BuildCacheKey(ip, vendorIdentity, username);
            FirmwareResult cached = TryGetCached(cacheKey);
            if (cached != null)
            {
                WriteDebug(vendorIdentity, ip, cached, "cache");
                return cached;
            }

            try
            {
                // ---- ONVIF GetDeviceInformation (authenticated) ----
                if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    FirmwareResult onvifResult = await TryOnvifGetDeviceInformation(ip, username, password);
                    if (onvifResult != null && !string.IsNullOrWhiteSpace(onvifResult.FirmwareVersion))
                    {
                        result.FirmwareVersion = onvifResult.FirmwareVersion;
                        result.ReleaseDate = onvifResult.ReleaseDate;
                        result.RawFirmwareString = onvifResult.RawFirmwareString;
                        result.RawReleaseDate = onvifResult.RawReleaseDate;
                    }
                }

                // ---- Vendor-specific unauthenticated endpoints ----
                if (string.IsNullOrWhiteSpace(result.FirmwareVersion))
                {
                    if (IsHikvisionIdentity(vendorIdentity))
                        result = await TryHikvisionIsapi(ip, username, password) ?? result;
                    else if (IsHoneywellIdentity(vendorIdentity))
                        result = await TryHoneywellIsapi(ip, username, password) ?? result;
                    else if (IsDahuaIdentity(vendorIdentity))
                        result = await TryDahuaCgi(ip, username, password) ?? result;
                    else if (IsUniviewIdentity(vendorIdentity))
                        result = await TryUniviewLapi(ip, username, password) ?? result;
                    else if (IsSunellIdentity(vendorIdentity))
                        result = await TrySunellWebAjax(ip) ?? result;
                    else if (IsTiandyIdentity(vendorIdentity))
                        result = await TryTiandyDeviceInfo(ip, username, password) ?? result;
                }
            }
            catch
            {
                // Fall through — return whatever we collected so far.
            }

            if (!string.IsNullOrWhiteSpace(result.FirmwareVersion) ||
                !string.IsNullOrWhiteSpace(result.ReleaseDate))
            {
                CacheResult(cacheKey, result);
            }

            WriteDebug(vendorIdentity, ip, result, "probe");
            return result;
        }

        #region ONVIF GetDeviceInformation

        private static async Task<FirmwareResult> TryOnvifGetDeviceInformation(
            string ip, string username, string password)
        {
            try
            {
                string bodyXml = "<GetDeviceInformation xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>";
                string response = await OnvifSoap.SendAsync(ip, username, password, bodyXml);

                string firmware = OnvifSoap.ExtractValue(response, "FirmwareVersion");
                return new FirmwareResult
                {
                    FirmwareVersion = firmware ?? "",
                    RawFirmwareString = firmware ?? "",
                    ReleaseDate = ""  // ONVIF GetDeviceInformation does not include release date
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Hikvision ISAPI

        private static Task<FirmwareResult> TryHikvisionIsapi(string ip, string username, string password)
        {
            return Task.Run(() =>
            {
                try
                {
                    System.Net.Security.RemoteCertificateValidationCallback prevCallback =
                        ServicePointManager.ServerCertificateValidationCallback;
                    ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

                    try
                    {
                        string[] urls =
                        {
                            "https://" + ip + "/ISAPI/System/deviceInfo",
                            "http://"  + ip + "/ISAPI/System/deviceInfo"
                        };

                        foreach (string url in urls)
                        {
                            try
                            {
                                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                                request.Timeout = 3000;
                                request.ReadWriteTimeout = 3000;
                                request.AllowAutoRedirect = false;
                                request.UserAgent = "UniversalScanner";
                                ApplyCredentials(request, username, password);

                                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                                {
                                    string xml = reader.ReadToEnd();
                                    string firmware = XmlElementValue(xml, "firmwareVersion");
                                    string releaseDate = FirstNonEmpty(
                                        XmlElementValue(xml, "firmwareReleasedDate"),
                                        XmlElementValue(xml, "releaseDate"),
                                        XmlElementValue(xml, "buildDate"));

                                    if (!string.IsNullOrWhiteSpace(firmware))
                                    {
                                        return new FirmwareResult
                                        {
                                            FirmwareVersion = firmware,
                                            ReleaseDate = NormalizeDate(releaseDate),
                                            RawFirmwareString = firmware,
                                            RawReleaseDate = releaseDate
                                        };
                                    }
                                }
                            }
                            catch (WebException)
                            {
                                // Try next URL
                            }
                        }
                    }
                    finally
                    {
                        ServicePointManager.ServerCertificateValidationCallback = prevCallback;
                    }
                }
                catch
                {
                }

                return null;
            });
        }

        #endregion

        #region Honeywell ISAPI

        private static Task<FirmwareResult> TryHoneywellIsapi(string ip, string username, string password)
        {
            return Task.Run(() =>
            {
                try
                {
                    System.Net.Security.RemoteCertificateValidationCallback prevCallback =
                        ServicePointManager.ServerCertificateValidationCallback;
                    ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

                    try
                    {
                        string[] urls =
                        {
                            "https://" + ip + "/ISAPI/System/deviceInfo",
                            "http://"  + ip + "/ISAPI/System/deviceInfo"
                        };

                        foreach (string url in urls)
                        {
                            try
                            {
                                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                                request.Timeout = 3000;
                                request.ReadWriteTimeout = 3000;
                                request.AllowAutoRedirect = false;
                                request.UserAgent = "UniversalScanner";
                                ApplyCredentials(request, username, password);

                                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                                {
                                    string xml = reader.ReadToEnd();
                                    string firmware = XmlElementValue(xml, "firmwareVersion");
                                    string releaseDate = FirstNonEmpty(
                                        XmlElementValue(xml, "firmwareReleasedDate"),
                                        XmlElementValue(xml, "releaseDate"),
                                        XmlElementValue(xml, "buildDate"));

                                    if (!string.IsNullOrWhiteSpace(firmware))
                                    {
                                        return new FirmwareResult
                                        {
                                            FirmwareVersion = firmware,
                                            ReleaseDate = NormalizeDate(releaseDate),
                                            RawFirmwareString = firmware,
                                            RawReleaseDate = releaseDate
                                        };
                                    }
                                }
                            }
                            catch (WebException)
                            {
                            }
                        }
                    }
                    finally
                    {
                        ServicePointManager.ServerCertificateValidationCallback = prevCallback;
                    }
                }
                catch
                {
                }

                return null;
            });
        }

        #endregion

        #region Dahua CGI

        private static Task<FirmwareResult> TryDahuaCgi(string ip, string username, string password)
        {
            return Task.Run(() =>
            {
                try
                {
                    string url = "http://" + ip + "/cgi-bin/magicBox.cgi?action=getSystemInfo";

                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Timeout = 3000;
                    request.ReadWriteTimeout = 3000;
                    request.AllowAutoRedirect = false;
                    request.UserAgent = "UniversalScanner";
                    ApplyCredentials(request, username, password);

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        string body = reader.ReadToEnd();
                        if (string.IsNullOrWhiteSpace(body))
                            return null;

                        // Dahua CGI returns key=value lines (similar to INI format) or simple text
                        string firmware = FirstNonEmpty(
                            ExtractKeyValue(body, "deviceVersion"),
                            ExtractKeyValue(body, "version"),
                            ExtractKeyValue(body, "firmwareVersion"),
                            ExtractKeyValue(body, "SoftwareVersion"));

                        string releaseDate = FirstNonEmpty(
                            ExtractKeyValue(body, "webVersion"),
                            ExtractKeyValue(body, "buildDate"),
                            ExtractKeyValue(body, "releaseDate"));

                        if (!string.IsNullOrWhiteSpace(firmware))
                        {
                            return new FirmwareResult
                            {
                                FirmwareVersion = firmware,
                                ReleaseDate = NormalizeDate(releaseDate),
                                RawFirmwareString = firmware,
                                RawReleaseDate = releaseDate
                            };
                        }
                    }
                }
                catch
                {
                }

                return null;
            });
        }

        #endregion

        #region Uniview LAPI

        private static Task<FirmwareResult> TryUniviewLapi(string ip, string username, string password)
        {
            return Task.Run(() =>
            {
                try
                {
                    string url = "http://" + ip + "/LAPI/V1.0/System/DeviceInfo";

                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Timeout = 3000;
                    request.ReadWriteTimeout = 3000;
                    ApplyCredentials(request, username, password);
                    request.AllowAutoRedirect = false;
                    request.UserAgent = "UniversalScanner";

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string statusCode = "";
                    string pwdSource = string.IsNullOrEmpty(password) ? "Empty" : "Supplied by Caller";
                    
                    try
                    {
                        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                        {
                            statusCode = $"{(int)response.StatusCode} {response.StatusDescription}";
                            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                            {
                                string json = reader.ReadToEnd();
                                return ParseUniviewDeviceInfoJson(json);
                            }
                        }
                    }
                    catch (WebException wex)
                    {
                        if (wex.Response is HttpWebResponse errResp) statusCode = $"{(int)errResp.StatusCode} {errResp.StatusDescription}";
                        else statusCode = "WebException";

                        // Try Uniview factory credentials, then try without auth.
                        request = (HttpWebRequest)WebRequest.Create(url);
                        request.Timeout = 3000;
                        request.ReadWriteTimeout = 3000;
                        request.Credentials = new NetworkCredential("admin", "123456");
                        request.PreAuthenticate = true;
                        request.AllowAutoRedirect = false;
                        request.UserAgent = "UniversalScanner";

                        var sw2 = System.Diagnostics.Stopwatch.StartNew();
                        string statusCode2 = "";
                        try
                        {
                            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                            {
                                statusCode2 = $"{(int)response.StatusCode} {response.StatusDescription}";
                                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                                {
                                    string json = reader.ReadToEnd();
                                    return ParseUniviewDeviceInfoJson(json);
                                }
                            }
                        }
                        catch (WebException wex2)
                        {
                            if (wex2.Response is HttpWebResponse errResp2) statusCode2 = $"{(int)errResp2.StatusCode} {errResp2.StatusDescription}";
                            else statusCode2 = "WebException";

                            request = (HttpWebRequest)WebRequest.Create(url);
                            request.Timeout = 3000;
                            request.ReadWriteTimeout = 3000;
                            request.AllowAutoRedirect = false;
                            request.UserAgent = "UniversalScanner";

                            var sw3 = System.Diagnostics.Stopwatch.StartNew();
                            string statusCode3 = "";
                            try
                            {
                                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                                {
                                    statusCode3 = $"{(int)response.StatusCode} {response.StatusDescription}";
                                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                                    {
                                        string json = reader.ReadToEnd();
                                        return ParseUniviewDeviceInfoJson(json);
                                    }
                                }
                            }
                            catch (WebException wex3)
                            {
                                if (wex3.Response is HttpWebResponse errResp3) statusCode3 = $"{(int)errResp3.StatusCode} {errResp3.StatusDescription}";
                                else statusCode3 = "WebException";
                            }
                            finally
                            {
                                sw3.Stop();
                                OnvifTraceLogger.LogRequest("DeviceFirmwareProbe", "DeviceFirmwareProbe", "TryUniviewLapi_Unauth", url, "N/A", "GET", "None", "", "None", 0, statusCode3, "None", 2, true, false, sw3.ElapsedMilliseconds);
                            }
                        }
                        finally
                        {
                            sw2.Stop();
                            OnvifTraceLogger.LogRequest("DeviceFirmwareProbe", "DeviceFirmwareProbe", "TryUniviewLapi_Factory", url, "N/A", "GET", "Basic (PreAuth)", "admin", "Hardcoded Fallback", 6, statusCode2, "None", 1, true, false, sw2.ElapsedMilliseconds);
                        }
                    }
                    finally
                    {
                        sw.Stop();
                        OnvifTraceLogger.LogRequest("DeviceFirmwareProbe", "DeviceFirmwareProbe", "TryUniviewLapi_Initial", url, "N/A", "GET", "Basic (PreAuth)", username ?? "", pwdSource, (password ?? "").Length, statusCode, "None", 0, true, false, sw.ElapsedMilliseconds);
                    }
                }
                catch
                {
                }

                return null;
            });
        }

        private static FirmwareResult ParseUniviewDeviceInfoJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                // Lightweight JSON extraction without System.Web.Extensions dependency
                // Uniview LAPI response: {"Response": {..., "FirmwareVersion": "V1.0.0", "BuildDate": "...", ...}}
                string firmware = FirstNonEmpty(
                    JsonStringValue(json, "FirmwareVersion"),
                    JsonStringValue(json, "firmwareVersion"),
                    JsonStringValue(json, "SoftwareVersion"));

                string releaseDate = FirstNonEmpty(
                    JsonStringValue(json, "BuildDate"),
                    JsonStringValue(json, "buildDate"),
                    JsonStringValue(json, "ReleaseDate"),
                    JsonStringValue(json, "FirmwareReleasedDate"));

                if (!string.IsNullOrWhiteSpace(firmware))
                {
                    return new FirmwareResult
                    {
                        FirmwareVersion = firmware,
                        ReleaseDate = NormalizeDate(releaseDate),
                        RawFirmwareString = firmware,
                        RawReleaseDate = releaseDate
                    };
                }
            }
            catch
            {
            }

            return null;
        }

        #endregion

        #region Sunell WEB_JsonAjax

        private static async Task<FirmwareResult> TrySunellWebAjax(string ip)
        {
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        // Reuse Sunell's GetModel/GetStatus pattern — command 30541 returns DeviceSummaryInfo
                        string xml = SunellWebApi.SendCommand(ip, "30541", "");
                        if (string.IsNullOrWhiteSpace(xml))
                            return null;

                        string firmware = FirstNonEmpty(
                            SunellWebApi.GetAttribute(xml, "DeviceSummaryInfo", "FirmwareVersion"),
                            SunellWebApi.GetAttribute(xml, "DeviceSummaryInfo", "SoftwareVersion"),
                            SunellWebApi.GetAttribute(xml, "DeviceSummaryInfo", "Version"));

                        string releaseDate = FirstNonEmpty(
                            SunellWebApi.GetAttribute(xml, "DeviceSummaryInfo", "FirmwareReleasedDate"),
                            SunellWebApi.GetAttribute(xml, "DeviceSummaryInfo", "BuildDate"),
                            SunellWebApi.GetAttribute(xml, "DeviceSummaryInfo", "ReleaseDate"));

                        if (!string.IsNullOrWhiteSpace(firmware))
                        {
                            return new FirmwareResult
                            {
                                FirmwareVersion = firmware,
                                ReleaseDate = NormalizeDate(releaseDate),
                                RawFirmwareString = firmware,
                                RawReleaseDate = releaseDate
                            };
                        }
                    }
                    catch
                    {
                    }

                    return null;
                });
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Tiandy CGI

        private static Task<FirmwareResult> TryTiandyDeviceInfo(string ip, string username, string password)
        {
            return Task.Run(() =>
            {
                try
                {
                    // Tiandy already probes /CGI/System/deviceInfo for model info.
                    // We use the same endpoint via Tiandy's existing infrastructure.
                    // Since we don't have credentials, try an unauthenticated GET first,
                    // then fall back to ONVIF (already tried above).

                    string url = "http://" + ip + "/CGI/System/deviceInfo";

                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Timeout = 3000;
                    request.ReadWriteTimeout = 3000;
                    request.AllowAutoRedirect = false;
                    request.UserAgent = "UniversalScanner";

                    try
                    {
                        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            string xml = reader.ReadToEnd();
                            if (string.IsNullOrWhiteSpace(xml))
                                return null;

                            string firmware = FirstNonEmpty(
                                TiandyWebApi.GetElementText(xml, "firmwareVersion"),
                                TiandyWebApi.GetElementText(xml, "softwareVersion"),
                                TiandyWebApi.GetElementText(xml, "version"));

                            string releaseDate = FirstNonEmpty(
                                TiandyWebApi.GetElementText(xml, "buildDate"),
                                TiandyWebApi.GetElementText(xml, "releaseDate"),
                                TiandyWebApi.GetElementText(xml, "firmwareReleasedDate"));

                            if (!string.IsNullOrWhiteSpace(firmware))
                            {
                                return new FirmwareResult
                                {
                                    FirmwareVersion = firmware,
                                    ReleaseDate = NormalizeDate(releaseDate),
                                    RawFirmwareString = firmware,
                                    RawReleaseDate = releaseDate
                                };
                            }
                        }
                    }
                    catch (WebException)
                    {
                        // Tiandy may require DIGEST auth — already covered by ONVIF fallback
                    }
                }
                catch
                {
                }

                return null;
            });
        }

        #endregion

        #region XML / JSON / Text helpers

        private static string XmlElementValue(string xml, string localName)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(localName))
                return "";

            try
            {
                Match match = Regex.Match(xml,
                    @"<" + Regex.Escape(localName) + @"[^>]*>(.*?)</" + Regex.Escape(localName) + @">",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return match.Success ? match.Groups[1].Value.Trim() : "";
            }
            catch
            {
                return "";
            }
        }

        private static string JsonStringValue(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return "";

            try
            {
                // Match "key": "value" where value may contain escaped quotes
                Match match = Regex.Match(json,
                    "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                    return Regex.Unescape(match.Groups[1].Value);
            }
            catch
            {
            }

            return "";
        }

        private static string ExtractKeyValue(string text, string key)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
                return "";

            try
            {
                Match match = Regex.Match(text,
                    Regex.Escape(key) + @"\s*=\s*(.+)$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline);
                return match.Success ? match.Groups[1].Value.Trim() : "";
            }
            catch
            {
                return "";
            }
        }

        private static void ApplyCredentials(HttpWebRequest request, string username, string password)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            request.Credentials = new NetworkCredential(username.Trim(), password);
            request.PreAuthenticate = true;
        }

        private static string ExtractHttpBody(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            int index = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (index >= 0 && index + 4 < raw.Length)
                return raw.Substring(index + 4);

            index = raw.IndexOf("\n\n", StringComparison.Ordinal);
            if (index >= 0 && index + 2 < raw.Length)
                return raw.Substring(index + 2);

            return raw;
        }

        private static string NormalizeDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            raw = raw.Trim();

            // If it's already yyyy-MM-dd, keep as-is
            if (Regex.IsMatch(raw, @"^\d{4}-\d{2}-\d{2}$"))
                return raw;

            // Try parsing common formats
            if (DateTime.TryParse(raw, out DateTime dt))
                return dt.ToString("yyyy-MM-dd");

            // Return original string if unparseable
            return raw;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string BuildCacheKey(string ip, string vendor, string username)
        {
            return (vendor ?? "").Trim() + "|" + (ip ?? "").Trim() + "|" + (username ?? "").Trim();
        }

        private static FirmwareResult TryGetCached(string cacheKey)
        {
            lock (CacheLock)
            {
                FirmwareResult cached;
                if (!ResultCache.TryGetValue(cacheKey, out cached))
                    return null;

                return new FirmwareResult
                {
                    FirmwareVersion = cached.FirmwareVersion,
                    ReleaseDate = cached.ReleaseDate,
                    RawFirmwareString = cached.RawFirmwareString,
                    RawReleaseDate = cached.RawReleaseDate
                };
            }
        }

        private static CredentialInfo TryGetRegisteredCredentials(string ip)
        {
            lock (CacheLock)
            {
                CredentialInfo credentials;
                if (!CredentialCache.TryGetValue((ip ?? "").Trim(), out credentials))
                    return null;

                return new CredentialInfo
                {
                    Username = credentials.Username,
                    Password = credentials.Password
                };
            }
        }

        private static void CacheResult(string cacheKey, FirmwareResult result)
        {
            if (result == null)
                return;

            lock (CacheLock)
            {
                ResultCache[cacheKey] = new FirmwareResult
                {
                    FirmwareVersion = result.FirmwareVersion ?? "",
                    ReleaseDate = result.ReleaseDate ?? "",
                    RawFirmwareString = result.RawFirmwareString ?? "",
                    RawReleaseDate = result.RawReleaseDate ?? ""
                };
            }
        }

        private static bool IsDiagnosticsEnabled()
        {
#if DEBUG
            return true;
#else
            return Config.getInstance().DebugMode;
#endif
        }

        private static void WriteDebug(string vendor, string ip, FirmwareResult result, string source)
        {
            if (!IsDiagnosticsEnabled()) return;

            try
            {
                string debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Debug");
                Directory.CreateDirectory(debugDir);
                string path = Path.Combine(debugDir, "FirmwareLookupDebug-" + DateTime.Now.ToString("yyyyMMdd") + ".txt");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Source: " + (source ?? ""));
                sb.AppendLine("Vendor: " + (vendor ?? ""));
                sb.AppendLine("Device: " + (ip ?? ""));
                sb.AppendLine("Raw firmware string: " + (result == null ? "" : result.RawFirmwareString ?? ""));
                sb.AppendLine("Raw release/build date: " + (result == null ? "" : result.RawReleaseDate ?? ""));
                sb.AppendLine("Final parsed Firmware: " + (result == null ? "" : result.FirmwareVersion ?? ""));
                sb.AppendLine("Final parsed Release Date: " + (result == null ? "" : result.ReleaseDate ?? ""));
                sb.AppendLine();

                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static bool IsHikvisionIdentity(string vendor)
        {
            return vendor?.IndexOf("Hikvision", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHoneywellIdentity(string vendor)
        {
            return vendor?.IndexOf("Honeywell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDahuaIdentity(string vendor)
        {
            return vendor?.IndexOf("Dahua", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUniviewIdentity(string vendor)
        {
            return vendor?.IndexOf("Uniview", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSunellIdentity(string vendor)
        {
            return vendor?.IndexOf("Sunell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTiandyIdentity(string vendor)
        {
            return vendor?.IndexOf("Tiandy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private class CredentialInfo
        {
            public string Username { get; set; }
            public string Password { get; set; }
        }

        #endregion
    }
}
