# Example: Enter Mode → Do Work → Exit Mode

Sequential flow for protocols that require the device to be in a specific mode.
Tested on ROG AZOTH 96 HE (PID 0x1C10, UsagePage 0xFF00).

## When to use

Use this when a protocol requires setup/teardown steps around the actual work — e.g. factory mode,
calibration mode, firmware update mode. The device must receive an "enter" command first, then the
work commands, then an "exit" command to return to normal state.

**Always exit the mode even if the work step fails** — use `try/finally` to guarantee it.

## The Script

```python
import json, time, urllib.parse, urllib.request

BASE_URL = "http://127.0.0.1:2345"
LISTENER = "default"

# ── Change these for each task ─────────────────────────────────────────────
TARGET_DEVICE = "azoth"        # fuzzy match against productName (case-insensitive)
USAGE_PAGE    = 65280          # 0xFF00 for ROG AZOTH 96 HE — read from device interfaces
USAGE         = 1

# Mode entry/exit commands
MODE_ENTER = "FA 00 D3 A5"    # factory_enter
MODE_EXIT  = "FA 00 00 00"    # factory_exit

# Work commands to run inside the mode (add as many steps as needed)
WORK_STEPS = [
    ("get device info", "12 00"),
    # ("next step",     "XX XX"),
]
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

def parse_packet(pkt):
    if isinstance(pkt, dict):
        hex_str = pkt.get("Bytes", "")
        phase   = pkt.get("Phase", "?")
        delta   = pkt.get("Delta", "?")
    else:
        hex_str = str(pkt)
        phase = delta = "?"
    bytes_ = [int(b, 16) for b in hex_str.split() if b.strip()]
    return bytes_, phase, delta, hex_str

def send_and_read(label, cmd, timeout=2.0):
    """Send a command, collect all IN responses within timeout, drain buffer after."""
    print(f"\n[{label}] Sending: {cmd}")
    request("POST", "/base/ui/pages/asusbushoundpage/sendcmd",
            {"usage": USAGE, "usagePage": USAGE_PAGE, "cmdText": cmd})
    deadline = time.monotonic() + timeout
    in_packets = []
    while time.monotonic() < deadline:
        pkt = get_data(request("GET", "/base/ui/pages/asusbushoundpage/readone", {"name": LISTENER}))
        if pkt:
            bytes_, phase, delta, hex_str = parse_packet(pkt)
            if phase == "IN":
                in_packets.append((bytes_, hex_str, delta))
        else:
            time.sleep(0.05)
    # Always drain — response may arrive just after the poll window
    for pkt in (get_data(request("GET", "/base/ui/pages/asusbushoundpage/readall", {"name": LISTENER})) or []):
        bytes_, phase, delta, hex_str = parse_packet(pkt)
        if phase == "IN":
            in_packets.append((bytes_, hex_str, delta))
    if in_packets:
        b, h, d = in_packets[0]
        print(f"  IN {len(b)}b Delta={d}: {h}")
        print(f"     {[hex(x) for x in b]}")
        if len(in_packets) > 1:
            print(f"  (+{len(in_packets) - 1} more packets)")
    else:
        print("  No IN response.")
    return in_packets

# ── Setup ─────────────────────────────────────────────────────────────────
request("POST", "/api/v1/selecttabbyname", {"name": "Bus Hound"})

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
USAGE_PAGE = cmd_iface["UsagePage"]
USAGE      = cmd_iface["Usage"]
print(f"Found: {device['productName']}  PID={pid}  usagePage={USAGE_PAGE}  usage={USAGE}")

connected = get_data(request("GET", "/base/services/deviceselection/connect/pid", {"pid": pid}))
if not connected:
    print("ERROR: connect returned False")
    raise SystemExit(1)
print("Connected.")

try:
    request("POST", "/base/ui/pages/asusbushoundpage/registernewlistener",
            {"name": LISTENER, "bufferSize": 16384, "bucketSize": 64})
    get_data(request("GET", "/base/ui/pages/asusbushoundpage/readall", {"name": LISTENER}))
    print("Listener ready, buffer flushed.")

    # ── Enter mode ────────────────────────────────────────────────────────
    send_and_read("mode_enter", MODE_ENTER)

    try:
        # ── Work steps ────────────────────────────────────────────────────
        for label, cmd in WORK_STEPS:
            send_and_read(label, cmd)

    finally:
        # ── Exit mode — always runs even if work fails ────────────────────
        send_and_read("mode_exit", MODE_EXIT)

finally:
    try:
        request("POST", "/base/ui/pages/asusbushoundpage/unregisterlistener", {"name": LISTENER})
        print("\nListener unregistered.")
    except Exception as e:
        print(f"Warning: cleanup failed: {e}")
```

## Key pattern: nested try/finally

The mode exit is wrapped in its own `try/finally` inside the outer cleanup block:

```python
send_and_read("mode_enter", MODE_ENTER)
try:
    # work steps
finally:
    send_and_read("mode_exit", MODE_EXIT)  # always runs
```

This guarantees the device exits the mode even if a work step raises an exception.

## Expected output (ROG AZOTH 96 HE, factory mode)

```text
Found: ROG AZOTH 96 HE  PID=7184  usagePage=65280  usage=1
Connected.
Listener ready, buffer flushed.

[mode_enter] Sending: FA 00 D3 A5
  IN [1] 64b Delta=2.07ms: FA 00 D3 A5  00 00 00 00 ...

[get device info] Sending: 12 00
  IN [1] 64b Delta=1.14ms: 12 00 00 00  24 00 07 00  06 03 06 01 ...

[mode_exit] Sending: FA 00 00 00
  IN [1] 64b Delta=1.99ms: FA 00 00 00  00 00 00 00 ...

Listener unregistered.
```

## Notes

- Mode enter/exit echo the command back as the IN response — that's the ACK, not data.
- `send_and_read` is reusable for any number of work steps; just extend `WORK_STEPS`.
- If a work step needs to inspect the response before proceeding, call `send_and_read` directly
  and check the returned `in_packets` list.
