# Feature History — Universal Scanner

Completed major features with verification and regression notes.

---

## Format

Each entry records:
- **Date**: Completion date
- **Feature**: Short descriptive name
- **Status**: Complete / Partial / Reverted
- **Files Changed**: List of modified files
- **Summary**: What was done and why
- **Verification**: How it was tested/confirmed
- **Regression Notes**: What could break, what to watch

---

## Completed Features

<!-- Example entry:
### 2026-07-06 | Example Feature | Complete
- **Files Changed**: `Foo.cs`, `Bar.cs`
- **Summary**: Added X capability to Y component.
- **Verification**: Tested with 3 device types, confirmed discovery and activation.
- **Regression Notes**: None. Existing tests pass.
-->

### 2026-07-07 | Firmware Version and Release Date Columns (FEAT-0001) | Complete
- **Files Changed**: `DeviceInfo.cs`, `DeviceFirmwareProbe.cs` (created), `UniversalScanner.cs`, `UniversalScanner.csproj`
- **Summary**: Added "Firmware" and "Release Date" columns to the main device grid. Data is extracted exclusively from the device itself using ONVIF GetDeviceInformation (authenticated) and vendor-specific unauthenticated endpoints: Hikvision/Honeywell ISAPI, Dahua CGI, Uniview LAPI, Sunell WEB_JsonAjax, Tiandy CGI. No internet connections or external databases used. Follows the existing QueueOnvifProfileLookup fire-and-forget background task pattern with BeginInvoke UI marshaling. Columns auto-included in CSV export, PDF export, and column sorting. Save/load format extended from 11 to 13 pipe-delimited fields with backward compatibility.
- **Verification**: Debug build (0 errors, 0 warnings). Release x86 build (0 errors, 0 warnings). Both produce working executables. All 5 CS1998 async-without-await warnings resolved. Architecture review confirmed thread safety (lock + BeginInvoke), vendor detection cascade unchanged, deduplication logic preserved, IP change guard windows intact. 7 firmware lookup integration points verified correct. Save/load backward compatibility verified for both 11-field and 13-field file formats. Output: `UniversalScanner\bin\x86\Release\UniversalScanner.exe`
- **Regression Notes**: None. No changes to ScannerViewer interface (30+ scanner implementations untouched). No changes to activation, IP change, discovery order, vendor detection, or authentication logic. Three vendor methods (Dahua, Uniview, Tiandy) use unauthenticated endpoints only — if a vendor requires auth for firmware info, ONVIF must be tried first with known credentials. The `_http` HttpClient field in DeviceFirmwareProbe is defined but unused (all methods use HttpWebRequest directly); it's harmless but could be cleaned up in a future refactor.

### 2026-07-07 | Network Profile Load/Save/Restore Fix (NET-0007) | Complete
- **Files Changed**: `NetworkProfileManager.cs`, `NetworkAdapterService.cs`
- **Summary**: Fixed PowerShell 5.1 `ConvertTo-Json` single-element array unwrapping bug in network profile capture/load. Added JSON normalization step (`NormalizeJsonForProfile()`) that detects single objects/strings where arrays are expected and wraps them into proper arrays before deserialization. Applied in both `LoadProfile()` and `CaptureProfile()` paths. Added PrefixLength validation (1-32) in `RestoreProfile()` to prevent invalid PowerShell commands.
- **Verification**: Release x86 build succeeds with 0 errors, 0 warnings. Previously JSON with unwrapped single-element Addresses/DnsServers will now correctly deserialize.
- **Regression Notes**: None. Normalization is a no-op for already-correct JSON arrays. The PrefixLength guard only skips invalid entries that would have produced broken PowerShell commands anyway.

### 2026-07-07 | Set-DnsClientServerAddress -AddressFamily PS 5.1 Fix (BUG-0001) | Complete
- **Files Changed**: `NetworkAdapterService.cs`
- **Summary**: Removed `-AddressFamily IPv4` from 3 `Set-DnsClientServerAddress` calls (ApplyDhcp and RestoreProfile). This parameter doesn't exist in Windows PowerShell 5.1's NetTCPIP module, causing `NamedParameterNotFound` errors when restoring profiles or switching to DHCP. The cmdlet infers address family from the IP addresses passed, so the parameter is unnecessary.
- **Verification**: Release x86 build succeeds with 0 errors, 0 warnings. EXE copied to `release/UniversalScanner.exe`.
- **Regression Notes**: None. DNS server addresses in this application are always IPv4; the cmdlet correctly infers this without the parameter on all PowerShell versions.
