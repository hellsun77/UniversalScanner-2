# Known Issues, Limitations & Missing Features

Documented stubs, incomplete implementations, architectural gaps, and known limitations across the Universal Scanner codebase.

---

## Incomplete Implementations

### HikvisionProvider — Activation "Not Implemented Yet"

**File**: `DeviceSDK/Hikvision/HikvisionProvider.cs`
**Issue**: The `InitializeDevice()` method is marked with a comment indicating activation is "not implemented yet" through the provider interface. However, `HikvisionActivator` exists and works when called directly from `ScannerWindow`.

**Impact**: The `IDeviceProvider` abstraction path for Hikvision is incomplete. Direct routing in `DeviceInitializationForm` works around this.

**Fix**: Wire `HikvisionActivator` into `HikvisionProvider.InitializeDevice()`.

---

### ProviderManager — Only HikvisionProvider Registered

**File**: `DeviceSDK/Common/ProviderManager.cs`
**Issue**: The static provider registry contains only `HikvisionProvider`. All other vendors (Honeywell, Dahua, Uniview, Sunell, Tiandy) are handled inline in `ScannerWindow` rather than through the `IDeviceProvider` abstraction.

**Impact**: Architectural inconsistency. Batch operations on non-Hikvision devices bypass the provider pattern entirely. Makes it harder to add unit tests or swap implementations.

**Fix**: Create providers for each vendor and register them. Refactor inline vendor logic in `ScannerWindow` to delegate to `ProviderManager`.

---

### DahuaDebugLog — No-Op Stubs

**File**: `DeviceSDK/Dahua/DahuaDebugLog.cs`
**Issue**: All methods are stubs returning empty strings or doing nothing. The class exists as a placeholder for future debug logging.

```csharp
// Typical stub pattern:
public static string LogRequest(string url, string body) => "";
public static string LogResponse(string response) => "";
```

**Impact**: No debug visibility into Dahua protocol exchanges. Troubleshooting Dahua activation/network issues requires external packet capture.

**Fix**: Implement logging methods, writing to `Logger.Debug()` when debug mode is enabled.

---

### Engine Array Null Slots

**File**: `Program.cs`
**Issue**: The 30-element engine array has null slots at indices 14-15 and 20. These are placeholders for engines that were planned but never implemented, or were removed.

**Impact**: Code that iterates the engine array must null-check. If new code forgets to null-check, `NullReferenceException` at runtime.

**Fix**: Either remove the null slots and compact the array, or document what was planned for those slots.

---

### Foscam Cipher — Partial Implementation

**File**: Foscam scanner (`Foscam.cs`)
**Issue**: References a `Foscam_cipher` variant but the implementation is partial or stubbed. The exact status is unclear from the index — the Foscam scanner exists but may not handle encrypted Foscam models.

**Impact**: Some Foscam camera models may not be discovered if they use the cipher protocol variant.

**Fix**: Review `Foscam.cs` and `Foscam_cipher` references; complete the cipher protocol implementation.

---

## Architectural Gaps

### No Unit Tests

**Issue**: The project has no unit test project. All testing is manual (live hardware) or via `.selftest` PCAP files.

**Impact**: Regressions are caught manually. Refactoring is risky. The self-test infrastructure exists but is not wired into an automated test runner (NUnit/xUnit/MSTest).

**Mitigation**: `.selftest` files provide PCAP-based decoder validation, but must be run manually.

**Fix**: Add a unit test project targeting .NET Framework 4.8. Wire `.selftest` files into automated test cases. Focus on protocol decoders first (deterministic, no hardware needed).

---

### No Dependency Injection

**Issue**: All dependencies are created with `new` inside constructors and methods. There is no DI container or constructor injection.

**Impact**: Tight coupling between components (e.g., `ScannerWindow` directly instantiates activator classes). Hard to mock for testing. Hard to swap implementations.

**Fix**: Introduce a lightweight DI pattern (constructor injection with defaults, or a minimal container like `Microsoft.Extensions.DependencyInjection`).

---

### ScannerWindow God Class

**File**: `UniversalScanner.cs` — ~4,989 lines
**Issue**: The main form class contains: device grid management, scan orchestration, IP change workflow, activation workflow, batch operations, ping monitoring, PDF export, UI theming, and 7 embedded classes.

**Impact**: Hard to navigate, hard to test, hard to modify without side effects. Violates Single Responsibility Principle.

**Fix**: Extract concerns into separate classes:
- `DeviceGridManager` — DataTable operations, deduplication, row management
- `ScanOrchestrator` — engine lifecycle, scan start/stop
- `VendorOperationRouter` — activation/IP change routing (already partially in forms)
- `PdfExportService` — extract `SimplePdfTableExporter` to its own file
- `MenuBuilder` — extract menu construction from `BuildRightClickMenu()`

---

### Vendor Logic Scattered Across Codebase

**Issue**: Vendor-specific logic is not centralized. For a given vendor, the code touches:
- `ScannerWindow` (type checks, routing, batch ops)
- `DeviceSDK/{Vendor}/` (activator, network, probes)
- Discovery engine (vendor-specific scanner)
- `ChangeIpForm` (IP change routing)
- `DeviceInitializationForm` (activation routing)

**Impact**: Adding a new vendor requires changes in 6+ files. Vendor behavior is hard to audit end-to-end.

**Fix**: Full `IDeviceProvider` implementation for each vendor, with a single entry point that encapsulates all vendor-specific behavior.

---

### Cross-Vendor Shared Code in Vendor Folders

| Shared File | Located In | Used By |
|------------|-----------|---------|
| `OnvifSoap.cs` | `DeviceSDK/Honeywell/` | GenericOnvifNetwork, Uniview, Dahua, Tiandy |
| `HoneywellRsaHelper.cs` | `DeviceSDK/Honeywell/` | Sunell |
| `DahuaDebugLog.cs` | `DeviceSDK/Dahua/` | No meaningful use (no-op stub) |

**Impact**: Misleading organization. Developers looking for the ONVIF client won't find it under `Honeywell/`.

**Fix**: Move cross-vendor shared code to `DeviceSDK/Common/` or a new `DeviceSDK/Shared/` directory.

---

## Hardcoded Values

### IP Change Refresh Constants

**File**: `UniversalScanner.cs` — `RefreshChangedHikvisionIpRowInBackgroundAsync()`

| Constant | Value | Issue |
|----------|-------|-------|
| Max attempts | 25 | Not configurable; some devices boot faster/slower |
| Initial wait | 20 seconds | May be too long for fast-rebooting devices |
| Inter-attempt delay | 4 seconds | No backoff strategy; same delay every attempt |

**Fix**: Make configurable via `Config` class or a settings form.

---

### Timeout Values

Multiple timeouts are hardcoded throughout the codebase:

| Location | Timeout | Issue |
|----------|---------|-------|
| HttpClient in OnvifSoap | 5 seconds | May be too short for slow networks/VPNs |
| Subnet scan per-IP | 3 seconds | 254 × 3s = 762 seconds theoretical max (mitigated by 8-concurrency) |
| PowerShell commands | 30 seconds | Some network operations (profile restore) may take longer |
| Activation steps | 10 seconds | Some legacy devices respond slowly during activation |

**Fix**: Make timeouts configurable, or add user-facing timeout settings.

---

### RTSP Paths

RTSP paths are hardcoded per vendor in `RtspLiveViewForm`. If a vendor changes their default RTSP path, or a non-default camera model uses a different path, RTSP view fails.

**Fix**: Store RTSP paths per model (from ONVIF GetProfiles or a configurable lookup table). Add a user-editable RTSP path override.

---

## Platform Limitations

### Windows-Only

**Issue**: The application is deeply tied to Windows APIs:

| Dependency | Windows-Only Component |
|-----------|----------------------|
| HCNetSDK.dll | Native Windows DLL (P/Invoke) |
| WinHTTP COM | Windows COM component |
| PowerShell | Windows-only (no PowerShell Core fallback) |
| WinForms | Windows-only UI framework |
| Registry | Windows configuration storage |
| user32.dll | Single-instance window activation |
| ARP table access | Windows `arp -a` parsing |

**Impact**: Cannot run on Linux/macOS without significant rework. Cross-platform port would require:
- Replace HCNetSDK with pure-managed Hikvision protocol (or skip Hikvision advanced features)
- Replace WinHTTP COM with `HttpClient` (already used elsewhere)
- Replace PowerShell network management with `ip link`/`ip addr`/`route` commands on Linux
- Replace WinForms with Avalonia or MAUI
- Replace Registry with JSON config files
- Replace user32.dll with platform-specific window activation mechanisms
- Replace `arp -a` parsing with `/proc/net/arp` on Linux

See `CLAUDE.md` "Cross-Platform Notes" section for details.

---

### .NET Framework 4.8 Only

**Issue**: The project targets .NET Framework 4.8 (Windows-only), not .NET 6/7/8+ (cross-platform).

**Impact**: Cannot leverage modern C# features (default interface methods, records, init-only properties, pattern matching improvements). Stuck on C# 7.3.

**Fix**: Port to .NET 8 with `UseWindowsForms=true` for a gradual migration path. This would enable cross-platform support with Avalonia/MAUI in the future.

---

## UI Limitations

### No DPI Awareness

**Issue**: The programmatically-built UI uses fixed pixel sizes. On high-DPI displays (150%+ scaling), text may be clipped, controls may overlap.

**Impact**: Poor visual experience on modern laptops with high-DPI displays.

**Fix**: Add DPI awareness manifest. Convert fixed pixel sizes to scaled values using `Graphics.DpiX`/`DpiY` ratios.

---

### No Dark Mode

**Issue**: The UI theming uses a hardcoded navy/light-blue scheme with white backgrounds. No dark mode option exists.

**Fix**: Extract color constants. Add a dark theme palette. Add a toggle in settings.

---

### Grid Performance with Large Device Counts

**Issue**: The DataGridView with `DataTable` binding redraws all rows on any update. With thousands of discovered devices, UI updates become sluggish.

**Mitigation**: Deduplication keeps the typical device count manageable. Subnet scans cap at 254 IPs.

**Fix**: Use `SuspendLayout()`/`ResumeLayout()` around batch updates. Consider virtual mode for the DataGridView.

---

## Protocol Gaps

### IPv6: Default Disabled

**Issue**: IPv6 discovery is implemented but disabled by default (`IPv6Enabled = false` in Config).

**Impact**: IPv6-only devices are not discovered. Dual-stack devices are only found via IPv4.

**Fix**: Test IPv6 scanners thoroughly, then enable by default, or add a prominent UI toggle.

---

### Link-Local: Default Disabled

**Issue**: Link-local addresses (169.254.x.x) are excluded by default (`LinkLocalEnabled = false`).

**Impact**: Cameras with Auto-IP/APIPA addresses (common fallback when DHCP fails) are not discovered.

---

### Some Vendor Scanners — Minimal Implementation

The following vendor scanners exist but have minimal or untested implementations:

| Scanner | Status |
|---------|--------|
| Axis | Basic implementation, limited model coverage |
| Bosch | Bin + XML self-tests exist, implementation appears partial |
| Hanwha | Basic implementation |
| Vivotek | Basic implementation |
| Sony | Basic implementation |
| Panasonic | Basic implementation |
| Arecont | Basic implementation |
| Vstarcam | Basic implementation |
| Ubiquiti | Basic implementation |
| Eaton | UPS discovery, limited testing |
| CyberPower | UPS discovery, limited testing |
| Lantronix | Serial/ethernet device, limited testing |
| Microchip | Embedded device, limited testing |
| Advantech | Industrial device, limited testing |
| EdenOptima | Limited testing |
| 360Vision | Camera discovery, limited testing |
| NiceVision | Camera discovery, limited testing |
| MSSQL | SQL Server Browser, limited testing |

**Impact**: These scanners may work for some device models but fail silently for others. No comprehensive device compatibility list exists.

---

## Security Notes

### Default Credentials in Code

**Issue**: `UniviewActivator` hardcodes factory default passwords `"123456"` and `"admin"` for initial authentication attempts.

**Justification**: Required for activation (the device ships with these passwords and needs to be activated with a new one).

**Risk**: Low — these are well-known factory defaults, and the intent is to change them during activation.

---

### Password Transmission

**Issue**: Some protocols transmit passwords with encryption (RSA+AES for Honeywell/Dahua/Sunell, DES for Tiandy), while others use HTTP Basic auth (Uniview LAPI) or HTTP DIGEST auth (Tiandy).

**Risk**: HTTP Basic auth over non-TLS connections transmits base64-encoded (not encrypted) credentials.

**Mitigation**: Most camera operations occur on local networks, limiting exposure. HTTPS is used when available (port 443 paths).

---

### No Certificate Validation

**Issue**: HTTPS connections to devices with self-signed certificates may not validate certificates properly. This is common for IP cameras that use self-signed certs.

**Risk**: Acceptable for local-network device management. Would be a problem if used over the internet.

**Mitigation**: The application is designed for local network use, not internet-facing deployments.

---

## Known Crash/Stability Issues

### PowerShell Script Execution Policy

**Issue**: If the system's PowerShell execution policy is `Restricted`, `NetworkAdapterService` PowerShell calls may fail.

**Symptom**: "File cannot be loaded because running scripts is disabled" error when opening network adapter dialogs.

**Workaround**: User must set execution policy to `RemoteSigned` or `Unrestricted`.

**Fix**: Use `-ExecutionPolicy Bypass` flag on all PowerShell invocations.

---

### HCNetSDK.dll Missing or Wrong Version

**Issue**: `RuntimeDependencyManager` extracts `HCNetSDK.dll` at startup. If extraction fails (permissions, antivirus, disk full), Hikvision operations silently fail.

**Symptom**: Hikvision devices are discovered but activation/IP change/firmware operations fail with no clear error.

**Fix**: Add a startup check that validates the SDK DLL is extractable and loadable. Surface a clear error message if not.

---

### VLC Not Installed

**Issue**: RTSP live view launches VLC as an external process. If VLC is not installed, the process launch fails silently.

**Symptom**: Nothing happens when clicking "Live View" on a device.

**Fix**: Check for VLC installation paths before launching. Show a "VLC not found" dialog with a download link.

---

### Port Conflicts

**Issue**: If another application is using UDP ports 1900, 3702, 5353, or other discovery ports, the scan engines fail to bind their listeners.

**Symptom**: Some discovery protocols don't return results. Error logged but not surfaced to the user.

**Fix**: `PortProvider` detects conflicts at startup. Add a UI warning showing which ports are unavailable and which protocols are affected.

---

## Summary of Fix Priorities

| Priority | Issue | Effort | Impact |
|----------|-------|--------|--------|
| **High** | PowerShell execution policy bypass | 1 line | Crashes for users with Restricted policy |
| **High** | HCNetSDK.dll verification at startup | ~20 lines | Prevents silent Hikvision failures |
| **High** | VLC not found dialog | ~15 lines | Confusing silent failure for users |
| **Medium** | Complete `IDeviceProvider` implementations for all vendors | Large refactor | Architectural consistency, testability |
| **Medium** | Extract `ScannerWindow` embedded classes | ~200 lines per class | Code navigation, maintainability |
| **Medium** | Move cross-vendor shared code to `Common/` | File moves | Discoverability |
| **Medium** | DPI awareness | ~50 lines + manifest | Visual quality on modern displays |
| **Medium** | Make timeouts configurable | ~100 lines + settings UI | Adaptability to different networks |
| **Low** | Dark mode | ~200 lines + color palette | User preference |
| **Low** | Fix DahuaDebugLog stubs | ~50 lines | Debug visibility |
| **Low** | Add unit test project | Ongoing | Regression prevention |
| **Low** | Fill or remove engine array null slots | 1 line change | Clean code |
| **Future** | .NET 8 migration | Large multi-week effort | Cross-platform support |
