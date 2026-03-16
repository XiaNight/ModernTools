namespace CommonProtocol.Protocols.GetInfo;

// 請求軟硬體資訊
internal class BasicInfo : Structure
{
    public override byte Command => 0x12;
    public override byte Key => 0x00;

    public ByteArrayData version1 = new(4, 4); // (有線時為鍵盤版號，無線時為 Dongle 版號)
    public ByteData sizeProfile = new(8); // Profile 的數量
    public ByteData currentEffectMode = new(9); // 目前的 effect mode
    public ByteData currentProfileIndex = new(10); // 目前的 profile index
    public ByteArrayData version2 = new(11, 4); // 有線時忽略，無線時為鍵盤版號
}

//請求電源資訊
internal class Power : Structure
{
    public override byte Command => 0x12;
    public override byte Key => 0x01;

    public ByteData currentPower = new(5);
    public ByteData idleMode = new(6);
    public ByteData powerSaving = new(7);
    public BoolData chargingStatus = new(8);
    public ByteData lowPowerMode = new(9);
    public ByteData secondaryPower = new(10);

    public IdleMode GetIdleMode() => (IdleMode)idleMode.Value;
    public PowerSaving GetPowerSaving() => (PowerSaving)powerSaving.Value;

    public enum IdleMode : byte
    {
        OneMinute = 0x00,
        TwoMinutes = 0x01,
        ThreeMinutes = 0x02,
        FiveMinutes = 0x03,
        TenMinutes = 0x04,
        Never = 0xff
    }
    public enum PowerSaving : byte
    {
        Off = 0x00,
        OnTurnOff = 0x01,
        OnDecreaseBrightness = 0x02
    }
}

//請求RGB指示燈效
internal class Indicator : Structure
{
    public override byte Command => 0x12;
    public override byte Key => 0x02;

    public ByteData indicatorMode = new(5);
    public IndicatorMode GetIndicatorMode() => (IndicatorMode)indicatorMode.Value;
    public enum IndicatorMode : byte
    {
        Normal = 0x00,
        Power = 0x01
    }
}

//請求裝置連線狀態
internal class ConnectionStatus : Structure
{
    public override byte Command => 0x12;
    public override byte Key => 0x03;

    public ByteData connectionStatus = new(5);
    public ConnectionMode GetConnectionMode() => (ConnectionMode)connectionStatus.Value;
    public enum ConnectionMode : byte
    {
        Wired = 0x00,
        WirelessRF = 0x01,
        Bluetooth = 0x02,
        Unavailable = 0xff
    }
}