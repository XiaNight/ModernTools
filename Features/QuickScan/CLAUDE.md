# QuickScan

Runs a set of **pre-defined protocol commands** against the connected device and reports, per command, whether it **passes or fails**. Built for developers validating an in-development peripheral's firmware against its protocol spec.

## What it does

A *scenario* is a per-model list of protocol entries (Command / Key / Index / parameters + how to judge the reply). QuickScan sends each enabled entry through the shared command queue and validates the reply. Scenarios are auto-selected by the connected device (PID, then product-name match) and can be edited, duplicated, and imported/exported as JSON. A built-in **M708** scenario ships with every *Get* protocol from the M708 USB spec (Common-Get 1-x and model Command-Get 4-x).

## Validation (per entry)

Three modes, matching how strictly the reply is judged:

- **Structural** — reply arrived within timeout, is not an `FF AA` error-ack, and echoes the request's Command/Key. This is the baseline ACK check.
- **ValidRange** — structural, plus each configured data field is within the spec's allowed range/enum.
- **ExactMatch** — structural, plus the reply's data equals a previously **captured baseline** ("same as last time / a saved good unit"). Use *Capture baseline* to record the current replies as the golden values.

Every failing entry records a `ScanFailure` (Timeout, ErrorAck, EchoMismatch, LengthMismatch, OutOfRange, BaselineMismatch, NoBaseline, Exception).

## Where the pieces live

The reusable protocol machinery — the declarative `Structure`/`Data`/`Listener` model, the request-frame builder, and the validator (`ProtocolValidator`, `ScanResult`, `ScanFailure`) — lives in **Base** (`Base.Protocol`), alongside the awaitable `ProtocolService.SendAsync` a scan loop awaits. This feature owns only the scenario model, the M708 seed, the runner, and the page.

Files: `QuickScanModels.cs` (scenario/entry) · `QuickScanStore.cs` (persistence, per-model selection, JSON import/export) · `M708Scenario.cs` (built-in seed) · `QuickScanRunner.cs` (send + validate + baseline capture) · `QuickScanPage.xaml` / `.xaml.cs` (the page — a XAML-backed `PageBase`, root `base:PageBase`) · `QuickScanEntryControl.xaml` / `.xaml.cs` (one protocol row, a `UserControl`).
