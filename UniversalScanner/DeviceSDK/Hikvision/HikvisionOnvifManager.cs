using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using UniversalScanner;

namespace UniversalScanner.DeviceSDK.Hikvision
{
    public static class HikvisionOnvifManager
    {
        private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };

        public static async Task<ProvisionResult> ProvisionAsync(string ip, string adminUser, string adminPass, string onvifUser, string onvifPass, IProgress<string> progress = null)
        {
            var result = new ProvisionResult { Success = false };
            var cookies = new CookieContainer();
            
            void Log(string msg) => progress?.Report(msg);

#if DEBUG
            string diagnosticLogPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HikvisionONVIF_Diagnostics.txt");
            try 
            {
                System.IO.File.WriteAllText(diagnosticLogPath, $"--- HIKVISION ONVIF DIAGNOSTIC LOG ---\r\nTimestamp: {DateTime.Now:O}\r\nIP: {ip}\r\n\r\n");
            }
            catch
            {
                diagnosticLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HikvisionONVIF_Diagnostics.txt");
                try { System.IO.File.WriteAllText(diagnosticLogPath, $"--- HIKVISION ONVIF DIAGNOSTIC LOG ---\r\nTimestamp: {DateTime.Now:O}\r\nIP: {ip}\r\n\r\n"); } catch { }
            }
#endif

            void LogDiagnostic(string title, HttpStatusCode code, string body)
            {
#if DEBUG
                try 
                {
                    string safeBody = body ?? "";
                    System.IO.File.AppendAllText(diagnosticLogPath, $"--- {title} ---\r\nStatus: {code}\r\nBody:\r\n{safeBody}\r\n\r\n");
                } 
                catch { }
#endif
            }
            void LogDiagnosticStr(string content)
            {
#if DEBUG
                try { System.IO.File.AppendAllText(diagnosticLogPath, content + "\r\n"); } catch { }
#endif
            }

#if DEBUG
            Log($"Diagnostic log path: {diagnosticLogPath}\r\n");
#endif

            try
            {
                var sw = Stopwatch.StartNew();

                // 1. Authentication & Capability Detection
                Log("Authentication...");
                string urlIntegrate = $"http://{ip}/ISAPI/System/Network/Integrate";
                var respIntegrate = await SendHikvisionRequestAsync("GET", urlIntegrate, adminUser, adminPass, null, "application/xml", cookies, ip, "CapabilityDetection");
                sw.Stop();
                LogRequest(ip, "CapabilityDetection", "GET", "/ISAPI/System/Network/Integrate", respIntegrate, sw.ElapsedMilliseconds);
                
                LogDiagnostic($"GET {urlIntegrate}", respIntegrate.StatusCode, respIntegrate.Body);
                LogDiagnosticStr($"Parsed ONVIF enabled before PUT: {ParseOnvifEnabled(respIntegrate.Body)}\r\n");
                
                if (respIntegrate.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Log("\r\nFailed: Unauthorized\r\n\r\n");
                    result.Stage = "Authentication";
                    result.ErrorMessage = $"Authentication failed: {respIntegrate.StatusCode}";
                    result.HttpStatusCode = (int)respIntegrate.StatusCode;
                    return result;
                }
                
                Log("\r\n✓ Success\r\n\r\n");

                Log("Capability detection...");
                if (respIntegrate.StatusCode != HttpStatusCode.OK)
                {
                    Log($"\r\nFailed ({respIntegrate.StatusCode})\r\n\r\n");
                    result.Stage = "CapabilityDetection";
                    result.ErrorMessage = $"Failed to detect capabilities: {respIntegrate.StatusCode}";
                    result.HttpStatusCode = (int)respIntegrate.StatusCode;
                    return result;
                }
                Log("\r\n✓ Success\r\n\r\n");

                bool isOnvifEnabled = ParseOnvifEnabled(respIntegrate.Body);

                // 2. Check ONVIF configuration
                Log("Checking ONVIF configuration...");
                if (isOnvifEnabled)
                {
                    Log("\r\n✓ Already enabled\r\n\r\n");
                }
                else
                {
                    sw.Restart();
                    sw.Restart();
                    string putPayload = null;
                    bool isPayloadValid = false;

                    try
                    {
                        var doc = XDocument.Parse(respIntegrate.Body);
                        var ns = doc.Root.GetDefaultNamespace();
                        
                        var onvifNode = doc.Descendants(ns + "ONVIF").FirstOrDefault() ?? doc.Descendants("ONVIF").FirstOrDefault();
                        if (onvifNode != null)
                        {
                            var enableNode = onvifNode.Element(ns + "enable") ?? onvifNode.Element("enable");
                            if (enableNode != null)
                            {
                                enableNode.Value = "true";
                                putPayload = doc.ToString(SaveOptions.DisableFormatting);
                                isPayloadValid = true;
                            }
                        }
                        else
                        {
                            var cgiNode = doc.Descendants(ns + "cgi").FirstOrDefault(x => (string)(x.Element(ns + "id") ?? x.Element("id")) == "1") 
                                          ?? doc.Descendants("cgi").FirstOrDefault(x => (string)(x.Element(ns + "id") ?? x.Element("id")) == "1");
                            if (cgiNode != null)
                            {
                                var enableNode = cgiNode.Element(ns + "enable") ?? cgiNode.Element("enable");
                                if (enableNode != null)
                                {
                                    enableNode.Value = "true";
                                    putPayload = doc.ToString(SaveOptions.DisableFormatting);
                                    isPayloadValid = true;
                                }
                            }
                        }
                    }
                    catch { }

                    if (!isPayloadValid || putPayload == null)
                    {
                        Log($"\r\nFailed: Unsupported ISAPI Integrate schema.\r\n\r\n");
                        LogDiagnosticStr($"--- UNSUPPORTED SCHEMA ---\r\n{respIntegrate.Body}\r\n");
                        result.Stage = "EnableONVIF";
                        result.ErrorMessage = "Unsupported ISAPI Integrate schema returned by the camera. Cannot safely construct PUT payload.";
                        return result;
                    }

                    if (respIntegrate.Body.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && !putPayload.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                    {
                        putPayload = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" + putPayload;
                    }

                    var respEnable = await SendHikvisionRequestAsync("PUT", urlIntegrate, adminUser, adminPass, putPayload, "application/xml", cookies, ip, "EnableONVIF");
                    sw.Stop();
                    LogRequest(ip, "EnableONVIF", "PUT", "/ISAPI/System/Network/Integrate", respEnable, sw.ElapsedMilliseconds);
                    
                    LogDiagnosticStr($"--- OUTGOING PUT {urlIntegrate} ---\r\nPayload:\r\n{putPayload}\r\n\r\n");
                    LogDiagnostic($"PUT RESPONSE", respEnable.StatusCode, respEnable.Body);
                    
                    if (respEnable.StatusCode == HttpStatusCode.OK)
                    {
                        Log("\r\n✓ Enabled\r\n\r\n");
                        
                        Log("Confirming ONVIF configuration...\r\n");
                        var respConfirm = await SendHikvisionRequestAsync("GET", urlIntegrate, adminUser, adminPass, null, "application/xml", cookies, ip, "ConfirmEnableONVIF");
                        
                        LogDiagnostic($"RE-READ GET {urlIntegrate}", respConfirm.StatusCode, respConfirm.Body);
                        bool isConfirmedEnabled = ParseOnvifEnabled(respConfirm.Body);
                        LogDiagnosticStr($"Parsed ONVIF enabled after PUT: {isConfirmedEnabled}\r\n");
                        
                        if (respConfirm.StatusCode == HttpStatusCode.OK && isConfirmedEnabled)
                        {
                            Log("✓ Configuration confirmed\r\n\r\n");
                        }
                        else
                        {
                            Log($"\r\nFailed: ONVIF read-back confirmed false after PUT.\r\n\r\n");
                            result.Stage = "EnableONVIF";
                            result.ErrorMessage = "The camera accepted the PUT request, but the configuration read-back still shows ONVIF as disabled.";
                            result.HttpStatusCode = (int)respConfirm.StatusCode;
                            return result;
                        }
                    }
                    else
                    {
                        Log($"\r\nFailed to enable ({respEnable.StatusCode})\r\n\r\n");
                        result.Stage = "EnableONVIF";
                        result.ErrorMessage = $"Failed to enable ONVIF: {respEnable.StatusCode}";
                        result.HttpStatusCode = (int)respEnable.StatusCode;
                        return result;
                    }
                }

                // 4. Check ONVIF users
                Log("Checking ONVIF users...\r\n");
                sw.Restart();
                string urlUsers = $"http://{ip}/ISAPI/Security/ONVIF/users";
                var respGetUsers = await SendHikvisionRequestAsync("GET", urlUsers, adminUser, adminPass, null, "application/xml", cookies, ip, "EnumerateUsers");
                sw.Stop();
                LogRequest(ip, "EnumerateUsers", "GET", "/ISAPI/Security/ONVIF/users", respGetUsers, sw.ElapsedMilliseconds);
                LogDiagnostic($"GET {urlUsers}", respGetUsers.StatusCode, respGetUsers.Body);
                
                bool userExists = false;
                if (respGetUsers.StatusCode == HttpStatusCode.OK)
                {
                    XDocument xdoc = null;
                    try { xdoc = XDocument.Parse(respGetUsers.Body); } catch { }
                    
                    if (xdoc != null)
                    {
                        XNamespace ns = xdoc.Root.GetDefaultNamespace();
                        var users = xdoc.Descendants(ns + "User");
                        if (!users.Any()) users = xdoc.Descendants("User"); // fallback

                        XElement existingUser = null;
                        foreach (var u in users)
                        {
                            string uName = (string)u.Element(ns + "userName") ?? (string)u.Element("userName");
                            if (uName == onvifUser)
                            {
                                existingUser = u;
                                break;
                            }
                        }

                        if (existingUser != null)
                        {
                            userExists = true;
                            // Hikvision ISAPI sometimes returns userLevel and sometimes userType
                            string currentLevel = (string)existingUser.Element(ns + "userLevel") ?? (string)existingUser.Element("userLevel") ?? (string)existingUser.Element(ns + "userType") ?? (string)existingUser.Element("userType") ?? "Unknown";
                            string userId = (string)existingUser.Element(ns + "id") ?? (string)existingUser.Element("id");

                            if (!string.Equals(currentLevel, "administrator", StringComparison.OrdinalIgnoreCase))
                            {
                                Log($"User \"{onvifUser}\" has role \"{currentLevel}\", upgrading to administrator...\r\n");
                                sw.Restart();
                                
                                // Update element (force both userLevel and userType to be safe on various firmware)
                                var levelElem = existingUser.Element(ns + "userLevel") ?? existingUser.Element("userLevel");
                                if (levelElem != null) levelElem.Value = "administrator";
                                else existingUser.Add(new XElement(ns + "userLevel", "administrator"));
                                
                                var typeElem = existingUser.Element(ns + "userType") ?? existingUser.Element("userType");
                                if (typeElem != null) typeElem.Value = "administrator";
                                else existingUser.Add(new XElement(ns + "userType", "administrator"));

                                string putUrl = $"{urlUsers}/{userId}";
                                var respUpdate = await SendHikvisionRequestAsync("PUT", putUrl, adminUser, adminPass, existingUser.ToString(), "application/xml", cookies, ip, "UpdateUser");
                                sw.Stop();
                                LogRequest(ip, "UpdateUser", "PUT", $"/ISAPI/Security/ONVIF/users/{userId}", respUpdate, sw.ElapsedMilliseconds);
                                LogDiagnostic($"PUT {putUrl}", respUpdate.StatusCode, respUpdate.Body);

                                if (respUpdate.StatusCode == HttpStatusCode.OK)
                                {
                                    if (await VerifyRoleAsync(ip, adminUser, adminPass, onvifUser, cookies))
                                    {
                                        Log("✓ User upgraded successfully.\r\n\r\n");
                                    }
                                    else
                                    {
                                        Log($"\r\nFailed verification after PUT. Role is not administrator.\r\n\r\n");
                                        result.Stage = "UpdateUser";
                                        result.ErrorMessage = "Role update verification failed.";
                                        return result;
                                    }
                                }
                                else
                                {
                                    ExtractHikvisionError(respUpdate.Body, out string hkStatus, out string hkSubStatus, out string hkString);
                                    Log($"\r\nFailed to upgrade existing user ({respUpdate.StatusCode} - {hkStatus}/{hkSubStatus}/{hkString}). User preserved unchanged.\r\n\r\n");
                                    result.Stage = "UpdateUser";
                                    result.ErrorMessage = $"Failed to upgrade existing ONVIF user to administrator: {respUpdate.StatusCode}. User preserved unchanged.";
                                    result.HttpStatusCode = (int)respUpdate.StatusCode;
                                    result.HikvisionStatusCode = hkStatus;
                                    result.HikvisionSubStatusCode = hkSubStatus;
                                    result.HikvisionStatusString = hkString;
                                    return result;
                                }
                            }
                            else
                            {
                                Log($"✓ User \"{onvifUser}\" already exists as administrator\r\n\r\n");
                            }
                        }
                    }
                }

                if (!userExists)
                {
                    Log("Creating ONVIF user...\r\n");
                    sw.Restart();
                    
                    // Extract namespace from GET response if possible, else use ISAPI default
                    string nsString = " xmlns=\"http://www.isapi.org/ver20/XMLSchema\"";
                    try 
                    {
                        if (respGetUsers.StatusCode == HttpStatusCode.OK) 
                        {
                            var xdoc = XDocument.Parse(respGetUsers.Body);
                            var ns = xdoc.Root.GetDefaultNamespace();
                            if (ns != null && !string.IsNullOrEmpty(ns.NamespaceName)) 
                            {
                                nsString = $" xmlns=\"{ns.NamespaceName}\"";
                            }
                        }
                    } catch { }

                    string userXml = $@"<User{nsString}><id>16</id><userName>{onvifUser}</userName><password>{onvifPass}</password><userLevel>administrator</userLevel><userType>administrator</userType></User>";
                    var respCreate = await SendHikvisionRequestAsync("POST", urlUsers, adminUser, adminPass, userXml, "application/xml", cookies, ip, "CreateUser");
                    sw.Stop();
                    LogRequest(ip, "CreateUser", "POST", "/ISAPI/Security/ONVIF/users", respCreate, sw.ElapsedMilliseconds);
                    LogDiagnostic($"POST {urlUsers}", respCreate.StatusCode, respCreate.Body);
                    
                    if (respCreate.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ExtractHikvisionError(respCreate.Body, out string hkStatus, out string hkSubStatus, out string hkString);
                        result.HikvisionStatusCode = hkStatus;
                        result.HikvisionSubStatusCode = hkSubStatus;
                        result.HikvisionStatusString = hkString;
                        
                        if (hkStatus == "4" && hkSubStatus == "security=1" || respCreate.Body.Contains("security=1"))
                        {
                            Log("\r\nRequires encrypted session (security=1).\r\n\r\n");
                            result.Stage = "CreateUser";
                            result.IsPartialSuccess = true;
                            result.ErrorMessage = "Requires encrypted session (security=1). ONVIF enabled, but user creation stopped.";
                            return result;
                        }
                    }

                    if (respCreate.StatusCode == HttpStatusCode.OK)
                    {
                        if (await VerifyRoleAsync(ip, adminUser, adminPass, onvifUser, cookies))
                        {
                            Log("✓ User created successfully.\r\n\r\n");
                        }
                        else
                        {
                            Log($"\r\nFailed verification after creation. Role is not administrator.\r\n\r\n");
                            result.Stage = "CreateUser";
                            result.ErrorMessage = "Role creation verification failed.";
                            return result;
                        }
                    }
                    else
                    {
                        ExtractHikvisionError(respCreate.Body, out string hkStatus, out string hkSubStatus, out string hkString);
                        Log($"\r\nFailed to create user ({respCreate.StatusCode} - {hkStatus}/{hkSubStatus}/{hkString})\r\n\r\n");
                        result.Stage = "CreateUser";
                        result.ErrorMessage = $"Failed to create user: {respCreate.StatusCode}";
                        result.HttpStatusCode = (int)respCreate.StatusCode;
                        return result;
                    }
                }

                // Wait briefly for camera service to initialize
                Log("Waiting for camera ONVIF service to initialize...\r\n");
                await Task.Delay(3000);

                // Final ONVIF Verification
                Log("ONVIF service verification...");
                sw.Restart();
                string urlOnvif = $"http://{ip}/onvif/device_service";
                string soapPayload = @"<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope""><s:Body><GetDeviceInformation xmlns=""http://www.onvif.org/ver10/device/wsdl""/></s:Body></s:Envelope>";
                var respVerifyFinal = await SendHikvisionRequestAsync("POST", urlOnvif, adminUser, adminPass, soapPayload, "application/soap+xml", cookies, ip, "ONVIFVerification");
                sw.Stop();
                LogRequest(ip, "ONVIFVerification", "POST", "/onvif/device_service", respVerifyFinal, sw.ElapsedMilliseconds);
                LogDiagnostic($"POST {urlOnvif}", respVerifyFinal.StatusCode, respVerifyFinal.Body);
                
                if (respVerifyFinal.StatusCode == HttpStatusCode.OK || respVerifyFinal.StatusCode == HttpStatusCode.Unauthorized || respVerifyFinal.StatusCode == HttpStatusCode.Forbidden)
                {
                    Log("\r\n✓ Success\r\n\r\n");
                    Log("Provision completed successfully.\r\n");
                    result.Success = true;
                    result.Stage = "Completed";
                    return result;
                }
                else
                {
                    Log($"\r\nWarning ({respVerifyFinal.StatusCode})\r\n\r\n");
                    Log("Provision completed with warnings.\r\n");
                    result.Success = true; // Overall provision succeeded (ISAPI configured)
                    result.IsPartialSuccess = true;
                    if (respVerifyFinal.StatusCode == HttpStatusCode.NotFound)
                    {
                        result.ErrorMessage = "ONVIF was enabled and the user was configured, but the ONVIF service is not responding yet. The camera may require additional initialization or a reboot.";
                    }
                    else
                    {
                        result.ErrorMessage = $"ONVIF was enabled and the user was configured, but the ONVIF service is not responding yet ({respVerifyFinal.StatusCode}). The camera may require additional initialization or a reboot.";
                    }
                    result.Stage = "ONVIFVerification";
                    result.HttpStatusCode = (int)respVerifyFinal.StatusCode;
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log($"\r\nException: {ex.Message}\r\n");
                LogDiagnosticStr($"--- EXCEPTION ---\r\n{ex.Message}\r\n{ex.StackTrace}\r\n");
                result.Stage = "Exception";
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private class HikvisionResponse
        {
            public HttpStatusCode StatusCode { get; set; }
            public string Body { get; set; }
            public string WwwAuthenticate { get; set; }
        }

        private static async Task<HikvisionResponse> SendHikvisionRequestAsync(string method, string url, string username, string password, string payload, string contentType, CookieContainer cookies, string ip, string stage)
        {
            var resp1 = await DoHttpRequestAsync(method, url, null, payload, contentType, cookies);
            
            if (resp1.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(resp1.WwwAuthenticate))
            {
                string wwwAuth = resp1.WwwAuthenticate;
                if (wwwAuth.IndexOf("Digest", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string realm = GetDigestValue(wwwAuth, "realm");
                    string nonce = GetDigestValue(wwwAuth, "nonce");
                    string qop = GetDigestValue(wwwAuth, "qop");
                    string opaque = GetDigestValue(wwwAuth, "opaque");
                    string algorithm = GetDigestValue(wwwAuth, "algorithm");
                    string stale = GetDigestValue(wwwAuth, "stale");

                    string authScheme = "Digest";

                    Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"[Hikvision ONVIF Provisioning] IP: {ip} | Stage: {stage} | Auth Challenge: {authScheme} | realm={realm}, qop={qop}, algorithm={algorithm}, stale={stale}");

                    string uriPath = new Uri(url).PathAndQuery;
                    string digestHeader = BuildDigestHeader(username, password, realm, nonce, qop, opaque, algorithm, method, uriPath);

                    var resp2 = await DoHttpRequestAsync(method, url, digestHeader, payload, contentType, cookies);
                    return resp2;
                }
            }

            return resp1;
        }

        private static async Task<HikvisionResponse> DoHttpRequestAsync(string method, string url, string authHeader, string payload, string contentType, CookieContainer cookies)
        {
            return await Task.Run(() =>
            {
                var result = new HikvisionResponse { StatusCode = HttpStatusCode.InternalServerError, Body = "" };
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = method;
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;
                request.AllowAutoRedirect = false;
                request.CookieContainer = cookies;

                if (!string.IsNullOrEmpty(authHeader))
                {
                    request.Headers.Add("Authorization", authHeader);
                }

                if (payload != null)
                {
                    byte[] data = Encoding.UTF8.GetBytes(payload);
                    request.ContentType = contentType;
                    request.ContentLength = data.Length;
                    using (var stream = request.GetRequestStream())
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }

                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        result.StatusCode = response.StatusCode;
                        result.WwwAuthenticate = response.Headers["WWW-Authenticate"];
                        using (var reader = new System.IO.StreamReader(response.GetResponseStream()))
                        {
                            result.Body = reader.ReadToEnd();
                        }
                    }
                }
                catch (WebException ex) when (ex.Response is HttpWebResponse errResp)
                {
                    result.StatusCode = errResp.StatusCode;
                    result.WwwAuthenticate = errResp.Headers["WWW-Authenticate"];
                    try
                    {
                        using (var reader = new System.IO.StreamReader(errResp.GetResponseStream()))
                        {
                            result.Body = reader.ReadToEnd();
                        }
                    }
                    catch { }
                }

                return result;
            });
        }

        private static string GetDigestValue(string challenge, string name)
        {
            var match = Regex.Match(challenge, $@"{name}=""([^""]*)""");
            if (match.Success) return match.Groups[1].Value;

            match = Regex.Match(challenge, $@"{name}=([^,\s]*)");
            if (match.Success) return match.Groups[1].Value;

            return "";
        }

        private static string BuildDigestHeader(string username, string password, string realm, string nonce, string qop, string opaque, string algorithm, string method, string uri)
        {
            string nc = "00000001";
            string cnonce = Guid.NewGuid().ToString("N");

            bool isSha256 = string.Equals(algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase) || string.Equals(algorithm, "SHA-256-sess", StringComparison.OrdinalIgnoreCase);

            string ha1 = isSha256 ? Sha256Hex(username + ":" + realm + ":" + password) : Md5Hex(username + ":" + realm + ":" + password);
            string ha2 = isSha256 ? Sha256Hex(method + ":" + uri) : Md5Hex(method + ":" + uri);

            string response = "";
            string qopValue = "";
            if (!string.IsNullOrWhiteSpace(qop))
            {
                qopValue = qop.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ? "auth" : qop.Split(',')[0].Trim();
                response = isSha256 ? Sha256Hex(ha1 + ":" + nonce + ":" + nc + ":" + cnonce + ":" + qopValue + ":" + ha2) : Md5Hex(ha1 + ":" + nonce + ":" + nc + ":" + cnonce + ":" + qopValue + ":" + ha2);
            }
            else
            {
                response = isSha256 ? Sha256Hex(ha1 + ":" + nonce + ":" + ha2) : Md5Hex(ha1 + ":" + nonce + ":" + ha2);
            }

            var sb = new StringBuilder();
            sb.Append($"Digest username=\"{username}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{uri}\", response=\"{response}\"");
            if (!string.IsNullOrWhiteSpace(qopValue)) sb.Append($", qop={qopValue}, nc={nc}, cnonce=\"{cnonce}\"");
            if (!string.IsNullOrWhiteSpace(opaque)) sb.Append($", opaque=\"{opaque}\"");
            if (!string.IsNullOrWhiteSpace(algorithm)) sb.Append($", algorithm={algorithm}");

            return sb.ToString();
        }

        private static string Md5Hex(string input)
        {
            using (var md5 = MD5.Create())
            {
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string Sha256Hex(string input)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-", "").ToLowerInvariant();
            }
        }

        private static async Task<bool> VerifyRoleAsync(string ip, string adminUser, string adminPass, string onvifUser, CookieContainer cookies)
        {
            try
            {
                var resp = await SendHikvisionRequestAsync("GET", $"http://{ip}/ISAPI/Security/ONVIF/users", adminUser, adminPass, null, "application/xml", cookies, ip, "VerifyRole");
                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    var xdoc = XDocument.Parse(resp.Body);
                    var ns = xdoc.Root.GetDefaultNamespace();
                    var users = xdoc.Descendants(ns + "User").Concat(xdoc.Descendants("User"));
                    foreach (var u in users)
                    {
                        string uName = (string)u.Element(ns + "userName") ?? (string)u.Element("userName");
                        if (uName == onvifUser)
                        {
                            string role = (string)u.Element(ns + "userLevel") ?? (string)u.Element("userLevel") ?? (string)u.Element(ns + "userType") ?? (string)u.Element("userType");
                            return string.Equals(role, "administrator", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static void ExtractHikvisionError(string xmlResponse, out string statusCode, out string subStatusCode, out string statusString)
        {
            statusCode = ""; subStatusCode = ""; statusString = "";
            try
            {
                if (string.IsNullOrEmpty(xmlResponse) || !xmlResponse.Contains("<")) return;
                var doc = XDocument.Parse(xmlResponse);
                var ns = doc.Root.GetDefaultNamespace();
                statusCode = doc.Root.Element(ns + "statusCode")?.Value;
                subStatusCode = doc.Root.Element(ns + "subStatusCode")?.Value;
                statusString = doc.Root.Element(ns + "statusString")?.Value;
            }
            catch { }
        }

        private static void LogRequest(string ip, string stage, string method, string path, HikvisionResponse response, long elapsedMs)
        {
            try
            {
                int httpCode = (int)response.StatusCode;
                string hkStatus = "", hkSubStatus = "", hkString = "";
                
                if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    ExtractHikvisionError(response.Body, out hkStatus, out hkSubStatus, out hkString);
                }

                string msg = $"[Hikvision ONVIF Provisioning] IP: {ip} | Stage: {stage} | Method: {method} | Path: {path} | " +
                             $"HTTP: {httpCode} | HKStatus: {hkStatus} | HKSubStatus: {hkSubStatus} | " +
                             $"HKString: {hkString} | Time: {elapsedMs}ms";
                
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, msg);
            }
            catch (Exception ex)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"[Hikvision ONVIF Provisioning] Log exception: {ex.Message}");
            }
        }

        private static bool ParseOnvifEnabled(string xmlBody)
        {
            if (string.IsNullOrEmpty(xmlBody)) return false;
            try
            {
                var doc = XDocument.Parse(xmlBody);
                var ns = doc.Root.GetDefaultNamespace();
                
                var onvifNode = doc.Descendants(ns + "ONVIF").FirstOrDefault() ?? doc.Descendants("ONVIF").FirstOrDefault();
                if (onvifNode != null)
                {
                    var enableNode = onvifNode.Element(ns + "enable") ?? onvifNode.Element("enable");
                    if (enableNode != null)
                    {
                        return string.Equals(enableNode.Value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                }
                
                var cgiNode = doc.Descendants(ns + "cgi").FirstOrDefault(x => (string)(x.Element(ns + "id") ?? x.Element("id")) == "1") 
                              ?? doc.Descendants("cgi").FirstOrDefault(x => (string)(x.Element(ns + "id") ?? x.Element("id")) == "1");
                if (cgiNode != null)
                {
                    var enableNode = cgiNode.Element(ns + "enable") ?? cgiNode.Element("enable");
                    if (enableNode != null)
                    {
                        return string.Equals(enableNode.Value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                }
                
                return xmlBody.Contains("<ONVIF><enable>true</enable>") || xmlBody.Contains("<ONVIF>\r\n<enable>true</enable>");
            }
            catch
            {
                return xmlBody.Contains("<ONVIF><enable>true</enable>") || xmlBody.Contains("<ONVIF>\r\n<enable>true</enable>");
            }
        }
    }
}
