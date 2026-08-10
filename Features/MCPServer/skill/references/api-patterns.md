# ModernToolset API Patterns Reference

## Live discovery (authoritative)

The table below is a fast-path cache. The running app is the source of truth: every route
publishes its own description and JSON Schema. When in doubt — a 404, an unfamiliar build, or a
body shape not spelled out here — read the live manifest instead of guessing:

```python
schema = get_data(request("GET", "/api/v1/schema"))     # verb, path, summary, description,
                                                         # inputSchema, outputSchema per route
routes = get_data(request("GET", "/api/v1/listroute"))   # quick human-readable list
```

`inputSchema` gives the exact params/body (names, types, defaults, required fields) each call
expects; `description` explains the data structure in prose. Prefer these over assumptions.

## Known Routes (verified)

| Method | Route | Key Params | Description |
| ------ | ----- | ---------- | ----------- |
| GET | `/api/v1/schema` | — | Structured manifest (verb, path, description, JSON Schemas) — the authoritative source; check when a route 404s |
| GET | `/api/v1/listroute` | — | Quick human-readable list of all routes |
| POST | `/api/v1/selecttabbyname` | `name` (e.g. `Bus Hound`) | Select UI tab by name — do this first |
| POST | `/api/v1/selecttabindex` | `index` | Select UI tab by index (fallback) |
| GET | `/base/services/deviceselection/listdiscovereddevices` | — | List connected devices with interfaces |
| GET | `/base/services/deviceselection/connect/pid` | `pid` (decimal int) | Connect to device by PID |
| POST | `/base/services/deviceselection/connect` | `vid`, `pid` | Connect by VID+PID |
| POST | `/base/services/pythonsandboxservice/run` | — | Run script asynchronously — use via `mcp__moderntoolset__python_run` |
| GET | `/base/services/pythonsandboxservice/read` | `name=default` | Read stdout — use via `mcp__moderntoolset__python_read` |
| GET | `/base/services/pythonsandboxservice/status` | `name=default` | Get session state — use via `mcp__moderntoolset__python_status` |
| POST | `/base/services/pythonsandboxservice/close` | `name=default` | Stop a session — use via `mcp__moderntoolset__python_close` |
| POST | `/base/ui/pages/asusbushoundpage/registernewlistener` | `name`, `bufferSize`, `bucketSize` | Register packet listener |
| POST | `/base/ui/pages/asusbushoundpage/unregisterlistener` | `name` | Unregister listener (always cleanup) |
| POST | `/base/ui/pages/asusbushoundpage/sendcmd` | `usage` (int), `usagePage` (int), `cmdText` | Send HID command (no read) |
| POST | `/base/ui/pages/asusbushoundpage/writeandread` | `usage` (int), `usagePage` (int), `cmdText`, `timeoutMs` (default 1000) | Send + wait for IN response in one call. No listener needed. Returns `{Bytes, Length}`. HTTP 408 if no response. |
| GET | `/base/ui/pages/asusbushoundpage/readone` | `name` | Read one packet from listener buffer — use in a poll loop when waiting for a single response |
| GET | `/base/ui/pages/asusbushoundpage/readall` | `name` | Read all buffered packets at once — call **once** after a `time.sleep()` collection window, **not** in a loop |

## Session Naming

**Always use `name=default`.** Custom session names appear to be bugged and are ignored — the session always resolves to `"default"`.

## Device Discovery

Use `listdiscovereddevices` (NOT `getdevicelist` — that route does not exist):

```python
devices = get_data(request("GET", "/base/services/deviceselection/listdiscovereddevices"))
```

Each entry contains:

- `productName` — human-readable name (may be longer than what the developer types)
- `PID`, `VID` — decimal integers
- `ProductIdentifier` — e.g. `"0B05:1C64"`
- `interfaces` — list of HID interfaces, each with `UsagePage`, `Usage`, `Product`, etc.

**Always fuzzy-match device names** — the developer may abbreviate (e.g. "Rakiri" → "ROG RAIKIRI II PRO PC").

## Picking the Right UsagePage / Usage

Read from the device's interface list rather than assuming defaults:

```python
# ASUS command channel is typically UsagePage=65283 (0xFF03)
cmd_iface = next(
    (i for i in device.get("interfaces", []) if i.get("UsagePage") == 65283),
    None
)
usage_page = cmd_iface["UsagePage"] if cmd_iface else 65283
usage      = cmd_iface["Usage"]     if cmd_iface else 1
```

Common ASUS usagePage values seen in the wild:

- `65283` (0xFF03) — main command/response channel
- `65282` (0xFF02), `65280` (0xFF00), `65281` (0xFF01) — secondary channels
- `65474`, `65472`, `65473` — device-specific

## PID Format

`connect/pid` expects a **decimal** integer, not hex:

```python
pid_hex = "1C64"
pid_dec = int(pid_hex, 16)  # → 7268
```

## cmdText Format

Hex bytes as a string, **no `0x` prefix**, space-separated. `usage` and `usagePage` must be **integers**:

```python
# Correct
request("POST", "/base/ui/pages/asusbushoundpage/sendcmd", {
    "usage": 1,          # int
    "usagePage": 65283,  # int
    "cmdText": "12 00 FF A3"
})

# From byte array
byte_array = [0x12, 0x00, 0xFF, 0xA3]
cmdText = " ".join(f"{b:02X}" for b in byte_array)
```

## Packet Format (readone / readall)

Returns a **dict**, not a raw hex string:

```python
{'Bytes': '12 00 AB CD ...', 'Phase': 'IN', 'Delta': '308us'}
```

- `Phase: 'OUT'` = host → device (your command going out)
- `Phase: 'IN'`  = device → host (the response)
- `Delta` = time since previous packet — seconds-range = likely stale pre-registration packet

**Always use `parse_packet()`** — never call `.split()` directly on the packet:

```python
def parse_packet(packet):
    if isinstance(packet, dict):
        hex_str = packet.get("Bytes", "")
        phase   = packet.get("Phase", "?")
        delta   = packet.get("Delta", "?")
    else:
        hex_str = str(packet)
        phase, delta = "?", "?"
    bytes_ = [int(b, 16) for b in hex_str.split() if b.strip()]
    return bytes_, phase, delta, hex_str
```

## Stale Packets

Packets that arrived **before listener registration** may appear in the buffer. Always flush after registering:

```python
request("POST", "/base/ui/pages/asusbushoundpage/registernewlistener",
        {"name": LISTENER, "bufferSize": 16384, "bucketSize": 64})
# Flush stale packets
get_data(request("GET", "/base/ui/pages/asusbushoundpage/readall", {"name": LISTENER}))
```

When polling, filter to `Phase == "IN"` to skip echo-back OUT packets.

## Listener Buffer Sizes

| Parameter | Recommended | Notes |
| --------- | ---------- | ----- |
| `bufferSize` | `16384` | Total buffer in bytes |
| `bucketSize` | `64` | Max bytes per packet (HID max is typically 64) |

## Complete One-Shot Send-and-Receive Template

```python
import json, time, urllib.parse, urllib.request
from typing import Any

BASE_URL = "http://127.0.0.1:2345"
LISTENER = "default"

def request(method, path, params=None, body=None):
    url = f"{BASE_URL}{path}"
    if params:
        url += "?" + urllib.parse.urlencode(params)
    data = body.encode("utf-8") if body else None
    req = urllib.request.Request(url, data=data, method=method)
    if data:
        req.add_header("Content-Type", "text/plain")
    try:
        with urllib.request.urlopen(req, timeout=10) as r:
            raw = r.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"{method} {url} → HTTP {e.code}: {raw}") from e
    except urllib.error.URLError as e:
        raise RuntimeError(f"{method} {url} → {e}") from e
    if not raw:
        return None
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return raw

def get_data(r):
    return r["data"] if isinstance(r, dict) and "data" in r else r

def parse_packet(packet):
    if isinstance(packet, dict):
        hex_str = packet.get("Bytes", "")
        phase   = packet.get("Phase", "?")
        delta   = packet.get("Delta", "?")
    else:
        hex_str = str(packet)
        phase, delta = "?", "?"
    bytes_ = [int(b, 16) for b in hex_str.split() if b.strip()]
    return bytes_, phase, delta, hex_str

# ── 1. Select tab ──────────────────────────────────────────────────────────
request("POST", "/api/v1/selecttabbyname", {"name": "Bus Hound"})

# ── 2. Discover device ────────────────────────────────────────────────────
devices = get_data(request("GET", "/base/services/deviceselection/listdiscovereddevices"))
target_keyword = "raikiri"  # adjust per device
device = next(
    (d for d in (devices or [])
     if target_keyword in str(d.get("productName", "")).lower()),
    None
)
if not device:
    print(f"ERROR: no device matching '{target_keyword}' found")
    raise SystemExit(1)

pid = device["PID"]
cmd_iface = next((i for i in device.get("interfaces", []) if i.get("UsagePage") == 65283), None)
usage_page = cmd_iface["UsagePage"] if cmd_iface else 65283
usage      = cmd_iface["Usage"]     if cmd_iface else 1
print(f"Found: {device['productName']} PID={pid} usagePage={usage_page} usage={usage}")

# ── 3. Connect ────────────────────────────────────────────────────────────
connected = get_data(request("GET", "/base/services/deviceselection/connect/pid", {"pid": pid}))
if not connected:
    print("ERROR: connect returned False")
    raise SystemExit(1)
print("Connected.")

try:
    # ── 4. Register listener + flush stale packets ────────────────────────
    request("POST", "/base/ui/pages/asusbushoundpage/registernewlistener",
            {"name": LISTENER, "bufferSize": 16384, "bucketSize": 64})
    get_data(request("GET", "/base/ui/pages/asusbushoundpage/readall", {"name": LISTENER}))
    print("Listener ready, buffer flushed.")

    # ── 5. Send command ───────────────────────────────────────────────────
    cmd = "12 00"
    print(f"Sending: {cmd}")
    request("POST", "/base/ui/pages/asusbushoundpage/sendcmd",
            {"usage": usage, "usagePage": usage_page, "cmdText": cmd})

    # ── 6. Poll for IN response ───────────────────────────────────────────
    deadline = time.monotonic() + 3.0
    response = None
    while time.monotonic() < deadline:
        pkt = get_data(request("GET", "/base/ui/pages/asusbushoundpage/readone", {"name": LISTENER}))
        if pkt:
            bytes_, phase, delta, hex_str = parse_packet(pkt)
            if phase == "IN":
                response = (bytes_, hex_str, delta)
                break
        time.sleep(0.05)

    if response:
        bytes_, hex_str, delta = response
        print(f"Response ({len(bytes_)} bytes, Delta={delta}): {hex_str}")
        print(f"Parsed: {[hex(b) for b in bytes_]}")
    else:
        print("No IN response within 3 seconds.")

finally:
    # ── 7. Cleanup ────────────────────────────────────────────────────────
    try:
        request("POST", "/base/ui/pages/asusbushoundpage/unregisterlistener", {"name": LISTENER})
        print("Listener unregistered.")
    except Exception as e:
        print(f"Warning: cleanup failed: {e}")
```
