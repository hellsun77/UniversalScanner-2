# AI Context — Universal Scanner

Long-term project context for AI assistants. Read this before starting any task to understand goals, constraints, and workflow.

---

## Project Identity

- **Name**: Universal Scanner (Universal Network Scanner)
- **Purpose**: Multi-brand ultra-fast network discovery tool for security cameras and network devices
- **Platform**: Windows-only, .NET Framework 4.8, WinForms, C# 7.3
- **Scale**: ~94 .cs files, ~50,000 lines, 30 protocol scanners, 7 vendor SDK integrations
- **Output**: Single .exe (Fody/Costura for DLL embedding)

## Core Goals

1. **Discover everything**: Maximize device discovery across all protocols and vendors
2. **Vendor coverage**: Support the top IP camera vendors with full activation + IP change + RTSP
3. **Speed**: Parallel scanning, concurrent operations, non-blocking UI
4. **Reliability**: Graceful degradation at every layer, never crash on unexpected data
5. **Single binary**: Zero-install deployment via embedded dependencies

## Design Philosophy

- **Protocol-first**: Generic standards (SSDP, WS-Discovery, mDNS) form the foundation. Vendor-specific scanners add coverage for proprietary protocols.
- **Degrade gracefully**: Every vendor operation has a fallback path. Vendor-specific → ONVIF standard → Generic → User-visible error.
- **Fire-and-forget background work**: Long operations (MAC lookup, activation probe, ping) run as background tasks that update the UI when complete. Don't block the main thread.
- **Inline is acceptable**: The codebase favors inline vendor routing over abstractions. This is a pragmatic choice, not a defect — the `IDeviceProvider` pattern exists but is only partially adopted. New code should use the provider pattern where possible, but match existing inline style when extending existing routing code.
- **Small files for utilities, large files for forms**: Utility classes are small and focused. Form classes (especially `UniversalScanner.cs`) are large because WinForms couples UI, data binding, and event handling.

## Things That Must Never Be Broken

1. **Device deduplication**: The UUID → MAC → IP priority system. Duplicate rows erode user trust.
2. **IP change guard windows**: The 5-minute `oldIpSuppressions` + `recentChangedIps` preservation. Breaking this causes phantom duplicate devices.
3. **UI thread safety**: Non-UI threads must never touch WinForms controls. Always `BeginInvoke`.
4. **Socket lifecycle**: Always use `ScanEngine` base class factory methods for socket creation. Never create `UdpClient`/`Socket` directly.
5. **Graceful degradation chain**: Vendor-specific → ONVIF → Generic → Error. Never regress to "just crash."
6. **Vendor detection cascade order**: Honeywell → Hikvision → Uniview → Dahua → Sunell → Tiandy. Don't change without understanding the implications (some probes are more specific than others).
7. **Honeywell dual-path routing**: HC30/HC35 (WEB_JsonAjaxAuth) vs Legacy (DevInit). Probing the wrong path can trigger device security lockouts.

## Coding Standards

- **Match surrounding style**: Read adjacent code before writing. Match comment density, naming, brace style, region usage.
- **Thread safety**: `BeginInvoke` for fire-and-forget UI updates, `ReaderWriterLockSlim` for `foundDeviceList`, `ConcurrentDictionary` for shared caches.
- **Error handling**: Every external call wrapped in try/catch. Log and degrade, never crash.
- **Naming**: `{Vendor}Activator.cs`, `{Vendor}Network.cs`, `{Vendor}ActivationStatusProbe.cs` pattern for vendor modules. See IMPLEMENTATION_GUIDELINES.md for full conventions.
- **Regions**: `#region` blocks for logical grouping in larger files.
- **No async void**: Use `async Task` for event handlers that need async work.

## Vendor Compatibility

| Vendor | Activation | IP Change | RTSP | Discovery | SDK |
|--------|-----------|-----------|------|-----------|-----|
| Hikvision | HCNetSDK P/Invoke | HCNetSDK | Via VLC | SADP (UDP 37020) | Native DLL |
| Honeywell HC30/35 | WEB_JsonAjaxAuth RSA+AES | ONVIF SOAP | Via VLC | mDNS/WS-Discovery | Pure managed |
| Honeywell Legacy | DevInit WinHTTP COM | WinHTTP COM RPC | Via VLC | WS-Discovery | Pure managed + COM |
| Uniview | LAPI REST → ONVIF SOAP | LAPI → ONVIF SOAP | Via VLC | WS-Discovery | Pure managed |
| Dahua | DevInit → RPC → CGI | ONVIF → CGI | Via VLC | UDP 5050 + 37810 | Pure managed |
| Sunell | XML WEB_JsonAjax RSA+AES | ONVIF SOAP | Via VLC | WS-Discovery | Pure managed |
| Tiandy | ISAPI XML DIGEST + DES | ONVIF SOAP | Via VLC | Tiandy Assist | Pure managed |

## Preferred Architecture

- **Add new vendors** through the 10-step process in IMPLEMENTATION_GUIDELINES.md
- **Add new protocol scanners** by subclassing `ScanEngine` and following existing patterns (study SSDP or WS-Discovery)
- **Add features to existing vendors** by extending the appropriate `{Vendor}Activator`/`{Vendor}Network`/`{Vendor}ActivationStatusProbe` class
- **Safe integration points**:
  - Vendor routing: `ChangeIpForm`, `DeviceInitializationForm`, `ScannerWindow` vendor-type-check methods
  - Discovery: New `ScanEngine` subclass in root namespace
  - UI: New form in root namespace, or embedded class in `UniversalScanner.cs`
  - Cross-vendor: `OnvifSoap` (ONVIF), `HoneywellRsaHelper` (crypto), `GenericOnvifNetwork` (ONVIF IP change)

## AI Workflow

### Git Workflow
Follow the standard Git workflow documented in [GIT_WORKFLOW.md](GIT_WORKFLOW.md). Key points:
- Always work on a feature branch off `main`
- Commit or stash manual work before letting Claude/Codex modify files
- Review all changes with `git diff` before committing
- Keep documentation and code in the same commit
- Tag the starting point for major features (`git tag backup/before-<name>`)

### Task File Workflow
Every bug investigation, feature plan, or Codex-ready prompt must have a corresponding task file in `tasks/`. See [GIT_WORKFLOW.md](GIT_WORKFLOW.md) for how tasks integrate with version control.

- **Naming**: `tasks/<CATEGORY>-<NNNN>-<short-description>.md`
- **Template**: `tasks/TASK-TEMPLATE.md`
- **Active tasks**: live in `tasks/`
- **Completed tasks**: moved to `tasks/archive/`
- **Never**: store task plans in `docs/` — that directory is for permanent architecture documentation only
- **Categories**: BUG, FEAT, DISC, VEND, NET, UI, DOC, REF, PERF

### Evidence Workflow
When a bug, error, crash, or issue is reported, Claude auto-checks the `evidence/` folder before asking questions. See [GIT_WORKFLOW.md](GIT_WORKFLOW.md) for how evidence integrates with version control.

- **Auto-check**: Always look in `evidence/` first when investigating any issue
- **Auto-select**: Use the newest file in the appropriate subfolder if no file is specified
- **Read-only**: Never rename, move, overwrite, or delete evidence files
- **Reference in tasks**: Every BUG task must cite the evidence file(s) used
- **Evidence type → folder mapping**:

| Issue Type | Primary Folder |
|------------|---------------|
| UI errors, dialogs, exceptions | `evidence/screenshots/` |
| Runtime/application errors | `evidence/logs/` |
| Network, discovery, ONVIF, activation, IP change, protocol | `evidence/pcaps/` |
| Configuration issues | `evidence/configs/` |
| Imported/exported project data | `evidence/exports/` |

- **Ask only when necessary**: If evidence is sufficient, begin investigation immediately. If multiple recent files exist and it's unclear which to use, ask.

Task status progression: Planned → In Progress → Implemented → Reviewed → Completed

### Before Every Task
1. Read `docs/PROJECT_SUMMARY.md` for a high-level overview (if new to the project)
2. Read `docs/PROJECT_INDEX.md` to locate relevant files
3. Read only the documentation files relevant to the task
3. Read only the source files needed — do not reread the entire codebase

### Implementation Process

#### Normal Path (Codex)
1. **Find relevant files** from PROJECT_INDEX.md
2. **Read only those files** to understand current implementation
3. **Make an implementation plan** identifying:
   - Exact files to modify/create
   - Integration points (which methods, which lines)
   - How it fits into existing patterns
   - Potential side effects
4. **Wait for approval** before writing any code
5. **Write a Codex-ready prompt** for implementation
6. **After implementation**, complete the mandatory post-implementation review

#### Fallback Path (Claude Direct Implementation)
When Codex is unavailable, Claude follows the same plan-then-implement pattern:
1. Same investigation and planning steps as above
2. Implement directly following the approved plan
3. Preserve existing architecture, modify only necessary files, avoid unrelated refactoring
4. Build the project if possible
5. Complete the same 9-step post-implementation review

### Post-Implementation Review (Mandatory)

**After every implementation** (by Codex or Claude in fallback), Claude must complete these 9 steps before marking the task done:

1. **Review the changed files** — Read every modified file. Verify the diff matches the intended change. No stray edits, no leftover debug code.

2. **Check for architecture regressions** — Confirm:
   - Thread safety preserved (BeginInvoke for UI, locks for shared state)
   - Socket lifecycle follows ScanEngine factory methods
   - Resource cleanup (Dispose, closing flags) intact
   - Device deduplication logic (UUID → MAC → IP priority) unbroken
   - IP change guard windows (oldIpSuppressions, recentChangedIps) still work

3. **Check for vendor regressions** — Confirm:
   - Vendor detection cascade order unchanged (Honeywell → Hikvision → Uniview → Dahua → Sunell → Tiandy)
   - Honeywell dual-path routing (HC30/HC35 vs Legacy) not broken
   - Graceful degradation chain intact (vendor-specific → ONVIF → generic → error)

4. **Verify design pattern compliance** — Match surrounding style, naming conventions, error handling patterns. New vendor code follows `{Vendor}Activator`/`{Vendor}Network`/`{Vendor}ActivationStatusProbe` pattern. New scanners subclass `ScanEngine`.

5. **Build verification** — See [Build Verification Policy](#build-verification-policy) below. If the environment supports it, run `msbuild UniversalScanner.sln /p:Configuration=Debug`. Zero errors required for task completion.

6. **Update documentation if required:**
   - High-level overview changes → PROJECT_SUMMARY.md
   - New/changed files, line counts → PROJECT_INDEX.md
   - Vendor capability changes → VENDOR_MATRIX.md
   - Discovery protocol changes → DISCOVERY_ENGINE.md
   - IP change or network flow changes → NETWORK_ARCHITECTURE.md
   - Coding patterns/conventions changes → IMPLEMENTATION_GUIDELINES.md
   - Issue resolution/discovery → KNOWN_ISSUES.md
   - Planned work completed/reprioritized → ROADMAP.md

7. **Update CHANGELOG_AI.md** — Every implementation. Date, feature, files changed, summary.

8. **Update FEATURE_HISTORY.md** — For major completed features. Date, status, files changed, summary, verification, regression notes.

9. **Report remaining risks and follow-up work** — What was NOT done, known limitations, untested scenarios, recommendations for follow-up, any behavior changes that could surprise users.

### Build Verification Policy

This policy applies whenever Claude implements code directly. Codex implementations follow the same standard as part of the 9-step review (step 5).

1. **Build the project** if the required build tools are available.
   - Command: `msbuild UniversalScanner.sln /p:Configuration=Debug`
   - If `msbuild` is not on PATH, check common locations or report that build tools are unavailable.

2. **If the build fails:**
   - Analyze the compiler errors.
   - Fix only the errors introduced by the implementation.
   - Rebuild.
   - Repeat until the project builds successfully or an unrelated existing issue blocks the build.

3. **Report:**
   - Build succeeded or failed.
   - Number of errors.
   - Number of warnings.
   - Remaining issues (if any).

4. **Never mark a task as completed unless:**
   - The build succeeds, OR
   - The remaining build failures are unrelated to the implemented change and are clearly documented in the task's Final Review Notes.

### Role Boundaries & Workflow

#### Primary Roles

| Role | Responsibilities |
|------|-----------------|
| **Claude** | Software Architect, Project Reviewer, Bug Investigator, Protocol Researcher, Documentation Maintainer, Implementation Planner |
| **Codex** | Primary Implementation Engine |

#### Normal Workflow

1. Claude investigates the problem.
2. Claude creates or updates the appropriate task under `tasks/`.
3. Claude produces the implementation plan.
4. Codex implements the approved changes.
5. Claude reviews the implementation.
6. Claude updates all required documentation.
7. Claude closes the task.

#### Fallback Workflow

If Codex is unavailable because of credits exhausted, rate limits, service outage, temporary unavailability, or the user explicitly requests it — **Claude is authorized to implement the approved changes directly**.

When implementing code, Claude must:
1. Follow the approved implementation plan.
2. Preserve the existing architecture.
3. Modify only the necessary files.
4. Avoid unrelated refactoring.
5. Build the project if possible.
6. Review the implementation.
7. Update CHANGELOG_AI.md, FEATURE_HISTORY.md, KNOWN_ISSUES.md (if needed), and any affected documentation.
8. Mark the task as Completed.

#### Role Philosophy

Codex is the preferred implementation engine, but not the only one. Claude should never stop progress simply because Codex is unavailable. If Claude has enough information and the implementation is low-risk, it implements the change itself while following the same quality standards used for Codex implementations. The objective is to keep the project moving while maintaining architecture, stability, and documentation quality.

**Never**: Modify unrelated code, convert the project to another language, make architectural changes without approval

## Key Dependencies

| Dependency | Purpose | Constraints |
|-----------|---------|------------|
| HCNetSDK.dll | Hikvision operations | Windows-only P/Invoke. Embedded, extracted at runtime |
| OnvifSoap.cs | ONVIF SOAP client | Pure managed. Located under Honeywell/ but used by 5+ vendors |
| HoneywellRsaHelper.cs | RSA+AES crypto | Pure managed. Used by Honeywell and Sunell |
| Costura.Fody 6.2.0 | DLL embedding | Weaves managed dependencies into single .exe. Build-time only |
| Fody 6.9.3 | IL weaving engine | Required by Costura. Build-time only |
| PowerShell 5.0+ | Network adapter management | Windows-only. Required for IP change workflows |
| VLC | RTSP live view | External process. Not bundled. |
| System.Web.Extensions | JSON serialization | .NET Framework built-in |

### Release Build Policy

- **Build command**: `msbuild UniversalScanner.sln /p:Configuration=Release /p:Platform=x86`
- **Output**: `UniversalScanner\bin\x86\Release\UniversalScanner.exe` (standalone, managed DLLs embedded via Costura)
- **Packaging**: `powershell -ExecutionPolicy Bypass -File scripts\Package-Release.ps1` produces `dist\UniversalScanner-Release\`
- **Full details**: See [BUILD_GUIDE.md](BUILD_GUIDE.md) for the complete build, packaging, and verification workflow
- **Never**: change target framework, project architecture, or existing packaging method without approval
- **Never**: break x86 native SDK DLL loading or Hikvision vendor SDK usage
- **Native SDKs**: 25 Hikvision files embedded as resources, extracted at runtime by `RuntimeDependencyManager` to `%TEMP%\UniversalScanner\<pid>-<guid>\SDK\`

## Common Pitfalls

1. **Modifying `foundDeviceList` without locks** → race conditions in device grid
2. **Creating sockets outside `ScanEngine` factory methods** → resource leaks
3. **Accessing UI controls from worker threads** → cross-thread exceptions
4. **Changing vendor detection cascade order** → unprobed devices, false negatives
5. **Adding vendor logic inline instead of through `IDeviceProvider`** → architectural inconsistency (though currently the codebase is split on this — match the existing pattern for the vendor)
6. **Forgetting Honeywell dual-path** → one size does not fit all Honeywell devices
7. **Not testing with actual hardware** → protocol quirks vs. spec compliance differ
