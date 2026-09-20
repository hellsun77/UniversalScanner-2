# Roadmap

Planned features, improvements, and priorities for the Universal Scanner project.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| 🔴 High | Addresses crashes, data loss, or critical user-facing failures |
| 🟡 Medium | Architectural improvement, code quality, or non-critical feature |
| 🟢 Low | Nice-to-have, cosmetic, or long-term |
| ⬜ Future | Requires major architectural change or platform migration |

---

## Immediate (High Priority)

### 🔴 PowerShell Execution Policy Bypass
**Effort**: ~1 line | **Status**: Not Started
Add `-ExecutionPolicy Bypass` to all PowerShell invocations in `NetworkAdapterService.cs`
**Why**: Prevents crashes for users with Restricted execution policy when using network adapter features.

### 🔴 HCNetSDK.dll Startup Verification
**Effort**: ~20 lines | **Status**: Not Started
Add a startup check in `RuntimeDependencyManager` that validates the SDK DLL is extractable and loadable. Surface clear error message if extraction fails.
**Why**: Silent Hikvision failure is confusing for users. No indication of root cause.

### 🔴 VLC Not Installed Detection
**Effort**: ~15 lines | **Status**: Not Started
Check for VLC installation paths before launching RTSP live view. Show a "VLC not found" dialog with download link.
**Why**: Silent failure on "Live View" click is confusing.

---

## Short-Term (Medium Priority)

### 🟡 Complete IDeviceProvider Implementations
**Effort**: Large refactor | **Status**: Not Started
Create provider classes for Honeywell, Dahua, Uniview, Sunell, and Tiandy. Register all providers in `ProviderManager`. Refactor inline vendor logic in `ScannerWindow` to delegate through provider pattern.
**Why**: Architectural consistency. Enables batch operations through a single abstraction. Makes unit testing feasible.

### 🟡 Extract ScannerWindow Embedded Classes
**Effort**: ~200 lines per class | **Status**: Not Started
Extract 7 embedded classes from `UniversalScanner.cs` into separate files:
- `ModernMenuRenderer.cs`
- `ModernMenuColorTable.cs`
- `ModernAboutDialog.cs`
- `SimplePdfTableExporter.cs` → `PdfExportService.cs`
- `CustomPingDialog.cs`
- `PingMonitorForm.cs`
- `PdfDeviceExportRow.cs`
**Why**: Improved code navigation, maintainability, and testability.

### 🟡 Move Cross-Vendor Shared Code
**Effort**: File moves | **Status**: Not Started
Move to `DeviceSDK/Common/` or `DeviceSDK/Shared/`:
- `OnvifSoap.cs` (from `Honeywell/`)
- `HoneywellRsaHelper.cs` (from `Honeywell/`)
- `DahuaDebugLog.cs` (from `Dahua/`)
**Why**: Discoverability. Developers looking for the ONVIF client shouldn't need to look under Honeywell.

### 🟡 DPI Awareness
**Effort**: ~50 lines + manifest | **Status**: Not Started
Add DPI awareness manifest. Convert fixed pixel sizes to scaled values using `Graphics.DpiX`/`DpiY` ratios.
**Why**: Poor visual experience on high-DPI displays (common on modern laptops).

### 🟡 Configurable Timeouts
**Effort**: ~100 lines + settings UI | **Status**: Not Started
Make the following configurable: HTTP timeouts, subnet scan per-IP timeout, IP change refresh parameters (max attempts, initial wait, inter-attempt delay), PowerShell command timeout, activation step timeout.
**Why**: Different network environments (local, VPN, remote) require different timeout tuning.

---

## Medium-Term (Low Priority)

### 🟢 Fix DahuaDebugLog Stubs
**Effort**: ~50 lines | **Status**: Not Started
Implement `DahuaDebugLog` methods to write to `Logger.Debug()` when debug mode is enabled.
**Why**: Currently no debug visibility into Dahua protocol exchanges.

### 🟢 Dark Mode
**Effort**: ~200 lines + color palette | **Status**: Not Started
Extract color constants. Add dark theme palette. Add toggle in settings.
**Why**: User preference. Increasingly expected in modern applications.

### 🟢 Add Unit Test Project
**Effort**: Ongoing | **Status**: Not Started
Add NUnit or xUnit test project targeting .NET Framework 4.8. Wire `.selftest` PCAP files into automated test cases. Start with protocol decoders (deterministic, no hardware needed).
**Why**: Regression prevention. Safer refactoring. Currently all testing is manual.

### 🟢 Fill or Remove Engine Array Null Slots
**Effort**: ~1 line | **Status**: Not Started
Document what was planned for engine array slots 14-15 and 20, or remove them and compact the array.
**Why**: Prevents `NullReferenceException` if iteration code forgets to null-check.

### 🟢 IPv6 Enabled by Default
**Effort**: Testing + config change | **Status**: Not Started
Thoroughly test IPv6 scanners, then enable `IPv6Enabled = true` by default or add prominent UI toggle.
**Why**: IPv6 adoption is increasing. Currently IPv6-only devices are invisible.

### 🟢 Foscam Cipher Completion
**Effort**: Investigation needed | **Status**: Not Started
Review `Foscam.cs` and complete the cipher protocol variant implementation for encrypted Foscam models.
**Why**: Some Foscam camera models may not be discovered.

### 🟢 Port Conflict UI Warning
**Effort**: ~30 lines | **Status**: Not Started
When `PortProvider` detects port conflicts at startup, surface a UI warning showing which ports are unavailable and which protocols are affected.
**Why**: Currently conflicts are logged but not visible to the user.

---

## Long-Term (Future)

### ⬜ .NET 8 Migration
**Effort**: Multi-week | **Status**: Not Planned
Port project from .NET Framework 4.8 to .NET 8 with `UseWindowsForms=true`. This enables:
- Modern C# features (records, pattern matching, default interface methods)
- Cross-platform potential (with Avalonia/MAUI)
- Performance improvements in the runtime
- Better async/await support
**Blockers**: HCNetSDK P/Invoke compatibility, WinHTTP COM replacement, PowerShell Core for Linux.

### ⬜ Cross-Platform Support
**Effort**: Multi-month | **Status**: Not Planned
Replace platform-specific dependencies:
- HCNetSDK → pure-managed Hikvision protocol or skip advanced features
- WinHTTP COM → HttpClient (already used elsewhere)
- WinForms → Avalonia or MAUI
- Registry → JSON config files
- user32.dll → platform-specific window activation
- `arp -a` → `/proc/net/arp` on Linux
- PowerShell → `ip link`/`ip addr`/`route` on Linux

### ⬜ Dependency Injection Container
**Effort**: Medium refactor | **Status**: Not Planned
Introduce constructor injection with a lightweight DI container. Enables mock-based testing and implementation swapping.
**Depends on**: .NET 8 migration (for `Microsoft.Extensions.DependencyInjection`).

### ⬜ RTSP Path Database
**Effort**: Medium | **Status**: Not Planned
Build a per-model RTSP path lookup table, populated from ONVIF GetProfiles. Add user-editable RTSP path override in UI.
**Why**: Currently RTSP paths are hardcoded per vendor. Non-standard models fail.

---

## Completed

_No completed items yet. First entries will come from implementation of the above._
