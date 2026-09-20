# Universal Scanner — Project File Index

Generated: 2026-07-06 | Target: .NET Framework 4.8, C# 7.3, WinForms
~94 .cs files | ~50,000+ lines

---

## Application Core

| File | Lines | Purpose |
|------|-------|---------|
| `Program.cs` | 159 | Entry point. Single-instance, splash, engine wiring (28 engines) |
| `UniversalScanner.cs` | 4989 | Main form. Device grid, scan orchestration, IP change, activation, batch ops, ping, PDF export, UI theming. 7 embedded classes |
| `UniversalScanner.Designer.cs` | 382 | Designer-generated InitializeComponent |

## Scanning Engine Framework

| File | Purpose |
|------|---------|
| `ScanEngine.cs` | Abstract base. Threading, socket listeners, device reporting. ~30 subclasses |
| `ScannerViewer` (interface) | Callback contract: `deviceFound()`, `scanStatusChanged()` |

## Protocol Scanners (30 files)

| File | Protocol | Transport |
|------|----------|-----------|
| `SSDP.cs` | UPnP/SSDP | UDP 1900, mcast 239.255.255.250 |
| `Wsdiscovery.cs` | ONVIF WS-Discovery | UDP 3702, mcast 239.255.255.250 |
| `mDNS.cs` | Zeroconf/DNS-SD | UDP 5353, mcast 224.0.0.251 |
| `Dahua1.cs` | Dahua legacy | UDP 5050 |
| `Dahua2.cs` | Dahua JSON | UDP 37810, mcast 239.255.255.251 |
| `Hikvision.cs` | Hikvision proprietary | UDP 37020, mcast 239.255.255.250 |
| `HoneywellMdns.cs` | Honeywell mDNS | mDNS |
| `HoneywellOnvif.cs` | Honeywell ONVIF | WS-Discovery |
| `GigEVision.cs` | GigE Vision | GVCP |
| `GoogleCast.cs` | Google Cast | mDNS |
| `SubnetVendorProbe.cs` | Subnet-wide probing | HTTP/ONVIF per-IP |
| `Tiandy.cs` | Tiandy Assist | Proprietary discovery |
| 18 more | Axis, Bosch, Hanwha, Vivotek, Sony, Ubiquiti, Panasonic, Arecont, Foscam, Vstarcam, Eaton, Lantronix, Microchip, Advantech, EdenOptima, CyberPower, 360Vision, NiceVision, MSSQL | Various |

## Utilities

| File | Purpose |
|------|---------|
| `Config.cs` | Registry-based config (`HKCU\Software\UniversalScanner`) |
| `Logger.cs` | Multi-level logging + PCAP capture in debug mode |
| `NetworkTools.cs` | Ping, port scan, ARP, subnet check, MAC resolution |
| `NetworkUtils.cs` | Endian conversion, IP parsing, subnet calculations |
| `VendorLookup.cs` | MAC OUI → vendor name (embedded `oui.csv`) |
| `PortProvider.cs` | UDP port allocation for concurrent engines |
| `PCapFile.cs` | PCAP file writer |
| `RegistryUtils.cs` | Registry helpers |
| `MemoryUtils.cs` | GDI/memory cleanup |
| `VersionManager.cs` | Version comparison, update checking |
| `RuntimeDependencyManager.cs` | Embedded SDK DLL extraction at runtime |

## Network Adapter Management

| File | Purpose |
|------|---------|
| `NetworkAdapterService.cs` | PowerShell-based adapter enumeration, DHCP/static IP, profile capture/restore |
| `NetworkAdapterInfo.cs` | POCO: adapter metadata |
| `NetworkProfile.cs` | Container: saved network config |
| `NetworkProfileAddress.cs` | POCO: individual IP entries |
| `NetworkProfileManager.cs` | Profile save/load logic |

## Forms / UI (10 files, ~7695 lines total)

| File | Lines | Purpose |
|------|-------|---------|
| `Program.cs` | 159 | Entry point |
| `UniversalScanner.cs` | 5097 | Main form + 7 embedded classes |
| `UniversalScanner.Designer.cs` | 382 | Designer code |
| `SplashForm.cs` | 240 | Animated splash screen |
| `BatchActivationForm.cs` | 111 | Batch password dialog |
| `BatchChangeIpForm.cs` | 181 | Batch IP change dialog |
| `ChangeIpForm.cs` | 525 | Single-device IP change (vendor routing) |
| `DeviceInitializationForm.cs` | 365 | Device activation (vendor routing) |
| `NetworkAdapterProfileForm.cs` | 468 | Adapter profile manager |
| `RtspLiveViewForm.cs` | 275 | VLC RTSP launcher |

## Device SDK (33 files across 6 vendors)

### Common (7 files)
| File | Purpose |
|------|---------|
| `IDeviceProvider.cs` | Interface: VendorName, SupportsVendor, GetDeviceInfo, CanInitialize, InitializeDevice |
| `DeviceInfo.cs` | DTO: Vendor, IP, MAC, Model, SerialNumber, FirmwareVersion, HttpPort, SdkPort, ActivationStatus |
| `DeviceActivationStatus.cs` | Enum: Unknown, Active, Inactive, NotSupported |
| `ProviderManager.cs` | Static registry of IDeviceProvider instances. Currently: HikvisionProvider only |
| `OnvifProfileProbe.cs` | ONVIF SOAP profile discovery (S, T, G, M, C, Q, A) |
| `DeviceFirmwareProbe.cs` | Extracts firmware version & release date from device. ONVIF GetDeviceInformation + vendor-specific endpoints (ISAPI, CGI, LAPI, WEB_JsonAjax). No internet. |
| `GenericOnvifNetwork.cs` | Cross-vendor ONVIF IP change via SOAP (SetNetworkInterfaces, SetNetworkDefaultGateway, SystemReboot) |

### Hikvision (7 files)
| File | Purpose |
|------|---------|
| `CHCNetSDK.cs` | ~903 KB P/Invoke wrapper for HCNetSDK.dll (structs, constants, externs) |
| `HikvisionSdk.cs` | SDK lifecycle: Initialize, Cleanup, GetLastError, SetDllDirectory |
| `HikvisionNative.cs` | P/Invoke for NET_DVR_ActivateDevice |
| `HikvisionProvider.cs` | IDeviceProvider impl. Activation marked "not implemented yet" |
| `HikvisionActivator.cs` | Activation via SDK on port 8000 |
| `HikvisionActivationStatusProbe.cs` | HTTP probe of 8 URLs for activation/login keywords |
| `HikvisionNetwork.cs` | IP change via SDK (NET_DVR_GetDVRConfig/NET_DVR_SetDVRConfig) |

### Honeywell (9 files)
| File | Purpose |
|------|---------|
| `OnvifSoap.cs` | Central pure-managed ONVIF client. WS-UsernameToken digest auth. Used cross-vendor by Dahua, Uniview, Tiandy, GenericOnvifNetwork |
| `HoneywellRsaHelper.cs` | RSA+AES crypto. Used cross-vendor by Sunell |
| `HoneywellActivator.cs` | HC30/HC35 activation via WEB_JsonAjaxAuth JSON RPC |
| `HoneywellLegacyActivator.cs` | Legacy (HBW4PER1) activation. WinHTTP COM, raw TLS, multiple protocol chains |
| `HoneywellNetwork.cs` | IP change via ONVIF SOAP. Falls back to HoneywellLegacyNetwork |
| `HoneywellLegacyNetwork.cs` | Legacy IP change via WinHTTP COM + encrypted RPC |
| `HoneywellActivationStatusProbe.cs` | Status probe: cache → getState JSON RPC → HTTP GET |
| `HoneywellActivationCache.cs` | File-based cache (`%AppData%/UniversalScanner/honeywell_activated_devices.txt`) |
| `HoneywellDebugProbe.cs` | Read-only diagnostic: probes 12+ endpoints, builds report |

### Dahua (5 files)
| File | Purpose |
|------|---------|
| `DahuaActivator.cs` | Activation chain: WebDevInit → RPC init → CGI addUser. RSA+AES encryption. Country auto-detect |
| `DahuaActivationStatusProbe.cs` | HTTP probe: root page → CGI magicBox |
| `DahuaNetwork.cs` | IP change: ONVIF first → CGI fallback (setConfig) |
| `DahuaDiscoveryState.cs` | Discovery JSON parser. Thread-safe cache by IP/MAC/serial |
| `DahuaDebugLog.cs` | Debug logging stubs (no-op, returns "") |

### Uniview (3 files)
| File | Purpose |
|------|---------|
| `UniviewActivator.cs` | Activation: LAPI REST → ONVIF SetUser fallback. Factory passwords ["123456","admin"] |
| `UniviewActivationStatusProbe.cs` | LAPI probe with TTL cache (Active:30min, Inactive:2min) |
| `UniviewNetwork.cs` | IP change: LAPI REST → ONVIF SOAP. PowerShell temporary route for off-subnet cameras |

### Sunell (2 files)
| File | Purpose |
|------|---------|
| `SunellActivator.cs` | Activation via XML WEB_JsonAjax command 11011. RSA+AES via HoneywellRsaHelper |
| `SunellActivationStatusProbe.cs` | Status probe via command 30541. Model lookup. Embedded SunellWebApi class |

### Tiandy (1 file)
| File | Purpose |
|------|---------|
| `TiandyActivationStatusProbe.cs` | 4 classes in 1 file: status probe, model probe (DIGEST auth + ONVIF fallback), activator (DES encryption), WebApi (raw TCP, DIGEST, pure DES implementation). ~65+ methods |

---

## Cross-Vendor Dependencies

| Source | Used By |
|--------|---------|
| `OnvifSoap` (Honeywell/) | GenericOnvifNetwork, HoneywellNetwork, TiandyInfoProbe, UniviewActivator, UniviewNetwork |
| `HoneywellRsaHelper` (Honeywell/) | HoneywellActivator, SunellActivator |
| `DahuaDebugLog` (Dahua/) | DahuaActivator, DahuaActivationStatusProbe, DahuaDiscoveryState |
| `HoneywellLegacyNetwork` (Honeywell/) | HoneywellNetwork (fallback path) |

---

## Embedded Resources

- `Resources/oui.csv` — MAC OUI vendor database
- `SDK/**/*.dll`, `SDK/**/*.lib` — Vendor SDKs (HCNetSDK.dll etc.), extracted at runtime
- `*.selftest` (24 files) — Self-test PCAPs per protocol scanner
- `icon.ico`, `UniversalScanner.ico` — App icons
- `UniversalScanner.iss` — Inno Setup installer

## Task Files

- `tasks/` — Active task files (one per investigation/feature/bug)
- `tasks/TASK-TEMPLATE.md` — Template for new task files
- `tasks/archive/` — Completed and reviewed task files
- Task files use naming pattern: `<CATEGORY>-<NNNN>-<short-description>.md`
- Categories: BUG, FEAT, DISC, VEND, NET, UI, DOC, REF, PERF
- Status progression: Planned → In Progress → Implemented → Reviewed → Completed

## Evidence Repository

- `evidence/screenshots/` — UI errors, dialogs, exceptions
- `evidence/logs/` — Runtime and application errors
- `evidence/pcaps/` — Network, discovery, ONVIF, activation, IP change, protocol captures
- `evidence/configs/` — Configuration exports and issues
- `evidence/exports/` — Imported/exported project data
- All evidence files are read-only. Referenced in BUG task files. Never modified or deleted.

## NuGet Dependencies

- **Costura.Fody 6.2.0** — Embeds DLLs into single executable
- **Fody 6.9.3** — IL weaving
- **System.Web.Extensions** — JavaScriptSerializer
- **.NET Framework 4.8** — Runtime
- **PowerShell 5.0+** — NetworkAdapterService

---

## Companion Documents

- [ARCHITECTURE.md](ARCHITECTURE.md) — Component architecture, patterns, data flow
- [DISCOVERY_ENGINE.md](DISCOVERY_ENGINE.md) — Scan engine framework, all 30 protocol scanners
- [VENDOR_MATRIX.md](VENDOR_MATRIX.md) — Full vendor routing: activation, IP change, RTSP, detection
- [NETWORK_ARCHITECTURE.md](NETWORK_ARCHITECTURE.md) — IP change workflow, network adapter management, ONVIF SOAP
- [IMPLEMENTATION_GUIDELINES.md](IMPLEMENTATION_GUIDELINES.md) — Coding standards, threading, error handling, patterns
- [KNOWN_ISSUES.md](KNOWN_ISSUES.md) — Limitations, stubs, missing features
- [FEATURE_HISTORY.md](FEATURE_HISTORY.md) — Completed major features with verification and regression notes
- [GIT_WORKFLOW.md](GIT_WORKFLOW.md) — Standard Git workflow: branching, saving, reviewing, committing, recovery
- [BUILD_GUIDE.md](BUILD_GUIDE.md) — Release x86 build workflow, Costura.Fody packaging, native SDK extraction, Inno Setup
