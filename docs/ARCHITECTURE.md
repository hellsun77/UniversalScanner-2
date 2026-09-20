# Architecture

## High-Level Component Diagram

```
┌─────────────────────────────────────────────────────────┐
│                     Program.cs                          │
│  Single-instance → Splash → ScannerWindow → 28 Engines │
└──────────────────────┬──────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────┐
│                  ScannerWindow (Form)                    │
│  ScannerViewer impl  │  Device grid  │  Batch ops       │
│  IP change workflow  │  Activation   │  Ping monitor    │
│  PDF export          │  UI theming   │  Context menus   │
└──────┬───────────────┬───────────────┬──────────────────┘
       │               │               │
       ▼               ▼               ▼
┌──────────┐  ┌──────────────┐  ┌──────────────────┐
│ 30 Scan  │  │  DeviceSDK   │  │  Network Adapter │
│ Engines  │  │  6 vendors   │  │  Management      │
└──────────┘  └──────────────┘  └──────────────────┘
```

## Startup Sequence

1. **Single-instance check** — Named Mutex prevents duplicate processes. If found, activates existing window via `ShowWindowAsync`/`SetForegroundWindow`.
2. **SplashForm** — Borderless custom-painted form with animated gradient progress bar. Calls `RuntimeDependencyManager` to extract embedded SDK DLLs.
3. **ScannerWindow construction** — `BuildInterface()` → `BuildDataTable()` → `BindDataTableToGrid()` → `ApplyMainDesign()` → `BuildRightClickMenu()` → `BuildStatusBar()`.
4. **Engine registration** — 28 `ScanEngine` objects instantiated. Each calls `viewer.registerViewer(this)` to wire its `ScanEvent` delegate. Engine array has nulls at slots 14-15 and 20.
5. **Port allocation** — `PortProvider` reserves UDP ports across engines.
6. **Listener startup** — Each engine's `listen()` method begins. All threads are `IsBackground = true`.
7. **Auto-scan** — `ScannerWindow_Shown` triggers a broadcast scan after 1 second.

## Core Patterns

### 1. Scanner Plugin Pattern

Every protocol scanner inherits from `ScanEngine` (abstract). The base provides:
- Socket infrastructure (unicast, multicast, per-interface listeners)
- Thread management (`addSenderThread`, `addListenerThread`, `startThread`, `stopThread`)
- Device reporting via `viewer.deviceFound(ScanEngine, DeviceMetadata)`
- Resource cleanup via `Dispose()` with `closing` flags

Scanners implement 6 abstract members:
- `name` (property) — display name e.g. "SSDP", "WS-Discovery"
- `color` (property) — row color in the grid
- `getUsedPort()` — UDP port for unicast listener
- `listen()` — register multicast/interface listeners
- `scan()` — trigger discovery (broadcast or subnet)
- `sender(IPEndPoint)` / `receiver(IPEndPoint, byte[])` — packet handlers

### 2. Viewer Callback Pattern

`ScannerViewer` interface has two methods:
- `deviceFound(ScanEngine engine, DeviceMetadata device)` — new device discovered
- `scanStatusChanged(ScanEngine engine, string status)` — engine state change

`ScannerWindow` implements `ScannerViewer`. The `deviceFound()` callback:
1. Marshals to UI thread via `BeginInvoke`
2. Deduplicates by UniqueId → MAC → IP
3. Creates or updates DataRow in `foundDeviceList`
4. Queues async background work: MAC lookup, ONVIF profile probe, activation status

### 3. Device Provider Pattern

`IDeviceProvider` defines vendor-specific operations:
```
VendorName, SupportsVendor(string), GetDeviceInfo(string ip),
CanInitialize(DeviceInfo), InitializeDevice(DeviceInfo, username, newPassword, out message)
```

`ProviderManager` maintains the registered list. Currently only `HikvisionProvider` is registered. Other vendors are handled inline in `ScannerWindow` rather than through the provider abstraction.

### 4. Vendor Detection Cascade

When a device is discovered via a generic protocol (WS-Discovery, SSDP), the vendor is resolved by probing in priority order:

```
Honeywell → Hikvision → Uniview → Dahua → Sunell → Tiandy
```

Each vendor's activation status probe is called. If it returns "Active" or "Inactive" (not "Unknown"), that vendor is assigned. This runs in `ScanSubnetAsync()` and `RefreshChangedHikvisionIpRowInBackgroundAsync()`.

## Data Flow

### Device Discovery Path
```
ScanEngine.scan() → multicast/broadcast
    → ScanEngine.receiver(IPEndPoint, byte[])
    → viewer.deviceFound(engine, metadata)     [worker thread]
    → ScannerWindow.deviceFound()               [BeginInvoke → UI thread]
    → FindOrCreateDeviceRow()                   [ReaderWriterLockSlim]
    → QueueMacLookup()                          [async, background]
    → QueueOnvifProfileLookup()                 [async, background]
    → SubnetVendorProbe.DetectAsync()           [on subnet scan]
```

### IP Change Path
```
ChangeIpForm → vendor-specific module
    → RefreshChangedHikvisionIpRowAsync()
    → ARP cache flush
    → RemoveDuplicateRowsForIpChange()
    → RefreshChangedHikvisionIpRowInBackgroundAsync()
        → 25 attempts, 4s interval, 20s initial wait
        → Vendor re-identification via cascade
        → Activation status re-probe
        → MAC/OUI re-lookup
```

### Activation Path
```
DeviceInitializationForm
    → Honeywell: HasActivationEndpointAsync() → HC30/35 vs Legacy
    → Uniview: LAPI REST → ONVIF SetUser fallback
    → Dahua: WebDevInit → RPC → CGI addUser
    → Hikvision: SDK port 8000
    → Sunell: XML WEB_JsonAjax command 11011
    → Tiandy: DIGEST auth → DES-encrypted PUT
    → Fallback: HoneywellLegacyActivator (Dahua-style DevInit)
```

## Thread Safety

- **Device table**: `ReaderWriterLockSlim` protecting `foundDeviceList`
- **UI updates**: All marshaled via `Invoke`/`BeginInvoke` pattern
- **`detectedIPs`**: `HashSet<string>` for spatial dedup per scan pass
- **`oldIpSuppressions`**: `Dictionary<string, DateTime>` — 5-minute suppression after IP change
- **`recentChangedIps`**: `HashSet<string>` — preserved during clear operations
- **Ping cancellation**: `CancellationTokenSource` per ping operation

## UI Architecture

The UI is fully programmatic (not just designer-generated):

- **Header panel** (64px navy): icon, title, subtitle, clock (1-sec timer), menu strip
- **Toolbar panel** (72px): scan buttons, subnet textbox, progress bar, about
- **DataGridView**: light blue headers, alternating rows, single horizontal borders
- **Status strip**: single Spring-aligned label
- **Context menu**: Built entirely in `BuildRightClickMenu()` — 30+ items with submenus

**Embedded classes** in `UniversalScanner.cs`:
- `ModernMenuRenderer` — navy menu background, light blue dropdowns, 4px navy accent on hover
- `ModernMenuColorTable` — custom ProfessionalColorTable
- `ModernAboutDialog` — navy header, icon, LGPL 3.0 text
- `SimplePdfTableExporter` — hand-rolled PDF 1.4 generation (~490 lines)
- `CustomPingDialog` — count/timeout/interval modal
- `PingMonitorForm` — real-time DataGridView ping monitor with statistics
- `PdfDeviceExportRow` — data holder POCO

## ONVIF SOAP Architecture

`OnvifSoap.cs` (in Honeywell/) is the central pure-managed ONVIF client:
- WS-UsernameToken digest authentication (SHA-1 nonce + created + password)
- Uses `HttpClient` for transport
- SOAP envelope wrapping via `WrapSoap()`
- XML extraction: `ExtractValue()`, `ExtractAttribute()`
- Used by: GenericOnvifNetwork, HoneywellNetwork, TiandyInfoProbe, UniviewActivator, UniviewNetwork

This is a cross-vendor shared dependency that lives under Honeywell/ because that's where it originated, but it has no Honeywell-specific logic.

## External Dependencies

| Dependency | Type | Used For |
|------------|------|----------|
| HCNetSDK.dll | Native P/Invoke | Hikvision activation, IP change, firmware |
| PowerShell 5.0+ | Process invocation | Network adapter management (Get-NetAdapter, Set-NetIPInterface, New-NetRoute) |
| user32.dll | P/Invoke | Single-instance window activation |
| WinHTTP COM | COM interop | Legacy Honeywell IP change (HoneywellLegacyNetwork) |
| VLC Media Player | External process | RTSP live view (shell execute) |
