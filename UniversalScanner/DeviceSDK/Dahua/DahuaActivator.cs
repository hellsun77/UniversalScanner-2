using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UniversalScanner.DeviceSDK.Dahua
{
    public static class DahuaActivator
    {
        public static bool ActivateDevice(
            string ipAddress,
            string username,
            string newPassword,
            out string message)
        {
            message = "";

            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                message = "IP address is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(username))
                username = "admin";

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                message = "Password is empty.";
                return false;
            }

            if (newPassword.Length < 8)
            {
                message = "Dahua password must be at least 8 characters.";
                return false;
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
                StringBuilder debug = new StringBuilder();
                string error;
                if (TryWebDevInit(ipAddress, username, newPassword, debug, out error))
                {
                    string path = WriteDebugFile(ipAddress, "SUCCESS", debug.ToString());
                    message = "Dahua device initialized successfully." + FormatDebugPath(path);
                    return true;
                }

                string firstError = error;

                if (TryRpcInit(ipAddress, username, newPassword, debug, out error))
                {
                    string path = WriteDebugFile(ipAddress, "SUCCESS", debug.ToString());
                    message = "Dahua device initialized successfully." + FormatDebugPath(path);
                    return true;
                }

                if (TryCgiAddAdmin(ipAddress, username, newPassword, debug, out error))
                {
                    string path = WriteDebugFile(ipAddress, "SUCCESS", debug.ToString());
                    message = "Dahua admin password set successfully." + FormatDebugPath(path);
                    return true;
                }

                if (TryVerifyLogin(ipAddress, username, newPassword, debug))
                {
                    string path = WriteDebugFile(ipAddress, "SUCCESS", debug.ToString());
                    message = "Dahua device already accepts this admin password." + FormatDebugPath(path);
                    return true;
                }

                string debugPath = WriteDebugFile(ipAddress, "FAILED", debug.ToString());
                string webStatus = DahuaActivationStatusProbe.GetStatus(ipAddress);
                string setupHint = webStatus.IndexOf("Setup Required", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "\r\n\r\nDahua web setup is still waiting for the first-run region/country page. Open http://" + ipAddress + " in the browser, choose the region/language/video standard and press Next, then retry activation here."
                    : "";
                string initializedHint = string.IsNullOrWhiteSpace(setupHint) && LooksAlreadyInitialized(debug.ToString())
                    ? "\r\n\r\nDahua returned HTTP 401 / Invalid Authority. That usually means this device is already initialized and protected by an admin password. Use the current admin password for IP changes, or factory-reset the camera if the password is unknown."
                    : "";
                message =
                    "Dahua initialization failed. " +
                    (string.IsNullOrWhiteSpace(firstError) ? error : firstError) +
                    FormatDebugPath(debugPath) +
                    "\r\n\r\nCurrent Dahua web status: " + webStatus +
                    setupHint +
                    initializedHint +
                    "\r\n\r\nIf the camera shows a security-question/email step in the web page, finish that first in the browser, then retry here.";
                return false;
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = prevCallback;
                ServicePointManager.SecurityProtocol = prevProtocol;
                ServicePointManager.Expect100Continue = prevExpect100;
            }
        }

        private static bool TryWebDevInit(string ipAddress, string username, string password, StringBuilder debug, out string error)
        {
            error = "";

            string[] endpoints =
            {
                "https://" + ipAddress + "/OutsideCmd",
                "http://" + ipAddress + "/OutsideCmd"
            };

            foreach (string endpoint in endpoints)
            {
                if (TryWebDevInitEndpoint(endpoint, ipAddress, username, password, debug, out error))
                    return true;
            }

            return false;
        }

        private static bool TryWebDevInitEndpoint(string endpoint, string ipAddress, string username, string password, StringBuilder debug, out string error)
        {
            error = "";

            string statusResponse;
            HttpStatusCode statusCode;
            string sendError;
            string statusBody = "{\"method\":\"DevInit.getStatus\",\"params\":null,\"id\":1}";
            if (SendText(endpoint, "POST", statusBody, "application/json", null, null, out statusResponse, out statusCode, out sendError))
                AppendDebug(debug, "Dahua DevInit.getStatus", endpoint, statusCode, statusResponse, null, statusBody);
            else
                AppendDebug(debug, "Dahua DevInit.getStatus", endpoint, statusCode, statusResponse, sendError, statusBody);

            string encryptInfoResponse;
            string encryptInfoBody = "{\"method\":\"Security.getEncryptInfo\",\"params\":null,\"id\":2}";
            if (!SendText(endpoint, "POST", encryptInfoBody, "application/json", null, null, out encryptInfoResponse, out statusCode, out sendError))
            {
                error = "Dahua Security.getEncryptInfo failed: " + sendError;
                AppendDebug(debug, "Dahua Security.getEncryptInfo", endpoint, statusCode, encryptInfoResponse, sendError, encryptInfoBody);
                return false;
            }

            AppendDebug(debug, "Dahua Security.getEncryptInfo", endpoint, statusCode, encryptInfoResponse, null, encryptInfoBody);
            if (!IsRpcSuccess(statusCode, encryptInfoResponse))
            {
                error = "Dahua Security.getEncryptInfo rejected: " + DescribeRpcFailure(statusCode, encryptInfoResponse);
                return false;
            }

            string publicKey = ExtractJsonString(encryptInfoResponse, "pub");
            string cipherHint = ExtractJsonString(encryptInfoResponse, "cipher");
            if (string.IsNullOrWhiteSpace(publicKey))
            {
                error = "Dahua Security.getEncryptInfo did not return a public key.";
                return false;
            }

            bool accountOk = false;
            string accountJson = "{\"name\":\"" + JsonEscape(username) + "\",\"pwd\":\"" + JsonEscape(password) + "\"}";
            if (TryEncryptedOutsideCmd(endpoint, "DevInit.account", publicKey, cipherHint, accountJson, debug, out error))
                accountOk = true;
            else if (LooksAlreadyInitialized(error))
                accountOk = true;

            bool accessOk = false;
            string[] countries = GetCountryCandidates(ipAddress, debug);
            foreach (string country in countries)
            {
                string accessJson =
                    "{\"Country\":\"" + JsonEscape(country) + "\"," +
                    "\"Language\":\"English\"," +
                    "\"VideoStandard\":\"PAL\"," +
                    "\"NetAccess\":0," +
                    "\"UpgradeCheck\":0}";

                string accessError;
                if (TryEncryptedOutsideCmd(endpoint, "DevInit.access", publicKey, cipherHint, accessJson, debug, out accessError))
                {
                    accessOk = true;
                    break;
                }

                error = accessError;
            }

            if (accountOk && accessOk)
                return true;

            if (accountOk && TryVerifyLogin(ipAddress, username, password, debug))
                return true;

            if (string.IsNullOrWhiteSpace(error))
                error = "Dahua DevInit account/access did not complete.";

            return false;
        }

        private static bool TryEncryptedOutsideCmd(
            string endpoint,
            string method,
            string publicKey,
            string cipherHint,
            string plainJson,
            StringBuilder debug,
            out string error)
        {
            error = "";

            EncryptedPayload payload;
            try
            {
                payload = EncryptDahuaPayload(publicKey, cipherHint, plainJson);
            }
            catch (Exception ex)
            {
                error = method + " encryption failed: " + ex.GetType().Name + ": " + ex.Message;
                AppendDebug(debug, "Dahua " + method + " encrypt", endpoint, 0, null, error, plainJson);
                return false;
            }

            string body =
                "{\"method\":\"" + method + "\"," +
                "\"params\":{\"salt\":\"" + JsonEscape(payload.Salt) + "\"," +
                "\"cipher\":\"" + JsonEscape(payload.Cipher) + "\"," +
                "\"content\":\"" + JsonEscape(payload.Content) + "\"}," +
                "\"id\":3}";

            string response;
            HttpStatusCode status;
            string sendError;
            if (!SendText(endpoint, "POST", body, "application/json", null, null, out response, out status, out sendError))
            {
                error = method + " failed: " + sendError;
                AppendDebug(debug, "Dahua " + method, endpoint, status, response, sendError, body);
                return false;
            }

            AppendDebug(debug, "Dahua " + method, endpoint, status, response, null, body);
            if (IsRpcSuccess(status, response))
                return true;

            error = method + " rejected: " + DescribeRpcFailure(status, response);
            return false;
        }

        private static bool TryRpcInit(string ipAddress, string username, string password, StringBuilder debug, out string error)
        {
            error = "";

            string escapedUser = JsonEscape(username);
            string escapedPassword = JsonEscape(password);

            string[] endpoints =
            {
                "https://" + ipAddress + "/RPC2",
                "https://" + ipAddress + "/RPC2_Login",
                "http://" + ipAddress + "/RPC2",
                "http://" + ipAddress + "/RPC2_Login"
            };

            string[] bodies =
            {
                "{\"method\":\"global.init\",\"params\":{\"userName\":\"" + escapedUser + "\",\"password\":\"" + escapedPassword + "\",\"phone\":\"\",\"email\":\"\",\"pwdResetWay\":0},\"id\":1}",
                "{\"method\":\"global.init\",\"params\":{\"name\":\"" + escapedUser + "\",\"password\":\"" + escapedPassword + "\",\"phone\":\"\",\"email\":\"\"},\"id\":1}",
                "{\"method\":\"Security.setPassword\",\"params\":{\"userName\":\"" + escapedUser + "\",\"password\":\"" + escapedPassword + "\"},\"id\":1}"
            };

            foreach (string endpoint in endpoints)
            {
                foreach (string body in bodies)
                {
                    string response;
                    HttpStatusCode status;
                    string sendError;

                    if (!SendText(endpoint, "POST", body, "application/json", null, null, out response, out status, out sendError))
                    {
                        error = sendError;
                        AppendDebug(debug, "RPC POST", endpoint, status, response, sendError, body);
                        continue;
                    }

                    AppendDebug(debug, "RPC POST", endpoint, status, response, null, body);

                    if (IsRpcSuccess(status, response))
                        return true;

                    string parsedError = ExtractJsonString(response, "message") ?? ExtractJsonNumber(response, "code");
                    if (!string.IsNullOrWhiteSpace(parsedError))
                        error = "RPC rejected initialization: " + parsedError;
                    else if (!string.IsNullOrWhiteSpace(response))
                        error = "RPC rejected initialization: " + Truncate(response, 180);
                    else
                        error = "RPC returned HTTP " + (int)status + ".";
                }
            }

            return false;
        }

        private static bool TryCgiAddAdmin(string ipAddress, string username, string password, StringBuilder debug, out string error)
        {
            error = "";

            string query =
                "/cgi-bin/userManager.cgi?action=addUser" +
                "&user.Name=" + Uri.EscapeDataString(username) +
                "&user.Password=" + Uri.EscapeDataString(password) +
                "&user.Group=admin" +
                "&user.Memo=" +
                "&user.Reserved=false";

            string[] urls =
            {
                "https://" + ipAddress + query,
                "http://" + ipAddress + query
            };

            foreach (string url in urls)
            {
                string response;
                HttpStatusCode status;
                string sendError;
                if (!SendText(url, "GET", null, null, null, null, out response, out status, out sendError))
                {
                    error = sendError;
                    AppendDebug(debug, "CGI addUser", url, status, response, sendError, null);
                    continue;
                }

                AppendDebug(debug, "CGI addUser", url, status, response, null, null);

                if (IsCgiSuccess(status, response))
                    return true;

                error = "CGI rejected initialization: " + Truncate(response, 180);
            }

            return false;
        }

        private static bool TryVerifyLogin(string ipAddress, string username, string password, StringBuilder debug)
        {
            string response;
            HttpStatusCode status;
            string error;

            string[] urls =
            {
                "https://" + ipAddress + "/cgi-bin/magicBox.cgi?action=getSystemInfo",
                "http://" + ipAddress + "/cgi-bin/magicBox.cgi?action=getSystemInfo"
            };

            foreach (string url in urls)
            {
                bool sent = SendText(
                           url,
                           "GET",
                           null,
                           null,
                           username,
                           password,
                           out response,
                           out status,
                           out error);

                AppendDebug(debug, "Verify login", url, status, response, sent ? null : error, null);

                if (sent &&
                    status == HttpStatusCode.OK &&
                    !IsRejected(response))
                    return true;
            }

            return false;
        }

        private static string[] GetCountryCandidates(string ipAddress, StringBuilder debug)
        {
            string discovered = TryReadCountryCode(ipAddress, debug);
            if (!string.IsNullOrWhiteSpace(discovered))
                return new[] { discovered, "IL", "PS", "US" };

            return new[] { "IL", "PS", "US" };
        }

        private static string TryReadCountryCode(string ipAddress, StringBuilder debug)
        {
            string response;
            HttpStatusCode status;
            string error;
            string url = "https://" + ipAddress + "/web_caps/webCapsConfig?version=2.400&" + DateTime.Now.Ticks.ToString();
            if (!SendText(url, "GET", null, null, null, null, out response, out status, out error))
            {
                AppendDebug(debug, "Dahua webCapsConfig", url, status, response, error, null);
                url = "http://" + ipAddress + "/web_caps/webCapsConfig?version=2.400&" + DateTime.Now.Ticks.ToString();
                if (!SendText(url, "GET", null, null, null, null, out response, out status, out error))
                {
                    AppendDebug(debug, "Dahua webCapsConfig", url, status, response, error, null);
                    return null;
                }
            }

            AppendDebug(debug, "Dahua webCapsConfig", url, status, response, null, null);

            string[] names = { "Israel", "Palestine", "United States", "USA" };
            foreach (string name in names)
            {
                Match m = Regex.Match(response ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*\\[\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (m.Success)
                    return m.Groups[1].Value;
            }

            return null;
        }

        private sealed class EncryptedPayload
        {
            public string Salt;
            public string Cipher;
            public string Content;
        }

        private static EncryptedPayload EncryptDahuaPayload(string publicKey, string cipherHint, string plainJson)
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
                Content = Convert.ToBase64String(encryptedContent)
            };
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

        private static string CreateAsciiKey(int length)
        {
            const string alphabet = "0123456789abcdef";
            byte[] random = new byte[length];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(random);

            char[] chars = new char[length];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[random[i] % alphabet.Length];

            return new string(chars);
        }

        private static byte[] RsaEncrypt(string publicKey, byte[] value)
        {
            string modulusHex = ExtractPublicKeyPart(publicKey, "N");
            string exponentHex = ExtractPublicKeyPart(publicKey, "E");
            if (string.IsNullOrWhiteSpace(modulusHex) || string.IsNullOrWhiteSpace(exponentHex))
                throw new InvalidOperationException("Unsupported Dahua public key format.");

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

        private static string ExtractPublicKeyPart(string publicKey, string name)
        {
            Match m = Regex.Match(publicKey ?? "", "(^|,)" + Regex.Escape(name) + ":([0-9a-fA-F]+)");
            return m.Success ? m.Groups[2].Value : null;
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

        private static bool SendText(
            string url,
            string method,
            string body,
            string contentType,
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
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.UserAgent = "Mozilla/5.0";
                request.Accept = "application/json, text/plain, */*";

                if (!string.IsNullOrWhiteSpace(username))
                {
                    request.Credentials = new NetworkCredential(username, password ?? "");
                    request.PreAuthenticate = false;
                }

                if (body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    request.ContentType = contentType ?? "text/plain";
                    request.ContentLength = bytes.Length;
                    using (Stream stream = request.GetRequestStream())
                        stream.Write(bytes, 0, bytes.Length);
                }

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
                    HttpWebResponse response = (HttpWebResponse)ex.Response;
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

                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + Truncate(ex.Message, 160);
                return false;
            }
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

        private static bool IsCgiSuccess(HttpStatusCode status, string response)
        {
            if ((int)status < 200 || (int)status >= 300)
                return false;

            if (string.IsNullOrWhiteSpace(response))
                return true;

            return response.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private static bool IsRejected(string response)
        {
            return string.IsNullOrWhiteSpace(response) ||
                   response.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("Forbidden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Compact(string text)
        {
            return (text ?? "").Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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

        private static void AppendDebug(
            StringBuilder debug,
            string label,
            string url,
            HttpStatusCode status,
            string response,
            string error,
            string requestBody)
        {
            if (debug == null)
                return;

            debug.Append(label)
                .Append(" ")
                .Append(ScrubPassword(url))
                .Append(" -> ");

            if ((int)status != 0)
                debug.Append("HTTP ").Append((int)status);
            else
                debug.Append("no HTTP status");

            if (!string.IsNullOrWhiteSpace(error))
                debug.Append(", error=").Append(CompactLine(error, 140));

            if (!string.IsNullOrWhiteSpace(response))
                debug.Append(", body=").Append(CompactLine(response, 220));

            if (!string.IsNullOrWhiteSpace(requestBody))
                debug.Append(", request=").Append(CompactLine(ScrubPassword(requestBody), 180));

            debug.AppendLine();
        }

        private static string ScrubPassword(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            string scrubbed = Regex.Replace(value, "(password=)[^&\\s\"]+", "$1***", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, "(\"password\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, "(\"pwd\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, "(\"content\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, "(\"salt\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            return scrubbed;
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

        private static string WriteDebugFile(string ipAddress, string result, string trace)
        {
            StringBuilder file = new StringBuilder();
            file.AppendLine("Dahua activation debug");
            file.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            file.AppendLine("IP: " + ipAddress);
            file.AppendLine("Result: " + result);
            file.AppendLine();
            file.AppendLine("Trace:");
            file.AppendLine(trace ?? "");

            return DahuaDebugLog.WriteActivation(ipAddress, file.ToString());
        }

        private static string FormatDebugPath(string path)
        {
            return "";
        }

        private static bool LooksAlreadyInitialized(string debug)
        {
            return !string.IsNullOrWhiteSpace(debug) &&
                   (debug.IndexOf("HTTP 401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    debug.IndexOf("Invalid Authority", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }
    }
}
