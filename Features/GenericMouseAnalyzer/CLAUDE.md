# GenericMouseAnalyzer

Mouse report-rate / raw-input analyzer and sensor view. Measures report rate via Win32 raw input (`WM_INPUT`) and shows sensor data.

Two pages under nav path `["Mouse"]`: `GenericMouseAnalyzerPage` and `SensorViewPage`. Pattern here separates the visual `UserControl` from the `PageBase` that hosts it. Uses `Base.Services.Peripheral.PeripheralInterface`. References the `KeyboardHallSensor` project.
