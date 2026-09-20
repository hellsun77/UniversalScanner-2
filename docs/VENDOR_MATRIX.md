# Vendor Matrix

Complete routing tables for all vendor-specific operations across 7 supported vendors.

---

## Activation

| Vendor | Activator Class | Protocol | Port | Transport | Fallback | Notes |
|--------|----------------|----------|------|-----------|----------|-------|
| **Honeywell HC30/35** | `HoneywellActivator` | ISAPI/WEB_JsonAjaxAuth JSON RPC | 80/443 | HTTP/HTTPS | — | 3-step: getState → encrypt(AES+RSA) → createPassword. Error codes: -523 weak, -524 short, -508 already activated |
| **Honeywell Legacy** | `HoneywellLegacyActivator` | Dahua-style DevInit | varies | WinHTTP COM, raw TLS, TCP | — | HBW4PER1 models. Tries: 4 password-change methods → encrypted RPC. Country auto-detect (IL/PS/US) |
| **Hikvision** | `HikvisionActivator` | HCNetSDK P/Invoke | 8000 | Native SDK | — | Login → NET_DVR_ActivateDevice. Password 8-16 chars |
| **Uniview** | `UniviewActivator` | LAPI REST JSON | 80/443 | HTTP Basic auth | ONVIF SetUser SOAP | Tries factory passwords "123456", "admin". LAPI → ONVIF fallback |
| **Dahua** | `DahuaActivator` | RPC/DevInit/CGI | 80/443 | HTTP(S) RSA+AES | — | Chain: WebDevInit → RPC init → CGI addUser → verify login. Country auto-detect |
| **Sunell** | `SunellActivator` | XML WEB_JsonAjax | 80 | HTTP POST | — | Command 11011. RSA+AES via HoneywellRsaHelper. Pass max 20 chars, no Chinese/full-width |
| **Tiandy** | `TiandyActivator` | ISAPI/XML | 80 | HTTP DIGEST auth | — | DES-encrypted ActiveInfo PUT. Pass 8-15 chars, 2+ character classes, no special chars |
| **Generic/Unknown** | `HoneywellLegacyActivator` | DevInit (Dahua-style) | varies | — | — | Last-resort fallback |

### Activation Status Probing

| Vendor | Probe Class | Method | Cache |
|--------|------------|--------|-------|
| Honeywell | `HoneywellActivationStatusProbe` | Cache → getState JSON RPC → HTTP GET "/" | `HoneywellActivationCache` (file-based, %AppData%) |
| Hikvision | `HikvisionActivationStatusProbe` | 8 URLs: HTTPS root/ISAPI/deviceInfo + HTTP equivalents + /doc/page/login.asp | None |
| Uniview | `UniviewActivationStatusProbe` | LAPI endpoints with Basic auth (admin/123456) | TTL: Active=30min, Inactive=2min |
| Dahua | `DahuaActivationStatusProbe` | HTTP "/" → /cgi-bin/magicBox.cgi | `DahuaDiscoveryState` (in-memory by IP/MAC/serial) |
| Sunell | `SunellActivationStatusProbe` | XML command 30541 → ActivateFlag attribute | None |
| Tiandy | `TiandyActivationStatusProbe` | GET /CGI/System/ActivationStatus → <activated> element | None |

---

## IP Change

| Vendor | Module | Method | Protocol | DHCP | Notes |
|--------|--------|--------|----------|------|-------|
| **Hikvision** | `HikvisionNetwork` | Sync (blocking) | SADP SDK on port 8000 | No | GetConfig → modify → SetConfig → Reboot. Login V40/V30 |
| **Honeywell** | `HoneywellNetwork` | Async | ONVIF SOAP → legacy fallback | No (DHCP disabled) | GetNetworkInterfaces → SetNetworkInterfaces → SetNetworkDefaultGateway → SystemReboot |
| **Honeywell Legacy** | `HoneywellLegacyNetwork` | Sync (blocking) | WinHTTP COM + encrypted RPC | No | Login (MD5 challenge) → getEncryptInfo → encrypted getConfig/setConfig → Reboot |
| **Uniview** | `UniviewNetwork` | Async | LAPI REST → ONVIF SOAP | Yes | 3 JSON body variants. PowerShell temporary route for off-subnet cameras |
| **Dahua** | `DahuaNetwork` | Async | ONVIF → CGI fallback | Yes | CGI: setConfig with Network.eth0.* params |
| **Sunell** | `GenericOnvifNetwork` | Async | ONVIF SOAP | No | Standard ONVIF SetNetworkInterfaces |
| **Tiandy** | `GenericOnvifNetwork` | Async | ONVIF SOAP | No | Standard ONVIF SetNetworkInterfaces |
| **Generic ONVIF** | `GenericOnvifNetwork` | Async | ONVIF SOAP | No | Fallback for unrecognized vendors |

### IP Change Post-Refresh

After IP change, `RefreshChangedHikvisionIpRowInBackgroundAsync()` runs:
- 25 attempts with 20-second initial reboot wait, 4-second inter-attempt delay
- Each attempt: ping new IP → SubnetVendorProbe.DetectAsync() → MAC/OUI → vendor cascade → activation probe
- ARP cache flushed for old IP
- Honeywell legacy: old IP must become unreachable before row moves
- Falls back to "Refresh needed" or "Unreachable subnet" status

---

## RTSP Live View

| Vendor | Default RTSP Path |
|--------|-------------------|
| Dahua | `/cam/realmonitor?channel=1&subtype=0` |
| Honeywell HC30/35 | `/h264/ch1/main/av_stream` |
| Honeywell (other) | `/cam/realmonitor?channel=1&subtype=0` |
| Uniview | `/media/video1` |
| Tiandy | `/profile1` |
| Sunell | `/profile1` |
| Default (ONVIF standard) | `/Streaming/Channels/101` |

RTSP URL format: `rtsp://{ip}:554{path}`
Credentials passed via VLC CLI arguments (`--rtsp-user`, `--rtsp-pwd`), not embedded in URL.

---

## Vendor Detection Cascade

When a device is discovered via generic protocol (WS-Discovery, SSDP), vendor identity is determined by probing in order:

```
1. Honeywell → 2. Hikvision → 3. Uniview → 4. Dahua → 5. Sunell → 6. Tiandy
```

Each vendor's activation status probe is called. First to return "Active"/"Inactive" wins.
If all return "Unknown", the device remains as "Generic WS-Discovery" or "Generic SSDP".

### Vendor Type-Check Methods (ScannerWindow)

| Method | Detection Logic |
|--------|----------------|
| `IsHikvisionText/Row()` | Vendor string contains "Hikvision" |
| `IsHoneywellText/Row()` | Vendor string contains "Honeywell" |
| `IsUniviewText/Row()` | Vendor string contains "Uniview" |
| `IsDahuaText/Row()` | Vendor string contains "Dahua" |
| `IsSunellText/Row()` | Vendor string contains "Sunell" |
| `IsTiandyText/Row()` | Vendor string contains "Tiandy" |
| `IsGenericWsDiscoveryText()` | Vendor is generic WS-Discovery |
| `IsGenericSsdpText()` | Vendor is generic SSDP |

### Identity Preservation

`ShouldPreserveExistingIdentity()` prevents generic discovery results from overwriting a known vendor identity. If a device was previously identified as "Hikvision" via SADP, a later generic "ONVIF WS-Discovery" hit will not downgrade it.

---

## Batch Operations

### Batch Activation

Flow in `ScannerWindow.batchActivateDevicesToolStripMenuItem_Click()`:
1. Show `BatchActivationForm` — collect single password
2. Skip already-Active rows
3. Per row: detect vendor type → route to activator
4. Honeywell: probe HC30/35 endpoint first to decide path
5. On success: mark Honeywell activation cache, queue MAC lookup
6. Report success/failure summary

### Batch IP Change

Flow in `ScannerWindow.batchChangeIPAddressesToolStripMenuItem_Click()`:
1. Show `BatchChangeIpForm` — collect start IP, subnet, gateway, username, password
2. Iterate selected rows, assign incremented IPs
3. Route to vendor-specific network module
4. Call `RefreshChangedHikvisionIpRowAsync()` on success
5. Report success/failure summary

---

## Unique Vendor Characteristics

| Vendor | Auth Method | Transport Tricks | Notable |
|--------|-------------|-----------------|---------|
| Hikvision | Native SDK + HTTP Basic | SADP port 8000 | Only vendor using native P/Invoke SDK for activation |
| Honeywell HC30/35 | RSA+AES encrypted JSON RPC | ISAPI over HTTP/HTTPS | Activation cache needed because activated devices still expose activation endpoints |
| Honeywell Legacy | WinHTTP COM, raw TLS, MD5 challenge | Cookie containers, single TLS session reuse | Most complex activator. ~65+ private helper methods |
| Uniview | LAPI Basic auth (admin/123456) | PowerShell New-NetRoute for off-subnet | Temporary route auto-cleanup via IDisposable |
| Dahua | WebDevInit RSA+AES, RPC2_Login, CGI | Country auto-detection (IL/PS/US) | 4-chain protocol fallback |
| Sunell | XML WEB_JsonAjax with RSA+AES | Shares crypto from HoneywellRsaHelper | Max 20-char password |
| Tiandy | HTTP DIGEST auth, DES encryption | Raw TCP client (HTTP/1.0), pure managed DES | Full DES implementation in managed code (IP/FP, 16 Feistel rounds, S-boxes) |
