using System;
using System.IO;
using System.Net;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using UniversalScanner.DeviceSDK.Honeywell;

namespace UniversalScanner.DeviceSDK.Uniview
{
    public static class UniviewNetwork
    {
        public static async Task<bool> ChangeNetworkAddressAsync(
            string currentIp,
            string username,
            string password,
            string newIp,
            string subnetMask,
            string gateway,
            bool enableDhcp,
            Action<string> statusCallback = null)
        {
            try
            {
                statusCallback?.Invoke("Applying Uniview IP via LAPI...");

                string lapiMessage;
                if (TryChangeViaLapi(
                    currentIp,
                    username,
                    password,
                    newIp,
                    subnetMask,
                    gateway,
                    enableDhcp,
                    out lapiMessage))
                {
                    statusCallback?.Invoke("Uniview IP address changed via LAPI.");
                    return true;
                }

                if (string.Equals(lapiMessage, "Uniview rejected the credentials for LAPI network change.", StringComparison.OrdinalIgnoreCase))
                {
                    statusCallback?.Invoke(lapiMessage);
                    return false;
                }

                statusCallback?.Invoke("Getting Uniview ONVIF network interfaces...");

                string getIfBody = "<GetNetworkInterfaces xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>";
                string getIfResponse = await OnvifSoap.SendAsync(currentIp, username, password, getIfBody);
                ThrowIfSoapFault(getIfResponse, "read network interfaces");

                string token = ExtractInterfaceToken(getIfResponse);
                if (string.IsNullOrWhiteSpace(token))
                    token = "eth0";

                statusCallback?.Invoke("Applying Uniview network configuration...");

                string ipv4Config;
                if (enableDhcp)
                {
                    ipv4Config =
                        "<IPv4 xmlns=\"http://www.onvif.org/ver10/schema\">" +
                        "<Enabled>true</Enabled>" +
                        "<DHCP>true</DHCP>" +
                        "</IPv4>";
                }
                else
                {
                    int prefixLength = MaskToPrefixLength(subnetMask);
                    ipv4Config =
                        "<IPv4 xmlns=\"http://www.onvif.org/ver10/schema\">" +
                        "<Enabled>true</Enabled>" +
                        "<Manual>" +
                        "<Address>" + SecurityElement.Escape(newIp) + "</Address>" +
                        "<PrefixLength>" + prefixLength + "</PrefixLength>" +
                        "</Manual>" +
                        "<DHCP>false</DHCP>" +
                        "</IPv4>";
                }

                string setIfBody =
                    "<SetNetworkInterfaces xmlns=\"http://www.onvif.org/ver10/device/wsdl\">" +
                    "<InterfaceToken>" + SecurityElement.Escape(token) + "</InterfaceToken>" +
                    "<NetworkInterface>" + ipv4Config + "</NetworkInterface>" +
                    "</SetNetworkInterfaces>";

                string setIfResponse = await OnvifSoap.SendAsync(currentIp, username, password, setIfBody);
                ThrowIfSoapFault(setIfResponse, "apply network interface");

                if (!enableDhcp && !string.IsNullOrWhiteSpace(gateway))
                {
                    statusCallback?.Invoke("Setting Uniview default gateway...");
                    string setGwBody =
                        "<SetNetworkDefaultGateway xmlns=\"http://www.onvif.org/ver10/device/wsdl\">" +
                        "<IPv4Address>" + SecurityElement.Escape(gateway) + "</IPv4Address>" +
                        "</SetNetworkDefaultGateway>";

                    string setGwResponse = await OnvifSoap.SendAsync(currentIp, username, password, setGwBody);
                    ThrowIfSoapFault(setGwResponse, "set default gateway");
                }

                bool rebootNeeded = true;
                string rebootVal = OnvifSoap.ExtractValue(setIfResponse, "RebootNeeded");
                if (!string.IsNullOrWhiteSpace(rebootVal))
                    bool.TryParse(rebootVal.Trim(), out rebootNeeded);

                if (rebootNeeded)
                {
                    statusCallback?.Invoke("Rebooting Uniview device...");
                    try
                    {
                        string rebootBody = "<SystemReboot xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>";
                        await OnvifSoap.SendAsync(currentIp, username, password, rebootBody);
                    }
                    catch
                    {
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                statusCallback?.Invoke("Uniview IP change failed: " + ex.Message);
                return false;
            }
        }

        private static bool TryChangeViaLapi(
            string currentIp,
            string username,
            string password,
            string newIp,
            string subnetMask,
            string gateway,
            bool enableDhcp,
            out string message)
        {
            message = "";

            string[] endpoints =
            {
                "/LAPI/V1.0/Network/Interfaces/1",
                "/LAPI/V1.0/Network/Interfaces/0"
            };

            foreach (string endpoint in endpoints)
            {
                string url = "http://" + currentIp + endpoint;
                int interfaceId = endpoint.EndsWith("/1", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

                string getResponse;
                HttpStatusCode getStatus;
                string getError;
                SendJson(url, "GET", null, username, password, out getResponse, out getStatus, out getError);

                string ipGetType = enableDhcp ? "1" : "0";
                string address = JsonEscape(enableDhcp ? "" : newIp);
                string mask = JsonEscape(enableDhcp ? "" : subnetMask);
                string gw = JsonEscape(enableDhcp ? "" : gateway);

                string ipv4 =
                    "\"IPv4\":{" +
                    "\"IPGetType\":" + ipGetType + "," +
                    "\"AddressNum\":1," +
                    "\"AddressList\":[{" +
                    "\"Address\":\"" + address + "\"," +
                    "\"Netmask\":\"" + mask + "\"," +
                    "\"Gateway\":\"" + gw + "\"}]}";

                string[] bodies =
                {
                    "{\"ID\":" + interfaceId + ",\"Name\":\"eth0\"," + ipv4 + "}",
                    "{\"NetworkInterface\":{\"ID\":" + interfaceId + ",\"Name\":\"eth0\"," + ipv4 + "}}",
                    "{\"NetworkInterfaceInfo\":{\"ID\":" + interfaceId + ",\"Name\":\"eth0\"," + ipv4 + "}}"
                };

                foreach (string body in bodies)
                {
                    string response;
                    HttpStatusCode status;
                    string error;
                    if (!SendJson(url, "PUT", body, username, password, out response, out status, out error))
                        continue;

                    if (IsLapiSuccess(status, response))
                    {
                        message = "Uniview IP address changed via LAPI.";
                        return true;
                    }

                    if (IsUnauthorized(response, status))
                    {
                        message = "Uniview rejected the credentials for LAPI network change.";
                        return false;
                    }
                }
            }

            message = "Uniview LAPI network change failed.";
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
                request.Timeout = 7000;
                request.ReadWriteTimeout = 7000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.Accept = "application/json, text/plain, */*";
                request.UserAgent = "Mozilla/5.0";
                // Uniview LAPI challenges with HTTP Digest (MD5/qop=auth). Do not
                // force a Basic header: that prevents HttpWebRequest from completing
                // the camera's Digest challenge and makes every LAPI request look
                // like an authentication failure.
                CredentialCache credentials = new CredentialCache();
                credentials.Add(
                    new Uri(url),
                    "Digest",
                    new NetworkCredential(username ?? "admin", password ?? ""));
                request.Credentials = credentials;
                request.PreAuthenticate = false;

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
            if (status != HttpStatusCode.OK &&
                status != HttpStatusCode.NoContent &&
                status != HttpStatusCode.Created)
                return false;

            if (string.IsNullOrWhiteSpace(response))
                return true;

            string compact = response.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
            return compact.IndexOf("\"ResponseCode\":0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   compact.IndexOf("\"StatusCode\":0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("Succeed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   response.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUnauthorized(string response, HttpStatusCode status)
        {
            return status == HttpStatusCode.Unauthorized ||
                   (!string.IsNullOrWhiteSpace(response) &&
                    response.IndexOf("Not Authorized", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string ExtractInterfaceToken(string xml)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);

                foreach (XmlElement el in doc.GetElementsByTagName("*"))
                {
                    if (el.LocalName.Equals("NetworkInterfaces", StringComparison.OrdinalIgnoreCase))
                    {
                        string attr = el.GetAttribute("token");
                        if (!string.IsNullOrWhiteSpace(attr))
                            return attr;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static int MaskToPrefixLength(string mask)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(mask, out ip))
                return 24;

            int bits = 0;
            foreach (byte b in ip.GetAddressBytes())
            {
                for (int i = 7; i >= 0; i--)
                {
                    if ((b & (1 << i)) != 0)
                        bits++;
                    else
                        return bits;
                }
            }

            return bits;
        }

        private static void ThrowIfSoapFault(string xml, string action)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new InvalidOperationException("Empty ONVIF response while trying to " + action + ".");

            if (xml.IndexOf("Fault", StringComparison.OrdinalIgnoreCase) >= 0 ||
                xml.IndexOf("NotAuthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                xml.IndexOf("Sender", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidOperationException("ONVIF rejected request to " + action + ".");
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
