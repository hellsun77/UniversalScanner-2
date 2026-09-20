using System;
using System.Net;
using System.Security;
using System.Threading.Tasks;
using System.Xml;
using UniversalScanner.DeviceSDK.Honeywell;

namespace UniversalScanner.DeviceSDK.Common
{
    public static class GenericOnvifNetwork
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
                statusCallback?.Invoke("Getting ONVIF network interfaces...");

                string getIfBody = "<GetNetworkInterfaces xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>";
                string getIfResponse = await OnvifSoap.SendAsync(currentIp, username, password, getIfBody);
                ThrowIfSoapFault(getIfResponse, "read network interfaces");

                string token = ExtractInterfaceToken(getIfResponse);
                if (string.IsNullOrWhiteSpace(token))
                    token = "eth0";

                statusCallback?.Invoke("Applying ONVIF network configuration...");

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
                    statusCallback?.Invoke("Setting ONVIF default gateway...");
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
                    statusCallback?.Invoke("Rebooting device via ONVIF...");
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
                if (!enableDhcp && await LooksLikeStaticChangeAppliedAsync(currentIp, newIp, statusCallback))
                    return true;

                statusCallback?.Invoke("ONVIF IP change failed: " + ex.Message);
                return false;
            }
        }

        private static async Task<bool> LooksLikeStaticChangeAppliedAsync(
            string oldIp,
            string newIp,
            Action<string> statusCallback)
        {
            if (string.IsNullOrWhiteSpace(newIp) ||
                string.Equals(oldIp, newIp, StringComparison.OrdinalIgnoreCase))
                return false;

            statusCallback?.Invoke("ONVIF response was interrupted; checking whether the new IP is active...");

            for (int attempt = 1; attempt <= 8; attempt++)
            {
                await Task.Delay(1500);

                if (await global::UniversalScanner.NetworkTools.IsDeviceReachableAsync(newIp, 900))
                {
                    statusCallback?.Invoke("Device answered at the new IP address.");
                    return true;
                }

                if (!await global::UniversalScanner.NetworkTools.IsDeviceReachableAsync(oldIp, 900))
                {
                    statusCallback?.Invoke("Device stopped answering at the old IP address after ONVIF change.");
                    return true;
                }

                statusCallback?.Invoke("Waiting for device at new IP (" + attempt + "/8)...");
            }

            return false;
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
    }
}
