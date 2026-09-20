# Discovery Engine

## ScanEngine Base Class

`ScanEngine.cs` is the abstract base for all 30 protocol scanners. Every scanner inherits from it.

### Abstract Members (must implement)

| Member | Type | Purpose |
|--------|------|---------|
| `name` | `string` (property) | Display name shown in Protocol column |
| `color` | `Color` (property) | Row highlight color in device grid |
| `getUsedPort()` | `int` | UDP port for unicast listener |
| `listen()` | `void` | Register multicast/interface-specific listeners |
| `scan()` | `void` | Trigger discovery broadcast or subnet scan |
| `sender(IPEndPoint)` | `void` | Called when sending to a remote endpoint |
| `receiver(IPEndPoint, byte[])` | `void` | Called when data arrives on any listener |

### Infrastructure Provided by Base

- **Thread management**: `addSenderThread(ThreadStart)`, `addListenerThread(ThreadStart)`, `startThread(ThreadStart, string name, int timeoutMs)`, `stopThread()`
- **Socket factory**: `getUnicastReceiver(int port)`, `getMulticastReceiver(IPAddress group, int port)`, `getInterfaceReceiver(NetworkInterface nic, IPAddress group, int port)`
- **Listener storage**: `List<UdpClient> listeners` — auto-closed on Dispose
- **Sender storage**: `List<UdpClient> senders` — auto-closed on Dispose
- **Viewer reference**: `ScannerViewer viewer` — set by `registerViewer()`, used to report discoveries
- **Lifecycle flags**: `bool closing`, `bool listening` — checked by threads for graceful shutdown
- **Dispose pattern**: Proper cleanup of all sockets, threads, and managed resources

### ScannerViewer Interface

```csharp
interface ScannerViewer {
    void deviceFound(ScanEngine engine, DeviceMetadata device);
    void scanStatusChanged(ScanEngine engine, string status);
}
```

`DeviceMetadata` carries: IP address, port, protocol, version, vendor hints, serial/model strings, UUID, and raw discovery data.

### Typical Scanner Implementation Pattern

```
1. Constructor: set name, color, port
2. listen(): add multicast listener + interface listeners via base methods
3. scan(): create sender thread → broadcast probe packet
4. receiver(): parse response → build DeviceMetadata → call viewer.deviceFound()
5. Dispose(bool): set closing=true, clean up threads, call base.Dispose()
```

---

## Protocol Scanner Catalog (30 scanners)

### Standards-Based Discovery

| # | Scanner | Protocol | Port | Multicast Group | Transport |
|---|---------|----------|------|-----------------|-----------|
| 1 | **SSDP** | UPnP/SSDP | 1900 | 239.255.255.250:1900 | M-SEARCH over UDP |
| 2 | **Wsdiscovery** | ONVIF WS-Discovery | 3702 | 239.255.255.250:3702 | SOAP Probe over UDP |
| 3 | **mDNS** | Zeroconf/DNS-SD | 5353 | 224.0.0.251:5353 | DNS-SD queries |

### Vendor-Specific Discovery

| # | Scanner | Vendor | Port | Multicast Group | Protocol Type |
|---|---------|--------|------|-----------------|---------------|
| 4 | **Dahua1** | Dahua | 5050 | — | Legacy UDP binary |
| 5 | **Dahua2** | Dahua | 37810 | 239.255.255.251:37810 | JSON over UDP |
| 6 | **Hikvision** | Hikvision | 37020 | 239.255.255.250:37020 | Proprietary UDP |
| 7 | **HoneywellMdns** | Honeywell | 5353 | 224.0.0.251:5353 | mDNS with Honeywell-specific service types |
| 8 | **HoneywellOnvif** | Honeywell | 3702 | 239.255.255.250:3702 | WS-Discovery with Honeywell scopes |
| 9 | **Tiandy** | Tiandy | varies | — | Tiandy Assist proprietary protocol |
| 10 | **Axis** | Axis | varies | — | Vendor protocol |
| 11 | **Bosch** | Bosch | varies | — | Vendor protocol (bin + XML self-tests) |
| 12 | **Hanwha** | Hanwha Techwin | varies | — | Vendor protocol |
| 13 | **Vivotek** | Vivotek | varies | — | Vendor protocol |
| 14 | **Sony** | Sony | varies | — | Vendor protocol |
| 15 | **Panasonic** | Panasonic | varies | — | Vendor protocol |
| 16 | **Arecont** | Arecont Vision | varies | — | Vendor protocol |
| 17 | **Foscam** | Foscam | varies | — | Vendor protocol (also Foscam_cipher) |
| 18 | **Vstarcam** | Vstarcam | varies | — | Vendor protocol |
| 19 | **Ubiquiti** | Ubiquiti | varies | — | Vendor protocol |
| 20 | **GigEVision** | GigE Vision | varies | — | GVCP industrial camera protocol |

### Non-Camera Device Discovery

| # | Scanner | Device Type | Protocol |
|---|---------|-------------|----------|
| 21 | **Eaton** | Eaton UPS | Proprietary |
| 22 | **CyberPower** | CyberPower UPS | Proprietary |
| 23 | **Lantronix** | Lantronix serial/ethernet | Proprietary |
| 24 | **Microchip** | Microchip embedded | Proprietary |
| 25 | **Advantech** | Advantech industrial | Proprietary |
| 26 | **EdenOptima** | Eden Optima | Proprietary |
| 27 | **GoogleCast** | Google Cast / Chromecast | mDNS |
| 28 | **MSSQL** | Microsoft SQL Server | SQL Browser |
| 29 | **360Vision** | 360Vision cameras | Proprietary |
| 30 | **NiceVision** | NiceVision cameras | Proprietary |

### SubnetVendorProbe (Hybrid)

`SubnetVendorProbe.cs` is not a broadcast scanner. It's invoked during subnet scanning (`ScanSubnetAsync()`) to probe individual IPs:
- Sends HTTP GET to common device endpoints
- Parses responses for vendor signatures
- Runs activation status probes
- Returns vendor identification to populate the device grid

---

## Engine Lifecycle in Program.cs

28 engines are instantiated in `Program.cs` at indices 0-30 with nulls at slots 14-15 and 20:

```
engines[0] = new SSDP(this)
engines[1] = new Wsdiscovery(this)
engines[2] = new mDNS(this)
...
engines[30] = new NiceVision(this)
```

Each engine is:
1. Instantiated with `this` (the ScannerWindow as ScannerViewer)
2. Registered via `viewer.registerViewer(ScanEngine)` — wires the `ScanEvent` delegate
3. Port-reserved via `PortProvider.reserve(engine)`
4. Started via `engine.listen()` — begins listening on sockets
5. Scanned via `engine.scan()` — sends discovery broadcast (called by scanButton_Click)

All engine threads are `IsBackground = true`. When the application exits, `closing` flags are set and threads terminate naturally.

---

## Discovery Flow Per Protocol

### SSDP (UPnP)
1. `scan()`: Sends M-SEARCH * HTTP/1.1 to 239.255.255.250:1900
2. `receiver()`: Parses HTTP response, extracts LOCATION header
3. If needed, fetches XML description from LOCATION URL
4. Extracts device type, manufacturer, model, serial, UUID
5. Reports via `viewer.deviceFound()`

### WS-Discovery (ONVIF)
1. `scan()`: Sends SOAP Probe to 239.255.255.250:3702
2. `receiver()`: Parses SOAP ProbeMatch response
3. Extracts Types (dn:NetworkVideoTransmitter), Scopes (onvif://www.onvif.org/...), XAddrs
4. Extracts device UUID from EndpointReference/Address
5. Reports via `viewer.deviceFound()`

### mDNS (Zeroconf)
1. `scan()`: Sends DNS-SD PTR queries for common service types (_http._tcp, _onvif._tcp, etc.) to 224.0.0.251:5353
2. `receiver()`: Parses DNS response, extracts SRV/TXT/A records
3. Matches known service types to vendor identities
4. Reports via `viewer.deviceFound()`

### Dahua1 (Legacy)
1. `scan()`: Sends binary query to UDP 5050
2. `receiver()`: Parses binary response structure
3. Extracts IP, MAC, serial, model, activation state

### Dahua2 (JSON)
1. `scan()`: Sends JSON query to 239.255.255.251:37810
2. `receiver()`: Deserializes JSON response
3. `DahuaDiscoveryState.Record()` caches activation status and model
4. Reports via `viewer.deviceFound()`

### Hikvision
1. `scan()`: Sends proprietary UDP packet to 239.255.255.250:37020
2. `receiver()`: Parses structured binary response
3. Extracts device info, IP, port, serial
4. Reports via `viewer.deviceFound()`

### HoneywellMdns
1. `scan()`: Sends mDNS queries for Honeywell-specific service types
2. `receiver()`: Parses TXT records for model and capabilities
3. Distinguishes HC30/HC35 (newer) from legacy (HBW4PER1) models
4. Reports via `viewer.deviceFound()`

---

## Subnet Scanning Integration

When the user enters a subnet (e.g., "192.168.1") and clicks "Scan Subnet":

1. `scanSubnetButton_Click`: Clears device list (preserving recent IP changes), triggers `scanEvent`, waits 2 seconds
2. `ScanSubnetAsync()`: For each of 254 IPs in the subnet:
   - Pings the IP
   - If reachable: runs `SubnetVendorProbe.DetectAsync()` for vendor identification
   - Gets MAC via ARP, resolves OUI vendor
   - Runs vendor detection cascade: Honeywell → Hikvision → Uniview → Dahua → Sunell → Tiandy
   - Probes activation status per identified vendor
   - Fills or updates the device DataRow
   - 8 concurrent IPs with `SemaphoreSlim` for Tiandy Assist path

## Self-Test Infrastructure

Each scanner has a corresponding `.selftest` file containing PCAP packet captures. These are used for automated testing to verify that decoders correctly parse device responses without requiring live hardware.
