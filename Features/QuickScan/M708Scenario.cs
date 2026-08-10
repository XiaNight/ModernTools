using Base.Protocol;

namespace QuickScan;

/// <summary>
/// Built-in QuickScan scenario seeded from the M708 USB Protocol spec — every *Get*
/// command: the Common-Get group (1-x) and the model Command-Get group (4-x).
///
/// Notes:
///  - Default validation is <see cref="ValidationMode.Structural"/> (reply + Command/Key
///    echo). Range rules are pre-filled where the spec defines enums/ranges, so a dev can
///    switch an entry to ValidRange without re-authoring. ExactMatch needs a captured baseline.
///  - Entries marked <see cref="QuickScanEntry.RequiresParam"/> take a Key Code / Index /
///    Effect Id in the request; a placeholder default is provided and is meant to be edited.
///    Key Codes follow the model's Key Table (location), not USB HID usages.
///  - Get FW Info and Get Speed Tap Info have a fixed set of sub-indexes, so they are seeded
///    as one entry per sub-index under a shared SubGroup (rendered as a group in the UI).
/// </summary>
public static class M708Scenario
{
	public const string ScenarioId = "builtin-m708";

	private const string Common = "Common (Get)";
	private const string Model = "Get (M708)";

	// Sub-group headers (fixed-index protocols rendered under one group control).
	private const string FwInfo = "1-7 Get FW Info";
	private const string SpeedTap = "4-15 Get Speed Tap Info";

	// TODO: set the real M708 USB PID here once known so auto-select is exact; until then
	// the scenario is matched by the product name containing "M708".
	public static QuickScanScenario Create()
	{
		QuickScanScenario scenario = new()
		{
			Id = ScenarioId,
			Name = "M708 — All Get",
			ModelName = "M708",
			VID = 0x0B05, // ASUS
			PID = 0x0000, // unknown; match by product name
			BuiltIn = true,
			Version = 2, // bump when the entry/group structure changes (2 = FW Info & Speed Tap grouped)
			Entries = new List<QuickScanEntry>
			{
				// ---- Command - Common(Get) : 1-1 .. 1-10 ----
				Entry(Common, "1-1 Get Device Info", 0x12, 0x00,
					"Firmware/profile info."),
				Entry(Common, "1-2 Get Power Info", 0x12, 0x01,
					"Battery / idle / power-saving state.",
					rules: Rules(
						Range("Current power", 5, 0, 0x64),
						Allowed("Idle mode", 6, 0x00, 0x01, 0x02, 0x03, 0x04, 0xFF),
						Allowed("Power saving", 7, 0x00, 0x01, 0x02),
						Allowed("Charging", 8, 0x00, 0x01))),
				Entry(Common, "1-3 Get Connection Info", 0x12, 0x03,
					"Wired / RF / BT / unavailable.",
					rules: Rules(Allowed("Connection", 4, 0x00, 0x01, 0x02, 0xFF))),
				Entry(Common, "1-4 Get Support WDL Info", 0x12, 0x07,
					"Windows Dynamic Lighting supported.",
					rules: Rules(Allowed("WDL", 4, 0x00, 0x01))),
				Entry(Common, "1-5 Get KB Layout & Nation", 0x12, 0x12,
					"Physical layout + nation code.",
					rules: Rules(
						Allowed("Layout", 4, 0x01, 0x02, 0x03),
						Range("Nation", 5, 1, 25))),
				Entry(Common, "1-6 Get KB Nation ID", 0x12, 0x13,
					"Nation ID.",
					rules: Rules(Range("Nation ID", 4, 1, 25))),

				// 1-7 Get FW Info — one entry per fixed sub-index (Index selects the sub-info).
				Entry(Common, "Demo mode", 0x12, 0x14, "Whether demo mode is enabled.",
					index: 0x0000, subGroup: FwInfo, expectIndexEcho: true,
					rules: Rules(Allowed("Demo mode", 4, 0x00, 0x01))),
				Entry(Common, "ISN (Identification SN)", 0x12, 0x14, "Identification serial number.",
					index: 0x0001, subGroup: FwInfo, expectIndexEcho: true),
				Entry(Common, "SSN (Sequential SN)", 0x12, 0x14, "Sequential serial number.",
					index: 0x0002, subGroup: FwInfo, expectIndexEcho: true),
				Entry(Common, "MSN (deprecated)", 0x12, 0x14, "Manufacturer SN — removed in the M708 spec.",
					index: 0x0003, subGroup: FwInfo, expectIndexEcho: true, enabled: false),
				Entry(Common, "MP date (deprecated)", 0x12, 0x14, "Manufacture date — removed in the M708 spec.",
					index: 0x0004, subGroup: FwInfo, expectIndexEcho: true, enabled: false),
				Entry(Common, "DG ISN (deprecated)", 0x12, 0x14, "Dongle Identification SN — removed in the M708 spec.",
					index: 0x0005, subGroup: FwInfo, expectIndexEcho: true, enabled: false),
				Entry(Common, "DG SSN (deprecated)", 0x12, 0x14, "Dongle Sequential SN — removed in the M708 spec.",
					index: 0x0006, subGroup: FwInfo, expectIndexEcho: true, enabled: false),
				Entry(Common, "DG MSN (deprecated)", 0x12, 0x14, "Dongle Manufacturer SN — removed in the M708 spec.",
					index: 0x0007, subGroup: FwInfo, expectIndexEcho: true, enabled: false),

				Entry(Common, "1-8 Get Polling Rate", 0x12, 0x15,
					"Stored polling-rate index (0:1000Hz, 1:8000Hz).",
					rules: Rules(Allowed("Polling index", 4, 0x00, 0x01))),
				Entry(Common, "1-9 Get Effect", 0x25, 0x0E,
					"Current main lighting effect. Index = (High)effect index + (Low)effect id.",
					index: 0x0000, requiresParam: true),
				Entry(Common, "1-10 Get SW Mode Status", 0x27, 0x00,
					"In SW (Aura) lighting mode.",
					rules: Rules(Allowed("SW mode", 4, 0x00, 0x01))),

				// ---- Command - Get : 4-1 .. 4-31 ----
				Entry(Model, "4-1 Get Rapid Trigger Info", 0x25, 0x00,
					"Rapid Trigger on/off.",
					rules: Rules(Allowed("RT switch", 4, 0x00, 0x01))),
				Entry(Model, "4-2 Get Speed Tap Info", 0x25, 0x01,
					"Speed Tap on/off + random delay mode.",
					rules: Rules(
						Allowed("Speed Tap", 4, 0x00, 0x01),
						Allowed("Random delay", 5, 0x00, 0x01))),
				Entry(Model, "4-3 Get Key Info - Switch Type", 0x25, 0x30,
					"Per-key switch category. Data 4-5 = Source Key.",
					param: SampleKey(), requiresParam: true,
					rules: Rules(Allowed("Key type", 6, 0x00, 0x01, 0x02, 0x03, 0x04))),
				Entry(Model, "4-4 Get Key Info - Mechanical", 0x25, 0xA0,
					"Mechanical-switch key mapping. Data 4-5 = Source Key.",
					param: SampleKey(), requiresParam: true),
				Entry(Model, "4-5 Get Key Info - Magnetic", 0x25, 0xA2,
					"Magnetic-switch key info. Data 4-5 = Source Key.",
					param: SampleKey(), requiresParam: true),
				Entry(Model, "4-6 Get Key Info - DKS", 0x25, 0x29,
					"DKS settings for a magnetic key. Data 4-5 = Source Key.",
					param: SampleKey(), requiresParam: true),
				Entry(Model, "4-7 Get Combination Key Info", 0x25, 0x31,
					"Combination-key macro. Data 4-5 = Source Key.",
					param: SampleKey(), requiresParam: true),
				Entry(Model, "4-8 Get Key Info - Plug-in", 0x25, 0x38,
					"Plug-in key (knob/switch/dial). Index 0=knob,1=switch,2=dial. Data 4-5 = Source Key.",
					index: 0x0000, param: SampleKey(), requiresParam: true),
				Entry(Model, "4-9 Get EC Effect - Analog on/off", 0x25, 0x03,
					"EC-effect analog brightness on/off. Data 4 = Effect Id.",
					param: Bytes(0x00), requiresParam: true,
					rules: Rules(Allowed("LED brightness", 4, 0x00, 0x01))),
				Entry(Model, "4-10 Get Actuation - per key", 0x25, 0xA4,
					"Per-key actuation (0.20-3.50mm). Data 4-5 = Source Key.",
					param: SampleKey(), requiresParam: true,
					rules: Rules(Range("Actuation", 6, 20, 350, 2))),
				Entry(Model, "4-11 Get Actuation - all key", 0x25, 0x05,
					"Actuation of all keys."),
				Entry(Model, "4-12 Get Rapid Trigger - per key", 0x25, 0xA6,
					"Per-key Rapid Trigger values. Data 4-5 = Source Key.",
					param: SampleKey(), requiresParam: true,
					rules: Rules(
						Range("RT press", 6, 5, 250, 2),
						Range("RT release", 8, 5, 250, 2))),
				Entry(Model, "4-13 Get Rapid Trigger - Global Settings", 0x25, 0x22,
					"Global Rapid Trigger settings (call before Global Key Info)."),
				Entry(Model, "4-14 Get Rapid Trigger - Global Key Info", 0x25, 0x23,
					"Keys set to Rapid Trigger. Index = packet index (H)+00.",
					index: 0x0000, requiresParam: true),

				// 4-15 Get Speed Tap Info — one entry per fixed group index.
				Entry(Model, "Groups 1-3", 0x25, 0x08, "Speed-Tap paired keys, groups 1-3.",
					index: 0x0000, subGroup: SpeedTap, expectIndexEcho: true),
				Entry(Model, "Groups 4-5", 0x25, 0x08, "Speed-Tap paired keys, groups 4-5.",
					index: 0x0001, subGroup: SpeedTap, expectIndexEcho: true),

				Entry(Model, "4-16 Get Dead Zone - per key", 0x25, 0xA9,
					"Per-key dead zone (top+bottom). Data 4-5 = Source Key.",
					param: SampleKey(), requiresParam: true,
					rules: Rules(
						Range("Dead zone bottom", 6, 0, 50),
						Range("Dead zone top", 7, 0, 50))),
				Entry(Model, "4-17 Get Dead Zone - all key", 0x25, 0x0A,
					"Dead zone of all keys."),
				Entry(Model, "4-18 Get Screen Wheel Mode", 0x22, 0x01,
					"Current screen-wheel (Lever) mode index.",
					rules: Rules(Range("Lever index", 4, 0, 8))),
				Entry(Model, "4-19 Get Screen Wheel Switch", 0x25, 0x0B,
					"Screen-wheel function on/off flags."),
				Entry(Model, "4-20 Get Screen Wheel Info", 0x25, 0x0C,
					"Screen-wheel key mapping. Data 4 = Lever Index, 5-6 = Source Key.",
					param: Bytes(0x00, 0x00, 0x00), requiresParam: true),
				Entry(Model, "4-21 Get Default Animation", 0x21, 0x00,
					"OLED current animation index.",
					rules: Rules(Range("Animation index", 4, 0, 7))),
				Entry(Model, "4-22 Get Screen Info", 0x23, 0x00,
					"Screen firmware version."),
				Entry(Model, "4-23 Get Screen Switch", 0x23, 0x02,
					"Screen on/off.",
					rules: Rules(Allowed("Screen", 4, 0x00, 0x01))),
				Entry(Model, "4-24 Get Screen Brightness", 0x23, 0x03,
					"Screen brightness percent.",
					rules: Rules(Range("Brightness", 4, 0, 0x64))),
				Entry(Model, "4-25 Get Screen Bar Status", 0x23, 0x04,
					"Screen status-bar option bitfield."),
				Entry(Model, "4-26 Get Screen Parameters", 0x24, 0x00,
					"Current screen function parameters."),
				Entry(Model, "4-27 Get Screen Mode - Index", 0x24, 0x01,
					"Current screen function id.",
					rules: Rules(Range("Function id", 4, 0, 4))),
				Entry(Model, "4-28 Get Screen Mode - Switch", 0x24, 0x02,
					"Screen mode function on/off flags."),
				Entry(Model, "4-29 Get KPS Switch Status", 0x24, 0x03,
					"KPS (keystrokes/sec) on/off.",
					rules: Rules(Allowed("KPS", 4, 0x00, 0x01))),
				Entry(Model, "4-30 Get Polling Rate - Mode", 0x25, 0x24,
					"Current device mode setting.",
					rules: Rules(Allowed("Mode", 4, 0x00, 0x01, 0x02))),
				Entry(Model, "4-31 Get Polling Rate - Setting", 0x25, 0x25,
					"Per-key mode setting. Data 4-5 = Target Key.",
					param: SampleKey(), requiresParam: true,
					rules: Rules(
						Allowed("Mode", 4, 0x00, 0x01, 0x02),
						Allowed("Sub-mode", 5, 0x00, 0x01, 0x02))),
			},
		};

		return scenario;
	}

	private static QuickScanEntry Entry(string group, string name, byte command, byte key,
		string description = "", ushort index = 0, byte[] param = null,
		bool requiresParam = false, List<RangeRule> rules = null,
		string subGroup = "", bool enabled = true, bool expectIndexEcho = false)
	{
		return new QuickScanEntry
		{
			Group = group,
			Name = name,
			SubGroup = subGroup,
			Command = command,
			Key = key,
			Description = description,
			Index = index,
			ParamBytes = param ?? Array.Empty<byte>(),
			RequiresParam = requiresParam,
			ExpectIndexEcho = expectIndexEcho,
			Enabled = enabled,
			Rules = rules ?? new List<RangeRule>(),
		};
	}

	private static List<RangeRule> Rules(params RangeRule[] rules) => new(rules);

	private static RangeRule Allowed(string label, int offset, params byte[] values)
		=> RangeRule.Allowed(label, offset, values);

	private static RangeRule Range(string label, int offset, long min, long max, int length = 1)
		=> new(label, offset, min, max, length);

	private static byte[] Bytes(params byte[] values) => values;

	// Placeholder Source/Target Key Code (2 bytes). Edit per model's Key Table.
	private static byte[] SampleKey() => new byte[] { 0x00, 0x00 };
}
