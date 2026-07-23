# Example: Send One Command and Read All Responses

Full end-to-end script — discovers device, connects, sends one command, reads all responses, cleans up.
Tested against ROG RAIKIRI II PRO PC (`12 00` → 64-byte response in 1.64ms).

## When to use

Use this as the base template whenever a developer wants to send a single protocol command and capture the device's response. Adapt `TARGET_DEVICE`, `CMD`, and `USAGE_PAGE` for the specific device and command.

## The Script

```python
import json
import time
import urllib.parse
import urllib.request
from typing import Any

BASE_URL = "http://127.0.0.1:2345"
LISTENER = "default"

# ── Change these for each task ─────────────────────────────────────────────
TARGET_DEVICE = "raikiri"      # fuzzy match against productName (case-insensitive)
CMD           = "12 00"        # hex bytes, no 0x prefix, space-separated
USAGE_PAGE    = 65283          # 0xFF03 — standard ASUS command channel
# ──────────────────────────────────────────────────────────────────────────

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
        raise RuntimeError(f"{method} {url} -> HTTP {e.code}: {raw}") from e
    except urllib.error.URLError as e:
        raise RuntimeError(f"{method} {url} -> {e}") from e
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

# ── 1. Select BusHound tab ────────────────────────────────────────────────
request("POST", "/api/v1/selecttabbyname", {"name": "Bus Hound"})

# ── 2. Discover and fuzzy-match device ───────────────────────────────────
devices = get_data(request("GET", "/base/services/deviceselection/listdiscovereddevices"))
device = next(
    (d for d in (devices or [])
     if TARGET_DEVICE.lower() in str(d.get("productName", "")).lower()),
    None
)
if not device:
    print(f"ERROR: no device matching '{TARGET_DEVICE}' found")
    print("Available:", [d.get("productName") for d in (devices or [])])
    raise SystemExit(1)

pid = device["PID"]
cmd_iface = next((i for i in device.get("interfaces", []) if i.get("UsagePage") == USAGE_PAGE), None)
if not cmd_iface:
    print(f"ERROR: UsagePage={USAGE_PAGE} not found on '{device['productName']}'")
    print("Available interfaces:")
    for iface in device.get("interfaces", []):
        print(f"  UsagePage={iface.get('UsagePage')}  Usage={iface.get('Usage')}  Product={iface.get('Product', '?')}")
    print("Update USAGE_PAGE at the top of this script and retry.")
    raise SystemExit(1)
usage_page = cmd_iface["UsagePage"]
usage      = cmd_iface["Usage"]
print(f"Found: {device['productName']}  PID={pid}  usagePage={usage_page}  usage={usage}")

# ── 3. Connect ────────────────────────────────────────────────────────────
connected = get_data(request("GET", "/base/services/deviceselection/connect/pid", {"pid": pid}))
if not connected:
    print("ERROR: connect returned False — check PID or re-run discovery")
    raise SystemExit(1)
print("Connected.")

try:
    # ── 4. Register listener + flush stale packets ────────────────────────
    request("POST", "/base/ui/pages/asusbushoundpage/registernewlistener",
            {"name": LISTENER, "bufferSize": 16384, "bucketSize": 64})
    get_data(request("GET", "/base/ui/pages/asusbushoundpage/readall", {"name": LISTENER}))
    print("Listener ready, buffer flushed.")

    # ── 5. Send command ───────────────────────────────────────────────────
    print(f"Sending: {CMD}")
    request("POST", "/base/ui/pages/asusbushoundpage/sendcmd",
            {"usage": usage, "usagePage": usage_page, "cmdText": CMD})

    # ── 6. Poll for responses (2 second window) ───────────────────────────
    deadline = time.monotonic() + 2.0
    in_packets = []
    while time.monotonic() < deadline:
        pkt = get_data(request("GET", "/base/ui/pages/asusbushoundpage/readone", {"name": LISTENER}))
        if pkt:
            bytes_, phase, delta, hex_str = parse_packet(pkt)
            if phase == "IN":
                in_packets.append((bytes_, hex_str, delta))
        else:
            time.sleep(0.05)

    # ── 7. Drain any remaining packets ───────────────────────────────────
    # Response may arrive just after the poll window — always drain after.
    remaining = get_data(request("GET", "/base/ui/pages/asusbushoundpage/readall", {"name": LISTENER}))
    for pkt in (remaining or []):
        bytes_, phase, delta, hex_str = parse_packet(pkt)
        if phase == "IN":
            in_packets.append((bytes_, hex_str, delta))

    # ── 8. Print results ──────────────────────────────────────────────────
    if in_packets:
        b, h, d = in_packets[0]
        print(f"\nResponse ({len(b)} bytes, Delta={d}):")
        print(f"  {h}")
        print(f"  {[hex(x) for x in b]}")
        if len(in_packets) > 1:
            print(f"  (+{len(in_packets) - 1} more packets)")
    else:
        print("No IN response received within 2 seconds.")

finally:
    # ── 9. Cleanup ────────────────────────────────────────────────────────
    try:
        request("POST", "/base/ui/pages/asusbushoundpage/unregisterlistener", {"name": LISTENER})
        print("\nListener unregistered.")
    except Exception as e:
        print(f"Warning: cleanup failed: {e}")
```

## How to run via MCP

```
mcp__moderntools__python_run   { "code": "<paste script here>" }
mcp__moderntools__python_read  {}
```

Output is in the `output` field of the `python_read` response.

## Expected output (ROG RAIKIRI II PRO PC, cmd `12 00`)

```text
Found: ROG RAIKIRI II PRO PC  PID=7268  usagePage=65283  usage=1
Connected.
Listener ready, buffer flushed.
Sending: 12 00

1 IN response(s):
  [1] 64 bytes  Delta=1.64ms
       12 00 00 00  09 00 03 00  05 01 01 01  00 00 00 00 ...
       ['0x12', '0x0', '0x0', '0x0', '0x9', '0x0', '0x3', '0x0', ...]

Listener unregistered.
```
