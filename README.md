# UniversalScanner 2

**Version: 2.6**

UniversalScanner 2 is a Windows Forms network discovery and device-management tool for IP cameras and other network devices. The core of this application is built from the original UniversalScanner project at https://github.com/julienblitte/UniversalScanner and targets .NET Framework 4.8.

The application discovers devices using multicast, broadcast, and vendor-specific protocols, then displays the collected information in a Windows desktop interface. It includes discovery support for generic protocols such as SSDP/UPnP, WS-Discovery/ONVIF, mDNS/DNS-SD, and GigE Vision, plus vendor integrations for Hikvision, Dahua, Axis, Bosch, Honeywell, Uniview, Hanwha, Sony, Panasonic, Vivotek, Foscam, Ubiquiti, Eaton, Lantronix, Microchip, MSSQL discovery, and others.

## Current Features

- Multi-protocol network scanning with a shared `ScanEngine` framework.
- Windows Forms UI for viewing discovered device IP, MAC, vendor, model, firmware, protocol, ONVIF profile, and status data.
- Device deduplication and vendor lookup using an embedded OUI database.
- ONVIF profile probing and ONVIF-based network operations where supported.
- Vendor-specific activation, readiness, and IP-change helpers for several camera families.
- Batch activation and batch IP-change workflows.
- Optional temporary-subnet workflows for reaching cameras outside the current adapter subnet.
- RTSP live-view helper forms and VLC-related viewing support.
- ONVIF live event monitoring and motion-detection configuration forms.
- Build-time embedding with Costura.Fody and runtime extraction of native SDK dependencies.

## Supported Brands and Integrations

Supported camera/security brands:

- 360Vision
- Advantech
- Arecont
- Axis
- Bosch
- Dahua
- Eaton
- FLIR
- Hanwha/Samsung
- Hikvision
- Honeywell
- Mobotix
- NiceVision
- Panasonic/i-PRO
- Siqura/TKH
- Sony
- Sunell
- Tiandy
- Ubiquiti
- Uniview/UNV
- Vivotek

Additional discovery and device integrations:

- SSDP/UPnP
- WS-Discovery/ONVIF
- mDNS/DNS-SD
- Google Cast
- GigE Vision
- Vstarcam
- Foscam
- Lantronix
- Microchip
- Eden/Optima
- CyberPower
- MSSQL discovery

Vendor-specific activation, readiness, IP-change, RTSP, or motion-detection features vary by brand. See `docs/VENDOR_MATRIX.md` for the detailed operation matrix.

## Project Structure

- `UniversalScanner.sln` - Visual Studio solution.
- `UniversalScanner/UniversalScanner.csproj` - main .NET Framework 4.8 WinForms application.
- `UniversalScanner/` - scanner engines, forms, device SDK integrations, resources, and embedded native SDK files.
- `UniversalScanner/DeviceSDK/` - vendor-specific activation, network, ONVIF, and motion-detection helpers.
- `UniversalScanner/Resources/oui.csv` - embedded OUI vendor lookup database.
- `selftest2pcap/` - utility project for converting scanner self-test data to packet captures.
- `scannerTemplate/` - template project for adding scanner implementations.
- `docs/` - architecture, build, workflow, and feature notes.
- `scripts/Package-Release.ps1` - release packaging helper.

## Development Requirements

- Windows.
- Visual Studio 2022 or Visual Studio Build Tools 2022.
- .NET Framework 4.8 developer/targeting pack.
- NuGet package restore enabled.
- x86 build support.

The main application is designed to build as `Release|x86`.

```powershell
nuget restore UniversalScanner.sln
msbuild UniversalScanner\UniversalScanner.csproj /t:Build /p:Configuration=Release /p:Platform=x86
```

If MSBuild is not on `PATH`, use the Visual Studio Build Tools path, for example:

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" UniversalScanner\UniversalScanner.csproj /t:Build /p:Configuration=Release /p:Platform=x86
```

The expected release executable is:

```text
UniversalScanner\bin\x86\Release\UniversalScanner.exe
```

## Notes Before Publishing

This repository intentionally excludes local build outputs, release bundles, packet captures, HAR files, logs, scratch folders, private assistant/tool settings, and machine-specific secrets. Do not commit live camera credentials, API keys, private certificates, registry exports, packet captures from customer networks, or local diagnostic archives.

## Contact

For fixes and troubleshooting, contact anan.awwad@gmail.com.

## License

The original Universal Scanner project is licensed under the GNU Lesser General Public License v3. See `LICENSE.txt`.
