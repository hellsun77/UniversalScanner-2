using System;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    /// <summary>
    /// Sends raw ONVIF SOAP requests with WS-UsernameToken digest authentication.
    /// No vendor DLL required — pure HttpClient over HTTP port 80.
    /// </summary>
    public static class OnvifSoap
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        /// <summary>
        /// Wraps <paramref name="bodyXml"/> in a SOAP envelope with WS-UsernameToken digest
        /// and POSTs it to http://[ip]/onvif/device_service.
        /// Returns the raw XML response string, or throws on network error.
        /// </summary>
        public static async Task<string> SendAsync(
            string ip,
            string username,
            string password,
            string bodyXml)
        {
            return await SendAsync(ip, username, password, bodyXml, "/onvif/device_service");
        }

        public static async Task<string> SendAsync(
            string ip,
            string username,
            string password,
            string bodyXml,
            string endpoint,
            string soapAction = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Random nonce — 16 bytes, Base64-encoded
            byte[] nonceBytes = new byte[16];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(nonceBytes);
            string nonce = Convert.ToBase64String(nonceBytes);

            // PasswordDigest = Base64( SHA1( nonce_bytes + UTF8(created) + UTF8(password) ) )
            byte[] createdBytes = Encoding.UTF8.GetBytes(created);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password ?? "");
            byte[] toHash = new byte[nonceBytes.Length + createdBytes.Length + passwordBytes.Length];
            Buffer.BlockCopy(nonceBytes,  0, toHash, 0,                                                 nonceBytes.Length);
            Buffer.BlockCopy(createdBytes, 0, toHash, nonceBytes.Length,                                createdBytes.Length);
            Buffer.BlockCopy(passwordBytes, 0, toHash, nonceBytes.Length + createdBytes.Length,         passwordBytes.Length);

            string digest;
            using (SHA1 sha1 = SHA1.Create())
                digest = Convert.ToBase64String(sha1.ComputeHash(toHash));

            string envelope =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<s:Envelope" +
                " xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"" +
                " xmlns:wsse=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\"" +
                " xmlns:wsu=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\">" +
                "<s:Header>" +
                "<wsse:Security s:mustUnderstand=\"1\">" +
                "<wsse:UsernameToken>" +
                "<wsse:Username>" + SecurityElement.Escape(username ?? "") + "</wsse:Username>" +
                "<wsse:Password Type=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest\">" +
                digest + "</wsse:Password>" +
                "<wsse:Nonce EncodingType=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary\">" +
                nonce + "</wsse:Nonce>" +
                "<wsu:Created>" + created + "</wsu:Created>" +
                "</wsse:UsernameToken>" +
                "</wsse:Security>" +
                "</s:Header>" +
                "<s:Body>" + bodyXml + "</s:Body>" +
                "</s:Envelope>";

            string url = "http://" + ip + endpoint;
            
            string detectedAction = "Unknown";
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(bodyXml, @"<([a-zA-Z0-9_]+:)?([a-zA-Z0-9_]+)");
                if (match.Success) detectedAction = match.Groups[2].Value;
            }
            catch { }

            string statusCode = "";
            string soapFault = "None";
            string responseString = "";

            try
            {
                using (StringContent content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml"))
                {
                    if (!string.IsNullOrWhiteSpace(soapAction))
                    {
                        content.Headers.Remove("Content-Type");
                        content.Headers.TryAddWithoutValidation("Content-Type", "application/soap+xml; charset=utf-8; action=\"" + soapAction + "\"");
                        content.Headers.TryAddWithoutValidation("SOAPAction", "\"" + soapAction + "\"");
                    }

                    HttpResponseMessage response = await _http.PostAsync(url, content);
                    statusCode = $"{(int)response.StatusCode} {response.ReasonPhrase}";
                    responseString = await response.Content.ReadAsStringAsync();
                    
                    if (responseString.Contains("Fault>") || responseString.Contains("faultcode"))
                    {
                        soapFault = "Fault Detected";
                        var faultMatch = System.Text.RegularExpressions.Regex.Match(responseString, @"<[^>]*Reason[^>]*>(.*?)</[^>]*Reason>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (faultMatch.Success) soapFault = faultMatch.Groups[1].Value;
                        else
                        {
                            var textMatch = System.Text.RegularExpressions.Regex.Match(responseString, @"<[^>]*Text[^>]*>(.*?)</[^>]*Text>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (textMatch.Success) soapFault = textMatch.Groups[1].Value;
                        }
                    }
                    return responseString;
                }
            }
            catch (Exception ex)
            {
                statusCode = "Exception";
                soapFault = ex.Message;
                throw;
            }
            finally
            {
                sw.Stop();
                string pwdSource = "Passed as Argument";
                OnvifTraceLogger.LogRequest(
                    "OnvifSoap",
                    "OnvifSoap",
                    "SendAsync",
                    url,
                    detectedAction,
                    "POST",
                    "WS-Security Digest",
                    username ?? "",
                    pwdSource,
                    (password ?? "").Length,
                    statusCode,
                    soapFault,
                    0,
                    false,
                    false,
                    sw.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Returns the InnerText of the first element whose LocalName matches
        /// <paramref name="localName"/> (case-insensitive), or null if not found.
        /// </summary>
        public static string ExtractValue(string xml, string localName)
        {
            if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(localName))
                return null;

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);

                foreach (XmlElement el in doc.GetElementsByTagName("*"))
                {
                    if (el.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                        return el.InnerText;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Returns the value of a named attribute on the first element whose LocalName
        /// matches <paramref name="elementLocalName"/> (case-insensitive), or null.
        /// </summary>
        public static string ExtractAttribute(string xml, string elementLocalName, string attributeName)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return null;

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);

                foreach (XmlElement el in doc.GetElementsByTagName("*"))
                {
                    if (el.LocalName.Equals(elementLocalName, StringComparison.OrdinalIgnoreCase))
                    {
                        string val = el.GetAttribute(attributeName);
                        if (!string.IsNullOrWhiteSpace(val))
                            return val;
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
