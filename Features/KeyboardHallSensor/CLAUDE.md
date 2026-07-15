# KeyboardHallSensor

Inspection and calibration for **Hall-effect (analog / magnetic) keyboards** — raw, analog, gain, segment, and baseline calibration views, plus key-data visualization and keyboard-layout rendering.

Pages live under the nav path `["Keyboard", "Hall Effect"]` with explicit `NavOrder` (Raw = 0, Analog = 1, Segment = 2, Gain = 3, …). The feature defines its own intermediate bases (`KeyboardPageBase : PageBase`, `MFGKeyboardBasePage`, `GenericKeyboardInspectorPage`) that its pages extend — the convention for sharing logic within a feature. Protocol helper: `KeyboardCommonProtocol.cs`. Key-display controls: `KeyDisplay`, `KeyDisplayRendered`. Layout assets in `Config/` (`keyboard_layout.txt`, `M708.txt`).

Note: `ArmouryProtocol` and `GenericMouseAnalyzer` reference this project, so treat its shared bases as semi-public — coordinate before breaking changes.
