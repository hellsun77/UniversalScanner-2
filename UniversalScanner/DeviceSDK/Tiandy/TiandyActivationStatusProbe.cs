using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using UniversalScanner.DeviceSDK.Honeywell;

namespace UniversalScanner.DeviceSDK.Tiandy
{
    public static class TiandyActivationStatusProbe
    {
        public static string GetStatus(string ipAddress)
        {
            try
            {
                string response = TiandyWebApi.SendRaw(ipAddress, "GET", "/CGI/System/ActivationStatus", null, null);
                string activated = TiandyWebApi.GetElementText(TiandyWebApi.GetBody(response), "activated");

                if (activated.Equals("active", StringComparison.OrdinalIgnoreCase))
                    return "Active";

                if (activated.Equals("inactive", StringComparison.OrdinalIgnoreCase) ||
                    activated.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                    activated.Equals("0", StringComparison.OrdinalIgnoreCase))
                    return "Inactive";
            }
            catch
            {
            }

            return "Unknown";
        }
    }

    public static class TiandyInfoProbe
    {
        public static string LastModelDebugPath { get; private set; }
        public static string LastUniqueId { get; private set; }

        public static async Task<string> GetModelAsync(string ipAddress, string username = null, string password = null)
        {
            LastModelDebugPath = "";
            LastUniqueId = "";
            StringBuilder debug = new StringBuilder();
            debug.AppendLine("Tiandy model lookup debug");
            debug.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            debug.AppendLine("IP: " + ipAddress);
            debug.AppendLine("Username: " + (string.IsNullOrWhiteSpace(username) ? "" : username.Trim()));
            debug.AppendLine();

            string model = GetUnauthenticatedModel(ipAddress);
            if (!string.IsNullOrWhiteSpace(model))
            {
                debug.AppendLine("Unauthenticated model found: " + model);
                return model;
            }

            debug.AppendLine("Unauthenticated model lookup did not return a model.");

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                model = GetAuthenticatedModel(ipAddress, username, password, debug);
                if (!string.IsNullOrWhiteSpace(model))
                    return model;

                model = await GetOnvifModelAsync(ipAddress, username, password);
                if (!string.IsNullOrWhiteSpace(model))
                {
                    debug.AppendLine("ONVIF model found: " + model);
                    return model;
                }

                debug.AppendLine("ONVIF model lookup did not return a model.");
            }
            else
            {
                debug.AppendLine("Authenticated model lookup skipped because username/password were not supplied.");
            }

            return "";
        }

        private static string GetAuthenticatedModel(string ipAddress, string username, string password, StringBuilder debug)
        {
            string[] paths =
            {
                "/CGI/System/deviceInfo",
                "/ISAPI/System/deviceInfo",
                "/CGI/System/DeviceInfo"
            };

            foreach (string path in paths)
            {
                try
                {
                    if (debug != null)
                        debug.AppendLine("Authenticated model path: " + path);

                    string response = TiandyWebApi.SendRawDigest(
                        ipAddress,
                        "GET",
                        path,
                        null,
                        null,
                        username,
                        password);

                    if (debug != null)
                    {
                        debug.AppendLine("Response:");
                        debug.AppendLine(TiandyWebApi.Truncate(response, 2000));
                        debug.AppendLine();
                    }

                    string body = TiandyWebApi.GetBody(response);
                    LastUniqueId = GetUniqueId(body);
                    string model = FirstNonEmpty(
                        TiandyWebApi.GetElementText(body, "model"),
                        TiandyWebApi.GetElementText(body, "modelNumber"),
                        TiandyWebApi.GetElementText(body, "deviceModel"),
                        TiandyWebApi.GetElementText(body, "deviceName"));

                    if (!string.IsNullOrWhiteSpace(model))
                    {
                        if (debug != null)
                            debug.AppendLine("Authenticated model found: " + model);
                        return model;
                    }
                }
                catch (Exception ex)
                {
                    if (debug != null)
                    {
                        debug.AppendLine("Exception on " + path + ":");
                        debug.AppendLine(ex.ToString());
                        debug.AppendLine();
                    }
                }
            }

            return "";
        }

        private static string GetUnauthenticatedModel(string ipAddress)
        {
            string[] paths =
            {
                "/ISAPI/System/deviceInfo",
                "/CGI/System/DeviceInfo",
                "/CGI/Device/DeviceInfo"
            };

            foreach (string path in paths)
            {
                try
                {
                    string response = TiandyWebApi.SendRaw(ipAddress, "GET", path, null, null);
                    string body = TiandyWebApi.GetBody(response);
                    if (response.IndexOf(" 401 ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        body.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    LastUniqueId = GetUniqueId(body);
                    string model = FirstNonEmpty(
                        TiandyWebApi.GetElementText(body, "model"),
                        TiandyWebApi.GetElementText(body, "modelNumber"),
                        TiandyWebApi.GetElementText(body, "deviceModel"),
                        TiandyWebApi.GetElementText(body, "deviceName"));

                    if (!string.IsNullOrWhiteSpace(model))
                        return model;
                }
                catch
                {
                }
            }

            return "";
        }

        private static string GetUniqueId(string body)
        {
            return FirstNonEmpty(
                TiandyWebApi.GetElementText(body, "serialNumber"),
                TiandyWebApi.GetElementText(body, "factoryNumber"),
                TiandyWebApi.GetElementText(body, "deviceID"),
                TiandyWebApi.GetElementText(body, "deviceId"));
        }

        private static async Task<string> GetOnvifModelAsync(string ipAddress, string username, string password)
        {
            try
            {
                string response = await OnvifSoap.SendAsync(
                    ipAddress,
                    username,
                    password,
                    "<GetDeviceInformation xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>");

                return OnvifSoap.ExtractValue(response, "Model") ?? "";
            }
            catch
            {
                return "";
            }
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
    }

    public static class TiandyActivator
    {
        public static bool ActivateDevice(string ipAddress, string username, string newPassword, out string message)
        {
            message = "";
            StringBuilder debug = new StringBuilder();

            if (string.IsNullOrWhiteSpace(username))
                username = "admin";

            try
            {
                string validationError = ValidatePassword(newPassword);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    message = validationError;
                    return false;
                }

                debug.AppendLine("Tiandy activation debug");
                debug.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                debug.AppendLine("IP: " + ipAddress);
                debug.AppendLine("Username: " + username);
                debug.AppendLine("Password length: " + (newPassword == null ? 0 : newPassword.Length));
                debug.AppendLine();

                string status = TiandyActivationStatusProbe.GetStatus(ipAddress);
                debug.AppendLine("Initial status: " + status);

                if (status.Equals("Active", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Tiandy camera is already active.";
                    return true;
                }

                string payload = BuildActiveInfo(username, newPassword);
                debug.AppendLine("ActiveInfo payload:");
                debug.AppendLine(payload);
                debug.AppendLine();

                string response = TiandyWebApi.SendRaw(
                    ipAddress,
                    "PUT",
                    "/ISAPI/Security/users/active",
                    payload,
                    "application/xml; charset=utf-8");

                debug.AppendLine("Activation response:");
                debug.AppendLine(response);
                debug.AppendLine();

                string afterStatus = TiandyActivationStatusProbe.GetStatus(ipAddress);
                debug.AppendLine("Status after activation: " + afterStatus);

                if (afterStatus.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
                    TiandyWebApi.IsSuccessStatus(response))
                {
                    message = "Tiandy password created successfully.";
                    return true;
                }

                message = "Tiandy activation failed.\r\n\r\n" +
                    TiandyWebApi.Truncate(TiandyWebApi.GetBody(response), 700);
                return false;
            }
            catch (Exception ex)
            {
                debug.AppendLine();
                debug.AppendLine("Exception:");
                debug.AppendLine(ex.ToString());

                message = "Tiandy activation failed: " + ex.Message;
                return false;
            }
        }

        public static string ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return "Tiandy password is required.";

            if (password.Length < 8 || password.Length > 15)
                return "Tiandy password must be 8-15 characters.";

            if (Regex.IsMatch(password, "[\"':;#\\\\\\s]"))
                return "Tiandy password cannot contain quotes, colons, semicolons, #, spaces, or backslashes.";

            int classes = 0;
            if (Regex.IsMatch(password, "[0-9]")) classes++;
            if (Regex.IsMatch(password, "[a-z]")) classes++;
            if (Regex.IsMatch(password, "[A-Z]")) classes++;
            if (Regex.IsMatch(password, "[^0-9A-Za-z]")) classes++;

            if (classes < 2)
                return "Tiandy password must use at least two character types.";

            return "";
        }

        private static string BuildActiveInfo(string username, string password)
        {
            string user = string.IsNullOrWhiteSpace(username) ? "admin" : username.Trim();

            return
                "<ActiveInfo>" +
                "<userName>" + SecurityElement.Escape(TiandyWebApi.EncryptByDes(user)) + "</userName>" +
                "<password>" + SecurityElement.Escape(TiandyWebApi.EncryptByDes(password ?? "")) + "</password>" +
                "<userLevel>" + SecurityElement.Escape(TiandyWebApi.EncryptByDes("Administrator")) + "</userLevel>" +
                "</ActiveInfo>";
        }

    }

    internal static class TiandyWebApi
    {
        public static string SendRaw(string ipAddress, string method, string path, string body, string contentType)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("IP address is empty.", "ipAddress");

            byte[] bodyBytes = string.IsNullOrEmpty(body) ? new byte[0] : Encoding.UTF8.GetBytes(body);

            StringBuilder request = new StringBuilder();
            request.Append((method ?? "GET").ToUpperInvariant()).Append(' ').Append(path ?? "/").Append(" HTTP/1.0\r\n");
            request.Append("Host: ").Append(ipAddress.Trim()).Append("\r\n");
            request.Append("Connection: close\r\n");
            request.Append("Accept: */*\r\n");
            request.Append("User-Agent: UniversalScanner\r\n");
            if (bodyBytes.Length > 0)
            {
                request.Append("Content-Type: ").Append(string.IsNullOrWhiteSpace(contentType) ? "application/xml" : contentType).Append("\r\n");
                request.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
            }
            request.Append("\r\n");

            using (TcpClient client = new TcpClient())
            {
                IAsyncResult connect = client.BeginConnect(ipAddress.Trim(), 80, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(2000)))
                    throw new TimeoutException("Connection to Tiandy HTTP port timed out.");
                client.EndConnect(connect);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 3000;
                    stream.WriteTimeout = 3000;

                    byte[] headerBytes = Encoding.ASCII.GetBytes(request.ToString());
                    stream.Write(headerBytes, 0, headerBytes.Length);
                    if (bodyBytes.Length > 0)
                        stream.Write(bodyBytes, 0, bodyBytes.Length);

                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] buffer = new byte[4096];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                            ms.Write(buffer, 0, read);

                        return Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
            }
        }

        public static string SendRawDigest(
            string ipAddress,
            string method,
            string path,
            string body,
            string contentType,
            string username,
            string password)
        {
            string firstResponse = SendRaw(ipAddress, method, path, body, contentType);
            if (firstResponse.IndexOf(" 401 ", StringComparison.OrdinalIgnoreCase) < 0)
                return firstResponse;

            string challenge = GetHeaderValue(firstResponse, "WWW-Authenticate");
            if (string.IsNullOrWhiteSpace(challenge) ||
                challenge.IndexOf("Digest", StringComparison.OrdinalIgnoreCase) < 0)
                return firstResponse;

            string realm = GetDigestValue(challenge, "realm");
            string nonce = GetDigestValue(challenge, "nonce");
            string qop = GetDigestValue(challenge, "qop");
            string opaque = GetDigestValue(challenge, "opaque");
            string cleanMethod = (method ?? "GET").ToUpperInvariant();
            string cleanPath = string.IsNullOrWhiteSpace(path) ? "/" : path;
            string cleanUser = string.IsNullOrWhiteSpace(username) ? "admin" : username.Trim();

            List<string> userCandidates = new List<string>();
            userCandidates.Add(cleanUser);
            if (cleanUser.Equals("admin", StringComparison.OrdinalIgnoreCase))
            {
                if (!userCandidates.Contains("Admin"))
                    userCandidates.Add("Admin");
                if (!userCandidates.Contains("admin"))
                    userCandidates.Add("admin");
            }

            string lastResponse = firstResponse;
            foreach (string userCandidate in userCandidates)
            {
                string auth = BuildDigestAuthorization(
                    userCandidate,
                    password ?? "",
                    realm,
                    nonce,
                    qop,
                    opaque,
                    cleanMethod,
                    cleanPath);

                lastResponse = SendRaw(ipAddress, method, path, body, contentType, auth, false);
                if (lastResponse.IndexOf(" 401 ", StringComparison.OrdinalIgnoreCase) < 0)
                    return lastResponse;
            }

            return lastResponse;
        }

        private static string BuildDigestAuthorization(
            string username,
            string password,
            string realm,
            string nonce,
            string qop,
            string opaque,
            string method,
            string uri)
        {
            string nc = "00000001";
            string cnonce = GenerateCnonce();
            string cleanQop = qop;

            string ha1 = Md5Hex(username + ":" + realm + ":" + password);
            string ha2 = Md5Hex(method + ":" + uri);
            string digestResponse;
            if (!string.IsNullOrWhiteSpace(cleanQop))
            {
                string qopValue = cleanQop.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "auth"
                    : cleanQop.Split(',')[0].Trim();
                digestResponse = Md5Hex(ha1 + ":" + nonce + ":" + nc + ":" + cnonce + ":" + qopValue + ":" + ha2);
                cleanQop = qopValue;
            }
            else
            {
                digestResponse = Md5Hex(ha1 + ":" + nonce + ":" + ha2);
            }

            StringBuilder auth = new StringBuilder();
            auth.Append("Digest username=\"").Append(username).Append("\"");
            auth.Append(",realm=\"").Append(realm).Append("\"");
            auth.Append(",nonce=\"").Append(nonce).Append("\"");
            auth.Append(",uri=\"").Append(uri).Append("\"");
            if (!string.IsNullOrWhiteSpace(cleanQop))
            {
                auth.Append(",cnonce=\"").Append(cnonce).Append("\"");
                auth.Append(",nc=").Append(nc);
            }
            auth.Append(",response=\"").Append(digestResponse).Append("\"");
            if (!string.IsNullOrWhiteSpace(cleanQop))
                auth.Append(",qop=\"").Append(cleanQop).Append("\"");
            if (!string.IsNullOrWhiteSpace(opaque))
                auth.Append(",opaque=\"").Append(opaque).Append("\"");

            return auth.ToString();
        }

        private static string SendRaw(string ipAddress, string method, string path, string body, string contentType, string authorization)
        {
            return SendRaw(ipAddress, method, path, body, contentType, authorization, false);
        }

        private static string SendRaw(string ipAddress, string method, string path, string body, string contentType, string authorization, bool http11)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("IP address is empty.", "ipAddress");

            byte[] bodyBytes = string.IsNullOrEmpty(body) ? new byte[0] : Encoding.UTF8.GetBytes(body);

            StringBuilder request = new StringBuilder();
            request.Append((method ?? "GET").ToUpperInvariant()).Append(' ').Append(path ?? "/").Append(http11 ? " HTTP/1.1\r\n" : " HTTP/1.0\r\n");
            request.Append("Host: ").Append(ipAddress.Trim()).Append("\r\n");
            request.Append("Connection: close\r\n");
            request.Append("Accept: */*\r\n");
            request.Append("User-Agent: UniversalScanner\r\n");
            if (!string.IsNullOrWhiteSpace(authorization))
                request.Append("Authorization: ").Append(authorization).Append("\r\n");
            if (bodyBytes.Length > 0)
            {
                request.Append("Content-Type: ").Append(string.IsNullOrWhiteSpace(contentType) ? "application/xml" : contentType).Append("\r\n");
                request.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
            }
            request.Append("\r\n");

            using (TcpClient client = new TcpClient())
            {
                IAsyncResult connect = client.BeginConnect(ipAddress.Trim(), 80, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(2000)))
                    throw new TimeoutException("Connection to Tiandy HTTP port timed out.");
                client.EndConnect(connect);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 3000;
                    stream.WriteTimeout = 3000;

                    byte[] headerBytes = Encoding.ASCII.GetBytes(request.ToString());
                    stream.Write(headerBytes, 0, headerBytes.Length);
                    if (bodyBytes.Length > 0)
                        stream.Write(bodyBytes, 0, bodyBytes.Length);

                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] buffer = new byte[4096];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                            ms.Write(buffer, 0, read);

                        return Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
            }
        }

        public static string GetBody(string response)
        {
            if (string.IsNullOrEmpty(response))
                return "";

            int split = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (split >= 0)
                return response.Substring(split + 4);

            split = response.IndexOf("\n\n", StringComparison.Ordinal);
            return split >= 0 ? response.Substring(split + 2) : response;
        }

        private static string GetHeaderValue(string response, string headerName)
        {
            if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(headerName))
                return "";

            using (StringReader reader = new StringReader(response))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0)
                        break;

                    int colon = line.IndexOf(':');
                    if (colon <= 0)
                        continue;

                    string name = line.Substring(0, colon).Trim();
                    if (name.Equals(headerName, StringComparison.OrdinalIgnoreCase))
                        return line.Substring(colon + 1).Trim();
                }
            }

            return "";
        }

        private static string GetDigestValue(string challenge, string name)
        {
            if (string.IsNullOrWhiteSpace(challenge) || string.IsNullOrWhiteSpace(name))
                return "";

            Match match = Regex.Match(
                challenge,
                name + "\\s*=\\s*\"([^\"]*)\"",
                RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;

            match = Regex.Match(
                challenge,
                name + "\\s*=\\s*([^,\\s]+)",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private static string GenerateCnonce()
        {
            byte[] bytes = new byte[8];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(bytes);

            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        private static string Md5Hex(string value)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.ASCII.GetBytes(value ?? ""));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static bool IsSuccessStatus(string response)
        {
            Match match = Regex.Match(response ?? "", @"^HTTP/\d(?:\.\d)?\s+(\d{3})", RegexOptions.IgnoreCase);
            int code;
            return match.Success && int.TryParse(match.Groups[1].Value, out code) && code >= 200 && code < 300;
        }

        public static string GetElementText(string xml, string localName)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(localName))
                return "";

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml.Trim());

                foreach (XmlElement element in doc.GetElementsByTagName("*"))
                {
                    if (element.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                        return element.InnerText.Trim();
                }
            }
            catch
            {
            }

            return "";
        }

        public static string EncryptByDes(string value)
        {
            int[] words = TiandyDesEncrypt(value ?? "", ActivationKey());
            byte[] bytes = new byte[words.Length * 4];
            for (int i = 0; i < words.Length; i++)
            {
                unchecked
                {
                    uint word = (uint)words[i];
                    bytes[i * 4] = (byte)((word >> 24) & 0xff);
                    bytes[i * 4 + 1] = (byte)((word >> 16) & 0xff);
                    bytes[i * 4 + 2] = (byte)((word >> 8) & 0xff);
                    bytes[i * 4 + 3] = (byte)(word & 0xff);
                }
            }

            return Convert.ToBase64String(bytes);
        }

        public static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? "";

            return value.Substring(0, max) + "...";
        }

        private static string ActivationKey()
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("admin1111ZzYyXxWw12345678"));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));

                return sb.ToString().ToUpperInvariant().Substring(12, 8);
            }
        }

        private static int[] TiandyDesEncrypt(string text, string key)
        {
            int[] input = TiandyUtf8Bytes(text);
            int remainder = input.Length % 8;
            int blocks = (input.Length + 7) / 8;
            if (remainder > 0)
            {
                int[] padded = new int[blocks * 8];
                Array.Copy(input, padded, input.Length);
                padded[padded.Length - 1] = 8 - remainder;
                input = padded;
            }

            int[] keyBytes = new int[key.Length];
            for (int i = 0; i < key.Length; i++)
                keyBytes[i] = key[i];

            List<int> keyBits = new List<int>();
            BytesToBits(keyBytes, keyBits);
            List<int[]> subKeys = BuildSubKeys(keyBits);

            List<int> result = new List<int>();
            for (int block = 0; block < blocks; block++)
            {
                int[] blockBytes = new int[8];
                Array.Copy(input, block * 8, blockBytes, 0, 8);

                List<int> bits = new List<int>();
                BytesToBits(blockBytes, bits);
                PermuteInPlace(bits, H);

                for (int round = 0; round < 16; round++)
                {
                    List<int> work = new List<int>(bits.GetRange(32, 32));
                    while (work.Count < 48)
                        work.Add(0);
                    PermuteInPlace(work, J);
                    XorInPlace(work, subKeys[round], 48);
                    SBox(work);
                    PermuteInPlace(work, K);
                    XorInPlace(bits, work, 32);
                    if (round != 15)
                        SwapHalves(bits);
                }

                PermuteInPlace(bits, I);
                BitsToWords(bits, result);
            }

            return result.ToArray();
        }

        private static int[] TiandyUtf8Bytes(string value)
        {
            List<int> bytes = new List<int>();
            for (int i = 0; i < value.Length; i++)
            {
                int ch = value[i];
                if (ch >= 65536 && ch <= 2097151)
                {
                    bytes.Add((ch >> 18 & 7) | 240);
                    bytes.Add((ch >> 12 & 63) | 128);
                    bytes.Add((ch >> 6 & 63) | 128);
                    bytes.Add((ch & 63) | 128);
                }
                else if (ch >= 2048 && ch <= 65535)
                {
                    bytes.Add((ch >> 12 & 15) | 224);
                    bytes.Add((ch >> 6 & 63) | 128);
                    bytes.Add((ch & 63) | 128);
                }
                else if (ch >= 128 && ch <= 2047)
                {
                    bytes.Add((ch >> 6 & 31) | 192);
                    bytes.Add((ch & 63) | 128);
                }
                else
                {
                    bytes.Add(ch & 255);
                }
            }

            return bytes.ToArray();
        }

        private static void BytesToBits(int[] bytes, List<int> bits)
        {
            for (int i = 0; i < 8; i++)
            {
                int value = i < bytes.Length ? bytes[i] : 0;
                for (int bit = 0; bit < 8; bit++)
                    bits.Add((value >> bit) & 1);
            }
        }

        private static void BitsToWords(List<int> bits, List<int> words)
        {
            int[] outWords = { 0, 0 };
            int[] bytes = new int[8];
            for (int i = 0; i < 8; i++)
            {
                for (int bit = 0; bit < 8; bit++)
                    bytes[i] |= bits[8 * i + bit] << bit;

                unchecked
                {
                    outWords[i >> 2] |= bytes[i] << (24 - (i % 4) * 8);
                }
            }

            words.Add(outWords[0]);
            words.Add(outWords[1]);
        }

        private static List<int[]> BuildSubKeys(List<int> keyBits)
        {
            List<int> key = new List<int>();
            for (int i = 0; i < 56; i++)
                key.Add(keyBits[E[i]]);

            List<int[]> subKeys = new List<int[]>();
            for (int round = 0; round < 16; round++)
            {
                int shift = F[round];
                List<int> rotated = new List<int>();
                rotated.AddRange(key.GetRange(shift, 28 - shift));
                rotated.AddRange(key.GetRange(0, shift));
                rotated.AddRange(key.GetRange(28 + shift, 28 - shift));
                rotated.AddRange(key.GetRange(28, shift));
                key = rotated;

                int[] subKey = new int[48];
                for (int i = 0; i < 48; i++)
                    subKey[i] = key[G[i]];
                subKeys.Add(subKey);
            }

            return subKeys;
        }

        private static void PermuteInPlace(List<int> bits, int[] table)
        {
            int[] copy = new int[table.Length];
            for (int i = 0; i < table.Length; i++)
                copy[i] = bits[table[i]];

            for (int i = 0; i < table.Length; i++)
                bits[i] = copy[i];
        }

        private static void XorInPlace(List<int> target, IList<int> source, int count)
        {
            for (int i = 0; i < count; i++)
                target[i] ^= source[i];
        }

        private static void SBox(List<int> bits)
        {
            for (int box = 0; box < 8; box++)
            {
                int inStart = 6 * box;
                int outStart = box << 2;
                int row = (bits[inStart] << 1) + bits[inStart + 5];
                int col = (bits[inStart + 1] << 3) + (bits[inStart + 2] << 2) + (bits[inStart + 3] << 1) + bits[inStart + 4];
                int value = L[box][row][col];
                bits[outStart] = (value & 8) >> 3;
                bits[outStart + 1] = (value & 4) >> 2;
                bits[outStart + 2] = (value & 2) >> 1;
                bits[outStart + 3] = value & 1;
            }

            while (bits.Count > 32)
                bits.RemoveAt(bits.Count - 1);
        }

        private static void SwapHalves(List<int> bits)
        {
            for (int i = 0; i < 32; i++)
            {
                int temp = bits[i + 32];
                bits[i + 32] = bits[i];
                bits[i] = temp;
            }
        }

        private static readonly int[] H = { 57, 49, 41, 33, 25, 17, 9, 1, 59, 51, 43, 35, 27, 19, 11, 3, 61, 53, 45, 37, 29, 21, 13, 5, 63, 55, 47, 39, 31, 23, 15, 7, 56, 48, 40, 32, 24, 16, 8, 0, 58, 50, 42, 34, 26, 18, 10, 2, 60, 52, 44, 36, 28, 20, 12, 4, 62, 54, 46, 38, 30, 22, 14, 6 };
        private static readonly int[] I = { 39, 7, 47, 15, 55, 23, 63, 31, 38, 6, 46, 14, 54, 22, 62, 30, 37, 5, 45, 13, 53, 21, 61, 29, 36, 4, 44, 12, 52, 20, 60, 28, 35, 3, 43, 11, 51, 19, 59, 27, 34, 2, 42, 10, 50, 18, 58, 26, 33, 1, 41, 9, 49, 17, 57, 25, 32, 0, 40, 8, 48, 16, 56, 24 };
        private static readonly int[] J = { 31, 0, 1, 2, 3, 4, 3, 4, 5, 6, 7, 8, 7, 8, 9, 10, 11, 12, 11, 12, 13, 14, 15, 16, 15, 16, 17, 18, 19, 20, 19, 20, 21, 22, 23, 24, 23, 24, 25, 26, 27, 28, 27, 28, 29, 30, 31, 0 };
        private static readonly int[] K = { 15, 6, 19, 20, 28, 11, 27, 16, 0, 14, 22, 25, 4, 17, 30, 9, 1, 7, 23, 13, 31, 26, 2, 8, 18, 12, 29, 5, 21, 10, 3, 24 };
        private static readonly int[] E = { 56, 48, 40, 32, 24, 16, 8, 0, 57, 49, 41, 33, 25, 17, 9, 1, 58, 50, 42, 34, 26, 18, 10, 2, 59, 51, 43, 35, 62, 54, 46, 38, 30, 22, 14, 6, 61, 53, 45, 37, 29, 21, 13, 5, 60, 52, 44, 36, 28, 20, 12, 4, 27, 19, 11, 3 };
        private static readonly int[] G = { 13, 16, 10, 23, 0, 4, 2, 27, 14, 5, 20, 9, 22, 18, 11, 3, 25, 7, 15, 6, 26, 19, 12, 1, 40, 51, 30, 36, 46, 54, 29, 39, 50, 44, 32, 47, 43, 48, 38, 55, 33, 52, 45, 41, 49, 35, 28, 31 };
        private static readonly int[] F = { 1, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 1 };
        private static readonly int[][][] L =
        {
            new[] { new[] { 14, 4, 13, 1, 2, 15, 11, 8, 3, 10, 6, 12, 5, 9, 0, 7 }, new[] { 0, 15, 7, 4, 14, 2, 13, 1, 10, 6, 12, 11, 9, 5, 3, 8 }, new[] { 4, 1, 14, 8, 13, 6, 2, 11, 15, 12, 9, 7, 3, 10, 5, 0 }, new[] { 15, 12, 8, 2, 4, 9, 1, 7, 5, 11, 3, 14, 10, 0, 6, 13 } },
            new[] { new[] { 15, 1, 8, 14, 6, 11, 3, 4, 9, 7, 2, 13, 12, 0, 5, 10 }, new[] { 3, 13, 4, 7, 15, 2, 8, 14, 12, 0, 1, 10, 6, 9, 11, 5 }, new[] { 0, 14, 7, 11, 10, 4, 13, 1, 5, 8, 12, 6, 9, 3, 2, 15 }, new[] { 13, 8, 10, 1, 3, 15, 4, 2, 11, 6, 7, 12, 0, 5, 14, 9 } },
            new[] { new[] { 10, 0, 9, 14, 6, 3, 15, 5, 1, 13, 12, 7, 11, 4, 2, 8 }, new[] { 13, 7, 0, 9, 3, 4, 6, 10, 2, 8, 5, 14, 12, 11, 15, 1 }, new[] { 13, 6, 4, 9, 8, 15, 3, 0, 11, 1, 2, 12, 5, 10, 14, 7 }, new[] { 1, 10, 13, 0, 6, 9, 8, 7, 4, 15, 14, 3, 11, 5, 2, 12 } },
            new[] { new[] { 7, 13, 14, 3, 0, 6, 9, 10, 1, 2, 8, 5, 11, 12, 4, 15 }, new[] { 13, 8, 11, 5, 6, 15, 0, 3, 4, 7, 2, 12, 1, 10, 14, 9 }, new[] { 10, 6, 9, 0, 12, 11, 7, 13, 15, 1, 3, 14, 5, 2, 8, 4 }, new[] { 3, 15, 0, 6, 10, 1, 13, 8, 9, 4, 5, 11, 12, 7, 2, 14 } },
            new[] { new[] { 2, 12, 4, 1, 7, 10, 11, 6, 8, 5, 3, 15, 13, 0, 14, 9 }, new[] { 14, 11, 2, 12, 4, 7, 13, 1, 5, 0, 15, 10, 3, 9, 8, 6 }, new[] { 4, 2, 1, 11, 10, 13, 7, 8, 15, 9, 12, 5, 6, 3, 0, 14 }, new[] { 11, 8, 12, 7, 1, 14, 2, 13, 6, 15, 0, 9, 10, 4, 5, 3 } },
            new[] { new[] { 12, 1, 10, 15, 9, 2, 6, 8, 0, 13, 3, 4, 14, 7, 5, 11 }, new[] { 10, 15, 4, 2, 7, 12, 9, 5, 6, 1, 13, 14, 0, 11, 3, 8 }, new[] { 9, 14, 15, 5, 2, 8, 12, 3, 7, 0, 4, 10, 1, 13, 11, 6 }, new[] { 4, 3, 2, 12, 9, 5, 15, 10, 11, 14, 1, 7, 6, 0, 8, 13 } },
            new[] { new[] { 4, 11, 2, 14, 15, 0, 8, 13, 3, 12, 9, 7, 5, 10, 6, 1 }, new[] { 13, 0, 11, 7, 4, 9, 1, 10, 14, 3, 5, 12, 2, 15, 8, 6 }, new[] { 1, 4, 11, 13, 12, 3, 7, 14, 10, 15, 6, 8, 0, 5, 9, 2 }, new[] { 6, 11, 13, 8, 1, 4, 10, 7, 9, 5, 0, 15, 14, 2, 3, 12 } },
            new[] { new[] { 13, 2, 8, 4, 6, 15, 11, 1, 10, 9, 3, 14, 5, 0, 12, 7 }, new[] { 1, 15, 13, 8, 10, 3, 7, 4, 12, 5, 6, 11, 0, 14, 9, 2 }, new[] { 7, 11, 4, 1, 9, 12, 14, 2, 0, 6, 10, 13, 15, 3, 5, 8 }, new[] { 2, 1, 14, 7, 4, 10, 8, 13, 15, 12, 9, 0, 3, 5, 6, 11 } }
        };
    }
}
