# Unit Tests

Unit tests for ModernTools, using [xUnit](https://xunit.net/).

## Running

```powershell
dotnet test Tests/ModernTools.Tests/ModernTools.Tests.csproj
```

The test project targets `net8.0-windows` because it references the WPF
assemblies (`Base`, `CommonProtocol`). Tests must run on **Windows** — CI runs
them on `windows-latest` via `.github/workflows/tests.yml`.

## Strategy

We do not aim for blanket coverage. UI (XAML code-behind) and hardware I/O
(HID/BLE/USB, arm/relay controllers) are intentionally out of scope — they need
real devices and are low value to unit test. Instead we cover the **pure,
deterministic, high-risk logic**:

| Suite | Covers |
|---|---|
| `ProtocolDataTests` | Binary device-stream decoders (`Data<T>`, `Structure`) — offsets, endianness, null-terminated strings, bounds checks. |
| `RingBufferTests` | `RingBuffer<T>` — wraparound, full/empty, overwrite-on-full, peek-by-index, `AdvanceTail`. |

### Adding tests
- One test class per production class (`FooTests`).
- Prefer `[Theory]` + `[InlineData]` for parsing/math with many inputs.
- Don't mock hardware — if a function needs a device, it's out of scope.

Planned next (see the implementation plan): `Raycast2D`, `DpiTestRunner.CalcResult`,
`Utilities.Is<T>`, `CalibrationReport`.
