using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    public static class HoneywellLegacyNetwork
    {
        private const string LegacyAjaxContentType = "application/x-www-form-urlencoded; charset=UTF-8";

        public static bool ChangeNetworkAddress(
            string currentIp,
            string username,
            string password,
            string newIp,
            string subnetMask,
            string gateway,
            out string message)
        {
            message = "";

            StringBuilder debug = new StringBuilder();
            string debugPath = "";

            SecurityProtocolType prevProtocol = ServicePointManager.SecurityProtocol;
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls;

            RemoteCertificateValidationCallback prevCallback = ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            bool prevExpect100 = ServicePointManager.Expect100Continue;
            ServicePointManager.Expect100Continue = false;

            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    username = "admin";

                Type winHttpType = Type.GetTypeFromProgID("WinHttp.WinHttpRequest.5.1");
                if (winHttpType == null)
                {
                    message = "Honeywell legacy WinHTTP COM object is not available.";
                    return false;
                }

                object request = Activator.CreateInstance(winHttpType);
                string session;
                if (!Login(request, currentIp, username, password, debug, out session, out message))
                {
                    debugPath = WriteDebugFile(currentIp, "FAILED", debug.ToString());
                    message += FormatDebugPath(debugPath);
                    return false;
                }

                string rpcUrl = "https://" + currentIp + "/RPC2";
                string sessionPart = BuildSessionPart(session);
                string response;
                HttpStatusCode status;
                string sendError;

                SendWinHttpRequest(request, "https://" + currentIp + "/RPC2_Notify_Method", "GET", null, out response, out status, out sendError);
                AppendDebug(debug, "Honeywell legacy notify method", "https://" + currentIp + "/RPC2_Notify_Method", status, response, sendError, null);

                string encryptInfoBody = "{\"method\":\"Security.getEncryptInfo\",\"params\":null,\"id\":10" + sessionPart + "}";
                if (!SendWinHttpRequest(request, rpcUrl, "POST", encryptInfoBody, out response, out status, out sendError) ||
                    !IsRpcSuccess(status, response))
                {
                    message = "Honeywell legacy Security.getEncryptInfo failed: " + (sendError ?? DescribeRpcFailure(status, response));
                    AppendDebug(debug, "Honeywell legacy Security.getEncryptInfo", rpcUrl, status, response, sendError, encryptInfoBody);
                    debugPath = WriteDebugFile(currentIp, "FAILED", debug.ToString());
                    message += FormatDebugPath(debugPath);
                    return false;
                }

                AppendDebug(debug, "Honeywell legacy Security.getEncryptInfo", rpcUrl, status, response, null, encryptInfoBody);
                string publicKey = ExtractJsonString(response, "pub");
                if (string.IsNullOrWhiteSpace(publicKey))
                {
                    message = "Honeywell legacy Security.getEncryptInfo did not return a public key.";
                    debugPath = WriteDebugFile(currentIp, "FAILED", debug.ToString());
                    message += FormatDebugPath(debugPath);
                    return false;
                }

                string[] configNames = { "Network", "NetAddrIPv4" };
                foreach (string configName in configNames)
                {
                    string tableJson;
                    string readError;
                    if (!TryGetEncryptedConfig(request, rpcUrl, sessionPart, publicKey, configName, debug, out tableJson, out readError))
                    {
                        AppendDebug(debug, "Honeywell legacy encrypted getConfig skipped " + configName, rpcUrl, 0, null, readError, null);
                        continue;
                    }

                    string updatedTable = UpdateNetworkTable(tableJson, newIp, subnetMask, gateway);
                    if (string.Equals(updatedTable, tableJson, StringComparison.Ordinal))
                    {
                        AppendDebug(debug, "Honeywell legacy network table unchanged " + configName, rpcUrl, 0, tableJson, "No known IP fields found.", null);
                        continue;
                    }

                    string setPlain = "{\"name\":\"" + JsonEscape(configName) + "\",\"table\":" + updatedTable + "}";
                    EncryptedPayload payload = EncryptPayload(publicKey, "AES", setPlain);
                    string setBody =
                        "{\"method\":\"Security.setConfig\",\"params\":{" +
                        "\"salt\":\"" + payload.Salt + "\"," +
                        "\"cipher\":\"" + payload.Cipher + "\"," +
                        "\"content\":\"" + payload.Content + "\"}," +
                        "\"id\":30" + sessionPart + "}";

                    if (SendWinHttpRequest(request, rpcUrl, "POST", setBody, out response, out status, out sendError) &&
                        IsRpcSuccess(status, response))
                    {
                        AppendDebug(debug, "Honeywell legacy encrypted setConfig " + configName, rpcUrl, status, response, null, setBody);
                        TryReboot(request, rpcUrl, sessionPart, debug);
                        debugPath = WriteDebugFile(currentIp, "SUCCESS", debug.ToString());
                        message = "Honeywell legacy IP address changed. The camera may reboot before it answers at the new address." + FormatDebugPath(debugPath);
                        return true;
                    }

                    AppendDebug(debug, "Honeywell legacy encrypted setConfig " + configName, rpcUrl, status, response, sendError, setBody);

                    if (IsLikelyConnectionDropAfterSetConfig(sendError))
                    {
                        debugPath = WriteDebugFile(currentIp, "SUCCESS", debug.ToString());
                        message = "Honeywell legacy IP settings were sent. The camera may already be applying the new address and rebooting." + FormatDebugPath(debugPath);
                        return true;
                    }
                }

                message = "Honeywell legacy IP change failed. The camera logged in, but no supported network config table was accepted.";
                debugPath = WriteDebugFile(currentIp, "FAILED", debug.ToString());
                message += FormatDebugPath(debugPath);
                return false;
            }
            catch (Exception ex)
            {
                debugPath = WriteDebugFile(currentIp, "FAILED", debug.ToString());
                message = "Honeywell legacy IP change failed: " + ex.Message + FormatDebugPath(debugPath);
                return false;
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = prevCallback;
                ServicePointManager.SecurityProtocol = prevProtocol;
                ServicePointManager.Expect100Continue = prevExpect100;
            }
        }

        public static bool RebootOnly(
            string currentIp,
            string username,
            string password,
            out string message)
        {
            message = "";
            StringBuilder debug = new StringBuilder();
            string debugPath = "";

            SecurityProtocolType prevProtocol = ServicePointManager.SecurityProtocol;
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls;

            RemoteCertificateValidationCallback prevCallback = ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            bool prevExpect100 = ServicePointManager.Expect100Continue;
            ServicePointManager.Expect100Continue = false;

            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    username = "admin";

                Type winHttpType = Type.GetTypeFromProgID("WinHttp.WinHttpRequest.5.1");
                if (winHttpType == null)
                {
                    message = "Honeywell legacy WinHTTP COM object is not available.";
                    return false;
                }

                object request = Activator.CreateInstance(winHttpType);
                string session;
                if (!Login(request, currentIp, username, password, debug, out session, out message))
                {
                    debugPath = WriteDebugFile(currentIp, "FAILED", debug.ToString());
                    message += FormatDebugPath(debugPath);
                    return false;
                }

                string rpcUrl = "https://" + currentIp + "/RPC2";
                string sessionPart = BuildSessionPart(session);
                bool rebooted = TryReboot(request, rpcUrl, sessionPart, debug);
                debugPath = WriteDebugFile(currentIp, rebooted ? "SUCCESS" : "FAILED", debug.ToString());
                message = rebooted
                    ? "Honeywell reboot command sent." + FormatDebugPath(debugPath)
                    : "Honeywell reboot command was not accepted." + FormatDebugPath(debugPath);
                return rebooted;
            }
            catch (Exception ex)
            {
                debugPath = WriteDebugFile(currentIp, "FAILED", debug.ToString());
                message = "Honeywell reboot failed: " + ex.Message + FormatDebugPath(debugPath);
                return false;
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = prevCallback;
                ServicePointManager.SecurityProtocol = prevProtocol;
                ServicePointManager.Expect100Continue = prevExpect100;
            }
        }

        private static bool Login(object request, string ip, string username, string password, StringBuilder debug, out string session, out string error)
        {
            session = "";
            error = "";
            string loginUrl = "https://" + ip + "/RPC2_Login";

            string firstBody =
                "{\"method\":\"global.login\"," +
                "\"params\":{\"userName\":\"" + JsonEscape(username) + "\",\"password\":\"\",\"clientType\":\"Web3.0\",\"loginType\":\"Direct\"}," +
                "\"id\":1}";

            string response;
            HttpStatusCode status;
            string sendError;
            if (!SendWinHttpRequest(request, loginUrl, "POST", firstBody, out response, out status, out sendError))
            {
                error = "Honeywell legacy first login failed: " + sendError;
                AppendDebug(debug, "Honeywell legacy firstLogin", loginUrl, status, response, sendError, firstBody);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy firstLogin", loginUrl, status, response, null, firstBody);
            session = ExtractJsonString(response, "session") ?? ExtractJsonNumber(response, "session");
            string encryption = ExtractJsonString(response, "encryption");
            string random = ExtractJsonString(response, "random");
            string realm = ExtractJsonString(response, "realm");
            string authPassword = BuildLoginAuth(username, password, encryption, random, realm);

            string secondBody =
                "{\"method\":\"global.login\"," +
                "\"params\":{\"userName\":\"" + JsonEscape(username) + "\"," +
                "\"password\":\"" + JsonEscape(authPassword) + "\"," +
                "\"clientType\":\"Web3.0\",\"loginType\":\"Direct\"," +
                "\"authorityType\":\"" + JsonEscape(encryption) + "\"}," +
                "\"id\":2" + BuildSessionPart(session) + "}";

            if (!SendWinHttpRequest(request, loginUrl, "POST", secondBody, out response, out status, out sendError))
            {
                error = "Honeywell legacy second login failed: " + sendError;
                AppendDebug(debug, "Honeywell legacy secondLogin", loginUrl, status, response, sendError, secondBody);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy secondLogin", loginUrl, status, response, null, secondBody);
            if (!IsRpcSuccess(status, response))
            {
                error = "Honeywell legacy login rejected: " + DescribeRpcFailure(status, response);
                return false;
            }

            session = ExtractJsonString(response, "session") ?? ExtractJsonNumber(response, "session") ?? session;
            return true;
        }

        private static bool TryGetEncryptedConfig(object request, string rpcUrl, string sessionPart, string publicKey, string configName, StringBuilder debug, out string tableJson, out string error)
        {
            tableJson = "";
            error = "";
            string plain = "{\"name\":\"" + JsonEscape(configName) + "\"}";
            EncryptedPayload payload = EncryptPayload(publicKey, "AES", plain);
            string body =
                "{\"method\":\"Security.getConfig\",\"params\":{" +
                "\"salt\":\"" + payload.Salt + "\"," +
                "\"cipher\":\"" + payload.Cipher + "\"," +
                "\"content\":\"" + payload.Content + "\"}," +
                "\"id\":20" + sessionPart + "}";

            string response;
            HttpStatusCode status;
            string sendError;
            if (!SendWinHttpRequest(request, rpcUrl, "POST", body, out response, out status, out sendError))
            {
                error = "send failed: " + sendError;
                AppendDebug(debug, "Honeywell legacy encrypted getConfig " + configName, rpcUrl, status, response, sendError, body);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy encrypted getConfig " + configName, rpcUrl, status, response, null, body);
            if (!IsRpcSuccess(status, response))
            {
                error = "RPC rejected: " + DescribeRpcFailure(status, response);
                return false;
            }

            string content = ExtractJsonString(response, "content");
            string decrypted = "";
            if (!string.IsNullOrWhiteSpace(content))
                decrypted = AesDecryptBase64(content, payload.KeyBytes, payload.Cipher);

            if (string.IsNullOrWhiteSpace(decrypted))
                decrypted = response;

            AppendDebug(debug, "Honeywell legacy decrypted getConfig " + configName, rpcUrl, status, decrypted, null, null);
            tableJson = ExtractJsonValue(decrypted, "table");
            if (string.IsNullOrWhiteSpace(tableJson))
            {
                error = "no table value in decrypted response";
                return false;
            }

            return true;
        }

        private static bool TryReboot(object request, string rpcUrl, string sessionPart, StringBuilder debug)
        {
            string[] bodies =
            {
                "{\"method\":\"magicBox.reboot\",\"params\":null,\"id\":40" + sessionPart + "}",
                "{\"method\":\"magicBox.reboot\",\"params\":{\"delay\":0},\"id\":41" + sessionPart + "}",
                "{\"method\":\"magicBox.rebootSystem\",\"params\":null,\"id\":42" + sessionPart + "}",
                "{\"method\":\"System.reboot\",\"params\":null,\"id\":43" + sessionPart + "}",
                "{\"method\":\"system.reboot\",\"params\":null,\"id\":44" + sessionPart + "}"
            };

            foreach (string body in bodies)
            {
                string response;
                HttpStatusCode status;
                string error;
                SendWinHttpRequest(request, rpcUrl, "POST", body, out response, out status, out error);
                AppendDebug(debug, "Honeywell legacy reboot attempt", rpcUrl, status, response, error, body);
                if (IsRpcSuccess(status, response))
                    return true;

                if (IsLikelyConnectionDropAfterSetConfig(error))
                    return true;
            }

            return false;
        }

        private sealed class EncryptedPayload
        {
            public string Salt;
            public string Cipher;
            public string Content;
            public byte[] KeyBytes;
        }

        private static EncryptedPayload EncryptPayload(string publicKey, string cipherHint, string plainJson)
        {
            string cipher = SelectCipher(cipherHint);
            int keyLength = cipher.EndsWith("-256", StringComparison.OrdinalIgnoreCase) ? 32 : 16;
            string key = CreateAsciiKey(keyLength);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] encryptedKey = RsaEncrypt(publicKey, keyBytes);
            byte[] encryptedContent = AesEncrypt(Encoding.UTF8.GetBytes(plainJson), keyBytes, cipher);

            return new EncryptedPayload
            {
                Salt = BytesToHex(encryptedKey).ToLowerInvariant(),
                Cipher = cipher,
                Content = Convert.ToBase64String(encryptedContent),
                KeyBytes = keyBytes
            };
        }

        private static string UpdateNetworkTable(string tableJson, string newIp, string subnetMask, string gateway)
        {
            string updated = tableJson ?? "";
            updated = ReplaceJsonStringField(updated, "IPAddress", newIp);
            updated = ReplaceJsonStringField(updated, "IpAddress", newIp);
            updated = ReplaceJsonStringField(updated, "Address", newIp);
            updated = ReplaceJsonStringField(updated, "SubnetMask", subnetMask);
            updated = ReplaceJsonStringField(updated, "DefaultGateway", gateway);
            updated = ReplaceJsonStringField(updated, "Gateway", gateway);
            updated = ReplaceJsonBoolField(updated, "DhcpEnable", false);
            updated = ReplaceJsonBoolField(updated, "DHCPEnable", false);
            updated = ReplaceJsonBoolField(updated, "Dhcp", false);
            updated = ReplaceJsonBoolField(updated, "DHCP", false);
            return updated;
        }

        private static string ReplaceJsonStringField(string json, string field, string value)
        {
            return Regex.Replace(
                json,
                "(\"" + Regex.Escape(field) + "\"\\s*:\\s*\")((?:[^\"\\\\]|\\\\.)*)(\")",
                "$1" + JsonEscape(value) + "$3",
                RegexOptions.IgnoreCase);
        }

        private static string ReplaceJsonBoolField(string json, string field, bool value)
        {
            return Regex.Replace(
                json,
                "(\"" + Regex.Escape(field) + "\"\\s*:\\s*)(true|false)",
                "$1" + (value ? "true" : "false"),
                RegexOptions.IgnoreCase);
        }

        private static bool SendWinHttpRequest(object request, string url, string method, string body, out string responseBody, out HttpStatusCode statusCode, out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            try
            {
                InvokeCom(request, "Open", method, url, false);
                SetComProperty(request, "Option", 4, 13056);
                InvokeCom(request, "SetTimeouts", 6000, 6000, 6000, 6000);
                InvokeCom(request, "SetRequestHeader", "User-Agent", GetBrowserUserAgent());
                InvokeCom(request, "SetRequestHeader", "Accept", "application/json, text/javascript, */*; q=0.01");
                InvokeCom(request, "SetRequestHeader", "Accept-Language", "en-US,en;q=0.9");
                InvokeCom(request, "SetRequestHeader", "X-Requested-With", "XMLHttpRequest");
                InvokeCom(request, "SetRequestHeader", "Referer", "https://" + new Uri(url).Host + "/");

                if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    Uri uri = new Uri(url);
                    string origin = uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString());
                    InvokeCom(request, "SetRequestHeader", "Content-Type", LegacyAjaxContentType);
                    InvokeCom(request, "SetRequestHeader", "Origin", origin);
                    InvokeCom(request, "Send", body ?? "");
                }
                else
                {
                    InvokeCom(request, "Send");
                }

                object status = GetComProperty(request, "Status");
                if (status != null)
                    statusCode = (HttpStatusCode)Convert.ToInt32(status);

                object text = GetComProperty(request, "ResponseText");
                responseBody = text == null ? "" : Convert.ToString(text);
                return (int)statusCode >= 200;
            }
            catch (Exception ex)
            {
                Exception actual = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                error = "WinHTTP " + actual.GetType().Name + ": " + Truncate(actual.Message, 220);

                if (actual.Message.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0)
                    return SendHttpWebRequest(url, method, body, out responseBody, out statusCode, out error);

                return false;
            }
        }

        private static bool SendHttpWebRequest(string url, string method, string body, out string responseBody, out HttpStatusCode statusCode, out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = method;
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.KeepAlive = false;
                request.UserAgent = GetBrowserUserAgent();
                request.Accept = "application/json, text/javascript, */*; q=0.01";
                request.Headers["Accept-Language"] = "en-US,en;q=0.9";
                request.Headers["X-Requested-With"] = "XMLHttpRequest";
                request.Referer = "https://" + new Uri(url).Host + "/";

                if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    Uri uri = new Uri(url);
                    string origin = uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString());
                    byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
                    request.ContentType = LegacyAjaxContentType;
                    request.Headers["Origin"] = origin;
                    request.ContentLength = payload.Length;
                    using (Stream stream = request.GetRequestStream())
                        stream.Write(payload, 0, payload.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    statusCode = response.StatusCode;
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        responseBody = reader.ReadToEnd();
                }

                return (int)statusCode >= 200;
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    statusCode = response.StatusCode;
                    try
                    {
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                            responseBody = reader.ReadToEnd();
                    }
                    catch
                    {
                    }
                }

                error = "HttpWebRequest WebException: " + Truncate(ex.Message, 220);
                return false;
            }
            catch (Exception ex)
            {
                error = "HttpWebRequest " + ex.GetType().Name + ": " + Truncate(ex.Message, 220);
                return false;
            }
        }

        private static bool IsLikelyConnectionDropAfterSetConfig(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return false;

            return error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("aborted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("receive", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static object InvokeCom(object target, string member, params object[] args)
        {
            return target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
        }

        private static object GetComProperty(object target, string property)
        {
            return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);
        }

        private static void SetComProperty(object target, string property, params object[] args)
        {
            try
            {
                target.GetType().InvokeMember(property, BindingFlags.SetProperty, null, target, args);
                return;
            }
            catch
            {
            }

            target.GetType().InvokeMember(property, BindingFlags.PutDispProperty, null, target, args);
        }

        private static string SelectCipher(string cipherHint)
        {
            if (!string.IsNullOrWhiteSpace(cipherHint))
            {
                if (cipherHint.IndexOf("ROAE", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "ROAE-256";
                if (cipherHint.IndexOf("RPAC", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "RPAC-256";
            }

            return "AES-128";
        }

        private static byte[] RsaEncrypt(string publicKey, byte[] value)
        {
            string modulusHex = ExtractPublicKeyPart(publicKey, "N");
            string exponentHex = ExtractPublicKeyPart(publicKey, "E");
            if (string.IsNullOrWhiteSpace(modulusHex) || string.IsNullOrWhiteSpace(exponentHex))
                throw new InvalidOperationException("Unsupported Honeywell legacy public key format.");

            RSAParameters parameters = new RSAParameters
            {
                Modulus = HexToBytes(modulusHex),
                Exponent = HexToBytes(exponentHex)
            };

            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.ImportParameters(parameters);
                return rsa.Encrypt(value, false);
            }
        }

        private static byte[] AesEncrypt(byte[] value, byte[] key, string cipher)
        {
            using (AesManaged aes = new AesManaged())
            {
                aes.Key = key;
                aes.Mode = cipher.Equals("RPAC-256", StringComparison.OrdinalIgnoreCase) ? CipherMode.CBC : CipherMode.ECB;
                aes.Padding = PaddingMode.Zeros;
                if (aes.Mode == CipherMode.CBC)
                    aes.IV = Encoding.UTF8.GetBytes("0000000000000000");

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                    return encryptor.TransformFinalBlock(value, 0, value.Length);
            }
        }

        private static string AesDecryptBase64(string content, byte[] key, string cipher)
        {
            try
            {
                byte[] encrypted = Convert.FromBase64String(content);
                using (AesManaged aes = new AesManaged())
                {
                    aes.Key = key;
                    aes.Mode = cipher.Equals("RPAC-256", StringComparison.OrdinalIgnoreCase) ? CipherMode.CBC : CipherMode.ECB;
                    aes.Padding = PaddingMode.Zeros;
                    if (aes.Mode == CipherMode.CBC)
                        aes.IV = Encoding.UTF8.GetBytes("0000000000000000");

                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    {
                        byte[] plain = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                        return Encoding.UTF8.GetString(plain).TrimEnd('\0', '\r', '\n', ' ');
                    }
                }
            }
            catch
            {
                return "";
            }
        }

        private static string BuildLoginAuth(string username, string password, string encryption, string random, string realm)
        {
            if (string.Equals(encryption, "Basic", StringComparison.OrdinalIgnoreCase))
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));

            if (string.Equals(encryption, "Default", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(random) &&
                !string.IsNullOrWhiteSpace(realm))
            {
                string inner = Md5Hex(username + ":" + realm + ":" + password).ToUpperInvariant();
                return Md5Hex(username + ":" + random + ":" + inner).ToUpperInvariant();
            }

            return password;
        }

        private static string BuildSessionPart(string session)
        {
            if (string.IsNullOrWhiteSpace(session))
                return "";

            if (Regex.IsMatch(session, "^[0-9]+$"))
                return ",\"session\":" + session;

            return ",\"session\":\"" + JsonEscape(session) + "\"";
        }

        private static bool IsRpcSuccess(HttpStatusCode status, string response)
        {
            if ((int)status < 200 || (int)status >= 300)
                return false;

            string compact = Compact(response);
            return compact.IndexOf("\"result\":true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   compact.IndexOf("\"code\":0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   compact.IndexOf("\"error\":null", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeRpcFailure(HttpStatusCode status, string response)
        {
            string message = ExtractJsonString(response, "message") ?? ExtractJsonNumber(response, "code");
            if (!string.IsNullOrWhiteSpace(message))
                return "HTTP " + (int)status + " / " + message;

            if (!string.IsNullOrWhiteSpace(response))
                return "HTTP " + (int)status + " / " + Truncate(response, 180);

            return "HTTP " + (int)status;
        }

        private static string ExtractJsonValue(string json, string field)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "";

            Match m = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:");
            if (!m.Success)
                return "";

            int i = m.Index + m.Length;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
                i++;

            if (i >= json.Length)
                return "";

            if (json[i] == '{' || json[i] == '[')
                return ExtractBalancedJson(json, i);

            if (json[i] == '"')
                return "\"" + ExtractJsonString(json.Substring(m.Index), field) + "\"";

            int end = i;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']')
                end++;

            return json.Substring(i, end - i).Trim();
        }

        private static string ExtractBalancedJson(string json, int start)
        {
            char open = json[start];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < json.Length; i++)
            {
                char ch = json[i];
                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (ch == '\\')
                        escaped = true;
                    else if (ch == '"')
                        inString = false;
                    continue;
                }

                if (ch == '"')
                    inString = true;
                else if (ch == open)
                    depth++;
                else if (ch == close)
                {
                    depth--;
                    if (depth == 0)
                        return json.Substring(start, i - start + 1);
                }
            }

            return "";
        }

        private static string ExtractPublicKeyPart(string publicKey, string name)
        {
            Match m = Regex.Match(publicKey ?? "", "(^|,)" + Regex.Escape(name) + ":([0-9a-fA-F]+)");
            return m.Success ? m.Groups[2].Value : null;
        }

        private static string ExtractJsonString(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field))
                return null;

            Match m = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : null;
        }

        private static string ExtractJsonNumber(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field))
                return null;

            Match m = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:\\s*(-?[0-9]+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string Md5Hex(string value)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));

                return sb.ToString();
            }
        }

        private static string CreateAsciiKey(int length)
        {
            const string alphabet = "0123456789";
            byte[] random = new byte[length];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(random);

            char[] chars = new char[length];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[random[i] % alphabet.Length];

            return new string(chars);
        }

        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return new byte[0];

            hex = hex.Trim();
            if ((hex.Length % 2) != 0)
                hex = "0" + hex;

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            return bytes;
        }

        private static string BytesToHex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }

        private static void AppendDebug(StringBuilder debug, string label, string url, HttpStatusCode status, string response, string error, string requestBody)
        {
            if (debug == null)
                return;

            debug.Append(label).Append(" ").Append(url).Append(" -> ");
            debug.Append((int)status != 0 ? "HTTP " + (int)status : "no HTTP status");

            if (!string.IsNullOrWhiteSpace(error))
                debug.Append(", error=").Append(CompactLine(error, 700));

            if (!string.IsNullOrWhiteSpace(response))
                debug.Append(", body=").Append(CompactLine(response, 1100));

            if (!string.IsNullOrWhiteSpace(requestBody))
                debug.Append(", request=").Append(CompactLine(ScrubSecret(requestBody), 500));

            debug.AppendLine();
        }

        private static string WriteDebugFile(string ipAddress, string result, string trace)
        {
            return "";
        }

        private static string FormatDebugPath(string path)
        {
            return "";
        }

        private static string SafeFilePart(string value)
        {
            return Regex.Replace((value ?? "unknown").Trim(), "[^A-Za-z0-9_.-]", "_");
        }

        private static string ScrubSecret(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            string scrubbed = Regex.Replace(value, "(\"password\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, "(\"content\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, "(\"salt\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            return scrubbed;
        }

        private static string GetBrowserUserAgent()
        {
            return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36";
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Compact(string text)
        {
            return (text ?? "").Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
        }

        private static string CompactLine(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            string compact = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (compact.IndexOf("  ", StringComparison.Ordinal) >= 0)
                compact = compact.Replace("  ", " ");

            return Truncate(compact.Trim(), max);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? "";

            return value.Substring(0, maxLength) + "...";
        }
    }
}
