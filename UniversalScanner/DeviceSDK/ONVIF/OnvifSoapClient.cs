using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace UniversalScanner.DeviceSDK.ONVIF
{
    public class OnvifSoapClient : IDisposable
    {
        private class DynamicCredentials : ICredentials
        {
            public string Username;
            public string Password;
            public NetworkCredential GetCredential(Uri uri, string authType) => new NetworkCredential(Username, Password);
        }

        private readonly HttpClient _httpClient;
        private readonly DynamicCredentials _credentials;
        
        public OnvifSoapClient()
        {
            _credentials = new DynamicCredentials();
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                Credentials = _credentials,
                PreAuthenticate = true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15.0)
            };
        }

        public async Task<string> PostSoapAsync(string url, string bodyXml, string action, string username, string password, CancellationToken cancellationToken)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string content2 = OnvifSoapAuthHelper.WrapSoapWithAuth(bodyXml, action, url, username, password);
            using (StringContent content = new StringContent(content2, Encoding.UTF8, "application/soap+xml"))
            {
                _credentials.Username = username;
                _credentials.Password = password;

                if (action.Contains("SetSystemDateAndTime"))
                {
                    string sanitizedBody = content2;
                    try
                    {
                        int securityStart = sanitizedBody.IndexOf("<wsse:Security");
                        int securityEnd = sanitizedBody.IndexOf("</wsse:Security>");
                        if (securityStart > 0 && securityEnd > securityStart)
                        {
                            sanitizedBody = sanitizedBody.Substring(0, securityStart) + "<wsse:Security>[SANITIZED]</wsse:Security>" + sanitizedBody.Substring(securityEnd + 16);
                        }
                    }
                    catch { }
                    Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"ONVIF SetSystemDateAndTime Request. URL: {url}, Action: {action}, Body: {sanitizedBody}");
                }

                string statusCode = "";
                string soapFault = "None";
                string responseXml = "";

                try
                {
                    HttpResponseMessage response = await _httpClient.PostAsync(url, content, cancellationToken);
                    statusCode = $"{(int)response.StatusCode} {response.ReasonPhrase}";
                    responseXml = await response.Content.ReadAsStringAsync();
                    
                    if (action.Contains("SetSystemDateAndTime"))
                    {
                        Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"ONVIF SetSystemDateAndTime Response. Status: {response.StatusCode}, Body: {responseXml}");
                    }

                    if (!string.IsNullOrWhiteSpace(responseXml) && (responseXml.IndexOf("Fault", StringComparison.OrdinalIgnoreCase) >= 0 || responseXml.IndexOf("NotAuthorized", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        soapFault = ParseFault(responseXml);
                        throw new InvalidOperationException($"Camera returned an ONVIF SOAP fault or authorization error. {soapFault}");
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"HTTP error {response.StatusCode}. Response: {responseXml}");
                    }

                    return responseXml;
                }
                catch (Exception ex)
                {
                    if (statusCode == "") statusCode = "Exception";
                    if (soapFault == "None") soapFault = ex.Message;
                    throw;
                }
                finally
                {
                    sw.Stop();
                    string soapAction = action;
                    if (string.IsNullOrEmpty(soapAction)) soapAction = "Unknown";
                    string pwdSource = "Passed as Argument";
                    
                    OnvifTraceLogger.LogRequest(
                        "OnvifSoapClient",
                        "OnvifSoapClient",
                        "PostSoapAsync",
                        url,
                        soapAction,
                        "POST",
                        "WS-Security Digest (HttpClientHandler)",
                        username ?? "",
                        pwdSource,
                        (password ?? "").Length,
                        statusCode,
                        soapFault,
                        0,
                        false, // instance was already created
                        true,  // credentials copied
                        sw.ElapsedMilliseconds);
                }
            }
        }

        private string ParseFault(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var fault = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault");
                if (fault != null)
                {
                    var code = fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "Code" || e.Name.LocalName == "faultcode")?.Value;
                    var reason = fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "Reason" || e.Name.LocalName == "faultstring")?.Value;
                    var subcode = fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "Subcode")?.Value;
                    return $"Code: {code}, Subcode: {subcode}, Reason: {reason}";
                }
            }
            catch { }
            return "";
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
