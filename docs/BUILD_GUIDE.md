# Build Guide — Universal Scanner

How to build, package, and distribute the Universal Scanner standalone EXE.

---

## Quick Start

```powershell
# Build Release x86
msbuild UniversalScanner.sln /p:Configuration=Release /p:Platform=x86
```

The standalone EXE is produced at `UniversalScanner\bin\x86\Release\UniversalScanner.exe`.

---

## Standalone EXE Packaging

### How It Works

Universal Scanner uses **Costura.Fody 6.2.0** to embed managed .NET dependencies into a single executable. Native (unmanaged) SDK DLLs are embedded as standard assembly resources and extracted at runtime by `RuntimeDependencyManager`.

| Layer | Method | Details |
|-------|--------|---------|
| Managed DLLs | Costura.Fody IL weaving | All referenced managed assemblies (System.Web.Extensions, etc.) are embedded as resources in the EXE and loaded from memory at startup |
| Native SDK DLLs | `RuntimeDependencyManager` | 25 Hikvision SDK files (DLLs + LIBs) embedded as `EmbeddedResource` under `SDK/**`, extracted to `%TEMP%\UniversalScanner\<pid>-<guid>\SDK\` at startup |

### Key Files

| File | Role |
|------|------|
| `UniversalScanner\FodyWeavers.xml` | Enables Costura via `<Costura />` |
| `UniversalScanner\packages.config` | NuGet: Costura.Fody 6.2.0 + Fody 6.9.3 |
| `UniversalScanner\RuntimeDependencyManager.cs` | Extracts native SDK DLLs from embedded resources at startup; cleans up on exit |
| `UniversalScanner\Program.cs` | Calls `RuntimeDependencyManager.Prepare()` during splash screen |
| `scripts\Package-Release.ps1` | Builds and packages the release into `dist\` |

### NuGet Dependencies

```
Costura.Fody 6.2.0   — IL weaver that embeds managed DLLs into the EXE
Fody 6.9.3           — IL weaving engine (required by Costura)
```

Both are marked `developmentDependency="true"` — they are build-time only and not shipped as separate files.

### FodyWeavers.xml

```xml
<Weavers xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="FodyWeavers.xsd">
  <Costura />
</Weavers>
```

Costura is configured with defaults — no custom exclusions, no custom include filters. This means Costura auto-embeds every managed reference where `CopyLocal=true`.

---

## Build Configurations

The solution defines four build configurations:

| Configuration | Platform | Output Path | Optimized | Debug Symbols |
|---------------|----------|-------------|-----------|---------------|
| Debug\|AnyCPU | AnyCPU | `bin\Debug\` | No | Full (.pdb) |
| Release\|AnyCPU | AnyCPU | `bin\Release\` | Yes | pdbonly |
| Debug\|x86 | x86 | `bin\x86\Debug\` | No | Full (.pdb) |
| **Release\|x86** | **x86** | **`bin\x86\Release\`** | **Yes** | **pdbonly** |

**Use Release\|x86 for distribution.** This is the target platform for all production builds.

### Build Commands

```powershell
# Release x86 (recommended for distribution)
msbuild UniversalScanner.sln /p:Configuration=Release /p:Platform=x86

# Debug x86 (for testing)
msbuild UniversalScanner.sln /p:Configuration=Debug /p:Platform=x86

# Release AnyCPU
msbuild UniversalScanner.sln /p:Configuration=Release /p:Platform=AnyCPU
```

### MSBuild Locations

The packaging script searches these paths for MSBuild:

1. `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe`
2. `C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe`
3. `C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe`

Visual Studio Build Tools 2022 with the .NET Framework 4.8 targeting pack is required.

---

## Release Packaging

### Automated: `scripts\Package-Release.ps1`

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Package-Release.ps1
```

This script:
1. Builds the project with MSBuild (default: Release\|AnyCPU)
2. Copies `UniversalScanner.exe` and `.config` to `dist\UniversalScanner-Release\`
3. Copies all `.selftest` files
4. Copies the Inno Setup script (`UniversalScanner.iss`)
5. Copies Hikvision native SDK DLLs flat beside the EXE (for P/Invoke resolution)
6. Copies the full `SDK\` folder structure
7. Generates `README.txt`
8. Creates `dist\UniversalScanner-Release.zip`

**Note:** The script defaults to `Release|AnyCPU`. To build x86, pass the platform explicitly:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\Package-Release.ps1 -Configuration Release -Platform x86
```

### Output Structure

```
dist\UniversalScanner-Release\
├── UniversalScanner.exe           # Standalone EXE (managed DLLs embedded by Costura)
├── UniversalScanner.exe.config    # .NET Framework runtime config
├── AudioRender.dll                # ┐
├── HCCore.dll                     # │
├── HCNetSDK.dll                   # │ Hikvision native SDK DLLs
├── hlog.dll                       # │ (flat beside EXE for P/Invoke)
├── libeay32.dll                   # │
├── PlayCtrl.dll                   # │
├── ssleay32.dll                   # │
├── SuperRender.dll                # ┘
├── HCNetSDKCom\                   # Hikvision SDK COM components
│   ├── AnalyzeData.dll
│   ├── AudioIntercom.dll
│   ├── HCAlarm.dll / .lib
│   ├── HCCoreDevCfg.dll
│   ├── HCDisplay.dll
│   ├── HCGeneralCfgMgr.dll / .lib
│   ├── HCIndustry.dll
│   ├── HCPlayBack.dll
│   ├── HCPreview.dll / .lib
│   ├── HCVoiceTalk.dll
│   ├── libiconv2.dll
│   ├── msvcr90.dll
│   ├── OpenAL32.dll
│   ├── StreamTransClient.dll
│   └── SystemTransform.dll
├── SDK\                           # Full SDK folder (RuntimeDependencyManager fallback)
│   └── Hikvision\
│       ├── (same DLLs as above)
│       ├── HCNetSDKCom\ (same)
│       └── CHCNetSDK.cs
├── *.selftest                     # Self-test PCAPs (24 files)
├── UniversalScanner.iss           # Inno Setup installer script
├── README.txt                     # Release notes
└── (no managed DLLs — all embedded in the EXE)
```

---

## Native SDK DLLs

### Files Embedded and Extracted at Runtime

`RuntimeDependencyManager` manages 25 Hikvision SDK files:

**Root SDK DLLs (7):**
- `AudioRender.dll`, `HCCore.dll`, `HCNetSDK.dll`, `hlog.dll`, `libeay32.dll`, `PlayCtrl.dll`, `ssleay32.dll`, `SuperRender.dll`

**HCNetSDKCom folder (15):**
- `AnalyzeData.dll`, `AudioIntercom.dll`, `HCAlarm.dll`, `HCAlarm.lib`, `HCCoreDevCfg.dll`, `HCDisplay.dll`, `HCGeneralCfgMgr.dll`, `HCGeneralCfgMgr.lib`, `HCIndustry.dll`, `HCPlayBack.dll`, `HCPreview.dll`, `HCPreview.lib`, `HCVoiceTalk.dll`, `libiconv2.dll`, `msvcr90.dll`, `OpenAL32.dll`, `StreamTransClient.dll`, `SystemTransform.dll`

### Extraction Process

1. **Startup:** `Program.Main()` calls `RuntimeDependencyManager.Prepare()` during splash screen
2. **Extraction:** Files are read from embedded resources (`UniversalScanner.SDK.Hikvision.*`) and written to `%TEMP%\UniversalScanner\<pid>-<guid>\SDK\`
3. **Cleanup on exit:** `Application.ApplicationExit` and `AppDomain.ProcessExit` handlers call `Cleanup()`
4. **Stale folder cleanup:** On startup, any folders left from crashed previous runs are deleted
5. **Locked files:** Files locked by the Hikvision SDK are scheduled for deletion on next reboot via `MoveFileEx` with `MOVEFILE_DELAY_UNTIL_REBOOT` (flag `0x4`)

### P/Invoke Resolution

HCNetSDK.dll is loaded via P/Invoke in `DeviceSDK\Hikvision\CHCNetSDK.cs`. Windows searches for native DLLs in this order:
1. The directory containing the EXE
2. The system directory
3. The Windows directory
4. The current directory
5. Directories in the PATH environment variable

This is why the packaging script copies the native DLLs flat beside the EXE — P/Invoke finds them there without needing PATH modifications.

---

## Inno Setup Installer

`UniversalScanner\UniversalScanner.iss` creates a Windows installer:

- **Output:** `UniversalScanner Setup.exe` (LZMA2 compressed)
- **Install location:** `C:\Program Files (x86)\UniversalScanner\`
- **Start menu group:** UniversalScanner
- **Version:** Extracted from `UniversalScanner.exe` metadata at build time

The installer only packages `UniversalScanner.exe` — no additional DLLs are needed since managed assemblies are embedded and native SDK files are extracted at runtime from the EXE's resources.

To build the installer:
1. Install [Inno Setup](https://jrsoftware.org/isinfo.php)
2. Open `UniversalScanner.iss` in Inno Setup Compiler
3. Compile (Ctrl+F9)

---

## Verification Checklist

After building a release, verify:

1. **EXE exists:** `UniversalScanner\bin\x86\Release\UniversalScanner.exe`
2. **No loose managed DLLs:** Only `.selftest` files and native content should be in the output folder — no `System.Web.Extensions.dll`, `Costura.dll`, etc. should be visible (they're embedded)
3. **File size:** The EXE should be several megabytes (includes embedded managed DLLs + SDK resources)
4. **Launch test:** Run the EXE from its folder — the splash screen should appear, SDK extraction should complete, and the main window should open
5. **Hikvision SDK extraction:** After launch, check `%TEMP%\UniversalScanner\` for the extracted SDK folder
6. **Cleanup:** Close the application — the temp folder should be cleaned up (or scheduled for deletion on reboot if files are locked)

---

## Known Limitations

1. **Costura default config:** No custom include/exclude filters. All `CopyLocal=true` managed references are embedded. If new NuGet packages are added, they will be auto-embedded unless explicitly excluded.
2. **Native SDK files duplicated in dist:** The packaging script copies native DLLs both flat at the root AND inside `SDK\Hikvision\`. This is intentional — flat for P/Invoke, SDK folder for RuntimeDependencyManager fallback.
3. **x86 only:** HCNetSDK.dll is x86-only. Building for AnyCPU on a 64-bit machine will produce a 64-bit process that cannot load the x86 native DLL. **Always use Release|x86 for production.**
4. **HCNetSDKCom dependency:** The HCNetSDKCom folder must be a direct subfolder beside HCNetSDK.dll. The Hikvision SDK loads COM components relative to HCNetSDK.dll's location.
5. **File locks on exit:** Some Hikvision SDK DLLs remain locked after the application exits. They are cleaned up on next reboot via `MoveFileEx` with `MOVEFILE_DELAY_UNTIL_REBOOT`.
6. **No strong-name signing:** The assembly is not signed. Some enterprise environments may block unsigned executables.
7. **.NET Framework 4.8:** Users must have .NET Framework 4.8 installed. The Inno Setup bootstrapper checks for .NET 4.5.2 but 4.8 is the actual requirement.

---

## Build Dependencies

| Dependency | Version | Required For |
|------------|---------|--------------|
| .NET Framework 4.8 SDK | 4.8 | Compiling |
| Visual Studio Build Tools 2022 | Any | MSBuild |
| Costura.Fody | 6.2.0 | Managed DLL embedding (NuGet auto-restore) |
| Fody | 6.9.3 | IL weaving engine (NuGet auto-restore) |
| Inno Setup | 6+ | Creating the Windows installer (optional) |

---

## Related Documentation

- [AI_CONTEXT.md](AI_CONTEXT.md) — Release build policy and key dependencies
- [GIT_WORKFLOW.md](GIT_WORKFLOW.md) — Git workflow for feature branches and commits
- [PROJECT_INDEX.md](PROJECT_INDEX.md) — Index of all project files
