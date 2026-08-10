---
name: moderntools-device
description: >
  Use this skill whenever a developer wants to communicate with a HID device (keyboard, mouse, gamepad, headphones, gaming peripheral, or any USB/HID device) through ModernToolset. Triggers include: sending protocol/commands to a device, reading device responses, discovering or listing connected devices, debugging device communication, writing or running unit tests for device protocols, capturing USB packets, or any mention of ModernToolset, BusHound, PID/VID, HID protocol, sendcmd, or byte arrays to send to a device. Also trigger when the developer mentions "get device info", "send this command", "read the response", "test this protocol", or describes a sequence of bytes to send.
---

# ModernToolset Device Communication Skill

ModernToolset exposes all device communication APIs as HTTP on `127.0.0.1:2345`. Scripts run inside the MCP Python sandbox and call these APIs internally.

---

## Discovering the API (authoritative source)

This skill pins the routes and request shapes below for speed, but the **running app is the source of truth**. Every registered endpoint carries its own description and a JSON Schema for its inputs/outputs, served as a live manifest:

- `GET /api/v1/schema` — structured manifest. For each route it returns `verb`, `path`, `summary`, `description`, `inputSchema` and (where known) `outputSchema`. Use it to confirm a route, discover new ones, or learn the exact body/params a call expects.
- `GET /api/v1/listroute` — quick, human-readable, one line per route.

Reach for the manifest whenever a pinned route 404s, when you need a data structure this skill doesn't spell out, or when working against an unfamiliar ModernToolset build. As with every endpoint, responses are wrapped in `{ "status", "data" }`.

---

## Hard Stop Rules

These conditions must cause an **immediate stop** — no workarounds, no fallbacks.

### 1. MCP tools not available
If `mcp__moderntoolset__*` tools are not in the tool list, read `references/mcp-setup.md` and follow the setup instructions there.

### 2. HTTP API not reachable
If a script returns `URLError` or connection refused on `127.0.0.1:2345`:

> **Stop.** Tell the user: "ModernToolset HTTP API is not reachable at 127.0.0.1:2345. Check that ModernToolset is running and that the version is compatible with this skill."

### 3. API route returns 404
If a pinned API call returns HTTP 404, the route may be stale for this build. Fetch the live manifest **once** to confirm the correct verb/path and request shape (see "Discovering the API"):

```
GET /api/v1/schema      # or /api/v1/listroute for a quick list
```

- If the operation exists under a different path or body shape, use what the manifest reports and continue.
- If the operation is genuinely **absent** from the manifest:

> **Stop.** Tell the user: "The API `[operation]` is not present in this ModernToolset build (checked `/api/v1/schema`). Either this skill is outdated or your ModernToolset version doesn't match. Please verify your ModernToolset version."

Do **not** brute-force or guess routes beyond what the manifest reports.

### 4. Skill references an API that is not present
If a tool, route, or parameter referenced by this skill does not exist in the running instance:

> **Stop.** Tell the user: "The API `[name]` was not found. Either this skill is outdated or the installed ModernToolset version doesn't support it. Please check for updates."

---

## Known Devices

| Device | UsagePage | Usage |
|--------|-----------|-------|
| ROG RAIKIRI II PRO PC | 65283 (0xFF03) | 1 |
| ROG AZOTH 96 HE | 65280 (0xFF00) | 1 |

If a device's UsagePage is not in this table, stop and ask the user rather than guessing. See `references/workflow.md` for the interface discovery and abort pattern.

---

## Standard Boilerplate

Always include these three functions at the top of every script:

```python
import json, time, urllib.parse, urllib.request
from typing import Any

BASE_URL = "http://127.0.0.1:2345"

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
```

---

## Decision Guide

Read the relevant file before writing any script.

| Task | Read this |
|------|-----------|
| Confirm a route / find a data structure | fetch `GET /api/v1/schema` (see "Discovering the API") |
| Send one command, read one response | `examples/send-and-read.md` |
| Mode enter → work → exit | `examples/mode-enter-work-exit.md` |
| Any other task (full workflow) | `references/workflow.md` |
| All API routes and formats | `references/api-patterns.md` |
| Debugging a protocol or running unit tests | `references/debugging.md` |
| Something went wrong | `references/troubleshooting.md` |
