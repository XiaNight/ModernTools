# Debugging & Unit Testing

---

## Debugging Mode

When a developer asks to debug a protocol sequence:

1. Run the sequence as requested
2. Print every send and every response with clear labels (Phase, Delta, hex bytes)
3. Parse response bytes and annotate known fields if the developer provided a protocol spec
4. Summarize at the end:
   - What was sent
   - What was received
   - Any unexpected values, timeouts, or errors
   - Your interpretation / hypothesis

Adapt the output style to the developer — some prefer a structured log, others prefer a conversational diagnosis.

---

## Unit Test Mode

When a developer asks to run a unit test:

### Input required from developer
- Test name / description
- Command(s) to send (byte arrays + timing)
- Expected response (byte pattern, specific byte values, or a condition)
- Output file path for the `.txt` report

### Script structure

```python
import datetime

test_name = "Get Device Info"
fw_version = None
result = "FAIL"
notes = ""

# ... run protocol, capture response_bytes ...

if len(response_bytes) >= 2 and response_bytes[0] == 0x00:
    result = "PASS"
else:
    notes = f"Expected 0x00 at byte[0], got {response_bytes[0]:#04x}"

now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
fw_str = f"FW: {fw_version}" if fw_version else "FW: N/A"
print(f"[{result}] {test_name} | {now} | {fw_str}")
if notes:
    print(f"  Notes: {notes}")

report_path = r"C:\Tests\result.txt"  # developer provides this
with open(report_path, "a", encoding="utf-8") as f:
    f.write(f"[{result}] {test_name} | {now} | {fw_str}\n")
    if notes:
        f.write(f"  Notes: {notes}\n")
```

### Report format

```
[PASS] Get Device Info | 2025-04-28 14:32:01 | FW: 1.2.3
[FAIL] Set LED Color   | 2025-04-28 14:32:04 | FW: 1.2.3
  Notes: Expected 0x01 at byte[2], got 0xFF
```
