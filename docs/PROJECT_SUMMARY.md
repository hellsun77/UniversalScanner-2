# Universal Scanner Project Summary

## Overview
Universal Scanner is a multi-brand ultra-fast network discovery tool based on multicast and broadcast discovery. It implements a flexible framework to support various discovery protocols including SSDP/UPnP, WS-Discovery (ONVIF), DNS-SD (Zeroconf), GigE Vision, and numerous proprietary vendor protocols. The application is a Windows Forms application targeting .NET Framework 4.8.

## Overall Architecture
The application follows a modular plugin architecture where each protocol scanner implements a common `ScanEngine` base class. The main form (`ScannerWindow`) implements the `ScannerViewer` interface to receive device discovery events. The application uses a multi-threaded design with separate threads for listening and sending for each protocol. Device information is aggregated and displayed in a DataGridView with vendor-specific post-processing for MAC address lookup, vendor identification, activation status, and ONVIF profile discovery.

## Folder Structure
```
UniversalScanner/
├── UniversalScanner.csproj          # Main Windows Forms application project
├── Program.cs                       # Application entry point with single-instance checking
├── UniversalScanner.cs              # Main form (ScannerWindow) implementing ScannerViewer
├── ScanEngine.cs                    # Abstract base class for all scan engines
├── VendorLookup.cs                  # MAC address vendor lookup using OUI database
├── Config.cs                        # Application configuration (registry-based)
├── Logger.cs                        # Logging and packet capture utilities
├── NetworkUtils.cs                  # Network utility functions (endian conversion, IP utilities)
├── NetworkTools.cs                  # Network diagnostics (ping, port scanning, ARP, etc.)
├── Resources/                       # Embedded resources (OUI database, icons)
├── Properties/                      # Assembly info, resources, settings
├── selftest2pcap/                   # Self-test utilities
├── DeviceSDK/                       # Device-specific SDK implementations
│   ├── Common/                      # Shared interfaces and base classes
│   │   ├── IDeviceProvider.cs       # Interface for vendor-specific operations
│   │   ├── DeviceInfo.cs            # Standardized device information container
│   │   ├── DeviceActivationStatus.cs# Enumeration for activation states
│   │   ├── ProviderManager.cs       # Manages available providers
│   │   ├── OnvifProfileProbe.cs     # ONVIF profile discovery utilities
│   │   └── GenericOnvifNetwork.cs   # ONVIF-based network configuration implementation
│   ├── Dahua/                       # Dahua device implementations
│   ├── Hikvision/                   # Hikvision device implementations (uses HCNetSDK)
│   ├── Honeywell/                   # Honeywell device implementations (ONVIF-based)
│   ├── Sunell/                      # Sunell device implementations
│   ├── Tiandy/                      # Tiandy device implementations
│   └── Uniview/                     # Uniview device implementations
```

## Major Classes and Their Responsibilities

### Core Scanning Engine (`ScanEngine.cs`)
- **Abstract Base Class**: Defines the contract for all protocol scanners
- **Key Responsibilities**:
  - Network Listening Infrastructure: Methods for unicast (`listenUdpGlobal`), multicast (`listenMulticast`), and interface-specific (`listenUdpInterfaces`) listeners
  - Sending Infrastructure: Unified sending methods (`send`, `sendBroadcast`, `sendMulticast`, `sendUnicast`)
  - Device Reporting: `viewer.deviceFound()` callback to report discovered devices to the UI
  - Utility Methods: Network interface detection, IP address listing, subnet scanning, self-test functionality
  - Threading: Manages listener and sender threads with proper cleanup via `Dispose()`
  - Abstract Methods: `name`, `color`, `getUsedPort()`, `listen()`, `scan()`, `sender(IPEndPoint)`, `receiver(IPEndPoint, byte[])`

### Scanner Protocol Implementations
Each protocol scanner inherits from `ScanEngine` and implements:
- **SSDP (SSDP.cs)**: UPnP discovery via M-SEARCH on UDP 1900, multicast 239.255.255.250:1900
- **WS-Discovery (Wsdiscovery.cs)**: ONVIF discovery via SOAP Probe on UDP 3702, multicast 239.255.255.250:3702
- **Vendor-Specific Protocols**: 
  - Dahua1: Legacy UDP binary protocol (port 5050)
  - Dahua2: JSON-over-UDP protocol (port 37810, multicast 239.255.255.251:37810)
  - Hikvision: Proprietary UDP protocol (port 37020, multicast 239.255.255.250:37020)
  - HoneywellMdns: mDNS-based discovery
  - And others for various vendors (Axis, Bosch, Sony, etc.)

### Main Application Form (`UniversalScanner.cs`)
- **Implements**: `ScannerViewer` interface to receive device discoveries
- **Key Responsibilities**:
  - Maintains a `DataTable` of discovered devices displayed in a `DataGridView`
  - Handles UI events (scan button, stop button, context menu)
  - Performs post-discovery device interrogation:
    - MAC address resolution via ARP (`NetworkTools.GetMacAsync`)
    - Vendor lookup via OUI (`VendorLookup.GetVendor`)
    - ONVIF profile lookup (`OnvifProfileProbe.GetProfilesAsync`)
    - Vendor-specific activation status checks (Hikvision, Honeywell, Dahua, etc.)
    - Ping monitoring for device reachability
  - Manages device deduplication and IP change tracking
  - Provides batch operations (IP address changes, activation, reboot)
  - System tray integration and context menu for configuration

### Configuration (`Config.cs`)
- **Storage**: Windows Registry under `HKEY_CURRENT_USER\Software\UniversalScanner`
- **Settings**:
  - `EnableIPv6`, `EnableIPv4`: IP version support
  - `ForceLinkLocal`, `ForceZeroConf`: Show link-local/ZeroConf addresses
  - `ForceGenericProtocols`: Show all protocols found on each device
  - `DebugMode`: Enable verbose logging and PCAP capture
  - `PortSharing`: Allow port sharing between applications
  - `OnvifVerbatim`: Use verbatim ONVIF SOAP payload (for debugging)
  - `DahuaNetScan`: Enable aggressive Dahua subnet scanning

### Logging (`Logger.cs`)
- **Singleton Pattern**: Thread-safe logger with file and console output
- **Features**:
  - Multiple debug levels (Fatal, Error, Warn, Info, Debug)
  - Network packet logging to PCAP files when in debug mode
  - Thread-aware logging with timestamps
  - Integration with `System.Diagnostics.Trace`

### Network Utilities (`NetworkUtils.cs`)
- **Endian Conversion**: Big/little endian conversion utilities
- **IP Helpers**: Private IP address checking, network byte order conversions

### Network Tools (`NetworkTools.cs`)
- **Diagnostics**: 
  - Ping (`PingAsync`, `IsOnlineAsync`)
  - Port scanning (`IsTcpPortOpenAsync`, `IsDeviceReachableAsync`)
  - ARP resolution (`GetMacAsync`, `ForceArp`, `FlushArpEntry`)
  - Subnet checking (`IsIpOnAnyLocalSubnet`)
  - Hostname resolution (`GetHostNameAsync`)
- **ARP Cache Management**: Ability to flush ARP entries after IP changes

### Vendor Lookup (`VendorLookup.cs`)
- **OUI Database**: Embedded CSV resource (`Resources\oui.csv`)
- **Functionality**: 
  - Case-insensitive MAC address to vendor mapping
  - Delimiter normalization (handles :, -, .)
  - Lazy loading of the OUI database from embedded resource

### Device SDK (`DeviceSDK/`)
- **Interface**: `IDeviceProvider` defines contract for vendor-specific operations
  - `VendorName`: Identifier for the vendor
  - `SupportsVendor(string)`: Checks if provider handles a vendor
  - `GetDeviceInfo(string ip)`: Retrieves basic device info
  - `CanInitialize(DeviceInfo)`: Checks if device can be initialized
  - `InitializeDevice(DeviceInfo, username, newPassword, out message)`: Activates/configures device
- **Provider Manager**: Static class that maintains a list of providers and returns appropriate provider for a vendor
- **Vendor Implementations**: 
  - Hikvision: Uses HCNetSDK.dll via P/Invoke for activation, IP config, reboot
  - Honeywell: Pure ONVIF SOAP implementation (no vendor DLL required)
  - Dahua: Proprietary protocol implementation
  - Others: Similar patterns with vendor-specific SDKs or protocols

## Scanning Workflow
1. **Application Start** (`Program.cs`):
   - Checks for existing instances (single-instance application)
   - Shows splash screen
   - Initializes main form (`ScannerWindow`)
   - Creates instances of all enabled scan engines (SSDP, WS-Discovery, vendor protocols)
   - Registers each engine with the main form as the `ScannerViewer`
   - Reserves UDP ports via `PortProvider`
   - Starts listeners for all engines
   - Closes splash screen and starts main application loop

2. **Scan Initiation** (User clicks Scan button or timer triggers):
   - Clears device list (preserving recently changed IPs to avoid flicker)
   - Calls `scanEvent` (triggered by `ScannerWindow.scanButton_Click`)
   - Each scan engine's `scan()` method is invoked (via the event)
   - Engines send discovery probes (multicast + broadcast) multiple times for reliability

3. **Listening and Response Processing**:
   - Each engine runs listener threads (unicast, multicast, interface-specific)
   - When data is received, `receiver(IPEndPoint, byte[])` is called
   - Parses protocol-specific response to extract:
     - IP address (from source endpoint)
     - Device type (from protocol-specific headers/payload)
     - Unique identifier (UUID, serial number, etc.)
   - Calls `viewer.deviceFound(protocol, version, ip, deviceType, deviceUUID)` on the main form

4. **Device Processing in UI** (`ScannerWindow.deviceFound()` and `addDevice()`):
   - Checks IPv4/IPv6 enablement settings
   - Checks for duplicate devices (by IP, MAC, or UUID)
   - For new devices:
     - Adds row to DataTable with basic info (protocol, IP, type, UUID)
     - Sets placeholder values (Pinging..., Unknown activation, etc.)
     - Queues asynchronous MAC address lookup
     - Queues asynchronous ONVIF profile lookup
     - Starts ping check
   - For existing devices:
     - Updates IP address if changed (handles IP renumbering)
     - Preserves existing vendor/type info if more specific than new discovery
     - Updates version if higher
     - Refreshes MAC lookup, ONVIF lookup, ping, and activation status

5. **Post-Processing (Asynchronous Operations)**:
   - **MAC Address Resolution**: Uses ARP to get MAC address, then OUI lookup for vendor
     - Updates MAC and OUI Vendor columns
     - Applies vendor-specific identity fixes (e.g., if MAC indicates Honeywell but device was discovered as generic WS-Discovery)
   - **ONVIF Profile Query**: Queries device for ONVIF profiles via SOAP
     - Updates ONVIF Profile column
   - **Ping Monitoring**: Sends ICMP echo request
     - Updates Ping column with response time or status
   - **Activation Status Check**:
     - For known vendors (Hikvision, Honeywell, Dahua, etc.), queries device for activation state
     - Updates Activation column (Active, Inactive, Unknown, N/A)
   - **UI Updates**: All asynchronous operations use `BeginInvoke` to update DataGridView on UI thread

## Device Model
The application tracks the following information for each discovered device:
- **Vendor**: Manufacturer name (from protocol response or OUI lookup)
- **Protocol**: Discovery protocol that found the device (SSDP, WS-Discovery, Dahua1, etc.)
- **Version**: Protocol version (typically 0 for most protocols)
- **IP Address**: Current IPv4/IPv6 address
- **Ping**: Response time or status (e.g., "15 ms", "No reply")
- **Activation**: Device activation state (Active, Inactive, Unknown, N/A)
- **MAC Address**: Hardware address (if resolvable via ARP)
- **OUI Vendor**: Vendor from MAC address lookup
- **Type**: Device type/model (from protocol response or model query)
- **ONVIF Profile**: Supported ONVIF profiles (if applicable)
- **Unique ID**: Device identifier (UUID, serial number, MAC address)

## NetworkTools Architecture
`NetworkTools` is a static class providing network diagnostics and utilities:
- **Ping Operations**: `PingAsync`, `IsOnlineAsync`, `IsDeviceReachableAsync` (checks common ports)
- **Port Scanning**: `IsTcpPortOpenAsync` for checking TCP port availability
- **ARP Resolution**: 
  - `GetMacAsync`: Repeatedly attempts ARP resolution with reachability checks
  - `ForceArp`: Direct ARP request via `SendARP` P/Invoke
  - `FlushArpEntry`: Removes ARP cache entry via `DeleteIpNetEntry` P/Invoke
- **Subnet Utilities**: 
  - `IsIpOnAnyLocalSubnet`: Checks if IP is on any local network interface
  - `GetReachabilityWarning`: Provides user-friendly message if target IP is not on local subnet
- **Additional**: Hostname resolution via DNS

## UI Architecture
### Main Form (`ScannerWindow`)
- **Layout**: 
  - Top: Scan button, Stop Subnet button, subnet text box
  - Center: DataGridView showing discovered devices with columns for Vendor, Protocol, Version, IP, Ping, Activation, MAC, OUI Vendor, Type, ONVIF Profile, Unique ID
  - Bottom: Status strip with status label
  - Menu Bar: File (Export/Import, Exit), View (IPv6, Exhaustive Protocols, Show Unconfigured), Tools (Ping, Change IP, Initialize Device, Batch Operations), Help
  - System Tray: Minimizes to tray with restore/exit options
- **Key Features**:
  - Double-click on device row opens device IP in default browser
  - Context menu for selected device: Copy IP/MAC/Vendor/Info, Initialize Device, Change IP, Ping
  - Color-coded rows by protocol (configured via `formatProtocol`)
  - Automatic sorting by IP address when clicking column header
  - Export/Import of scan results to/from `.usscan` file
  - Device initialization and IP change wizards for supported vendors
  - Batch operations for multiple devices

### Supporting Forms
- **SplashForm**: Shows startup progress
- **DeviceInitializationForm**: Guides user through device activation process
- **ChangeIpForm**: Assists with changing device IP address
- **BatchChangeIpForm**: Applies sequential IP addresses to multiple selected devices
- **PingMonitorForm**: Continuous ping monitoring with graph
- **CustomPingDialog**: Configures ping parameters (count, timeout, interval)

## Supported Protocols
| Protocol | Port | Multicast Address | Description |
|----------|------|-------------------|-------------|
| SSDP/UPnP | 1900 UDP | 239.255.255.250:1900 | Universal Plug and Play discovery |
| WS-Discovery/ONVIF | 3702 UDP | 239.255.255.250:3702 | Web Services Discovery for ONVIF devices |
| mDNS (Honeywell) | 5353 UDP | 224.0.0.251:5353 | Multicast DNS (used by Honeywell) |
| Dahua1 | 5050 UDP | None | Legacy Dahua binary protocol |
| Dahua2 | 37810 UDP | 239.255.255.251:37810 | Dahua JSON-over-UDP protocol |
| Hikvision | 37020 UDP | 239.255.255.250:37020 | Hikvision proprietary protocol |
| GigE Vision | Various | Various | Industrial camera standard |
| ONVIF (direct) | 80/8000 TCP | None | Direct ONVIF device access |
| Vendor-specific | Various | Various | Proprietary protocols for Axis, Bosch, Sony, etc. |

## Supported Vendors
The device discovery includes protocol-level detection for many vendors, with specialized SDK support for:
- **Hikvision**: Requires HCNetSDK.dll for advanced operations (activation, IP config, reboot)
- **Honeywell**: ONVIF-based; 30/35 series use Hikvision SDK (OEM), older series use ISAPI
- **Dahua**: Proprietary protocol for discovery and activation
- **Uniview**: Similar to Dahua/Honeywell patterns
- **Sunell**: Proprietary protocol
- **Tiandy**: Proprietary protocol
- **Others**: Axis, Bosch, Sony, Samsung, Panasonic, Arecont, Vivotek, Ubiquiti, etc. detected via protocol responses

## Activation Checking
For devices requiring initialization (password setup, etc.):
- **Hikvision**: Uses HCNetSDK.dll P/Invoke calls to `NET_DVR_Login_V40`, `NET_DVR_SetDVRConfig`, `NET_DVR_RebootDVR`
- **Honeywell 30/35 Series**: Uses Hikvision SDK (OEM firmware)
- **Honeywell Equip Series**: No activation needed (default admin/1234)
- **Dahua/Uniview**: Proprietary activation protocols via UDP/TCP
- **Sunell/Tiandy**: Vendor-specific activation methods
- **Process**: 
  1. After device discovery, MAC address is resolved
  2. Vendor-specific activation status is queried
  3. If inactive, user can initiate activation via context menu
  4. Activation flow: collect credentials, send vendor-specific commands, verify success
  5. On success, update device status to "Active" and optionally update model/UUID

## Ping Monitoring
- **Implementation**: Uses `System.Net.NetworkInformation.Ping` class
- **Integration**:
  - Initial ping check during device discovery (`UpdatePing` method)
  - Continuous monitoring available via right-click → Ping Selected Device
  - Custom ping dialog allows configuring count, timeout, interval
  - Results displayed in Ping column (e.g., "42 ms", "No reply", "Failed")
  - ARP resolution is performed before ping to ensure MAC is known for wake-on-LAN scenarios

## Threading Model
- **Per-Engine Threads**: Each `ScanEngine` instance manages:
  - One listener thread for unicast (`unicastReciever`)
  - One listener thread for multicast (`multicastReciever`) if applicable
  - One listener thread per network interface (`interfacesListerner`)
  - Optional sender thread for subnet scanning (`sendNetScannerGlobal` or `sendNetScannerInterfaces`)
- **Thread Safety**:
  - Shared data structures (device list) accessed via `InvokeRequired`/`BeginInvoke` for UI updates
  - Listener threads use `closing` flag for graceful shutdown
  - Sender threads can be aborted during subnet scanning
  - All threads marked as `IsBackground = true` to allow application exit
- **Resource Management**: 
  - `Dispose()` method aborts all threads and closes sockets
  - Finalizer ensures cleanup if `Dispose()` not called
  - `PortProvider` coordinates UDP port allocation to prevent conflicts

## Event/Callback Flow
1. **Scan Trigger**: User action → `scanButton_Click` → `scanEvent.Invoke()`
2. **Engine Scan**: Each `ScanEngine.scan()` → sends probes (multicast/broadcast)
3. **Response Reception**: Listener threads → `receiver(IPEndPoint, byte[])` → parses response
4. **Device Found**: `receiver` → `viewer.deviceFound(protocol, version, ip, deviceType, deviceUUID)`
5. **UI Updates** (`ScannerWindow.deviceFound()`):
   - Marshals to UI thread if needed (`BeginInvoke`)
   - Checks for duplicates (`FindDeviceRow`)
   - Adds/updates row in `DataTable`
   - Queues asynchronous operations:
     - MAC lookup: `QueueMacLookup(ip, uuid)` → `NetworkTools.GetMacAsync` → `ApplyMacLookupResult`
     - ONVIF lookup: `QueueOnvifProfileLookup(ip, uuid)` → `OnvifProfileProbe.GetProfilesAsync` → `ApplyOnvifProfileLookupResult`
     - Ping update: `UpdatePing(ip)` → `NetworkTools.SendPingAsync`
     - Activation check: `UpdateActivationStatus(ip)` → vendor-specific status probe
6. **Async Completion**: Each asynchronous operation completes → UI update via `BeginInvoke` → updates relevant DataGridView cells

## Utility Classes
- **PortProvider**: Singleton managing UDP port allocation to prevent conflicts between scan engines
- **VersionManager**: Retrieves application version and build date from assembly metadata
- **RegistryUtils**: Helper for reading/writing registry keys (used by Config)
- **MemoryUtils**: Memory-related utilities (not heavily used)
- **JulienBlitte.Namespace**: Contains shared utilities used across the codebase:
  - `NetworkUtils`: Endian conversion, IP helpers
  - `Logger`: Logging and PCAP capture
  - `PcapFile`: Packet capture implementation (used by Logger in debug mode)

## External SDKs and Dependencies
- **HCNetSDK.dll**: Hikvision SDK (required for advanced Hikvision features)
  - Used via P/Invoke in `DeviceSDK/Hikvision/` directory
  - Functions: Login, configuration, reboot, information retrieval
- **ONVIF**: Implemented via SOAP/XML parsing (no external DLL required for basic discovery)
- **OUI Database**: IEEE Standards Association MAC address vendor list (embedded as `oui.csv`)
- **Windows APIs**: 
  - `iphlpapi.dll`: ARP functions (`SendARP`, `DeleteIpNetEntry`)
  - `user32.dll`: Window management (single-instance handling)
- **.NET Framework 4.8**: Core framework for Windows Forms application

## Recent Architectural Improvements
Based on code review, recent improvements include:
- **Enhanced Device Deduplication**: Improved logic in `FindDeviceRow` and `RemoveDuplicateRowsForIpChange` to handle IP changes without creating duplicates
- **IP Change Guarding**: Mechanisms (`suppressedOldIpAddresses`, `recentChangedIpAddresses`) to prevent stale results after IP changes
- **Asynchronous Post-Processing**: Separation of device discovery from expensive operations (MAC lookup, ONVIF queries, activation checks) to keep scanning responsive
- **Vendor-Specific Identity Application**: Logic to override generic discovery results with more specific vendor information when MAC address or other indicators provide better data
- **Improved Error Handling**: Extensive try/catch blocks throughout to prevent crashes from network issues or device timeouts
- **Thread Management**: Better thread lifecycle control with `closing` flags and proper `Abort()` usage
- **Configuration Management**: Registry-based settings with runtime updates via `updateBool` method

## Important Implementation Details for Python Conversion
When converting to Python, preserve these key aspects:

1. **Threading Model**:
   - Use `threading.Thread` for listener/sender components
   - Implement proper thread termination flags (equivalent to `closing`)
   - Use `queue.Queue` or similar for thread-safe communication if needed
   - Consider using `concurrent.futures` for asynchronous operations (MAC lookups, pings)

2. **Network Operations**:
   - UDP socket options: `SO_REUSEADDR`, `IP_ADD_MEMBERSHIP` for multicast
   - Broadcast socket option: `SO_BROADCAST`
   - Raw socket access may require administrator/root privileges for some operations
   - ARP resolution: Platform-specific (use `scapy`, `netifaces`, or system calls)

3. **Protocol Implementation**:
   - SSDP: M-SEARCH request parsing of SERVER and USN headers
   - WS-Discovery: SOAP 1.2 with WS-Addressing headers; XML parsing with namespaces
   - Vendor protocols: Study existing C# implementations for packet formats

4. **Device Deduplication**:
   - Track devices by IP, MAC, and UUID
   - Implement IP change grace period to avoid duplicate entries during renumbering
   - Prefer UUID/MAC over IP for device identity

5. **UI Updates**:
   - Use thread-safe mechanisms to update GUI from worker threads
   - In PySide6: Use `QThread` with signals/slots or `QMetaObject.invokeMethod`
   - Update table model (e.g., `QStandardItemModel`) rather than direct widget access from threads

6. **Configuration**:
   - Replace Windows Registry with cross-platform solution (JSON file in user config directory)
   - Maintain same settings: IPv4/IPv6, link-local, generic protocols, debug mode, etc.

7. **Logging**:
   - Implement similar logging levels (DEBUG, INFO, WARNING, ERROR)
   - Consider file rotation and optional PCAP-like packet capture
   - Include thread/context information in log messages

8. **Device SDK Equivalent**:
   - Create abstract base class for vendor providers
   - Implement vendor-specific classes for authentication, configuration, reboot
   - Use factory pattern to load appropriate provider based on vendor detection

9. **Performance Considerations**:
   - Batch asynchronous operations (e.g., queue multiple MAC lookups)
   - Implement timeouts and retry mechanisms for network operations
   - Use connection pooling where applicable (e.g., HTTP sessions for ONVIF)

10. **Cross-Platform Adaptations**:
    - Replace Windows-specific P/Invoke calls with platform-appropriate implementations
    - For ARP: Use platform-specific libraries or parse output of system commands (`arp -a`, `ip neigh`)
    - For registry: Use JSON configuration file in platform-appropriate config directory
    - For single-instance implementation: Use platform-specific mechanisms (lock files, dbus, etc.)

This summary captures the essential architecture, components, and workflows of the Universal Scanner application to guide a faithful Python reproduction that maintains functional equivalence while adapting to Python idioms and cross-platform requirements.