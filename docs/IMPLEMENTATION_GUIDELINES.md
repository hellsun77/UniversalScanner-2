# Implementation Guidelines

Coding standards, threading model, error handling, logging, and design patterns used throughout the Universal Scanner codebase.

---

## Thread Safety Model

### UI Marshaling

All updates to the DataGridView or any WinForms control from non-UI threads must be marshaled:

```csharp
// Preferred: fire-and-forget UI update
this.BeginInvoke(new Action(() => {
    row["Status"] = "Active";
}));

// When the result is needed synchronously:
this.Invoke(new Action(() => {
    dataGridView1.Refresh();
}));
```

**Rule**: `BeginInvoke` for fire-and-forget updates, `Invoke` only when the caller must block for the result. Never access controls directly from a worker thread.

### Device Table Locking

`foundDeviceList` (DataTable) is protected by a single `ReaderWriterLockSlim`:

```csharp
_lock.EnterReadLock();
try {
    // read from foundDeviceList
} finally {
    _lock.ExitReadLock();
}

_lock.EnterWriteLock();
try {
    // modify foundDeviceList (add/update/delete rows)
} finally {
    _lock.ExitWriteLock();
}
```

**Rule**: Use read lock for row lookups and enumeration. Use write lock for all modifications. Keep lock duration minimal — do not perform I/O or network calls inside a lock.

### Concurrent Collections

| Collection | Type | Purpose |
|-----------|------|---------|
| `detectedIPs` | `HashSet<string>` | Spatial deduplication per scan pass. Cleared at start of each scan |
| `oldIpSuppressions` | `Dictionary<string, DateTime>` | 5-minute suppression of old IPs after IP change |
| `recentChangedIps` | `HashSet<string>` | Preserved across clear operations; prevents row loss during re-scan |
| `DahuaDiscoveryState._cache` | `ConcurrentDictionary` | Thread-safe cache keyed by IP/MAC/serial |

### Cancellation Tokens

Long-running operations use `CancellationTokenSource`:

```csharp
// ScannerWindow pattern
_pingCts?.Cancel();
_pingCts = new CancellationTokenSource();
await Task.Run(() => DoPingWork(_pingCts.Token));

// Inside worker: periodic token checks
token.ThrowIfCancellationRequested();
```

**Rule**: One `CancellationTokenSource` per operation category (ping, subnet scan, IP change refresh). Cancel the previous source before starting a new operation of the same category.

### Background Threads

All engine threads are `IsBackground = true`. This means:
- Threads terminate automatically when the application exits
- No explicit `Thread.Join()` needed on shutdown
- Use the `closing` flag in `ScanEngine` for graceful in-operation termination

---

## Error Handling

### General Pattern

Every external call (network, file I/O, COM interop, P/Invoke) must be wrapped:

```csharp
try {
    // operation
} catch (HttpRequestException ex) {
    Logger.Log($"HTTP error for {ip}: {ex.Message}");
    // graceful degradation
} catch (SocketException ex) {
    Logger.Log($"Socket error: {ex.Message}");
    // graceful degradation
} catch (Exception ex) {
    Logger.Log($"Unexpected error: {ex}");
    // never crash — always degrade gracefully
}
```

### Vendor-Specific Error Handling

| Error Type | Handling |
|-----------|----------|
| Honeywell activation error codes | `-523` (weak password), `-524` (short password), `-508` (already activated) — surfaced as user-facing messages |
| Hikvision SDK errors | `NET_DVR_GetLastError()` → mapped to human-readable strings |
| ONVIF SOAP faults | `ExtractValue(xml, "//*[local-name()='Fault']/*[local-name()='Reason']/*[local-name()='Text']")` — extracted and logged |
| HTTP non-200 responses | Status code logged; response body parsed for vendor-specific error messages |
| WinHTTP COM errors | `HRESULT` checked after every COM call; logged with context |
| PowerShell errors | `ErrorRecord` collection iterated; each error surfaced as aggregate exception |

### Timeout Handling

| Operation | Default Timeout | Configurable |
|-----------|----------------|-------------|
| HTTP requests (HttpClient) | 5 seconds | Per-call override |
| ONVIF SOAP calls | 5 seconds | Via HttpClient timeout |
| Ping | 1 second | CustomPingDialog |
| Subnet scan per-IP probe | 3 seconds | Hardcoded |
| IP change refresh total | ~120 seconds (25 × 4s + 20s) | Hardcoded |
| PowerShell commands | 30 seconds | Hardcoded |
| Device activation | 10 seconds per step | Hardcoded |

### Graceful Degradation

When a vendor-specific operation fails, the system always falls back to a less-specific alternative:

```
Vendor-specific path → ONVIF standard path → Generic path → User-visible error
```

Examples:
- Uniview activation: LAPI REST → ONVIF SetUser → "Activation failed"
- Dahua IP change: ONVIF → CGI setConfig → "IP change failed"
- Honeywell IP change: ONVIF → Legacy WinHTTP RPC → "IP change failed"

---

## Logging

### Logger Class

`Logger.cs` provides multi-level logging:

| Level | Method | Use Case |
|-------|--------|----------|
| Debug | `Logger.Debug(msg)` | Detailed diagnostic info, packet dumps |
| Info | `Logger.Info(msg)` | Normal operations: "Device found at 192.168.1.100" |
| Warning | `Logger.Warn(msg)` | Recoverable issues: timeouts, fallback paths used |
| Error | `Logger.Error(msg)` | Non-recoverable errors: activation failed, SDK crash |

### Debug Mode

Enabled via registry: `HKEY_CURRENT_USER\Software\UniversalScanner\DebugMode = 1`

When active:
- All debug-level messages are written
- **PCAP capture**: Raw network packets are written to `.pcap` files in the current directory
- PCAP files use `PCapFile.cs` writer (PCAP global header + per-packet headers)
- Each scan engine gets its own PCAP file named `{engineName}_{timestamp}.pcap`

### Log Output

- **Console**: When running with console attached (debug builds)
- **File**: `%AppData%/UniversalScanner/logs/` (when configured)
- **In-memory ring buffer**: Last 1000 messages available for in-app diagnostic display

### HoneywellDebugProbe

Read-only diagnostic tool for Honeywell devices:
- Probes 12+ endpoints on the device
- Builds a comprehensive report of device capabilities and state
- Logs all probe results at Debug level
- Does not modify device state — safe for production use

---

## Design Patterns

### 1. Plugin/Strategy Pattern (ScanEngine)

```csharp
abstract class ScanEngine {
    // Plugin interface
    public abstract string name { get; }
    public abstract Color color { get; }
    public abstract int getUsedPort();
    public abstract void listen();
    public abstract void scan();
    public abstract void sender(IPEndPoint ep);
    public abstract void receiver(IPEndPoint ep, byte[] data);
    
    // Infrastructure provided by base
    protected void addSenderThread(ThreadStart ts);
    protected void addListenerThread(ThreadStart ts);
    protected UdpClient getUnicastReceiver(int port);
    protected UdpClient getMulticastReceiver(IPAddress group, int port);
    protected ScannerViewer viewer;
}
```

30 concrete scanners implement this interface. The base class provides all threading, socket management, and lifecycle control. Scanners only implement protocol-specific logic.

### 2. Observer/Callback Pattern (ScannerViewer)

```csharp
interface ScannerViewer {
    void deviceFound(ScanEngine engine, DeviceMetadata device);
    void scanStatusChanged(ScanEngine engine, string status);
}
```

`ScannerWindow` implements `ScannerViewer`. Engines call back to report discoveries. This decouples protocol scanning from UI logic — engines have no knowledge of the DataGridView, DataTable, or UI thread.

### 3. Provider Pattern (IDeviceProvider)

```csharp
interface IDeviceProvider {
    string VendorName { get; }
    bool SupportsVendor(string vendorName);
    DeviceInfo GetDeviceInfo(string ip);
    bool CanInitialize(DeviceInfo device);
    bool InitializeDevice(DeviceInfo device, string username, string newPassword, out string message);
}
```

**Current state**: Only `HikvisionProvider` is registered in `ProviderManager`. Other vendor operations are handled inline in `ScannerWindow` rather than through this abstraction. The pattern exists for future refactoring — new vendor support should use `IDeviceProvider` rather than adding inline code.

### 4. Chain of Responsibility (Vendor Detection)

```
Generic discovery (WS-Discovery, SSDP)
    → Honeywell probe → Hikvision probe → Uniview probe
    → Dahua probe → Sunell probe → Tiandy probe
    → "Generic WS-Discovery" (if all return Unknown)
```

Each probe returns `DeviceActivationStatus.Unknown` if the vendor doesn't match, allowing the next probe in the chain to try. First non-Unknown result wins.

### 5. Factory Pattern (Vendor Routing)

`ChangeIpForm` and `DeviceInitializationForm` use vendor-type-check methods to route to the correct module:

```csharp
if (IsHikvisionRow(row))
    activator = new HikvisionActivator();
else if (IsHoneywellRow(row))
    activator = HasActivationEndpointAsync(ip) ? new HoneywellActivator() : new HoneywellLegacyActivator();
else if (IsUniviewRow(row))
    activator = new UniviewActivator();
// ... etc
```

### 6. Disposable Temporary Resource Pattern

```csharp
using (var route = new TemporaryRoute(cameraIP, gatewayIP)) {
    // route created via New-NetRoute
    await DoNetworkOperation();
    // route auto-removed via Remove-NetRoute in Dispose()
}
```

Used by `UniviewNetwork` for off-subnet temporary routes. Also used for `CancellationTokenSource` linking and socket lifetime management.

### 7. Async Fire-and-Forget with Error Logging

```csharp
// For background operations that shouldn't block the UI:
Task.Run(async () => {
    try {
        await DoBackgroundWork();
    } catch (Exception ex) {
        Logger.Error($"Background work failed: {ex}");
    }
}).ConfigureAwait(false);
```

Used extensively for: MAC lookup, ONVIF profile probing, activation status checks, IP change background refresh.

---

## Configuration Management

### Config Class

`Config.cs` uses Windows Registry under `HKEY_CURRENT_USER\Software\UniversalScanner`:

| Key | Type | Default | Purpose |
|-----|------|---------|---------|
| `IPv4Enabled` | bool | true | Enable IPv4 discovery |
| `IPv6Enabled` | bool | false | Enable IPv6 discovery |
| `LinkLocalEnabled` | bool | false | Include 169.254.x.x link-local addresses |
| `GenericProtocols` | bool | true | Enable SSDP/WS-Discovery/mDNS standards |
| `DebugMode` | bool | false | Enable debug logging + PCAP capture |
| `PortSharing` | bool | false | Allow multiple engines on same port |
| `WindowWidth` | int | — | Saved window width |
| `WindowHeight` | int | — | Saved window height |
| `WindowX` | int | — | Saved window X position |
| `WindowY` | int | — | Saved window Y position |

**Pattern**: `Config.Get<T>(key, defaultValue)` and `Config.Set(key, value)` wrap `RegistryKey` read/write with type conversion.

**Note for cross-platform**: Replace with JSON config file in user directory. See ARCHITECTURE.md "Cross-Platform Notes".

### HoneywellActivationCache

Separate from Config — file-based cache at `%AppData%/UniversalScanner/honeywell_activated_devices.txt`:
- One line per activated device: `{IP}={activation_status}`
- Read on startup, written on successful activation
- Prevents re-activation attempts on already-activated devices
- `HoneywellActivationStatusProbe` checks cache before making network calls

---

## Code Organization

### File Naming Conventions

| Pattern | Example |
|---------|---------|
| Scanner engines | `{Protocol}.cs` — `SSDP.cs`, `Wsdiscovery.cs` |
| Vendor activators | `{Vendor}Activator.cs` — `DahuaActivator.cs` |
| Vendor network modules | `{Vendor}Network.cs` — `HikvisionNetwork.cs` |
| Vendor status probes | `{Vendor}ActivationStatusProbe.cs` |
| Shared utilities | Descriptive: `NetworkTools.cs`, `VendorLookup.cs` |
| Forms | `{Purpose}Form.cs` — `ChangeIpForm.cs`, `SplashForm.cs` |
| Interfaces | `I{Purpose}.cs` — `IDeviceProvider.cs` |

### Namespace Structure

```
UniversalScanner                    // Root: ScanEngine, ScannerWindow, Program, utilities
UniversalScanner.DeviceSDK         // IDeviceProvider, DeviceInfo, ProviderManager
UniversalScanner.DeviceSDK.Dahua   // Dahua-specific classes
UniversalScanner.DeviceSDK.Hikvision // Hikvision-specific classes (incl. CHCNetSDK P/Invoke)
UniversalScanner.DeviceSDK.Honeywell // Honeywell-specific + cross-vendor (OnvifSoap)
UniversalScanner.DeviceSDK.Sunell  // Sunell-specific classes
UniversalScanner.DeviceSDK.Tiandy  // Tiandy-specific classes
UniversalScanner.DeviceSDK.Uniview // Uniview-specific classes
```

### Region Usage

Files use `#region` blocks for logical grouping:
- `#region Properties`
- `#region Abstract Members`
- `#region Public Methods`
- `#region Private Methods`
- `#region Event Handlers`
- `#region IDisposable`

### Embedded Classes

`UniversalScanner.cs` contains 7 embedded classes at the bottom of the file (after `#endregion`):
1. `ModernMenuRenderer` — ToolStripProfessionalRenderer override
2. `ModernMenuColorTable` — ProfessionalColorTable override
3. `ModernAboutDialog` — About form
4. `SimplePdfTableExporter` — PDF 1.4 generation
5. `CustomPingDialog` — Ping configuration dialog
6. `PingMonitorForm` — Real-time ping monitor
7. `PdfDeviceExportRow` — Data holder POCO for PDF export

New embedded classes should follow this pattern: placed at the bottom of `UniversalScanner.cs` within the `UniversalScanner` namespace.

---

## Resource Management

### Socket Lifecycle

```csharp
// ScanEngine pattern:
class MyScanner : ScanEngine {
    public override void listen() {
        // Add managed listeners — base class stores and auto-disposes
        var unicast = getUnicastReceiver(port);
        var multicast = getMulticastReceiver(group, port);
        addListenerThread(() => ReceiveLoop(unicast));
    }
    
    public override void Dispose(bool disposing) {
        closing = true;         // Signal threads to exit
        base.Dispose(disposing); // Closes all listeners/senders, stops threads
    }
}
```

**Rule**: Never create a `UdpClient` or `Socket` directly. Always use the base class factory methods (`getUnicastReceiver`, `getMulticastReceiver`, `getInterfaceReceiver`) so the base class can track and dispose them.

### Thread Cleanup

- Set `closing = true` before calling `base.Dispose()`
- All listener loops check `closing` flag: `while (!closing) { ... }`
- `UdpClient.ReceiveAsync()` calls naturally unblock when the socket is closed by `Dispose`
- Threads are `IsBackground = true` — no need to `Join()` on shutdown

### HttpClient

- `OnvifSoap` creates a single `HttpClient` per instance (not per request)
- Timeout configured at construction
- Disposed when the owning module is disposed

### PowerShell RunspacePool

- Single `RunspacePool` shared across all PowerShell calls in `NetworkAdapterService`
- Configured with min/max runspaces matching concurrency needs
- Disposed when the application exits

---

## Adding a New Vendor

1. **Create DeviceSDK folder**: `DeviceSDK/{VendorName}/`
2. **Implement activator**: `{Vendor}Activator.cs` — handle activation protocol
3. **Implement status probe**: `{Vendor}ActivationStatusProbe.cs` — detect active/inactive state
4. **Implement network module** (if needed): `{Vendor}Network.cs` — IP change
5. **Add type-check method** in `UniversalScanner.cs`: `Is{Vendor}Row()`
6. **Register in vendor detection cascade**: Add probe to cascade in `ScanSubnetAsync()` and `RefreshChangedHikvisionIpRowInBackgroundAsync()`
7. **Register in vendor routing**: Add cases to `ChangeIpForm` and `DeviceInitializationForm`
8. **Add to VENDOR_MATRIX.md**: Document activation, probing, IP change, RTSP path
9. **Optionally implement `IDeviceProvider`**: Register in `ProviderManager`
10. **Add discovery scanner** if the vendor has a proprietary discovery protocol

---

## Self-Test Guidelines

Each protocol scanner should have a `.selftest` file:

- **Location**: `UniversalScanner/selftest2pcap/{ScannerName}.selftest`
- **Format**: Text file with PCAP packet definitions
- **Purpose**: Automated testing of protocol decoders without live hardware
- **Pattern**: Define expected packets → parse → verify `DeviceMetadata` fields

Refer to existing `.selftest` files (24 exist) for the format specification.
