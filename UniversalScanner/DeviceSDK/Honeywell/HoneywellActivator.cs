using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    /// <summary>
    /// Activates an inactive Honeywell HC30/HC35 camera via its proprietary
    /// JSON RPC endpoint (the same one the camera's own web UI uses).
    ///
    /// Protocol (confirmed by HAR capture from the web UI):
    ///   1. POST https://&lt;ip&gt;/action/WEB_JsonAjaxAuth
    ///        body: {"cmd":"getState"}
    ///      response: {"Nonce":"&lt;b64&gt;","PublicKey":"-----BEGIN PUBLIC KEY-----..."}
    ///
    ///   2. Generate a client nonce (16 random bytes → hex → base64).
    ///      RSA-PKCS#1v1.5 encrypt the payload with the public key, base64 the cipher.
    ///
    ///   3. POST https://&lt;ip&gt;/action/WEB_JsonAjaxAuth
    ///        body: {"cmd":"createPassword","data":{"user":"admin",
    ///               "cnonce":"&lt;b64&gt;","response":"&lt;b64&gt;","protocolType":1}}
    ///      success: {"cmd":"createPassword","code":0}
    ///
    /// The exact contents of the RSA-encrypted payload aren't documented, so we
    /// try the most likely variants in sequence; the server returns a non-zero
    /// code on decryption failure with no side effect.
    /// </summary>
    public static class HoneywellActivator
    {
        public static bool ActivateDevice(
            string ipAddress,
            string newPassword,
            out string message)
        {
            message = "";

            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                message = "IP address is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                message = "Password is empty.";
                return false;
            }

            if (newPassword.Length < 8)
            {
                message = "Password must be at least 8 characters.";
                return false;
            }

            string url = "https://" + ipAddress + "/action/WEB_JsonAjaxAuth";

            // Force modern TLS — embedded cameras often refuse SSL3/TLS1.0 handshakes
            // initiated by .NET 4.x defaults, which manifests as "connection closed".
            SecurityProtocolType prevProtocol = ServicePointManager.SecurityProtocol;
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls;

            // Allow self-signed / untrusted SSL certificates from cameras
            System.Net.Security.RemoteCertificateValidationCallback prevCallback =
                ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            // Disable global 'Expect: 100-Continue' — most embedded HTTP stacks
            // never reply with the 100 status, causing HttpWebRequest to abort
            // the PUT/POST mid-handshake with a generic 'connection closed' error.
            bool prevExpect100 = ServicePointManager.Expect100Continue;
            ServicePointManager.Expect100Continue = false;

            try
            {
                // Step 1: getState → fetch Nonce + RSA public key from the camera.
                string getStateBody = "{\"cmd\":\"getState\"}";
                string getStateResp;
                HttpStatusCode getStateStatus;
                string getStateErr;
                if (!PostJson(url, getStateBody, out getStateResp, out getStateStatus, out getStateErr))
                {
                    message = "getState failed: " + getStateErr;
                    return false;
                }
                if ((int)getStateStatus < 200 || (int)getStateStatus >= 300)
                {
                    message = "getState HTTP " + (int)getStateStatus + ": " + Truncate(getStateResp, 200);
                    return false;
                }

                string nonce = ExtractJsonString(getStateResp, "Nonce");
                string publicKey = ExtractJsonString(getStateResp, "PublicKey");
                if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(publicKey))
                {
                    message = "getState response missing Nonce/PublicKey: " + Truncate(getStateResp, 200);
                    return false;
                }

                // Unescape the PEM: the camera sends "\n" literals inside the JSON string.
                publicKey = publicKey.Replace("\\n", "\n").Replace("\\r", "");

                // Step 2: build the encrypted payload following the camera's
                // web-UI algorithm (reverse-engineered from main-es2015.*.js):
                //   r          = 32 random chars from "abcdef0123456789-"
                //   iv         = UTF-8(r[0..16])
                //   cnonce     = base64(r)
                //   hashHex    = SHA-256(user + Nonce + cnonce) as lowercase hex
                //   aesKey     = UTF-8(hashHex[0..32])               // 32 bytes
                //   aesCipher  = base64(AES-256-CBC(password, aesKey, iv, PKCS7))
                //   response   = base64(RSA-PKCS1v15(pubKey, UTF-8(aesCipher)))
                const string user = "admin";

                string r = HoneywellRsaHelper.GenerateCnonceRaw();
                byte[] iv = Encoding.UTF8.GetBytes(r.Substring(0, 16));
                string cnonce = Convert.ToBase64String(Encoding.ASCII.GetBytes(r));

                string hashHex = HoneywellRsaHelper.Sha256Hex(user + nonce + cnonce);
                byte[] aesKey = Encoding.UTF8.GetBytes(hashHex.Substring(0, 32));

                string aesCipherB64;
                string responseField;
                try
                {
                    aesCipherB64 = HoneywellRsaHelper.AesCbcEncryptBase64(aesKey, iv, newPassword);
                    responseField = HoneywellRsaHelper.EncryptPkcs1Base64(publicKey, aesCipherB64);
                }
                catch (Exception ex)
                {
                    message = "Encryption failed: " + ex.Message;
                    return false;
                }

                string createBody =
                    "{\"cmd\":\"createPassword\",\"data\":{" +
                    "\"user\":\"" + user + "\"," +
                    "\"cnonce\":\"" + JsonEscape(cnonce) + "\"," +
                    "\"response\":\"" + JsonEscape(responseField) + "\"," +
                    "\"protocolType\":1}}";

                string createResp;
                HttpStatusCode createStatus;
                string createErr;
                if (!PostJson(url, createBody, out createResp, out createStatus, out createErr))
                {
                    message = "createPassword transport failed: " + createErr;
                    return false;
                }

                string code = ExtractJsonNumber(createResp, "code");
                if (code == "0")
                {
                    message = "Device activated successfully.";
                    return true;
                }

                if (code == "-529")
                {
                    message = "This Honeywell camera is already active. No activation is required.";
                    return true;
                }

                // Translate the camera's well-known error codes (mapping
                // pulled from the web UI bundle: LA_NotePasswordWeak etc.).
                string friendly;
                switch (code)
                {
                    case "-523":
                        friendly = "Password is too weak. The camera requires: 8+ characters, " +
                                   "both upper- and lower-case letters, a digit, a symbol, and no spaces.";
                        break;
                    case "-524":
                        friendly = "Password is too short. Minimum length is 8 characters.";
                        break;
                    case "-508":
                        friendly = "Old password is incorrect (device may already be activated).";
                        break;
                    case "-1":
                        friendly = "Authentication failed — encryption handshake rejected by device.";
                        break;
                    default:
                        friendly = "Activation rejected by device (code " + (code ?? "?") + ").";
                        break;
                }

                message = friendly;
                return false;
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = prevCallback;
                ServicePointManager.SecurityProtocol = prevProtocol;
                ServicePointManager.Expect100Continue = prevExpect100;
            }
        }

        /// <summary>
        /// POSTs a JSON body to the camera's WEB_JsonAjaxAuth endpoint with the same
        /// headers the web UI sends (Content-Type is intentionally "application/xml"
        /// — the camera's parser requires it despite the JSON body).
        /// </summary>
        private static bool PostJson(string url, string body,
            out string responseBody, out HttpStatusCode statusCode, out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/xml";
                request.Accept = "application/json, text/javascript, */*; q=0.01";
                request.ContentLength = bodyBytes.Length;
                request.Timeout = 5000;
                request.ReadWriteTimeout = 5000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.UserAgent = "Mozilla/5.0";
                request.Headers["Origin"] = url.Substring(0, url.IndexOf("/action/"));
                request.Headers["X-Requested-With"] = "XMLHttpRequest";
                request.ServicePoint.Expect100Continue = false;

                using (Stream stream = request.GetRequestStream())
                    stream.Write(bodyBytes, 0, bodyBytes.Length);

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
                    HttpWebResponse errResponse = (HttpWebResponse)ex.Response;
                    statusCode = errResponse.StatusCode;
                    try
                    {
                        using (StreamReader reader = new StreamReader(errResponse.GetResponseStream()))
                            responseBody = reader.ReadToEnd();
                    }
                    catch { }
                }
                return true;
            }
            catch (WebException ex)
            {
                error = ex.Status + ": " + Truncate(ex.Message, 120);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + Truncate(ex.Message, 120);
                return false;
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "…";
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// Minimal JSON string-field extractor — sufficient for the camera's
        /// flat single-level response objects. Handles \\ \" \n \r escapes.
        /// </summary>
        private static string ExtractJsonString(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field))
                return null;
            Match m = Regex.Match(json,
                "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return null;
            return m.Groups[1].Value
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        private static string ExtractJsonNumber(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field))
                return null;
            Match m = Regex.Match(json,
                "\"" + Regex.Escape(field) + "\"\\s*:\\s*(-?[0-9]+)");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
