using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace UniversalScanner.DeviceSDK.ONVIF
{
    public class OnvifDateTimeService
    {
        private readonly OnvifSoapClient _client;
        
        private bool _capabilitiesCached = false;
        private string _deviceServiceUrl;
        
        private static readonly XNamespace NsTds = "http://www.onvif.org/ver10/device/wsdl";
        private static readonly XNamespace NsTt = "http://www.onvif.org/ver10/schema";

        public OnvifDateTimeService(OnvifSoapClient client)
        {
            _client = client;
        }

        private void LogDiagnostic(string ipAddress, string action, string requestBody, Exception ex, string endpointUrl)
        {
#if DEBUG
            try
            {
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OnvifDateTime_Diagnostics.txt");
                try { System.IO.File.OpenWrite(logPath).Close(); }
                catch { logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OnvifDateTime_Diagnostics.txt"); }
                
                string sanitizedReq = requestBody;
                if (sanitizedReq.Contains("Password"))
                {
                    sanitizedReq = System.Text.RegularExpressions.Regex.Replace(sanitizedReq, @"<([A-Za-z0-9:]*)Password[^>]*>.*?</\1Password>", "<$1Password>[SANITIZED]</$1Password>");
                }

                string msg = $"\r\n--- ONVIF DATE TIME DIAGNOSTIC SESSION ---\r\n" +
                             $"Timestamp: {DateTime.Now:O}\r\n" +
                             $"Camera IP: {ipAddress}\r\n" +
                             $"Endpoint: {endpointUrl}\r\n" +
                             $"SOAPAction: {action}\r\n" +
                             $"Sanitized Request:\r\n{sanitizedReq}\r\n\r\n" +
                             $"Exception Type: {ex.GetType().Name}\r\n" +
                             $"Exception Message: {ex.Message}\r\n" +
                             $"Stack Trace:\r\n{ex.StackTrace}\r\n\r\n";

                System.IO.File.AppendAllText(logPath, msg);
            }
            catch { }
#endif
        }

        private OnvifDateTimeFailureType DetermineFailureType(Exception ex)
        {
            string msg = ex.Message ?? "";
            if (msg.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0)
                return OnvifDateTimeFailureType.AuthenticationFailed;
            if (msg.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0 || ex is TimeoutException || ex is TaskCanceledException)
                return OnvifDateTimeFailureType.Timeout;
            if (msg.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0)
                return OnvifDateTimeFailureType.PermissionDenied;
            if (msg.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0)
                return OnvifDateTimeFailureType.EndpointNotFound;
            if (msg.IndexOf("ActionNotSupported", StringComparison.OrdinalIgnoreCase) >= 0)
                return OnvifDateTimeFailureType.ActionNotSupported;
            if (msg.IndexOf("SOAP fault", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("Fault", StringComparison.OrdinalIgnoreCase) >= 0)
                return OnvifDateTimeFailureType.SoapFault;
            if (msg.IndexOf("HTTP error", StringComparison.OrdinalIgnoreCase) >= 0)
                return OnvifDateTimeFailureType.ConnectionFailed;
            
            return OnvifDateTimeFailureType.UnknownError;
        }

        public async Task<OnvifDateTimeConfiguration> GetConfigurationAsync(string ipAddress, int port, string username, string password, CancellationToken cancellationToken)
        {
            if (!_capabilitiesCached)
            {
                _deviceServiceUrl = $"http://{ipAddress}:{port}/onvif/device_service";
                
                try
                {
                    string capBody = "<GetCapabilities xmlns=\"http://www.onvif.org/ver10/device/wsdl\"><Category>Device</Category></GetCapabilities>";
                    string capResp = await _client.PostSoapAsync(_deviceServiceUrl, capBody, "http://www.onvif.org/ver10/device/wsdl/GetCapabilities", username, password, cancellationToken);
                    
                    var capDoc = XDocument.Parse(capResp);
                    var deviceElement = capDoc.Descendants(NsTt + "Device").FirstOrDefault();
                    if (deviceElement != null)
                    {
                        var xaddr = deviceElement.Element(NsTt + "XAddr")?.Value;
                        if (!string.IsNullOrWhiteSpace(xaddr))
                        {
                            _deviceServiceUrl = xaddr;
                        }
                    }
                }
                catch
                {
                }
                _capabilitiesCached = true;
            }

            var config = new OnvifDateTimeConfiguration();

            try
            {
                string dtBody = "<GetSystemDateAndTime xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>";
                string dtResp = await _client.PostSoapAsync(_deviceServiceUrl, dtBody, "http://www.onvif.org/ver10/device/wsdl/GetSystemDateAndTime", username, password, cancellationToken);
                
                var dtDoc = XDocument.Parse(dtResp);
                var dtElement = dtDoc.Descendants(NsTds + "SystemDateAndTime").FirstOrDefault() ?? dtDoc.Descendants(NsTt + "SystemDateAndTime").FirstOrDefault();

                if (dtElement != null)
                {
                    var type = dtElement.Element(NsTt + "DateTimeType")?.Value;
                    if (string.Equals(type, "NTP", StringComparison.OrdinalIgnoreCase))
                        config.DateTimeType = OnvifDateTimeType.NTP;
                    else if (string.Equals(type, "Manual", StringComparison.OrdinalIgnoreCase))
                        config.DateTimeType = OnvifDateTimeType.Manual;
                    else
                        config.DateTimeType = OnvifDateTimeType.Unknown;

                    var daylight = dtElement.Element(NsTt + "DaylightSavings")?.Value;
                    config.DaylightSavings = string.Equals(daylight, "true", StringComparison.OrdinalIgnoreCase);

                    var tzElement = dtElement.Element(NsTt + "TimeZone");
                    if (tzElement != null)
                    {
                        config.TimeZone = tzElement.Element(NsTt + "TZ")?.Value;
                    }

                    var localElem = dtElement.Element(NsTt + "LocalDateTime");
                    if (localElem != null)
                        config.LocalTime = ParseTime(localElem);

                    var utcElem = dtElement.Element(NsTt + "UTCDateTime");
                    if (utcElem != null)
                        config.UtcTime = ParseTime(utcElem);
                }
            }
            catch (Exception ex)
            {
                LogDiagnostic(ipAddress, "http://www.onvif.org/ver10/device/wsdl/GetSystemDateAndTime", "<GetSystemDateAndTime xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>", ex, _deviceServiceUrl);
                
                config.FailureType = DetermineFailureType(ex);
                config.ErrorMessage = ex.Message;
                config.IsSupported = (config.FailureType != OnvifDateTimeFailureType.ActionNotSupported);
                config.IsWriteSupported = false;
                
                return config;
            }

            try
            {
                string ntpBody = "<GetNTP xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>";
                string ntpResp = await _client.PostSoapAsync(_deviceServiceUrl, ntpBody, "http://www.onvif.org/ver10/device/wsdl/GetNTP", username, password, cancellationToken);
                var ntpDoc = XDocument.Parse(ntpResp);
                var ntpElement = ntpDoc.Descendants(NsTds + "NTPInformation").FirstOrDefault() ?? ntpDoc.Descendants(NsTt + "NTPInformation").FirstOrDefault();
                if (ntpElement != null)
                {
                    foreach (var server in ntpElement.Elements(NsTt + "NTPFromDHCP").Concat(ntpElement.Elements(NsTt + "NTPManual")))
                    {
                        var ipAddr = server.Element(NsTt + "IPv4Address")?.Value;
                        if (!string.IsNullOrWhiteSpace(ipAddr)) config.NtpServers.Add(ipAddr);
                        var dns = server.Element(NsTt + "DNSname")?.Value;
                        if (!string.IsNullOrWhiteSpace(dns)) config.NtpServers.Add(dns);
                    }
                }
            }
            catch
            {
                config.NtpSupported = false;
            }

            return config;
        }

        public async Task SetManualDateTimeAsync(string ipAddress, int port, string username, string password, OnvifManualDateTimeSettings settings, CancellationToken cancellationToken)
        {
            if (settings.IsHikvisionWorkaround)
            {
                await SetManualDateTimeIsapiAsync(ipAddress, username, password, settings, cancellationToken);
                return;
            }

            if (string.IsNullOrEmpty(_deviceServiceUrl)) _deviceServiceUrl = $"http://{ipAddress}:{port}/onvif/device_service";

            string tzString = settings.TimeZone;
            string tzXml = string.IsNullOrWhiteSpace(tzString) ? "" : $"<tds:TimeZone><tt:TZ>{System.Security.SecurityElement.Escape(tzString)}</tt:TZ></tds:TimeZone>";
            
            string body = $@"<tds:SetSystemDateAndTime xmlns:tds=""http://www.onvif.org/ver10/device/wsdl"" xmlns:tt=""http://www.onvif.org/ver10/schema"">
                <tds:DateTimeType>Manual</tds:DateTimeType>
                <tds:DaylightSavings>{settings.DaylightSavings.ToString().ToLower()}</tds:DaylightSavings>
                {tzXml}
                <tds:UTCDateTime>
                    <tt:Time>
                        <tt:Hour>{settings.UtcTime.Hour}</tt:Hour>
                        <tt:Minute>{settings.UtcTime.Minute}</tt:Minute>
                        <tt:Second>{settings.UtcTime.Second}</tt:Second>
                    </tt:Time>
                    <tt:Date>
                        <tt:Year>{settings.UtcTime.Year}</tt:Year>
                        <tt:Month>{settings.UtcTime.Month}</tt:Month>
                        <tt:Day>{settings.UtcTime.Day}</tt:Day>
                    </tt:Date>
                </tds:UTCDateTime>
            </tds:SetSystemDateAndTime>";


            await _client.PostSoapAsync(_deviceServiceUrl, body, "http://www.onvif.org/ver10/device/wsdl/SetSystemDateAndTime", username, password, cancellationToken);
        }

        private async Task SetManualDateTimeIsapiAsync(string ipAddress, string username, string password, OnvifManualDateTimeSettings settings, CancellationToken cancellationToken)
        {
            string url = $"http://{ipAddress}/ISAPI/System/time";
            string timeString = settings.LocalTime.ToString("yyyy-MM-ddTHH:mm:ss");
            string payload = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Time version=""2.0"" xmlns=""http://www.hikvision.com/ver20/XMLSchema"">
<timeMode>manual</timeMode>
<localTime>{timeString}</localTime>
<timeZone>{System.Security.SecurityElement.Escape(settings.TimeZone)}</timeZone>
</Time>";

            var resp = await SendIsapiRequestAsync("PUT", url, username, password, payload, "application/xml", cancellationToken);
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"ISAPI Time setup failed. Status: {resp.StatusCode}");
            }
        }

        private async Task<HttpWebResponse> SendIsapiRequestAsync(string method, string url, string username, string password, string payload, string contentType, CancellationToken cancellationToken)
        {
            HttpWebResponse ExecuteRequest(string authHeader)
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.Timeout = 10000;
                if (!string.IsNullOrEmpty(authHeader))
                {
                    req.Headers.Add("Authorization", authHeader);
                }
                if (!string.IsNullOrEmpty(payload))
                {
                    var data = Encoding.UTF8.GetBytes(payload);
                    req.ContentLength = data.Length;
                    req.ContentType = contentType;
                    using (var stream = req.GetRequestStream())
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }
                else
                {
                    req.ContentLength = 0;
                }
                
                try
                {
                    return (HttpWebResponse)req.GetResponse();
                }
                catch (WebException wex) when (wex.Response != null)
                {
                    return (HttpWebResponse)wex.Response;
                }
            }

            return await Task.Run(() =>
            {
                var resp1 = ExecuteRequest(null);
                if (resp1.StatusCode == HttpStatusCode.Unauthorized)
                {
                    string wwwAuth = resp1.Headers["WWW-Authenticate"];
                    if (!string.IsNullOrEmpty(wwwAuth) && wwwAuth.IndexOf("Digest", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        resp1.Close();
                        
                        string realm = GetDigestValue(wwwAuth, "realm");
                        string nonce = GetDigestValue(wwwAuth, "nonce");
                        string qop = GetDigestValue(wwwAuth, "qop");
                        string opaque = GetDigestValue(wwwAuth, "opaque");
                        string algorithm = GetDigestValue(wwwAuth, "algorithm");
                        
                        string uriPath = new Uri(url).PathAndQuery;
                        string digestHeader = BuildDigestHeader(username, password, realm, nonce, qop, opaque, algorithm, method, uriPath);
                        
                        return ExecuteRequest(digestHeader);
                    }
                }
                return resp1;
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

        public async Task SetNtpDateTimeAsync(string ipAddress, int port, string username, string password, OnvifNtpDateTimeSettings settings, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_deviceServiceUrl)) _deviceServiceUrl = $"http://{ipAddress}:{port}/onvif/device_service";

            string hostXml = "";
            foreach (var server in settings.NtpServers)
            {
                bool isIp = System.Net.IPAddress.TryParse(server, out _);
                if (isIp)
                    hostXml += $"<tds:NTPManual><tt:Type>IPv4</tt:Type><tt:IPv4Address>{System.Security.SecurityElement.Escape(server)}</tt:IPv4Address></tds:NTPManual>";
                else
                    hostXml += $"<tds:NTPManual><tt:Type>DNS</tt:Type><tt:DNSname>{System.Security.SecurityElement.Escape(server)}</tt:DNSname></tds:NTPManual>";
            }

            string ntpBody = $@"<tds:SetNTP xmlns:tds=""http://www.onvif.org/ver10/device/wsdl"" xmlns:tt=""http://www.onvif.org/ver10/schema"">
                <tds:FromDHCP>false</tds:FromDHCP>
                {hostXml}
            </tds:SetNTP>";

            await _client.PostSoapAsync(_deviceServiceUrl, ntpBody, "http://www.onvif.org/ver10/device/wsdl/SetNTP", username, password, cancellationToken);

            string tzString = settings.TimeZone;
            if (settings.IsHikvisionWorkaround && !string.IsNullOrWhiteSpace(tzString))
            {
                int firstDigitAfterPrefix = -1;
                for (int i = 0; i < tzString.Length; i++)
                {
                    if (char.IsDigit(tzString[i]) || tzString[i] == '-' || tzString[i] == '+')
                    {
                        firstDigitAfterPrefix = i;
                        break;
                    }
                }
                
                if (firstDigitAfterPrefix >= 0)
                {
                    int endOfBaseOffset = -1;
                    for (int i = firstDigitAfterPrefix; i < tzString.Length; i++)
                    {
                        if (char.IsLetter(tzString[i]))
                        {
                            endOfBaseOffset = i;
                            break;
                        }
                    }
                    if (endOfBaseOffset > 0)
                    {
                        tzString = tzString.Substring(0, endOfBaseOffset);
                    }
                }
            }

            string tzXml = string.IsNullOrWhiteSpace(tzString) ? "" : $"<tds:TimeZone><tt:TZ>{System.Security.SecurityElement.Escape(tzString)}</tt:TZ></tds:TimeZone>";
            
            string dtBody = $@"<tds:SetSystemDateAndTime xmlns:tds=""http://www.onvif.org/ver10/device/wsdl"" xmlns:tt=""http://www.onvif.org/ver10/schema"">
                <tds:DateTimeType>NTP</tds:DateTimeType>
                <tds:DaylightSavings>{settings.DaylightSavings.ToString().ToLowerInvariant()}</tds:DaylightSavings>
                {tzXml}
            </tds:SetSystemDateAndTime>";

            await _client.PostSoapAsync(_deviceServiceUrl, dtBody, "http://www.onvif.org/ver10/device/wsdl/SetSystemDateAndTime", username, password, cancellationToken);
        }

        private DateTime? ParseTime(XElement elem)
        {
            try
            {
                var timeElem = elem.Element(NsTt + "Time");
                var dateElem = elem.Element(NsTt + "Date");
                if (timeElem != null && dateElem != null)
                {
                    int year = int.Parse(dateElem.Element(NsTt + "Year")?.Value ?? "0");
                    int month = int.Parse(dateElem.Element(NsTt + "Month")?.Value ?? "0");
                    int day = int.Parse(dateElem.Element(NsTt + "Day")?.Value ?? "0");
                    int hour = int.Parse(timeElem.Element(NsTt + "Hour")?.Value ?? "0");
                    int min = int.Parse(timeElem.Element(NsTt + "Minute")?.Value ?? "0");
                    int sec = int.Parse(timeElem.Element(NsTt + "Second")?.Value ?? "0");
                    return new DateTime(year, month, day, hour, min, sec);
                }
            }
            catch { }
            return null;
        }
    }
}
