# Changelog — AI-Assisted Changes

Record of all significant changes made with AI assistance. Each entry includes date, feature/fix, files changed, summary, and compatibility notes.

---

## 2026-07-06 — Documentation System Established

**Type**: Project Infrastructure
**Files Created**: `docs/PROJECT_INDEX.md`, `docs/ARCHITECTURE.md`, `docs/DISCOVERY_ENGINE.md`, `docs/VENDOR_MATRIX.md`, `docs/NETWORK_ARCHITECTURE.md`, `docs/IMPLEMENTATION_GUIDELINES.md`, `docs/KNOWN_ISSUES.md`, `docs/CHANGELOG_AI.md`, `docs/ROADMAP.md`, `docs/AI_CONTEXT.md`
**Files Updated**: `CLAUDE.md`
**Summary**: Created the `/docs` folder as the permanent architectural knowledge base for the Universal Scanner project. Seven documentation files were extracted from deep codebase analysis, plus three new infrastructure files (CHANGELOG_AI, ROADMAP, AI_CONTEXT) to establish ongoing documentation practices. `CLAUDE.md` was updated to reference the `/docs` folder.
**Compatibility**: No code changes. Fully backward compatible.

---

## 2026-07-06 — CLAUDE.md Refactored

**Type**: Project Infrastructure
**Files Updated**: `CLAUDE.md`
**Summary**: Replaced the full project summary in `CLAUDE.md` with a pointer to `docs/PROJECT_INDEX.md` and the documentation system. Consolidated project instructions while preserving the user's role definition and workflow directives.
**Compatibility**: No code changes. Fully backward compatible.

---

---

## 2026-07-06 — PROJECT_SUMMARY.md Rewritten as Landing Page + Root Cleanup

**Type**: Documentation
**Files Changed**:
- `docs/PROJECT_SUMMARY.md` — Created as concise executive overview (~100 lines): purpose, capabilities, vendor table, architecture diagram, main modules, status, quick links. Reduced from 401-line legacy file that duplicated other docs.
- `UniversalScanner/PROJECT_SUMMARY.md` — Deleted (moved to docs/, rewritten)
- `UniversalScanner/ARCHITECTURE.md` — Deleted (duplicate, canonical copy in docs/)
- `UniversalScanner/DISCOVERY_ENGINE.md` — Deleted (duplicate, canonical copy in docs/)
- `UniversalScanner/IMPLEMENTATION_GUIDELINES.md` — Deleted (duplicate, canonical copy in docs/)
- `UniversalScanner/KNOWN_ISSUES.md` — Deleted (duplicate, canonical copy in docs/)
- `UniversalScanner/NETWORK_ARCHITECTURE.md` — Deleted (duplicate, canonical copy in docs/)
- `UniversalScanner/PROJECT_INDEX.md` — Deleted (duplicate, canonical copy in docs/)
- `UniversalScanner/VENDOR_MATRIX.md` — Deleted (duplicate, canonical copy in docs/)
- `docs/PROJECT_INDEX.md` — Updated Companion Documents section: added PROJECT_SUMMARY.md as first entry
- `docs/AI_CONTEXT.md` — Updated before-task workflow and doc maintenance list to include PROJECT_SUMMARY.md
- `CLAUDE.md` — Updated Development Guidelines section with more specific rules; added PROJECT_SUMMARY.md to doc table
**Summary**: Rewrote PROJECT_SUMMARY.md from a 401-line comprehensive document (with deep detail, Python conversion notes, and content overlapping 5+ other docs) into a ~100-line landing page for new developers. Cleaned up 7 stale duplicate .md files from UniversalScanner/ root that had been migrated to docs/ in the prior session. Updated all cross-references.
**Compatibility**: No code changes. Fully backward compatible.

---

## 2026-07-06 — BUILD_GUIDE.md Created (Release Build Workflow)

**Type**: Documentation
**Files Created**: `docs/BUILD_GUIDE.md`, `tasks/DOC-0001-release-build-workflow.md`
**Files Updated**: `docs/PROJECT_INDEX.md`, `docs/AI_CONTEXT.md`
**Summary**: Investigated the existing standalone EXE packaging system and created comprehensive build documentation. Documented the full chain: Costura.Fody 6.2.0 embeds managed DLLs via IL weaving, RuntimeDependencyManager extracts 25 Hikvision native SDK files from embedded resources at startup, the Package-Release.ps1 script collects everything into dist/UniversalScanner-Release/, and Inno Setup creates the final installer. BUILD_GUIDE.md covers all 4 build configurations, verification checklist, P/Invoke resolution, HCNetSDKCom dependency requirements, and 7 known limitations. Established that Release|x86 is the only supported production configuration.
**Compatibility**: No code changes. Fully backward compatible.

---

## 2026-07-07 — Network Profile Load/Save/Restore Fix (PowerShell 5.1 Array Unwrapping)

**Type**: Bug Fix
**Files Changed**:
- `UniversalScanner/NetworkProfileManager.cs` — Added `NormalizeJsonForProfile()` static method that deserializes JSON to Dictionary, wraps single-object Addresses into ArrayList, wraps single-string DnsServers into ArrayList, and re-serializes. Used in `LoadProfile()` before `Deserialize<NetworkProfile>()`.
- `UniversalScanner/NetworkAdapterService.cs` — Added `NormalizeJsonForProfile()` call in `CaptureProfile()` before deserializing PowerShell JSON output. Added PrefixLength validation (1-32 range check) in `RestoreProfile()` to skip addresses with invalid prefix lengths.
**Summary**: Fixed PowerShell 5.1 `ConvertTo-Json` single-element array unwrapping bug that caused profile load/save/restore failures. When a network adapter has only one IP address or one DNS server, PowerShell serializes these as single objects/strings instead of arrays. The JavaScriptSerializer then fails to deserialize them into `List<NetworkProfileAddress>` and `List<string>` respectively. The fix adds a JSON normalization step that detects and wraps single elements into arrays before deserialization, in both the load and capture paths. Also added PrefixLength validation in RestoreProfile to prevent invalid PowerShell `New-NetIPAddress` commands.
**Compatibility**: Fully backward compatible. Previously saved profile files with single-element unwrapping are now correctly loaded. New profiles are saved with proper arrays via the serializer.

---

## 2026-07-07 — Set-DnsClientServerAddress -AddressFamily PowerShell 5.1 Compatibility Fix

**Type**: Bug Fix
**Files Changed**:
- `UniversalScanner/NetworkAdapterService.cs` — Removed `-AddressFamily IPv4` from 3 `Set-DnsClientServerAddress` calls (ApplyDhcp line 185, RestoreProfile lines 266 and 272).
**Summary**: The `-AddressFamily` parameter on `Set-DnsClientServerAddress` does not exist in Windows PowerShell 5.1's NetTCPIP module — it was added later in PowerShell Core. Restoring a saved network profile failed with `NamedParameterNotFound` because the cmdlet rejected the unknown parameter. The fix removes the parameter from all three call sites. The cmdlet infers address family from the IP addresses passed to `-ServerAddresses`, so the parameter is unnecessary.
**Compatibility**: Fully backward compatible. Works on PowerShell 5.1 (where parameter didn't exist) and PowerShell Core 7.x (where parameter is optional). No behavioral change — DNS server addresses are always IPv4 in this application.

---

## 2026-07-07 — Firmware Version and Release Date Columns

**Type**: Feature
**Files Changed**:
- `UniversalScanner/DeviceSDK/Common/DeviceInfo.cs` — Added `FirmwareReleaseDate` property and constructor initialization
- `UniversalScanner/DeviceSDK/Common/DeviceFirmwareProbe.cs` — Created. Extracts firmware version & release date from devices using ONVIF GetDeviceInformation + vendor-specific endpoints (Hikvision/Honeywell ISAPI, Dahua CGI, Uniview LAPI, Sunell WEB_JsonAjax, Tiandy CGI). All methods return `Task<FirmwareResult>`; no CS1998 async-without-await warnings.
- `UniversalScanner/UniversalScanner.cs` — Added Firmware (index 11) and Release Date (index 12) columns to Columns enum, ColumnNames array, DataTable, saveScanResults, loadScanResults, and all 6 Rows.Add call sites. Added `QueueFirmwareLookup` background task + `ApplyFirmwareLookupResult` callback following the existing QueueOnvifProfileLookup pattern (HashSet dedup, Task.Run, BeginInvoke). Integrated at 7 call sites: normal discovery (new/existing), Tiandy assist (new/existing), bulk scan (new/existing), device refresh/IP-change.
- `UniversalScanner/UniversalScanner.csproj` — Added `<Compile Include>` entry for DeviceFirmwareProbe.cs
**Summary**: Added two new columns ("Firmware" and "Release Date") to the main device grid. Data comes exclusively from the device itself — no internet connections, no external databases. Uses ONVIF GetDeviceInformation for authenticated cameras (requires known username/password), then falls through to vendor-specific unauthenticated endpoints. CSV export, PDF export, and column sorting all automatically include the new columns without additional code changes. Backward-compatible save/load with pipe-delimited format (13 fields; old 11-field files load with empty strings for new columns).
**Compatibility**: Fully backward compatible. Existing `.usscan` files load with empty firmware/release date. No interface changes to ScannerViewer or ScanEngine. Vendor detection cascade, activation, IP change, and deduplication logic unchanged.

---

## Template for Future Entries

```markdown
## YYYY-MM-DD — Brief Title

**Type**: Feature | Bug Fix | Refactor | Infrastructure | Documentation
**Files Changed**:
- `path/to/File.cs` — What changed and why
- `path/to/OtherFile.cs` — What changed and why
**Summary**: One paragraph describing the change, rationale, and approach.
**Compatibility**: Backward compatible / Breaking change (describe migration path) / New feature (no migration needed)
```
