using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalScanner
{
    public static class NetworkTools
    {
        private static void ObserveFault(Task task)
        {
            task.ContinueWith(
                faultedTask =>
                {
                    AggregateException ignored = faultedTask.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(
            int destIp,
            int srcIp,
            byte[] macAddr,
            ref int physicalAddrLen);

        // MIB_IPNETROW used to delete a single ARP entry from the Windows ARP cache.
        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_IPNETROW
        {
            public int dwIndex;
            public int dwPhysAddrLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] bPhysAddr;
            public int dwAddr;
            public int dwType;
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int DeleteIpNetEntry(ref MIB_IPNETROW pArpEntry);

        public static async Task<PingReply> PingAsync(
            string ip,
            int timeoutMs = 1000,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (Ping ping = new Ping())
                using (cancellationToken.Register(() =>
                {
                    try { ping.SendAsyncCancel(); } catch { }
                }))
                {
                    PingReply reply = await ping.SendPingAsync(ip, timeoutMs);
                    cancellationToken.ThrowIfCancellationRequested();
                    return reply;
                }
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Debug, $"PingAsync cancelled for {ip}: {ex.Message}");
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception ex) when (ex is System.Net.NetworkInformation.PingException || 
                                       ex is System.ComponentModel.Win32Exception ||
                                       ex is System.Net.Sockets.SocketException ||
                                       ex is TimeoutException)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Debug, $"PingAsync failed for {ip}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"PingAsync unexpected exception for {ip}: {ex.Message}");
                return null;
            }
        }

        public static bool IsArpResolved(string ip)
        {
            try
            {
                if (!IPAddress.TryParse(ip, out IPAddress dest))
                    return false;
                    
#pragma warning disable CS0618 // Type or member is obsolete
                int destIp = (int)dest.Address;
#pragma warning restore CS0618 // Type or member is obsolete

                byte[] macAddr = new byte[6];
                int macAddrLen = macAddr.Length;
                int result = SendARP(destIp, 0, macAddr, ref macAddrLen);
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> IsOnlineAsync(
            string ip,
            int timeoutMs = 1000,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                PingReply reply = await PingAsync(ip, timeoutMs, cancellationToken);
                return reply != null && reply.Status == IPStatus.Success;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> IsDeviceReachableAsync(
            string ip,
            int timeoutMs = 1000,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsOnlineAsync(ip, timeoutMs, cancellationToken))
                return true;

            int[] ports = new int[] { 80, 443, 8000 };

            foreach (int port in ports)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await IsTcpPortOpenAsync(ip, port, timeoutMs, null, cancellationToken))
                    return true;
            }

            return false;
        }

        public static async Task<bool> IsTcpPortOpenAsync(
            string ip,
            int port,
            int timeoutMs = 1000,
            string sourceIp = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                IPEndPoint localEndPoint = null;
                if (!string.IsNullOrEmpty(sourceIp) && IPAddress.TryParse(sourceIp, out IPAddress srcAddr))
                {
                    localEndPoint = new IPEndPoint(srcAddr, 0);
                }

                using (TcpClient client = localEndPoint != null ? new TcpClient(localEndPoint) : new TcpClient())
                using (cancellationToken.Register(() =>
                {
                    try { client.Close(); } catch { }
                }))
                {
                    Task connectTask = client.ConnectAsync(ip, port);
                    Task timeoutTask = Task.Delay(timeoutMs, cancellationToken);
                    Task completed = await Task.WhenAny(connectTask, timeoutTask);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        ObserveFault(connectTask);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (completed != connectTask)
                    {
                        ObserveFault(connectTask);
                        return false;
                    }

                    await connectTask;
                    return client.Connected;
                }
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Debug, $"IsTcpPortOpenAsync cancelled for {ip}:{port}: {ex.Message}");
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException || ex is System.ComponentModel.Win32Exception || ex is TimeoutException)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Debug, $"IsTcpPortOpenAsync failed for {ip}:{port}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"IsTcpPortOpenAsync unexpected exception for {ip}:{port}: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> WaitUntilOnlineAsync(
            string ip,
            int attempts = 10,
            int timeoutMs = 1200,
            int delayMs = 4000,
            Action<int, int> progressCallback = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            for (int i = 1; i <= attempts; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progressCallback?.Invoke(i, attempts);

                if (await IsDeviceReachableAsync(ip, timeoutMs, cancellationToken))
                    return true;

                if (i < attempts)
                    await Task.Delay(delayMs, cancellationToken);
            }

            return false;
        }

        public static string ForceArp(string ip)
        {
            try
            {
                IPAddress address;

                if (!IPAddress.TryParse(ip, out address) ||
                    address.AddressFamily != AddressFamily.InterNetwork)
                    return "";

                byte[] mac = new byte[6];
                int length = mac.Length;
                int destIp = BitConverter.ToInt32(address.GetAddressBytes(), 0);

                int result = SendARP(destIp, 0, mac, ref length);

                if (result != 0 || length <= 0)
                    return "";

                StringBuilder sb = new StringBuilder();

                for (int i = 0; i < length; i++)
                {
                    if (i > 0)
                        sb.Append("-");

                    sb.Append(mac[i].ToString("X2"));
                }

                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Removes the ARP cache entry for <paramref name="ip"/> so that the next
        /// network operation resolves a fresh hardware address.  Call this after
        /// changing a device's IP address so that Windows stops routing packets
        /// to the old NIC binding.  Silently ignores errors (entry may not exist).
        /// </summary>
        public static void FlushArpEntry(string ip)
        {
            try
            {
                IPAddress address;

                if (!IPAddress.TryParse(ip, out address) ||
                    address.AddressFamily != AddressFamily.InterNetwork)
                    return;

                MIB_IPNETROW row = new MIB_IPNETROW();
                row.dwAddr = BitConverter.ToInt32(address.GetAddressBytes(), 0);
                row.bPhysAddr = new byte[8];
                row.dwPhysAddrLen = 6;
                row.dwType = 3; // dynamic

                DeleteIpNetEntry(ref row);
            }
            catch
            {
                // Non-critical; the ARP entry will expire on its own.
            }
        }

        private static readonly SemaphoreSlim _arpThrottle = new SemaphoreSlim(4, 4);

        public static async Task<string> GetMacAsync(
            string ip,
            int attempts = 5,
            int delayMs = 300,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            for (int i = 0; i < attempts; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string mac = null;
                
                await _arpThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    mac = ForceArp(ip);
                }
                finally
                {
                    _arpThrottle.Release();
                }

                if (!string.IsNullOrWhiteSpace(mac))
                    return mac;

                await IsDeviceReachableAsync(ip, 700, cancellationToken).ConfigureAwait(false);

                await _arpThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    mac = ForceArp(ip);
                }
                finally
                {
                    _arpThrottle.Release();
                }

                if (!string.IsNullOrWhiteSpace(mac))
                    return mac;

                if (i < attempts - 1)
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }

            return "";
        }

        public static string GetVendor(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
                return "";

            return VendorLookup.GetVendor(mac);
        }

        public static bool IsIpOnAnyLocalSubnet(string ip)
        {
            IPAddress target;

            if (!IPAddress.TryParse(ip, out target) ||
                target.AddressFamily != AddressFamily.InterNetwork)
                return false;

            foreach (LocalIPv4Network network in GetLocalIPv4Networks())
            {
                if (IsSameSubnet(target, network.Address, network.Mask))
                    return true;
            }

            return false;
        }

        public static bool IsIpOnAnyLocalSubnet(string ip, string subnetMask)
        {
            IPAddress target;
            IPAddress mask;

            if (!IPAddress.TryParse(ip, out target) ||
                target.AddressFamily != AddressFamily.InterNetwork ||
                !IPAddress.TryParse(subnetMask, out mask) ||
                mask.AddressFamily != AddressFamily.InterNetwork)
                return IsIpOnAnyLocalSubnet(ip);

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                foreach (UnicastIPAddressInformation ipInfo in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork &&
                        IsSameSubnet(target, ipInfo.Address, mask))
                        return true;
                }
            }

            return false;
        }

        public static bool IsIpOnLocalSubnetForAdapter(string adapterAlias, string ip, string subnetMask)
        {
            IPAddress target;
            IPAddress mask;

            if (string.IsNullOrWhiteSpace(adapterAlias) ||
                !IPAddress.TryParse(ip, out target) ||
                target.AddressFamily != AddressFamily.InterNetwork ||
                !IPAddress.TryParse(subnetMask, out mask) ||
                mask.AddressFamily != AddressFamily.InterNetwork)
                return false;

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                    !string.Equals(ni.Name, adapterAlias, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (UnicastIPAddressInformation ipInfo in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork &&
                        IsSameSubnet(target, ipInfo.Address, mask))
                        return true;
                }
            }

            return false;
        }

        public static string GetBestAdapterNameForTarget(string targetIp, string subnetMask, string fallbackIp = null)
        {
            IPAddress target;
            IPAddress mask;
            IPAddress fallback = null;

            if (!IPAddress.TryParse(targetIp, out target) ||
                target.AddressFamily != AddressFamily.InterNetwork ||
                !IPAddress.TryParse(subnetMask, out mask) ||
                mask.AddressFamily != AddressFamily.InterNetwork)
                return "";

            if (!string.IsNullOrWhiteSpace(fallbackIp))
                IPAddress.TryParse(fallbackIp, out fallback);

            NetworkInterface firstUsable = null;
            NetworkInterface firstPhysicalUsable = null;
            NetworkInterface virtualSubnetMatch = null;

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                if (firstUsable == null)
                    firstUsable = ni;

                bool isVirtual = IsLikelyVirtualAdapter(ni);
                if (!isVirtual && firstPhysicalUsable == null)
                    firstPhysicalUsable = ni;

                foreach (UnicastIPAddressInformation ipInfo in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ipInfo.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IsSameSubnet(target, ipInfo.Address, mask))
                    {
                        if (!isVirtual)
                            return ni.Name;

                        if (virtualSubnetMatch == null)
                            virtualSubnetMatch = ni;
                    }

                    if (fallback != null &&
                        fallback.AddressFamily == AddressFamily.InterNetwork &&
                        ipInfo.IPv4Mask != null &&
                        IsSameSubnet(fallback, ipInfo.Address, ipInfo.IPv4Mask))
                    {
                        if (!isVirtual)
                            return ni.Name;

                        if (virtualSubnetMatch == null)
                            virtualSubnetMatch = ni;
                    }
                }
            }

            string preferredPhysical = GetPreferredPhysicalAdapterName();
            if (!string.IsNullOrWhiteSpace(preferredPhysical))
                return preferredPhysical;

            if (firstPhysicalUsable != null)
                return firstPhysicalUsable.Name;

            return virtualSubnetMatch?.Name ?? firstUsable?.Name ?? "";
        }

        public static string GetPreferredPhysicalAdapterName()
        {
            List<NetworkInterface> ethernetCandidates = new List<NetworkInterface>();
            List<NetworkInterface> wirelessCandidates = new List<NetworkInterface>();

            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                    IsLikelyVirtualAdapter(adapter))
                {
                    continue;
                }

                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    ethernetCandidates.Add(adapter);
                }
                else if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    wirelessCandidates.Add(adapter);
                }
            }

            if (ethernetCandidates.Count == 1)
                return ethernetCandidates[0].Name;

            if (ethernetCandidates.Count == 0 && wirelessCandidates.Count == 1)
                return wirelessCandidates[0].Name;

            return "";
        }

        public static string GetKnownSubnetMaskForTarget(string targetIp)
        {
            IPAddress target;
            if (!IPAddress.TryParse(targetIp, out target) || target.AddressFamily != AddressFamily.InterNetwork)
                return "";

            string bestMask = "";
            int bestPrefix = -1;

            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.IPv4Mask == null)
                        continue;

                    if (!IsSameSubnet(target, address.Address, address.IPv4Mask))
                        continue;

                    int prefix = CountSubnetMaskBits(address.IPv4Mask);
                    if (prefix > bestPrefix)
                    {
                        bestPrefix = prefix;
                        bestMask = address.IPv4Mask.ToString();
                    }
                }
            }

            return bestMask;
        }

        private static int CountSubnetMaskBits(IPAddress subnetMask)
        {
            if (subnetMask == null)
                return 0;

            int prefix = 0;
            foreach (byte b in subnetMask.GetAddressBytes())
            {
                byte value = b;
                for (int i = 0; i < 8; i++)
                {
                    if ((value & 0x80) == 0)
                        return prefix;

                    prefix++;
                    value <<= 1;
                }
            }

            return prefix;
        }

        public static bool IsLikelyVirtualAdapter(NetworkInterface adapter)
        {
            if (adapter == null)
                return true;

            string text = ((adapter.Name ?? "") + " " + (adapter.Description ?? "")).ToLowerInvariant();
            string[] markers =
            {
                "virtual",
                "vmware",
                "virtualbox",
                "hyper-v",
                "vpn",
                "fortinet",
                "tap",
                "bluetooth",
                "loopback",
                "wan miniport",
                "npcap"
            };

            foreach (string marker in markers)
            {
                if (text.Contains(marker))
                    return true;
            }

            return false;
        }

        public static string GetReachabilityWarning(string ip)
        {
            if (IsIpOnAnyLocalSubnet(ip))
                return "";

            List<string> localNetworks = new List<string>();

            foreach (LocalIPv4Network network in GetLocalIPv4Networks())
            {
                localNetworks.Add(network.Address + " / " + network.Mask);
            }

            string suffix = localNetworks.Count == 0
                ? "No active IPv4 adapter subnet was found."
                : "Current PC IPv4 adapter subnets: " + string.Join(", ", localNetworks);

            return "The new IP address is not in any IPv4 subnet currently assigned to this PC. " +
                   "SADP may still see the camera, but ping, web access, MAC lookup, OUI lookup, and activation refresh will not work until the PC has an IP address or route for that subnet.\r\n\r\n" +
                   suffix;
        }

        public static async Task<string> GetHostNameAsync(string ip)
        {
            return await Task.Run(() =>
            {
                try
                {
                    IPHostEntry entry = Dns.GetHostEntry(ip);
                    return entry.HostName ?? "";
                }
                catch
                {
                    return "";
                }
            });
        }

        private static IEnumerable<LocalIPv4Network> GetLocalIPv4Networks()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (UnicastIPAddressInformation ipInfo in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ipInfo.Address.AddressFamily != AddressFamily.InterNetwork ||
                        ipInfo.IPv4Mask == null ||
                        ipInfo.Address.ToString().StartsWith("169.254."))
                        continue;

                    yield return new LocalIPv4Network
                    {
                        Address = ipInfo.Address,
                        Mask = ipInfo.IPv4Mask
                    };
                }
            }
        }

        private static bool IsSameSubnet(IPAddress left, IPAddress right, IPAddress mask)
        {
            uint leftValue = ToUInt32(left);
            uint rightValue = ToUInt32(right);
            uint maskValue = ToUInt32(mask);

            return (leftValue & maskValue) == (rightValue & maskValue);
        }

        private static uint ToUInt32(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();

            return (uint)(bytes[0] << 24
                | bytes[1] << 16
                | bytes[2] << 8
                | bytes[3]);
        }

        private class LocalIPv4Network
        {
            public IPAddress Address { get; set; }
            public IPAddress Mask { get; set; }
        }
    }
}
