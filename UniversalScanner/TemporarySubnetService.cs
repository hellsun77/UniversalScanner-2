using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace UniversalScanner
{
    public class TemporarySubnetService
    {
        private static TemporarySubnetService _instance;
        public static TemporarySubnetService Instance => _instance ?? (_instance = new TemporarySubnetService());

        public List<TemporaryIpRecord> ActiveRecords { get; } = new List<TemporaryIpRecord>();

        private TemporarySubnetService()
        {
        }

        public bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsCameraReachableAsync(IPAddress cameraIp, IPAddress sourceIp = null, string requiredAdapterAlias = null)
        {
            string ipStr = cameraIp.ToString();
            string sourceIpStr = sourceIp?.ToString();
            string routeAlias = "";
            if (!string.IsNullOrEmpty(sourceIpStr) && !string.IsNullOrEmpty(requiredAdapterAlias))
            {
                try
                {
                    string verifyScript = $@"
                        $ipObj = Get-NetIPAddress -IPAddress '{sourceIpStr}' -InterfaceAlias '{requiredAdapterAlias}' -ErrorAction SilentlyContinue
                        if ($ipObj) {{
                            if ($ipObj.SkipAsSource) {{
                                Set-NetIPAddress -IPAddress '{sourceIpStr}' -InterfaceAlias '{requiredAdapterAlias}' -SkipAsSource $false -ErrorAction SilentlyContinue
                            }}
                        }}
                    ";
                    await RunPowerShellAsync(verifyScript, 5000);
                }
                catch (Exception ex)
                {
                    Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"Failed to verify SkipAsSource for {sourceIpStr}: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(requiredAdapterAlias))
            {
                try 
                {
                    string routeCheckScript = $"$route = Find-NetRoute -RemoteIPAddress '{ipStr}' -ErrorAction SilentlyContinue | Select-Object -First 1; if ($route) {{ $route.InterfaceAlias }}";
                    routeAlias = await RunPowerShellWithOutputAsync(routeCheckScript, 5000);
                    if (routeAlias != null) routeAlias = routeAlias.Trim();

                    if (!string.IsNullOrEmpty(routeAlias) && !routeAlias.Equals(requiredAdapterAlias, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"Camera route uses '{routeAlias}' instead of '{requiredAdapterAlias}'. Treating as unreachable so a temporary IP is added on the correct adapter.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"Failed to check route for {ipStr}: {ex.Message}");
                }
            }

            bool arpResolved = NetworkTools.IsArpResolved(ipStr);
            TempSubnetLogger.Log($"ARP Result: {arpResolved}");
            if (arpResolved) 
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"Connectivity check for {ipStr} - ARP: True");
                return true;
            }
            
            bool tcp80 = await NetworkTools.IsTcpPortOpenAsync(ipStr, 80, 5000, sourceIpStr);
            TempSubnetLogger.Log($"TCP 80 Result: {tcp80}");
            if (tcp80) 
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"Connectivity check for {ipStr} - TCP 80: True");
                return true;
            }

            bool tcp443 = await NetworkTools.IsTcpPortOpenAsync(ipStr, 443, 5000, sourceIpStr);
            TempSubnetLogger.Log($"TCP 443 Result: {tcp443}");
            if (tcp443) 
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"Connectivity check for {ipStr} - TCP 443: True");
                return true;
            }

            bool tcp554 = await NetworkTools.IsTcpPortOpenAsync(ipStr, 554, 5000, sourceIpStr);
            TempSubnetLogger.Log($"RTSP 554 Result: {tcp554}");
            if (tcp554) 
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"Connectivity check for {ipStr} - TCP 554: True");
                return true;
            }

            bool ping = await NetworkTools.IsOnlineAsync(ipStr, 5000);
            TempSubnetLogger.Log($"Ping Result: {ping}");
            if (ping) 
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"Connectivity check for {ipStr} - Ping: True");
                return true;
            }

            // If we got here, all checks failed. Log detailed diagnostics.
            try
            {
                string diagScript = $@"
                    $ipObj = Get-NetIPAddress -IPAddress '{sourceIpStr}' -InterfaceAlias '{requiredAdapterAlias}' -ErrorAction SilentlyContinue
                    $state = if ($ipObj) {{ $ipObj.AddressState }} else {{ 'NotFound' }}
                    $skip = if ($ipObj) {{ $ipObj.SkipAsSource }} else {{ 'N/A' }}
                    return ""State=$state|Skip=$skip""
                ";
                string diagResult = "";
                if (!string.IsNullOrEmpty(sourceIpStr) && !string.IsNullOrEmpty(requiredAdapterAlias))
                {
                    diagResult = await RunPowerShellWithOutputAsync(diagScript, 5000);
                    if (diagResult != null) diagResult = diagResult.Trim();
                }

                Logger.getInstance().WriteLine(Logger.DebugLevel.Error, 
                    $"Reachability failure diagnostics for {ipStr}: " +
                    $"Alias='{requiredAdapterAlias}', SourceIP='{sourceIpStr}', {diagResult}, " +
                    $"Route='{routeAlias}', ARP={arpResolved}, TCP80={tcp80}, TCP443={tcp443}, TCP554={tcp554}, Ping={ping}");
            }
            catch { }

            return false;
        }

        public string DetermineSubnetMask(string cameraIp, string existingSubnetMask = null)
        {
            if (!string.IsNullOrEmpty(existingSubnetMask) && existingSubnetMask != "0.0.0.0")
            {
                return existingSubnetMask;
            }
            
            // Fallback to /24
            return "255.255.255.0";
        }

        private int GetPrefixLength(string subnetMask)
        {
            if (!IPAddress.TryParse(subnetMask, out var maskIp)) return 24;
            var bytes = maskIp.GetAddressBytes();
            int prefix = 0;
            foreach (var b in bytes)
            {
                int val = b;
                while ((val & 0x80) != 0)
                {
                    prefix++;
                    val <<= 1;
                }
            }
            return prefix;
        }

        private byte[] GetNetworkAddress(byte[] ip, byte[] mask)
        {
            var net = new byte[ip.Length];
            for (int i = 0; i < net.Length; i++)
                net[i] = (byte)(ip[i] & mask[i]);
            return net;
        }

        public IPAddress FindAvailableTemporaryIp(IPAddress cameraIp, IPAddress subnetMask)
        {
            if (cameraIp == null || subnetMask == null)
                return null;

            var cIp = cameraIp;
            var cMask = subnetMask;

            var ipBytes = cIp.GetAddressBytes();
            var maskBytes = cMask.GetAddressBytes();
            var netBytes = GetNetworkAddress(ipBytes, maskBytes);

            // Create a hashset of local IPs to avoid
            var localIps = new HashSet<string>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (var uipi in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (uipi.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            localIps.Add(uipi.Address.ToString());
                        }
                    }
                }
            }

            // Prefer .250 down to .2
            for (int i = 250; i >= 2; i--)
            {
                var candidate = new byte[4];
                Array.Copy(netBytes, candidate, 4);
                
                // Only modify the host portion (assuming /24 for simplicity in this fallback logic, 
                // but proper bitwise applying would be better. For a true /24, mask is 255.255.255.0)
                // A correct approach for any mask:
                for (int j = 0; j < 4; j++)
                {
                    candidate[j] = (byte)(netBytes[j] | (~maskBytes[j] & (i >> (8 * (3 - j)))));
                }

                var candidateIp = new IPAddress(candidate).ToString();

                // Skip camera IP
                if (candidateIp == cIp.ToString()) continue;
                
                // Skip local IPs
                if (localIps.Contains(candidateIp)) continue;

                // To be completely safe, we could optionally ping the candidate, but we skip it here to save time
                return IPAddress.Parse(candidateIp);
            }

            return null;
        }

        public TemporaryIpRecord GetExistingRecordForSubnet(IPAddress cameraIp, IPAddress subnetMask)
        {
            if (cameraIp == null || subnetMask == null)
                return null;

            var cIp = cameraIp;
            var cMask = subnetMask;

            var ipBytes = cIp.GetAddressBytes();
            var maskBytes = cMask.GetAddressBytes();
            var netBytes = GetNetworkAddress(ipBytes, maskBytes);

            foreach (var record in ActiveRecords)
            {
                if (!IPAddress.TryParse(record.TemporaryIp, out var rIp) || !IPAddress.TryParse(record.SubnetMask, out var rMask))
                    continue;

                var rIpBytes = rIp.GetAddressBytes();
                var rMaskBytes = rMask.GetAddressBytes();
                var rNetBytes = GetNetworkAddress(rIpBytes, rMaskBytes);

                if (netBytes.SequenceEqual(rNetBytes) && maskBytes.SequenceEqual(rMaskBytes))
                {
                    return record;
                }
            }

            return null;
        }

        public class AddIpResult
        {
            public bool Success { get; set; }
            public bool DhcpDetected { get; set; }
            public bool WasDhcpBefore { get; set; }
            public string OriginalDhcpIp { get; set; }
            public string OriginalDhcpMask { get; set; }
            public string OriginalDhcpGateway { get; set; }
            public string OriginalDhcpDns { get; set; }
            public string ErrorMessage { get; set; }
        }

        private async Task<string> GetNetworkStateSnapshotAsync(string adapterAlias)
        {
            string script = $@"
$iface = Get-NetIPInterface -InterfaceAlias '{adapterAlias}' -AddressFamily IPv4 -ErrorAction SilentlyContinue
$ip = Get-NetIPAddress -InterfaceAlias '{adapterAlias}' -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object PrefixOrigin -ne 'WellKnown' | Select-Object -First 1
$config = Get-NetIPConfiguration -InterfaceAlias '{adapterAlias}' -ErrorAction SilentlyContinue

$dhcp = if ($iface) {{ $iface.Dhcp }} else {{ 'Unknown' }}
$ipStr = if ($ip) {{ $ip.IPAddress }} else {{ 'None' }}

$maskStr = 'None'
if ($ip) {{
    $p = [int]$ip.PrefixLength
    $m = [uint32]::MaxValue -shl (32-$p)
    $maskStr = ""$([math]::Floor(($m -shr 24) -band 255)).$([math]::Floor(($m -shr 16) -band 255)).$([math]::Floor(($m -shr 8) -band 255)).$([math]::Floor($m -band 255))""
}}

$gwStr = if ($config -and $config.IPv4DefaultGateway) {{ $config.IPv4DefaultGateway[0].NextHop }} else {{ 'None' }}
$dnsStr = if ($config -and $config.DNSServer) {{ ($config.DNSServer | Select-Object -ExpandProperty ServerAddresses) -join ',' }} else {{ 'None' }}

Write-Output ""$dhcp|$ipStr|$maskStr|$gwStr|$dnsStr""
";
            try
            {
                string result = await RunPowerShellWithOutputAsync(script, 5000);
                return result?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private async Task LogAdapterState(string title, string adapterAlias)
        {
            TempSubnetLogger.LogSubHeader(title);
            string script = $@"
                $ips = Get-NetIPAddress -InterfaceAlias '{adapterAlias}' -AddressFamily IPv4 -ErrorAction SilentlyContinue | Format-Table IPAddress, PrefixLength, SkipAsSource, AddressState -AutoSize | Out-String
                $routes = Get-NetRoute -InterfaceAlias '{adapterAlias}' -AddressFamily IPv4 -ErrorAction SilentlyContinue | Format-Table DestinationPrefix, NextHop, RouteMetric -AutoSize | Out-String
                $dhcp = (Get-NetIPInterface -InterfaceAlias '{adapterAlias}' -AddressFamily IPv4 -ErrorAction SilentlyContinue).Dhcp
                Write-Output ""DHCP Enabled: $dhcp""
                Write-Output ""IPv4 Addresses:""
                Write-Output $ips
                Write-Output ""Routes:""
                Write-Output $routes
            ";
            await RunPowerShellWithOutputAsync(script, 8000);
        }

        public async Task<AddIpResult> AddTemporaryIpAsync(string adapterAlias, IPAddress temporaryIp, IPAddress subnetMask, IPAddress cameraIp = null, bool allowAdvancedDhcp = false)
        {
            string ipStr = temporaryIp.ToString();
            string maskStr = subnetMask.ToString();
            string cameraIpStr = cameraIp?.ToString();

            var result = new AddIpResult();
            if (string.IsNullOrWhiteSpace(adapterAlias))
            {
                result.ErrorMessage = "No network adapter was selected.";
                Logger.getInstance().WriteLine(Logger.DebugLevel.Error, "[TemporarySubnet] No network adapter was selected.");
                return result;
            }

            await LogAdapterState("Before adding temporary IP", adapterAlias);

            string preState = await GetNetworkStateSnapshotAsync(adapterAlias);
            var preParts = preState.Split('|');
            
            if (preParts.Length >= 5)
            {
                if (preParts[0] == "Enabled")
                {
                    if (!allowAdvancedDhcp)
                    {
                        result.DhcpDetected = true;
                        return result;
                    }
                    else
                    {
                        // Advanced mode: convert to static
                        result.WasDhcpBefore = true;
                        result.OriginalDhcpIp = preParts[1];
                        result.OriginalDhcpMask = preParts[2];
                        result.OriginalDhcpGateway = preParts[3];
                        result.OriginalDhcpDns = preParts[4];
                        
                        string gwParam = (result.OriginalDhcpGateway != "None" && !string.IsNullOrWhiteSpace(result.OriginalDhcpGateway)) ? result.OriginalDhcpGateway : "";
                        await RunProcessAsync("netsh", $"interface ipv4 set address name=\"{adapterAlias}\" static {result.OriginalDhcpIp} {result.OriginalDhcpMask} {gwParam}", 8000);
                        
                        if (result.OriginalDhcpDns != "None" && !string.IsNullOrWhiteSpace(result.OriginalDhcpDns))
                        {
                            var dnsArray = result.OriginalDhcpDns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (dnsArray.Length > 0)
                                await RunProcessAsync("netsh", $"interface ipv4 set dns name=\"{adapterAlias}\" static {dnsArray[0]}", 8000);
                            if (dnsArray.Length > 1)
                                await RunProcessAsync("netsh", $"interface ipv4 add dns name=\"{adapterAlias}\" {dnsArray[1]} index=2", 8000);
                        }
                        
                        // Let adapter settle
                        await Task.Delay(1000);
                    }
                }
            }

            int prefixLen = GetPrefixLength(maskStr);
            string script = $"New-NetIPAddress -InterfaceAlias '{adapterAlias}' -IPAddress '{ipStr}' -PrefixLength {prefixLen} -AddressFamily IPv4 -SkipAsSource $false -ErrorAction Stop";
            
            bool success = false;
            try
            {
                await RunPowerShellAsync(script, 8000);
                success = true;
            }
            catch (Exception ex)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"Failed to add temporary IP using PowerShell: {ex.Message}. Falling back to netsh.");
                try
                {
                    string netshArgs = $"interface ipv4 add address name=\"{adapterAlias}\" addr={ipStr} mask={maskStr} skipassource=false";
                    await RunProcessAsync("netsh", netshArgs, 8000);
                    success = true;
                }
                catch (Exception ex2)
                {
                    Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"Failed to add temporary IP using netsh: {ex2.Message}");
                    result.ErrorMessage = ex2.Message;
                    success = false;
                }
            }

            if (success)
            {
                await LogAdapterState("After adding temporary IP", adapterAlias);

                try
                {
                    string validateScript = $@"
                        $ipObj = Get-NetIPAddress -IPAddress '{ipStr}' -InterfaceAlias '{adapterAlias}' -ErrorAction SilentlyContinue
                        if (-not $ipObj) {{ throw 'IP does not exist' }}
                    ";
                    await RunPowerShellAsync(validateScript, 8000);
                    TempSubnetLogger.Log("Did temporary IP actually exist? YES");
                }
                catch (Exception ex)
                {
                    TempSubnetLogger.Log("Did temporary IP actually exist? NO");
                    Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"Temporary IP validation failed: {ex.Message}");
                    result.ErrorMessage = $"Validation failed: {ex.Message}";
                    result.Success = false;
                    try { await RunPowerShellAsync($"Remove-NetIPAddress -InterfaceAlias '{adapterAlias}' -IPAddress '{ipStr}' -Confirm:$false -ErrorAction SilentlyContinue", 5000); } catch {}
                    return result;
                }
            }

            result.Success = success;
            return result;
        }

        public async Task<bool> ValidateAndConfigureRouteAsync(string adapterAlias, IPAddress temporaryIp, IPAddress cameraIp = null)
        {
            string ipStr = temporaryIp.ToString();
            string cameraIpStr = cameraIp?.ToString();

            try
            {
                    string validateScript = $@"
                    $ipObj = Get-NetIPAddress -IPAddress '{ipStr}' -InterfaceAlias '{adapterAlias}' -ErrorAction SilentlyContinue
                    if (-not $ipObj) {{ throw 'IP does not exist' }}
                    if ($ipObj.AddressState -ne 'Preferred') {{ throw 'AddressState is not Preferred' }}
                    $adapter = Get-NetAdapter -Name '{adapterAlias}' -ErrorAction SilentlyContinue
                    if ($adapter.Status -ne 'Up') {{ throw 'Interface is not Connected/Up' }}
                ";
                await RunPowerShellAsync(validateScript, 8000);
            }
            catch (Exception ex)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"Route validation failed: {ex.Message}");
                return false;
            }

            if (!string.IsNullOrEmpty(cameraIpStr))
            {
                try
                {
                    string findRouteScript = $"Find-NetRoute -RemoteIPAddress '{cameraIpStr}' | Select-Object -ExpandProperty InterfaceAlias -First 1";
                    string selectedAlias = await RunPowerShellWithOutputAsync(findRouteScript, 5000);
                    selectedAlias = selectedAlias?.Trim();

                    if (!string.Equals(selectedAlias, adapterAlias, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"Windows selected interface '{selectedAlias}' for {cameraIpStr}. Forcing route through '{adapterAlias}'.");
                        string addRouteScript = $"Remove-NetRoute -DestinationPrefix '{cameraIpStr}/32' -Confirm:$false -ErrorAction SilentlyContinue; New-NetRoute -DestinationPrefix '{cameraIpStr}/32' -InterfaceAlias '{adapterAlias}' -NextHop 0.0.0.0 -RouteMetric 1 -ErrorAction Stop";
                        await RunPowerShellAsync(addRouteScript, 5000);
                    }
                }
                catch (Exception ex)
                {
                    Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"Failed to validate or add temporary route: {ex.Message}");
                    return false;
                }
            }
            return true;
        }

        public async Task<bool> RemoveTemporaryIpAsync(string adapterAlias, IPAddress temporaryIp, IPAddress cameraIp = null, TemporaryIpRecord record = null)
        {
            string ipStr = temporaryIp.ToString();
            string cameraIpStr = cameraIp?.ToString();

            if (!string.IsNullOrEmpty(cameraIpStr))
            {
                try
                {
                    string removeRouteScript = $"Remove-NetRoute -DestinationPrefix '{cameraIpStr}/32' -InterfaceAlias '{adapterAlias}' -NextHop 0.0.0.0 -Confirm:$false -ErrorAction SilentlyContinue";
                    await RunPowerShellAsync(removeRouteScript, 8000);
                }
                catch { }
            }

            TempSubnetLogger.LogSubHeader("Cleanup: Removing temporary IP");
            string script = $"Remove-NetIPAddress -InterfaceAlias '{adapterAlias}' -IPAddress '{ipStr}' -Confirm:$false -ErrorAction Stop";
            bool success = false;
            
            try
            {
                await RunPowerShellAsync(script, 8000);
                success = true;
            }
            catch (Exception ex)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"Failed to remove temporary IP using PowerShell: {ex.Message}. Falling back to netsh.");
                try
                {
                    string netshArgs = $"interface ipv4 delete address name=\"{adapterAlias}\" {ipStr}";
                    await RunProcessAsync("netsh", netshArgs, 8000);
                    success = true;
                }
                catch (Exception ex2)
                {
                    Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"Failed to remove temporary IP using netsh: {ex2.Message}");
                    success = false;
                }
            }

            if (record != null && record.WasDhcpBefore)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"Restoring DHCP for {adapterAlias} after removing temporary IP...");
                await RunProcessAsync("netsh", $"interface ipv4 set address name=\"{adapterAlias}\" dhcp", 5000);
                await RunProcessAsync("netsh", $"interface ipv4 set dns name=\"{adapterAlias}\" dhcp", 5000);
            }
            
            return success;
        }

        public async Task CleanupOnExitAsync(bool autoRemove)
        {
            if (!autoRemove) return;

            var toRemove = ActiveRecords.Where(r => r.CreatedByUniversalScanner).ToList();
            foreach (var record in toRemove)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"Auto-cleanup: Removing temporary IP {record.TemporaryIp} from adapter {record.AdapterName}");
                if (IPAddress.TryParse(record.TemporaryIp, out var rIp))
                {
                    IPAddress cIp = record.RouteAdded && IPAddress.TryParse(record.CameraIp, out var parsedCIp) ? parsedCIp : null;
                    await RemoveTemporaryIpAsync(record.AdapterName, rIp, cIp, record);
                }
            }
            ActiveRecords.Clear();
        }

        private Task RunPowerShellAsync(string script, int timeoutMs = 10000)
        {
            string command = "$ErrorActionPreference = 'Stop'; " + script;
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            return RunProcessAsync("powershell", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}", timeoutMs);
        }

        private async Task<string> RunPowerShellWithOutputAsync(string script, int timeoutMs = 10000)
        {
            string command = "$ErrorActionPreference = 'Stop'; " + script;
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            return await RunProcessWithOutputAsync("powershell", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}", timeoutMs);
        }

        public async Task RunDiagnosticsAsync(string cameraIp, string adapterAlias, string tempIp)
        {
            Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"--- DIAGNOSTICS FOR UNREACHABLE CAMERA {cameraIp} ---");
            
            try {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "ipconfig /all:\n" + await RunProcessWithOutputAsync("ipconfig", "/all", 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "Get-NetAdapter:\n" + await RunPowerShellWithOutputAsync("Get-NetAdapter", 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "Get-NetIPConfiguration:\n" + await RunPowerShellWithOutputAsync("Get-NetIPConfiguration", 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, $"Get-NetIPAddress:\n" + await RunPowerShellWithOutputAsync($"Get-NetIPAddress -InterfaceAlias '{adapterAlias}'", 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "Get-NetNeighbor:\n" + await RunPowerShellWithOutputAsync("Get-NetNeighbor", 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "Find-NetRoute:\n" + await RunPowerShellWithOutputAsync($"Find-NetRoute -RemoteIPAddress '{cameraIp}'", 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "route print -4:\n" + await RunProcessWithOutputAsync("route", "print -4", 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "arp -a:\n" + await RunProcessWithOutputAsync("arp", "-a", 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "Test-NetConnection Port 80:\n" + await RunPowerShellWithOutputAsync($"Test-NetConnection '{cameraIp}' -Port 80", 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "Test-NetConnection Port 443:\n" + await RunPowerShellWithOutputAsync($"Test-NetConnection '{cameraIp}' -Port 443", 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "ping:\n" + await RunProcessWithOutputAsync("ping", cameraIp, 10000));
                Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "tracert:\n" + await RunProcessWithOutputAsync("tracert", $"-d {cameraIp}", 20000));
            }
            catch (Exception ex) {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Error, $"Diagnostics failed: {ex.Message}");
            }
            Logger.getInstance().WriteLine(Logger.DebugLevel.Info, "--- END DIAGNOSTICS ---");
        }

        private async Task<bool> RunProcessAsync(string filename, string args, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (Process p = new Process())
            {
                p.StartInfo.FileName = filename;
                p.StartInfo.Arguments = args;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.EnableRaisingEvents = true;
                
                TempSubnetLogger.Log($"Command executed: {filename} {args}");

                p.Exited += (s, e) =>
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    TempSubnetLogger.Log($"Exit code: {p.ExitCode}");
                    if (!string.IsNullOrWhiteSpace(stdout)) TempSubnetLogger.Log($"Standard Output: {stdout.TrimEnd()}");
                    if (!string.IsNullOrWhiteSpace(stderr)) TempSubnetLogger.Log($"Standard Error: {stderr.TrimEnd()}");

                    if (p.ExitCode == 0)
                        tcs.TrySetResult(true);
                    else
                        tcs.TrySetException(new Exception($"{filename} exited with code {p.ExitCode}"));
                };
                p.Start();
                
                var timeoutTask = Task.Delay(timeoutMs);
                var completed = await Task.WhenAny(tcs.Task, timeoutTask);
                
                if (completed == timeoutTask)
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException($"Process {filename} timed out after {timeoutMs}ms");
                }
                
                return await tcs.Task;
            }
        }

        private async Task<string> RunProcessWithOutputAsync(string filename, string args, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<string>();
            using (Process p = new Process())
            {
                p.StartInfo.FileName = filename;
                p.StartInfo.Arguments = args;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.EnableRaisingEvents = true;

                TempSubnetLogger.Log($"Command executed: {filename} {args}");

                p.Exited += (s, e) =>
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    TempSubnetLogger.Log($"Exit code: {p.ExitCode}");
                    if (!string.IsNullOrWhiteSpace(stdout)) TempSubnetLogger.Log($"Standard Output: {stdout.TrimEnd()}");
                    if (!string.IsNullOrWhiteSpace(stderr)) TempSubnetLogger.Log($"Standard Error: {stderr.TrimEnd()}");

                    tcs.TrySetResult(stdout);
                };
                p.Start();
                
                var timeoutTask = Task.Delay(timeoutMs);
                var completed = await Task.WhenAny(tcs.Task, timeoutTask);
                
                if (completed == timeoutTask)
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException($"Process {filename} timed out after {timeoutMs}ms");
                }
                
                return await tcs.Task;
            }
        }
    }
}
