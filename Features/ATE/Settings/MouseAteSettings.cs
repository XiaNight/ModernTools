namespace MouseATE.Settings;

public class AteRelaySettings
{
    // Relay slot: 0=A0, 1=A1, 2=A2, 3=A3, 4=B0, 5=B1
    // Matches RelayApiClient.SlotLabels and the CommonProtocol RelayControlPage layout.
    public int  LeftRelaySlot   { get; set; } = 4;  // B0
    public int  RightRelaySlot  { get; set; } = 5;  // B1
    public int  TotalClicks     { get; set; } = 1000;
    public int  SolenoidOnMs    { get; set; } = 100;
    public int  ClickWindowMs   { get; set; } = 50;
    public int  CoolDownMs      { get; set; } = 200;
    public bool TestLeftButton  { get; set; } = true;
    public bool TestRightButton { get; set; } = true;
}

public class AteGlobalSettings
{
    public double TestDistance { get; set; } = 100;
    public int TestSpeed { get; set; } = 200;
    public int TestCycles { get; set; } = 5;
    public double ZOffset { get; set; } = 2.5;
    public double DeviationThresholdPct { get; set; } = 2.0;
    public double LodHeight { get; set; } = 3;
    public double LodInterval { get; set; } = 0.1;
    public double LodPathRadius { get; set; } = 50;
}

public class AteConnectionSettings
{
    public string RobotIpAddress { get; set; } = "192.168.0.100";
    public int RobotPort { get; set; } = 5000;
    public string Tc100Port { get; set; } = "COM3";
    public int Tc100BaudRate { get; set; } = 19200;
    public string Hf10Port { get; set; } = "COM5";
    public bool UseJths300 { get; set; } = false;
}

public class AteDeviceProfile
{
    public string Name { get; set; } = "Default";
    public int DevicePid { get; set; } = 0;
    public int DonglePid { get; set; } = 0;
    public string UsbInterface { get; set; } = "FF01";
    public int CalibrationDpi { get; set; } = 1600;
    public int DpiStep { get; set; } = 50;
    public int MaxLodLevel { get; set; } = 1;
    public List<int> MediaReviewDpis { get; set; } = new() { 400, 800, 1600, 3200 };
    public int FullTestMinDpi { get; set; } = 100;
    public int FullTestMaxDpi { get; set; } = 3200;
    public int FullTestDpiStep { get; set; } = 100;

    public override string ToString() => Name;
}
