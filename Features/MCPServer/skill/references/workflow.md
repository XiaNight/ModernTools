# ModernToolset Workflow Reference

Full step-by-step guide for any device communication task.

---

## Core Architecture

```
Developer prompt
      ↓
Claude writes Python script
      ↓
mcp__moderntoolset__python_run  (session = "default")
      ↓
Python script calls ModernToolset HTTP APIs internally
      ↓
mcp__moderntoolset__python_read
      ↓
Results interpreted and reported
```

The sandbox has no persistent state between runs unless `input()` is used to keep it alive. Scripts should be self-contained.

**Session naming is bugged** — always use `"default"`. Custom names are ignored.

---

## Step 1 — Select the BusHound Tab

Always do this first, before registering a listener or sending commands.

```python
request("POST", "/api/v1/selecttabbyname", {"name": "Bus Hound"})
```

---

## Step 2 — Discover & Connect to a Device

```python
devices = get_data(request("GET", "/base/services/deviceselection/listdiscovereddevices"))
```

Each device exposes: `productName`, `PID`, `VID`, `ProductIdentifier`, and an `interfaces` list.

**Fuzzy-match by name** (developer may abbreviate):
```python
target = "raikiri"  # → matches "ROG RAIKIRI II PRO PC"
device = next(
    (d for d in (devices or [])
     if target.lower() in str(d.get("productName", "")).lower()),
    None
)
if not device:
    print(f"ERROR: no device matching '{target}' found")
    print("Available:", [d.get("productName") for d in (devices or [])])
    raise SystemExit(1)
```

**Verify the UsagePage against known devices** — never fall back to a default:
```python
pid = device["PID"]
KNOWN_ASUS_USAGE_PAGES = [65283, 65282, 65281, 65280]
cmd_iface = None
for up in KNOWN_ASUS_USAGE_PAGES:
    cmd_iface = next((i for i in device.get("interfaces", []) if i.get("UsagePage") == up), None)
    if cmd_iface:
        break
if not cmd_iface:
    print(f"ERROR: no known command interface found on '{device['productName']}'")
    print("Available interfaces:")
    for iface in device.get("interfaces", []):
        print(f"  UsagePage={iface.get('UsagePage')}  Usage={iface.get('Usage')}  Product={iface.get('Product', '?')}")
    print("Specify the correct UsagePage and Usage for this device and retry.")
    raise SystemExit(1)
usage_page = cmd_iface["UsagePage"]
usage      = cmd_iface["Usage"]
```

**Connect by PID:**
```python
connected = get_data(request("GET", "/base/services/deviceselection/connect/pid", {"pid": pid}))
if not connected:
    print("ERROR: connect returned False — check PID or re-run discovery")
    raise SystemExit(1)
print("Connected.")
```

---

## Step 3 — Register a Listener + Flush

Always register before sending commands. Flush immediately after — old packets from before registration may be present.

```python
LISTENER = "default"
request("POST", "/base/ui/pages/asusbushoundpage/registernewlistener",
        {"name": LISTENER, "bufferSize": 16384, "bucketSize": 64})
get_data(request("GET", "/base/ui/pages/asusbushoundpage/readall", {"name": LISTENER}))
print("Listener ready, buffer flushed.")
```

---

## Step 4 — Send a Command

`usage` and `usagePage` are **integers** (not strings).
`cmdText` = hex bytes, no `0x` prefix, space-separated.

```python
request("POST", "/base/ui/pages/asusbushoundpage/sendcmd", {
    "usage": usage,
    "usagePage": usage_page,
    "cmdText": "12 00"
})
```

Building cmdText from a byte array:
```python
byte_array = [0x12, 0x00, 0x00, 0x00]
cmd_text = " ".join(f"{b:02X}" for b in byte_array)
```

---

## Step 5 — Read Responses

Packet format: `{'Bytes': '12 00 AB CD ...', 'Phase': 'IN', 'Delta': '308us'}`

- `Phase: 'IN'` = device → host (the response you want)
- `Phase: 'OUT'` = host → device (your command going out — skip)
- `Delta` = time since previous packet — seconds-range = stale

**Choose the right read pattern:**

| Situation | Pattern |
|---|---|
| Waiting for one response | `readone` poll loop — react as soon as it arrives |
| Collecting many packets over a time window | `time.sleep(N)` then one `readall` |

The buffer does **not** drop packets while sleeping. Never poll `readall` in a loop during a collection window.

**Single response — poll with `readone`:**
```python
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
else:
    print("No IN response within timeout.")
```

**Bulk collection — sleep then one `readall`:**
```python
print("Collecting for 1 second...")
time.sleep(1.0)

packets = get_data(request("GET", "/base/ui/pages/asusbushoundpage/readall", {"name": LISTENER}))
in_packets = [p for p in (packets or []) if parse_packet(p)[1] == "IN"]
print(f"Collected {len(in_packets)} IN packets.")
for i, pkt in enumerate(in_packets[:10], 1):
    bytes_, phase, delta, hex_str = parse_packet(pkt)
    print(f"  [{i}] Delta={delta} | {hex_str}")
if len(in_packets) > 10:
    print(f"  ... {len(in_packets) - 10} more packets")
```

---

## Step 6 — Cleanup

Always unregister in a `finally` block:
```python
finally:
    try:
        request("POST", "/base/ui/pages/asusbushoundpage/unregisterlistener", {"name": LISTENER})
        print("Listener unregistered.")
    except Exception as e:
        print(f"Warning: cleanup failed: {e}")
```

---

## Alternative: `writeandread` (no listener needed)

Sends a command and waits for the IN response in one call. Do **not** register a listener when using this.

```python
r = request("POST", "/base/ui/pages/asusbushoundpage/writeandread",
            {"usage": usage, "usagePage": usage_page, "cmdText": "12 00", "timeoutMs": 200})
data = get_data(r)
# data = {"Bytes": "00 12 00 00 ...", "Length": 65}
# NOTE: Bytes includes the HID report ID as byte[0]
```

| | `writeandread` | `sendcmd` + `readone/readall` |
|---|---|---|
| Listener required | No | Yes |
| Best for | Known fast-response protocols | Unknown timing, multi-packet responses |
| Bytes format | Includes report ID (byte[0]) | No report ID |
| No response | HTTP 408 after timeout | Empty poll + drain |

For protocols that may or may not respond, use `timeoutMs=10` and catch 408:
```python
try:
    data = get_data(request("POST", "/base/ui/pages/asusbushoundpage/writeandread",
                            {"usage": usage, "usagePage": usage_page, "cmdText": cmd, "timeoutMs": 10}))
    print(f"Response: {data['Bytes']}")
except RuntimeError as e:
    if "408" in str(e):
        print("No response — protocol may not echo.")
    else:
        raise
```

---

## Running Scripts via MCP

Session name is always `default`.

**Standard (self-contained script):**
```
mcp__moderntoolset__python_run   { "code": "<entire python script>" }
mcp__moderntoolset__python_read  {}
```

For longer scripts, check status before reading:
```
mcp__moderntoolset__python_status {}   → "Running" or "Stopped"
```

**Interactive / stateful (script uses `input()`):**
```
mcp__moderntoolset__python_run   { "code": "x = input('Ready: ')\nprint(f'Got: {x}')" }
mcp__moderntoolset__python_write { "input": "hello" }
mcp__moderntoolset__python_read  {}
mcp__moderntoolset__python_close {}
```

---

## Sequences & Conditional Logic

**Timed sequence:**
```python
request("POST", ".../sendcmd", {..., "cmdText": "12 00"})
time.sleep(0.05)
request("POST", ".../sendcmd", {..., "cmdText": "13 00"})
```

**Receive-then-send:**
```python
request("POST", ".../sendcmd", {..., "cmdText": "12 00"})
time.sleep(0.02)
pkt = get_data(request("GET", ".../readone", {"name": LISTENER}))
bytes_, phase, delta, _ = parse_packet(pkt or {})
if phase == "IN" and len(bytes_) > 1 and bytes_[1] == 0x01:
    request("POST", ".../sendcmd", {..., "cmdText": "13 00"})
else:
    print("Unexpected response, stopping.")
```
