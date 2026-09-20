using System;
using System.Net;
using System.Security;
using System.Threading.Tasks;
using System.Xml;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    /// <summary>
    /// Changes the network address of an ONVIF-capable Honeywell (equIP series) camera
    /// using four sequential SOAP calls over HTTP — no proprietary DLL required.
    ///
    /// Call sequence:
    ///   1. GetNetworkInterfaces  → obtain the interface token (e.g. "eth0")
    ///   2. SetNetworkInterfaces  → apply new IP + prefix length, DHCP = false
    ///   3. SetNetworkDefaultGateway → apply gateway
    ///   4. SystemReboot (if SetNetworkInterfaces returned RebootNeeded=true, or always)
    /// </summary>
    public static class HoneywellNetwork
    {
        public static async Task<bool> ChangeNetworkAddressAsync(
            string currentIp,
            string username,
            string password,
            string newIp,
            string subnetMask,
            string gateway,
            Action<string> statusCallback = null)
        {
            Exception onvifException = null;

            try
            {
                // ── Step 1: GetNetworkInterfaces ─────────────────────────────────────
                statusCallback?.Invoke("Getting ONVIF network interfaces...");

                string getIfBody = "<GetNetworkInterfaces xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>";
                string getIfResponse = await OnvifSoap.SendAsync(currentIp, username, password, getIfBody);
                ThrowIfSoapFault(getIfResponse, "read network interfaces");

                string token = ExtractInterfaceToken(getIfResponse);
                if (string.IsNullOrWhiteSpace(token))
                    token = "eth0"; // Default token on most ONVIF devices

                int prefixLength = MaskToPrefixLength(subnetMask);

                // ── Step 2: SetNetworkInterfaces ─────────────────────────────────────
                statusCallback?.Invoke("Applying new IP address via ONVIF...");

                string setIfBody =
                    "<SetNetworkInterfaces xmlns=\"http://www.onvif.org/ver10/device/wsdl\">" +
                    "<InterfaceToken>" + SecurityElement.Escape(token) + "</InterfaceToken>" +
                    "<NetworkInterface>" +
                    "<IPv4 xmlns=\"http://www.onvif.org/ver10/schema\">" +
                    "<Enabled>true</Enabled>" +
                    "<Manual>" +
                    "<Address>" + SecurityElement.Escape(newIp) + "</Address>" +
                    "<PrefixLength>" + prefixLength + "</PrefixLength>" +
                    "</Manual>" +
                    "<DHCP>false</DHCP>" +
                    "</IPv4>" +
                    "</NetworkInterface>" +
                    "</SetNetworkInterfaces>";

                string setIfResponse = await OnvifSoap.SendAsync(currentIp, username, password, setIfBody);
                ThrowIfSoapFault(setIfResponse, "apply network interface");

                // ── Step 3: SetNetworkDefaultGateway ─────────────────────────────────
                if (!string.IsNullOrWhiteSpace(gateway))
                {
                    statusCallback?.Invoke("Setting default gateway via ONVIF...");

                    string setGwBody =
                        "<SetNetworkDefaultGateway xmlns=\"http://www.onvif.org/ver10/device/wsdl\">" +
                        "<IPv4Address>" + SecurityElement.Escape(gateway) + "</IPv4Address>" +
                        "</SetNetworkDefaultGateway>";

                    string setGwResponse = await OnvifSoap.SendAsync(currentIp, username, password, setGwBody);
                    ThrowIfSoapFault(setGwResponse, "set default gateway");
                }

                // ── Step 4: SystemReboot ──────────────────────────────────────────────
                // Honeywell ONVIF can report RebootNeeded=false even though the new address is
                // not applied until reboot. Reboot after accepted network settings so single and
                // batch IP changes behave like the live camera.
                statusCallback?.Invoke("Rebooting Honeywell camera to apply network settings...");
                string rebootBody = "<SystemReboot xmlns=\"http://www.onvif.org/ver10/device/wsdl\"/>";

                // Best-effort — device may not reply if it reboots fast
                try
                {
                    await OnvifSoap.SendAsync(
                        currentIp,
                        username,
                        password,
                        rebootBody,
                        "/onvif/device_service",
                        "http://www.onvif.org/ver10/device/wsdl/SystemReboot");
                }
                catch { /* expected: connection reset on fast-reboot devices */ }

                // Some Honeywell models accept the ONVIF network change but ignore the ONVIF
                // reboot call. Try the legacy web reboot as a Honeywell-only fallback.
                try
                {
                    string rebootMessage;
                    await Task.Run(() => HoneywellLegacyNetwork.RebootOnly(
                        currentIp,
                        username,
                        password,
                        out rebootMessage));
                }
                catch
                {
                }

                return true;
            }
            catch (Exception ex)
            {
                onvifException = ex;
                statusCallback?.Invoke("Honeywell ONVIF IP change failed; trying legacy Honeywell web RPC...");
            }

            try
            {
                string message = "";
                bool ok = await Task.Run(() => HoneywellLegacyNetwork.ChangeNetworkAddress(
                    currentIp,
                    username,
                    password,
                    newIp,
                    subnetMask,
                    gateway,
                    out message));

                statusCallback?.Invoke(message);
                return ok;
            }
            catch (Exception ex)
            {
                string onvifMessage = onvifException == null ? "" : "ONVIF failed: " + onvifException.Message + " ";
                statusCallback?.Invoke("Honeywell IP change failed. " + onvifMessage + "Legacy failed: " + ex.Message);
                return false;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses GetNetworkInterfaces response to find the primary interface token.
        /// The ONVIF spec puts the token in the "token" attribute of the NetworkInterfaces element.
        /// </summary>
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
            catch { }

            return null;
        }

        /// <summary>
        /// Converts a dotted-decimal subnet mask ("255.255.255.0") to its CIDR prefix length (24).
        /// Returns 24 on parse failure.
        /// </summary>
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
                        return bits; // Stop at first 0 bit
                }
            }

            return bits;
        }

        private static void ThrowIfSoapFault(string xml, string action)
        {
            if (string.IsNullOrWhiteSpace(xml))
                throw new InvalidOperationException("Empty ONVIF response while trying to " + action + ".");

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);

                foreach (XmlElement el in doc.GetElementsByTagName("*"))
                {
                    if (el.LocalName.Equals("Fault", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("ONVIF rejected request to " + action + ".");
                }
            }
            catch (XmlException ex)
            {
                throw new InvalidOperationException("Invalid ONVIF response while trying to " + action + ": " + ex.Message);
            }
        }
    }
}
