# MouseATE

Mouse **automated-test-equipment (ATE)** station: DPI test/calibration, LOD test, click stress test, and fixture control. Drives physical test hardware through dedicated controllers (HF10, JTB500, JTHS300, TC100, three-axis stage, relay API) and exports Excel reports via ClosedXML.

Because it commands real test rigs, be careful with controller/fixture code — changes can move hardware. Keep hardware controllers and reporting inside this feature.
