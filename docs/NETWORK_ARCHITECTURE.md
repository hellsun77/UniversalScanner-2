# Network Architecture

## IP Change Workflow

### High-Level Flow

```
User selects device → ChangeIpForm opens (vendor-routed)
    → Vendor-specific network module executes IP change
    → On success: RefreshChangedHikvisionIpRowAsync()
        → ARP cache flush for old IP
        → RemoveDuplicateRowsForIpChange()
        → RefreshChangedHikvisionIpRowInBackgroundAsync() [25 attempts]
            → Ping new IP
            → SubnetVendorProbe.DetectAsync()
            → MAC/OUI resolution
            → Vendor detection cascade
            → Activation status probe
            → Update grid row
```

### RefreshChangedHikvisionIpRowAsync()

Entry point after a successful IP change. Despite the "Hikvision" name, this method handles all vendors:

1. **ARP cache flush**: Removes the old IP from the system ARP cache to prevent stale mappings
2. **RemoveDuplicateRowsForIpChange()**: Scans `foundDeviceList` for any existing rows matching the new IP and removes them, ensuring a clean slate for the refresh
3. **Row state update**: Marks the device row with "Refresh needed" status
4. **Background refresh launch**: Spawns `RefreshChangedHikvisionIpRowInBackgroundAsync()` as a fire-and-forget task

### RefreshChangedHikvisionIpRowInBackgroundAsync()

Background polling loop that re-discovers the device after IP change:

| Parameter | Value |
|-----------|-------|
| Max attempts | 25 |
| Initial reboot wait | 20 seconds |
| Inter-attempt delay | 4 seconds |
| Total max duration | ~120 seconds |

Each attempt:
1. **Ping new IP** — verifies the device is reachable at its new address
2. **SubnetVendorProbe.DetectAsync()** — HTTP probe to identify the vendor at the new IP
3. **MAC/OUI lookup** — resolves the MAC address via ARP and maps OUI to vendor name
4. **Vendor detection cascade** — runs activation status probes in priority order (Honeywell → Hikvision → Uniview → Dahua → Sunell → Tiandy)
5. **Activation status re-probe** — queries the identified vendor's activation endpoint
6. **Grid row update** — marshals results to the UI thread and refreshes the DataRow

**Honeywell legacy special case**: The old IP must become unreachable (not just changed) before the row is moved to the new IP. This is because legacy Honeywell devices may retain both IPs briefly during transition.

**Failure modes**:
- Device never responds to ping → row stays at "Refresh needed"
- Vendor probe fails → row stays as generic discovery type
- All 25 attempts exhausted → row marked "Unreachable subnet"

### RemoveDuplicateRowsForIpChange()

Called before the background refresh to prevent ghost rows:
- Iterates all rows in `foundDeviceList`
- Removes any row whose IP matches the new IP (same device appearing at new address via discovery before the refresh completes)
- Respects the 5-minute `oldIpSuppressions` dictionary — old IPs are suppressed from re-appearing as "new" discoveries

### IP Change Guard Windows

| Mechanism | Duration | Purpose |
|-----------|----------|---------|
| `oldIpSuppressions` | 5 minutes | Prevents old IP from being re-discovered and shown as a duplicate |
| `recentChangedIps` | Persists across clear operations | Preserves rows during subnet re-scan after IP change |
| `ShouldPreserveExistingIdentity()` | Permanent per session | Prevents generic discoveries from overwriting known vendor identities |

---

## Network Adapter Management

### Architecture

```
NetworkAdapterService (PowerShell bridge)
    ├── GetNetworkAdapters() → List<NetworkAdapterInfo>
    ├── SetStaticIP(adapter, ip, subnet, gateway)
    ├── SetDHCP(adapter)
    ├── AddTemporaryRoute(destination, gateway) → IDisposable
    └── GetCurrentProfile() → NetworkProfile

NetworkAdapterInfo (POCO)
    ├── Name, Description, InterfaceIndex
    ├── MAC address, Speed, Status
    ├── Current IP addresses (v4/v6)
    └── DHCP enabled state

NetworkProfile (serializable container)
    ├── Profile name, adapter reference
    ├── List<NetworkProfileAddress> (IP, subnet, gateway, DNS)
    └── DHCP preference per address

NetworkProfileManager
    ├── SaveProfile(name, adapter)
    ├── LoadProfile(name) → NetworkProfile
    ├── ApplyProfile(profile) → bool
    └── DeleteProfile(name)
```

### PowerShell Integration

`NetworkAdapterService` wraps PowerShell 5.0+ cmdlets via `System.Management.Automation`:

| Operation | PowerShell Cmdlet |
|-----------|-------------------|
| Enumerate adapters | `Get-NetAdapter` + `Get-NetIPAddress` + `Get-NetIPInterface` |
| Set static IP | `New-NetIPAddress` + `Set-NetIPInterface` |
| Remove static IP | `Remove-NetIPAddress` |
| Set DHCP | `Set-NetIPInterface -Dhcp Enabled` |
| Set DNS | `Set-DnsClientServerAddress` |
| Add temp route | `New-NetRoute` |
| Remove temp route | `Remove-NetRoute` |

All PowerShell calls use `RunspacePool` with a concurrency limit and 30-second timeout. Errors are captured via `ErrorRecord` collection and surfaced as exceptions.

### Profile Capture/Restore Pattern

1. **Capture**: `GetCurrentProfile()` snapshots the adapter's current IP configuration (static addresses, DHCP state, DNS servers) into a `NetworkProfile` object
2. **Save**: `NetworkProfileManager.SaveProfile()` serializes the profile to a JSON file in `%AppData%/UniversalScanner/profiles/`
3. **Modify**: The user or application changes the adapter's IP configuration (e.g., to match a camera's default subnet)
4. **Restore**: `NetworkProfileManager.ApplyProfile()` restores the original configuration

This pattern is critical for the IP change workflow when the PC's network adapter must be temporarily reconfigured to reach a device on a different subnet.

### Temporary Route for Off-Subnet Cameras (Uniview)

`UniviewNetwork` implements a PowerShell-based temporary route mechanism:

```csharp
// Simplifed pattern in UniviewNetwork
class TemporaryRoute : IDisposable {
    string _destination, _gateway;
    
    public TemporaryRoute(string destinationIP, string gatewayIP) {
        // New-NetRoute -DestinationPrefix "{destinationIP}/32" -NextHop "{gatewayIP}"
        PowerShell.Create()
            .AddCommand("New-NetRoute")
            .AddParameter("DestinationPrefix", $"{destinationIP}/32")
            .AddParameter("NextHop", gatewayIP)
            .Invoke();
    }
    
    public void Dispose() {
        // Remove-NetRoute -DestinationPrefix "{_destination}/32" -NextHop "{_gateway}" -Confirm:$false
        PowerShell.Create()
            .AddCommand("Remove-NetRoute")
            .AddParameter("DestinationPrefix", $"{_destination}/32")
            .AddParameter("NextHop", _gateway)
            .AddParameter("Confirm", false)
            .Invoke();
    }
}
```

The route is created before the IP change operation and disposed (route removed) after success or failure. Used in `using` blocks for automatic cleanup.

---

## ONVIF SOAP Architecture

### OnvifSoap.cs — Central Client

Located in `DeviceSDK/Honeywell/OnvifSoap.cs` but has zero Honeywell-specific logic. It is the shared ONVIF client used by 5+ modules across vendors.

**WS-UsernameToken Digest Authentication Flow:**

```
1. Generate nonce (random 16 bytes, base64-encoded)
2. Generate created timestamp (ISO 8601 UTC)
3. Compute digest = SHA1(nonce + created + password) → base64
4. Wrap SOAP body with WS-Security header:
   <wsse:Security>
     <wsse:UsernameToken>
       <wsse:Username>{username}</wsse:Username>
       <wsse:Password Type="...#PasswordDigest">{digest}</wsse:Password>
       <wsse:Nonce EncodingType="...#Base64Binary">{nonce}</wsse:Nonce>
       <wsu:Created>{created}</wsu:Created>
     </wsse:UsernameToken>
   </wsse:Security>
5. POST to device's ONVIF endpoint
```

**Key methods:**

| Method | Purpose |
|--------|---------|
| `SendAsync(endpoint, soapAction, body)` | Full SOAP round-trip: wrap, auth, POST, parse |
| `WrapSoap(body, auth)` | Builds complete SOAP envelope with optional WS-Security header |
| `ExtractValue(xml, xpath)` | Pulls a single text value from SOAP response |
| `ExtractAttribute(xml, xpath, attr)` | Pulls an XML attribute value |

**Transport:** `HttpClient` with configurable timeout (default 5 seconds).

### GenericOnvifNetwork — Cross-Vendor ONVIF IP Change

`DeviceSDK/Common/GenericOnvifNetwork.cs` uses `OnvifSoap` for standard ONVIF network configuration:

| SOAP Call | ONVIF Method | Purpose |
|-----------|-------------|---------|
| GetNetworkInterfaces | `trt:GetNetworkInterfaces` | Read current IP config (address, prefix length, gateway) |
| SetNetworkInterfaces | `trt:SetNetworkInterfaces` | Write new IP config (manual/DHCP, IPv4/IPv6) |
| SetNetworkDefaultGateway | `trt:SetNetworkDefaultGateway` | Update default gateway (separate from IP in ONVIF) |
| SystemReboot | `tds:SystemReboot` | Reboot device to apply network changes |

`GenericOnvifNetwork` is the fallback for Sunell, Tiandy, and any unrecognized ONVIF-compliant device. It is also the primary path for Honeywell (non-legacy models) and the fallback for Dahua and Uniview.

### Vendor-Specific Network Modules

| Module | Primary Protocol | Fallback |
|--------|-----------------|----------|
| `HikvisionNetwork` | HCNetSDK (P/Invoke): GetDVRConfig → modify → SetDVRConfig → Reboot | — |
| `HoneywellNetwork` | ONVIF SOAP via OnvifSoap | `HoneywellLegacyNetwork` (WinHTTP COM + encrypted RPC) |
| `HoneywellLegacyNetwork` | WinHTTP COM: MD5 challenge login → encrypted getConfig/setConfig → reboot | — |
| `UniviewNetwork` | LAPI REST JSON (3 body variants) | ONVIF SOAP via OnvifSoap |
| `DahuaNetwork` | ONVIF SOAP | CGI setConfig (`Network.eth0.*` params) |
| `SunellNetwork` | `GenericOnvifNetwork` | — |
| `TiandyNetwork` | `GenericOnvifNetwork` | — |

### Synchronous vs Asynchronous

| Type | Modules | Behavior |
|------|---------|----------|
| **Sync (blocking)** | HikvisionNetwork, HoneywellLegacyNetwork | Call blocks UI thread; used with `Task.Run()` wrapper |
| **Async** | HoneywellNetwork, UniviewNetwork, DahuaNetwork, GenericOnvifNetwork | Returns `Task`; awaited with progress reporting |

---

## ARP Resolution and MAC Lookup

### ARP Table Access

`NetworkTools.cs` resolves MAC addresses via the Windows ARP cache:

1. **Ping the IP** — ensures the device is in the ARP cache
2. **Read ARP table** — uses `System.Net.NetworkInformation` or parses `arp -a` output
3. **Extract MAC** — regex match for `xx-xx-xx-xx-xx-xx` or `xx:xx:xx:xx:xx:xx` format

The ARP cache is flushed for old IPs during IP change (`RefreshChangedHikvisionIpRowAsync`) to prevent stale MAC-to-IP mappings.

### VendorLookup — OUI Database

`VendorLookup.cs` maps the first 3 bytes of a MAC address (the Organizationally Unique Identifier) to a vendor name:

- **Database**: `Resources/oui.csv` — embedded resource containing MAC OUI assignments
- **Format**: `OUI,VendorName` pairs (e.g., `00:40:8C,SONY`)
- **Lookup**: `LookupVendor(mac)` → `string` vendor name or "Unknown"
- **Caching**: In-memory `Dictionary<string, string>` loaded once at startup

The OUI lookup provides the initial vendor hint in the device grid, which is later refined by the vendor detection cascade.

---

## Ping Subsystem

### Components

| Component | Type | Purpose |
|-----------|------|---------|
| `NetworkTools.PingAsync()` | Static method | Single-ping with timeout, used in subnet scanning and IP change refresh |
| `PingMonitorForm` | Form (embedded class) | Real-time multi-device ping monitor with DataGridView |
| `CustomPingDialog` | Form (embedded class) | Configuration dialog: ping count, timeout, interval |

### PingMonitorForm

- Real-time DataGridView showing per-device ping latency, packet loss, jitter
- Runs continuous ping cycles with configurable count and interval
- Color-coded rows: green (<50ms), yellow (50-200ms), red (>200ms or timed out)
- Statistics bar: min/avg/max latency, packet loss percentage
- Launched from device grid context menu for selected devices

### CustomPingDialog

- Modal configuration before starting ping monitor
- **Ping count**: Number of pings per cycle (default 4)
- **Timeout**: Per-ping timeout in ms (default 1000)
- **Interval**: Delay between ping cycles in ms (default 2000)

### Inline Ping in ScannerWindow

During subnet scanning (`ScanSubnetAsync()`), each IP is pinged before deeper probing:
- Reachable IPs proceed to `SubnetVendorProbe.DetectAsync()`
- Unreachable IPs are skipped (marked as "No response" in the grid)
- 8 concurrent pings with `SemaphoreSlim` throttling

---

## Port Allocation

### PortProvider

`PortProvider.cs` manages UDP port assignments across all 30 scan engines to prevent conflicts:

- **Reservation**: `PortProvider.reserve(ScanEngine engine)` — registers the engine's `getUsedPort()` value
- **Conflict detection**: Throws if two engines request the same port
- **Multicast group sharing**: Multiple engines can join the same multicast group (e.g., 239.255.255.250:3702 for WS-Discovery) as long as they use different unicast receive ports

### Port Usage by Protocol

| Port | Protocol(s) |
|------|-------------|
| 1900 | SSDP |
| 3702 | WS-Discovery, HoneywellOnvif |
| 5353 | mDNS, HoneywellMdns |
| 5050 | Dahua1 |
| 37810 | Dahua2 |
| 37020 | Hikvision |
| Various dynamic | All other vendor scanners |

---

## Subnet Scanning

### ScanSubnetAsync() Flow

```
User enters subnet prefix (e.g., "192.168.1") and clicks "Scan Subnet"
    → Clears device list (preserving recentChangedIps)
    → Triggers scanEvent (broadcast discovery)
    → Waits 2 seconds for broadcast responses
    → Parallel scan of 254 IPs with SemaphoreSlim(8):
        For each IP (192.168.1.1 through 192.168.1.254):
            1. Ping IP
            2. If reachable: SubnetVendorProbe.DetectAsync()
            3. Resolve MAC via ARP
            4. Look up OUI vendor name
            5. Vendor detection cascade (Honeywell → ... → Tiandy)
            6. Activation status probe
            7. Fill/update DataRow in foundDeviceList
            8. Marshal update to UI thread
```

### SubnetVendorProbe

Hybrid scanner invoked per-IP during subnet scanning (not a broadcast engine):
- Sends HTTP GET to common device endpoints (`/`, `/doc/page/login.asp`, `/ISAPI/...`, etc.)
- Parses HTTP responses for vendor signatures (server headers, page titles, XML elements)
- Integrates with vendor activation status probes
- Returns vendor identification used to populate the device grid

### Tiandy Assist Special Case

The Tiandy Assist discovery protocol requires a separate concurrency path during subnet scanning. While other IPs use the standard 8-concurrent `SemaphoreSlim`, the Tiandy Assist probe has its own throttling to prevent socket exhaustion, since it uses a different transport (raw TCP) than the HTTP-based probes.

---

## DHCP Considerations

| Vendor | DHCP Support | Notes |
|--------|-------------|-------|
| Hikvision | No | Static IP only via SDK |
| Honeywell | No | ONVIF path disables DHCP before setting static IP |
| Honeywell Legacy | No | Encrypted RPC sets static config |
| Uniview | Yes | LAPI supports DHCP mode toggle |
| Dahua | Yes | CGI supports DHCP via `Network.eth0.AddressType` |
| Sunell | No | ONVIF sets static only |
| Tiandy | No | ONVIF sets static only |
| Generic ONVIF | No | Standard ONVIF behavior: disable DHCP → set static |

When DHCP is supported (Uniview, Dahua), the `ChangeIpForm` provides a "Use DHCP" checkbox alongside the static IP fields.
