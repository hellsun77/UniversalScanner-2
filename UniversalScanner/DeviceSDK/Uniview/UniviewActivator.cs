using System;
using System.IO;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using UniversalScanner.DeviceSDK.Honeywell;

namespace UniversalScanner.DeviceSDK.Uniview
{
    public static class UniviewActivator
    {
        private static readonly string[] FactoryPasswords = { "123456", "admin" };

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
                message = "Uniview password must be at least 8 characters.";
                return false;
            }

            // Attempt to accept the privacy policy first so the web UI wizard is bypassed
            AcceptPrivacyPolicy(ipAddress, username, "123456");

            if (TryActivateViaLapi(ipAddress, username, "123456", newPassword, out message))
            {
                UniviewActivationStatusProbe.ClearCache(ipAddress);
                return true;
            }

            foreach (string factoryPassword in FactoryPasswords)
            {
                try
                {
                    string setUserBody =
                        "<SetUser xmlns=\"http://www.onvif.org/ver10/device/wsdl\">" +
                        "<User>" +
                        "<Username>" + SecurityElement.Escape(username) + "</Username>" +
                        "<Password>" + SecurityElement.Escape(newPassword) + "</Password>" +
                        "<UserLevel>Administrator</UserLevel>" +
                        "</User>" +
                        "</SetUser>";

                    string response = OnvifSoap
                        .SendAsync(ipAddress, username, factoryPassword, setUserBody)
                        .GetAwaiter()
                        .GetResult();

                    if (IsRejected(response))
                        continue;

                    string verifyBody = "<GetDeviceInformation xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>";
                    string verifyResponse = OnvifSoap
                        .SendAsync(ipAddress, username, newPassword, verifyBody)
                        .GetAwaiter()
                        .GetResult();

                    if (!IsRejected(verifyResponse))
                    {
                        UniviewActivationStatusProbe.ClearCache(ipAddress);
                        message = "Uniview admin password set successfully.";
                        return true;
                    }
                }
                catch
                {
                }
            }

            message =
                "Uniview activation failed. For brand-new devices, open the camera web page once, " +
                "log in as admin / 123456, check the privacy agreement, press Next, then retry here " +
                "or finish the password change in the browser.";
            return false;
        }

        private static bool TryActivateViaLapi(
            string ipAddress,
            string username,
            string oldPassword,
            string newPassword,
            out string message)
        {
            message = "";

            // 1. Fetch RSA PublicKey
            string n = "";
            string e = "";
            bool hasRsa = GetRsaPublicKey(ipAddress, username, oldPassword, out n, out e);

            // 2. Try modern V1.1 endpoint with RSA PIN if RSA keys are available
            if (hasRsa && !string.IsNullOrEmpty(n) && !string.IsNullOrEmpty(e))
            {
                try
                {
                    string pin = EncryptRSA(newPassword, n, e);
                    string bodyV11 = "{\"UerID\":0,\"UserName\":\"" + username + "\",\"Level\":0,\"RSAPublicKey\":{\"RSAPublicKeyE\":\"" + e + "\",\"RSAPublicKeyN\":\"" + n + "\"},\"PIN\":\"" + pin + "\"}";
                    
                    string vResp, vError;
                    HttpStatusCode vStatus;
                    string vertifyBody = "{\"FirstLogin\":0,\"E-Mail\":\"\",\"Phone\":\"\",\"RSAPublicKey\":{\"RSAPublicKeyE\":\"\",\"RSAPublicKeyN\":\"\"}}";
                    SendJson("http://" + ipAddress + "/LAPI/V1.0/System/Security/User/PIN/VertifyInfo", "PUT", vertifyBody, username, oldPassword, out vResp, out vStatus, out vError);

                    string responseV11;
                    HttpStatusCode statusV11;
                    string errorV11;

                    if (SendJson("http://" + ipAddress + "/LAPI/V1.1/System/Security/User", "PUT", bodyV11, username, oldPassword, out responseV11, out statusV11, out errorV11))
                    {
                        if (IsUserLocked(responseV11))
                        {
                            message =
                                "Uniview user is locked after failed login attempts. Wait a few minutes or reboot the camera, " +
                                "then retry once with the default old password admin / 123456.";
                            return false;
                        }

                        if (IsLapiSuccess(statusV11, responseV11))
                        {
                            ClearFirstLoginFlag(ipAddress, username, newPassword);
                            VerifyLapiLogin(ipAddress, username, newPassword);
                            message = "Uniview admin password set successfully via RSA.";
                            return true;
                        }
                    }
                }
                catch { }
            }

            string[] endpoints =
            {
                "/LAPI/V1.0/System/Security/User",
                "/LAPI/V1.0/System/Users/Users/0",
                "/LAPI/V1.0/System/Security/User/0",
                "/LAPI/V1.0/System/Security/Users/0"
            };

            foreach (string endpoint in endpoints)
            {
                string url = "http://" + ipAddress + endpoint;
                string escapedUser = JsonEscape(username);
                string escapedOld = JsonEscape(oldPassword);
                string escapedNew = JsonEscape(newPassword);

                string[] bodies =
                {
                    "{\"UerID\":0,\"UserName\":\"" + username + "\",\"Password\":\"" + newPassword + "\"}",
                    "{\"User\":{\"ID\":0,\"Name\":\"" + escapedUser + "\",\"Passwd\":\"" + escapedNew + "\",\"OldPasswd\":\"" + escapedOld + "\",\"Level\":0}}",
                    "{\"ID\":0,\"Name\":\"" + escapedUser + "\",\"Passwd\":\"" + escapedNew + "\",\"OldPasswd\":\"" + escapedOld + "\",\"Level\":0}",
                    "{\"ID\":0,\"Name\":\"" + escapedUser + "\",\"Password\":\"" + escapedNew + "\",\"OldPassword\":\"" + escapedOld + "\",\"Level\":0}"
                };

                foreach (string b in bodies)
                {
                    string response;
                    HttpStatusCode status;
                    string error;

                    if (!SendJson(url, "PUT", b, username, oldPassword, out response, out status, out error))
                        continue;

                    if (IsUserLocked(response))
                    {
                        message =
                            "Uniview user is locked after failed login attempts. Wait a few minutes or reboot the camera, " +
                            "then retry once with the default old password admin / 123456.";
                        return false;
                    }

                    if (IsLapiSuccess(status, response))
                    {
                        ClearFirstLoginFlag(ipAddress, username, newPassword);
                        VerifyLapiLogin(ipAddress, username, newPassword);
                        message = "Uniview admin password set successfully.";
                        return true;
                    }

                    if (RequiresPrivacyAgreement(response))
                    {
                        message =
                            "Uniview requires the privacy agreement first. Open http://" + ipAddress +
                            ", log in as admin / 123456, check the agreement, press Next, then retry activation here.";
                        return false;
                    }
                }
            }

            return false;
        }

        private static void AcceptPrivacyPolicy(string ipAddress, string username, string password)
        {
            string url = "http://" + ipAddress + "/LAPI/V1.0/System/PrivacyPolicy/Status";
            string body = "{\"IsAccepted\":1,\"IsFirstLogin\":0,\"LastModPwdTime\":2000000000}";
            string response;
            HttpStatusCode status;
            string error;
            SendJson(url, "PUT", body, username, password, out response, out status, out error);
        }

        private static bool GetRsaPublicKey(string ipAddress, string username, string password, out string modulusN, out string exponentE)
        {
            modulusN = "";
            exponentE = "";
            string response;
            HttpStatusCode status;
            string error;
            if (SendJson("http://" + ipAddress + "/LAPI/V1.0/System/Security/RSA", "GET", null, username, password, out response, out status, out error))
            {
                if (status == HttpStatusCode.OK && !string.IsNullOrWhiteSpace(response))
                {
                    modulusN = ExtractJsonValue(response, "RSAPublicKeyN");
                    exponentE = ExtractJsonValue(response, "RSAPublicKeyE");
                    return true;
                }
            }
            return false;
        }

        private static string ExtractJsonValue(string json, string key)
        {
            try
            {
                string searchKey = "\"" + key + "\"";
                int idx = json.IndexOf(searchKey, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    int colonIdx = json.IndexOf(":", idx + searchKey.Length);
                    if (colonIdx > 0)
                    {
                        int startQuote = json.IndexOf("\"", colonIdx);
                        if (startQuote > 0)
                        {
                            int endQuote = json.IndexOf("\"", startQuote + 1);
                            if (endQuote > 0)
                            {
                                return json.Substring(startQuote + 1, endQuote - startQuote - 1);
                            }
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        private static string EncryptRSA(string data, string modulusHex, string exponentHex)
        {
            byte[] modulus = StringToByteArray(modulusHex);
            byte[] exponent = StringToByteArray(exponentHex);

            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                RSAParameters rsaParams = new RSAParameters
                {
                    Modulus = modulus,
                    Exponent = exponent
                };
                rsa.ImportParameters(rsaParams);
                byte[] dataToEncrypt = Encoding.UTF8.GetBytes(data);
                byte[] encryptedData = rsa.Encrypt(dataToEncrypt, false);
                return Convert.ToBase64String(encryptedData);
            }
        }

        private static byte[] StringToByteArray(string hex)
        {
            hex = hex.Replace("-", "").Replace(" ", "");
            if (hex.Length % 2 != 0)
                hex = "0" + hex;
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        private static void ClearFirstLoginFlag(string ipAddress, string username, string password)
        {
            string url = "http://" + ipAddress + "/LAPI/V1.0/Network/Cloud";
            string body = "{\"Enabled\":0,\"IsFirstLogin\":0}";
            string response;
            HttpStatusCode status;
            string error;
            SendJson(url, "PUT", body, username, password, out response, out status, out error);
        }

        private static bool VerifyLapiLogin(string ipAddress, string username, string password)
        {
            string[] endpoints =
            {
                "/LAPI/V1.0/System/DeviceInfo",
                "/LAPI/V1.0/System/DeviceBasicInfo",
                "/LAPI/V1.0/Channel/0/System/DeviceInfo"
            };

            foreach (string endpoint in endpoints)
            {
                string response;
                HttpStatusCode status;
                string error;
                if (SendJson("http://" + ipAddress + endpoint, "GET", null, username, password, out response, out status, out error) &&
                    (status == HttpStatusCode.OK || status == HttpStatusCode.NoContent))
                    return true;
            }

            return false;
        }

        private static bool SendJson(
            string url,
            string method,
            string body,
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
                request.Timeout = 5000;
                request.ReadWriteTimeout = 5000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.Accept = "application/json, text/plain, */*";
                request.UserAgent = "Mozilla/5.0";
                request.Credentials = new NetworkCredential(username ?? "admin", password ?? "");
                request.PreAuthenticate = false;
                string basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes((username ?? "admin") + ":" + (password ?? "")));
                request.Headers[HttpRequestHeader.Authorization] = "Basic " + basicToken;

                if (body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    request.ContentType = "application/json";
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

        private static bool IsLapiSuccess(HttpStatusCode status, string response)
        {
            if (status == HttpStatusCode.OK || status == HttpStatusCode.NoContent || status == HttpStatusCode.Created)
            {
                if (string.IsNullOrWhiteSpace(response))
                    return true;

                string compact = response.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");

                return compact.IndexOf("\"ResponseCode\":0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       compact.IndexOf("\"Code\":0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       compact.IndexOf("\"Result\":0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       response.IndexOf("\"Status\":\"Success\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       response.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       response.IndexOf("Succeed", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        private static bool RequiresPrivacyAgreement(string response)
        {
            return !string.IsNullOrWhiteSpace(response) &&
                   (response.IndexOf("privacy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    response.IndexOf("policy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    response.IndexOf("agreement", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsUserLocked(string response)
        {
            return !string.IsNullOrWhiteSpace(response) &&
                   response.IndexOf("UserLocked", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static bool IsRejected(string xml)
        {
            return string.IsNullOrWhiteSpace(xml) ||
                   xml.IndexOf("Fault", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   xml.IndexOf("NotAuthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   xml.IndexOf("InvalidArgVal", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
