using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace UniversalScanner
{
    public class NetworkAdapterService
    {
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        public bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public List<NetworkAdapterInfo> GetAdapters()
        {
            string script =
                "Get-NetAdapter | Sort-Object InterfaceAlias | " +
                "Select-Object InterfaceAlias,Name,@{Name='Description';Expression={$_.InterfaceDescription}},Status,InterfaceIndex,MacAddress | " +
                "ConvertTo-Json -Depth 4";

            string json = RunPowerShell(script, 15000);
            List<NetworkAdapterInfo> adapters = DeserializeList<NetworkAdapterInfo>(json);
            return adapters ?? new List<NetworkAdapterInfo>();
        }

        public NetworkProfile CaptureProfile(string interfaceAlias, string profileName)
        {
            string psAlias = QuotePowerShell(interfaceAlias);
            string script = @"
$adapterAlias = " + psAlias + @"
$adapter = Get-NetAdapter -InterfaceAlias $adapterAlias -ErrorAction Stop
$ipif = Get-NetIPInterface -InterfaceAlias $adapterAlias -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1
$cimAdapter = Get-CimInstance Win32_NetworkAdapter -Filter (""NetConnectionID='"" + $adapterAlias.Replace(""'"", ""''"") + ""'"") -ErrorAction SilentlyContinue | Select-Object -First 1
$cimConfig = $null
if ($cimAdapter) {
    $cimConfig = Get-CimInstance Win32_NetworkAdapterConfiguration -Filter (""Index="" + $cimAdapter.Index) -ErrorAction SilentlyContinue | Select-Object -First 1
}
$registryConfig = $null
if ($cimAdapter -and $cimAdapter.GUID) {
    $registryPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\' + $cimAdapter.GUID
    if (Test-Path $registryPath) {
        $registryConfig = Get-ItemProperty -Path $registryPath -ErrorAction SilentlyContinue
    }
}
function Convert-MaskToPrefix([string]$mask) {
    if ([string]::IsNullOrWhiteSpace($mask)) { return 0 }
    $prefix = 0
    foreach ($part in $mask.Split('.')) {
        $value = 0
        if (-not [int]::TryParse($part, [ref]$value)) { return 0 }
        while ($value -gt 0) {
            $prefix += ($value -band 1)
            $value = $value -shr 1
        }
    }
    return $prefix
}
function Convert-PrefixToMask([int]$prefix) {
    if ($prefix -lt 0 -or $prefix -gt 32) { return '' }
    [uint32]$mask = 0
    if ($prefix -gt 0) {
        $mask = [uint32]::MaxValue -shl (32 - $prefix)
    }
    return (($mask -shr 24) -band 255).ToString() + '.' +
        (($mask -shr 16) -band 255).ToString() + '.' +
        (($mask -shr 8) -band 255).ToString() + '.' +
        ($mask -band 255).ToString()
}
$addresses = @()
$staticFromRegistry = ($registryConfig -and $registryConfig.EnableDHCP -ne $null -and [int]$registryConfig.EnableDHCP -eq 0)
if ($staticFromRegistry -and $registryConfig.IPAddress) {
    $registryIps = @($registryConfig.IPAddress)
    $registryMasks = @($registryConfig.SubnetMask)
    for ($i = 0; $i -lt $registryIps.Count; $i++) {
        $ip = [string]$registryIps[$i]
        if ($ip -match '^\d+\.\d+\.\d+\.\d+$' -and $ip -ne '0.0.0.0') {
            $mask = ''
            if ($registryMasks.Count -gt $i) { $mask = [string]$registryMasks[$i] }
            [int]$prefix = Convert-MaskToPrefix $mask
            if ($prefix -gt 0) {
                $addresses += [pscustomobject]@{ IPAddress = $ip; SubnetMask = $mask; PrefixLength = $prefix }
            }
        }
    }
}
if ($addresses.Count -eq 0 -and $cimConfig -and $cimConfig.IPAddress) {
    for ($i = 0; $i -lt $cimConfig.IPAddress.Count; $i++) {
        $ip = [string]$cimConfig.IPAddress[$i]
        if ($ip -match '^\d+\.\d+\.\d+\.\d+$' -and $ip -notlike '169.254.*') {
            $mask = ''
            if ($cimConfig.IPSubnet -and $cimConfig.IPSubnet.Count -gt $i) { $mask = [string]$cimConfig.IPSubnet[$i] }
            [int]$prefix = Convert-MaskToPrefix $mask
            if ($prefix -le 0) {
                $netIp = Get-NetIPAddress -InterfaceAlias $adapterAlias -AddressFamily IPv4 -IPAddress $ip -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($netIp) { $prefix = [int]$netIp.PrefixLength }
            }
            if ($prefix -gt 0) {
                if ([string]::IsNullOrWhiteSpace($mask)) { $mask = Convert-PrefixToMask $prefix }
                $addresses += [pscustomobject]@{ IPAddress = $ip; SubnetMask = $mask; PrefixLength = $prefix }
            }
        }
    }
}
if ($addresses.Count -eq 0) {
    $addresses = @(Get-NetIPAddress -InterfaceAlias $adapterAlias -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -and $_.IPAddress -notlike '169.254.*' -and $_.PrefixLength -lt 32 } |
        Sort-Object IPAddress |
        ForEach-Object { [pscustomobject]@{ IPAddress = $_.IPAddress; SubnetMask = Convert-PrefixToMask ([int]$_.PrefixLength); PrefixLength = $_.PrefixLength } })
}
$config = $null
$dnsInfo = $null
if (-not $staticFromRegistry) {
    $config = Get-NetIPConfiguration -InterfaceAlias $adapterAlias -ErrorAction SilentlyContinue
    $dnsInfo = Get-DnsClientServerAddress -InterfaceAlias $adapterAlias -AddressFamily IPv4 -ErrorAction SilentlyContinue
}
$gateway = ''
if ($staticFromRegistry -and $registryConfig.DefaultGateway) {
    $gateway = @($registryConfig.DefaultGateway | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } | Select-Object -First 1)
}
if ([string]::IsNullOrWhiteSpace($gateway) -and $cimConfig -and $cimConfig.DefaultIPGateway) {
    $gateway = @($cimConfig.DefaultIPGateway | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } | Select-Object -First 1)
}
if ([string]::IsNullOrWhiteSpace($gateway) -and $config -and $config.IPv4DefaultGateway) { $gateway = @($config.IPv4DefaultGateway)[0].NextHop }
$dns = @()
$dns = @()
if ($staticFromRegistry -and -not [string]::IsNullOrWhiteSpace([string]$registryConfig.NameServer)) {
    $dns = @(([string]$registryConfig.NameServer).Split(@(',', ' '), [System.StringSplitOptions]::RemoveEmptyEntries) |
        Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' })
}
if ($dns.Count -eq 0 -and $cimConfig -and $cimConfig.DNSServerSearchOrder) {
    $dns = @($cimConfig.DNSServerSearchOrder | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' })
}
if ($dns.Count -eq 0 -and $dnsInfo -and $dnsInfo.ServerAddresses) { $dns = @($dnsInfo.ServerAddresses) }
$dhcpEnabled = $false
if ($registryConfig -ne $null -and $registryConfig.EnableDHCP -ne $null) {
    $dhcpEnabled = ([int]$registryConfig.EnableDHCP -ne 0)
} elseif ($cimConfig -ne $null -and $cimConfig.DHCPEnabled -ne $null) {
    $dhcpEnabled = [bool]$cimConfig.DHCPEnabled
} elseif ($ipif) {
    $dhcpEnabled = ($ipif.Dhcp -eq 'Enabled')
}
[pscustomobject]@{
    ProfileName = " + QuotePowerShell(profileName) + @"
    InterfaceAlias = $adapter.InterfaceAlias
    DhcpEnabled = $dhcpEnabled
    Addresses = $addresses
    Gateway = $gateway
    DnsServers = $dns
    InterfaceMetric = if ($ipif) { $ipif.InterfaceMetric } elseif ($registryConfig -and $registryConfig.InterfaceMetric) { [int]$registryConfig.InterfaceMetric } else { $null }
} | ConvertTo-Json -Depth 6";

            string json = RunPowerShell(script, 30000);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("PowerShell returned no adapter profile data.");

            // Normalize JSON to fix PowerShell 5.1 ConvertTo-Json single-element array unwrapping
            string normalizedJson = NetworkProfileManager.NormalizeJsonForProfile(serializer, json);
            NetworkProfile profile = serializer.Deserialize<NetworkProfile>(normalizedJson);
            if (profile == null)
                throw new InvalidOperationException("Unable to parse adapter profile data.");

            if (profile.Addresses == null)
                profile.Addresses = new List<NetworkProfileAddress>();

            if (profile.DnsServers == null)
                profile.DnsServers = new List<string>();

            return profile;
        }

        public void ApplyDhcp(string interfaceAlias)
        {
            string psAlias = QuotePowerShell(interfaceAlias);
            string script = @"
$adapterAlias = " + psAlias + @"
Get-NetAdapter -InterfaceAlias $adapterAlias -ErrorAction Stop | Out-Null
Set-NetIPInterface -InterfaceAlias $adapterAlias -AddressFamily IPv4 -Dhcp Enabled -ErrorAction Stop
Set-DnsClientServerAddress -InterfaceAlias $adapterAlias -ResetServerAddresses -ErrorAction SilentlyContinue
Get-NetRoute -InterfaceAlias $adapterAlias -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
    Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue
Get-NetIPAddress -InterfaceAlias $adapterAlias -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -and $_.IPAddress -notlike '169.254.*' } |
    Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue
Set-NetIPInterface -InterfaceAlias $adapterAlias -AddressFamily IPv4 -Dhcp Enabled -ErrorAction Stop
";

            RunPowerShell(script, 45000);
        }

        public void RestoreProfile(NetworkProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException("profile");

            if (string.IsNullOrWhiteSpace(profile.InterfaceAlias))
                throw new ArgumentException("Profile does not contain an adapter InterfaceAlias.");

            string psAlias = QuotePowerShell(profile.InterfaceAlias);
            StringBuilder script = new StringBuilder();
            script.AppendLine("$ProgressPreference = 'SilentlyContinue'");
            script.AppendLine("$adapterAlias = " + psAlias);
            script.AppendLine("Get-NetAdapter -InterfaceAlias $adapterAlias -ErrorAction Stop | Out-Null");
            script.AppendLine("Get-NetRoute -InterfaceAlias $adapterAlias -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue");
            script.AppendLine("Get-NetIPAddress -InterfaceAlias $adapterAlias -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -and $_.IPAddress -notlike '169.254.*' } | Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue");
            script.AppendLine("Start-Sleep -Milliseconds 750");

            if (profile.DhcpEnabled)
            {
                script.AppendLine("Set-NetIPInterface -InterfaceAlias $adapterAlias -AddressFamily IPv4 -Dhcp Enabled -ErrorAction Stop");
            }
            else
            {
                script.AppendLine("Set-NetIPInterface -InterfaceAlias $adapterAlias -AddressFamily IPv4 -Dhcp Disabled -ErrorAction Stop");

                bool gatewayUsed = false;
                foreach (NetworkProfileAddress address in profile.Addresses ?? new List<NetworkProfileAddress>())
                {
                    if (string.IsNullOrWhiteSpace(address.IPAddress))
                        continue;

                    if (address.IPAddress.StartsWith("169.254.", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Validate PrefixLength: must be 1-32; skip invalid entries (fixes PowerShell 5.1 array unwrapping artifacts)
                    if (address.PrefixLength < 1 || address.PrefixLength > 32)
                        continue;

                    string quotedIp = QuotePowerShell(address.IPAddress);
                    script.AppendLine("$existingIp = Get-NetIPAddress -InterfaceAlias $adapterAlias -AddressFamily IPv4 -IPAddress " + quotedIp + " -ErrorAction SilentlyContinue | Select-Object -First 1");
                    script.AppendLine("if ($existingIp -and [int]$existingIp.PrefixLength -ne " + address.PrefixLength + ") { $existingIp | Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 250; $existingIp = $null }");

                    string command = "if (-not $existingIp) { New-NetIPAddress -InterfaceAlias $adapterAlias -IPAddress " +
                        quotedIp +
                        " -PrefixLength " + address.PrefixLength;

                    if (!gatewayUsed && !string.IsNullOrWhiteSpace(profile.Gateway))
                    {
                        command += " -DefaultGateway " + QuotePowerShell(profile.Gateway);
                        gatewayUsed = true;
                    }

                    command += " -ErrorAction Stop | Out-Null }";
                    script.AppendLine(command);
                }

            }

            if (profile.DnsServers != null && profile.DnsServers.Count > 0)
            {
                List<string> quotedDns = new List<string>();
                foreach (string dns in profile.DnsServers)
                {
                    if (!string.IsNullOrWhiteSpace(dns))
                        quotedDns.Add(QuotePowerShell(dns));
                }

                if (quotedDns.Count > 0)
                {
                    script.AppendLine("Set-DnsClientServerAddress -InterfaceAlias $adapterAlias -ServerAddresses @(" +
                        string.Join(",", quotedDns.ToArray()) + ") -ErrorAction Stop");
                }
            }
            else
            {
                script.AppendLine("Set-DnsClientServerAddress -InterfaceAlias $adapterAlias -ResetServerAddresses -ErrorAction SilentlyContinue");
            }

            if (profile.InterfaceMetric.HasValue)
            {
                script.AppendLine("Set-NetIPInterface -InterfaceAlias $adapterAlias -AddressFamily IPv4 -InterfaceMetric " +
                    profile.InterfaceMetric.Value + " -ErrorAction SilentlyContinue");
            }

            RunPowerShell(script.ToString(), 90000);
        }

        private List<T> DeserializeList<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<T>();

            string trimmed = json.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
                return serializer.Deserialize<List<T>>(trimmed);

            T single = serializer.Deserialize<T>(trimmed);
            List<T> list = new List<T>();
            if (single != null)
                list.Add(single);
            return list;
        }

        private string RunPowerShell(string script, int timeoutMs)
        {
            string command = "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " + script;
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "powershell.exe";
            startInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }

                    throw new TimeoutException("PowerShell network adapter command timed out.");
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException((error + "\r\n" + output).Trim());

                return output.Trim();
            }
        }

        private static string QuotePowerShell(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }
    }
}
