# Troubleshooting

| Symptom | Likely cause | Action |
|---|---|---|
| `connect/pid` returns `false` | Device not found or wrong PID | Re-run discovery, ask developer for correct PID |
| `readone` returns empty repeatedly | Listener not registered, wrong tab selected, or wrong usagePage/usage | Check tab selection, re-register listener, ask for correct interface values |
| Response bytes don't match expected | Wrong command, wrong device connected, or timing issue | Add delays, verify device is selected, confirm byte array with developer |
| Only OUT packets, no IN response | Device didn't respond, or wrong usagePage/usage | Verify usagePage from discovery, try longer timeout |
| Script output is empty | Script still running — `python_read` called too early | Call `mcp__moderntoolset__python_status` first; wait until `Stopped` |
| HTTP 408 from `writeandread` | Device didn't respond within `timeoutMs` | Expected for non-echoing protocols — catch and handle, or use `sendcmd` only |
