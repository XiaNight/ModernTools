using System.IO.Ports;
using System.Text;

namespace MouseATE.Hardware;

/// <summary>
/// Controls the TC100 vertical Z-axis controller via serial Modbus-ASCII.
/// All public methods accept Z in 0.01mm units (mm × 100).
/// Z range: 0–10,000 (0–100 mm). Speed range: 0–800.
/// </summary>
public class TC100Controller : IDisposable
{
    private SerialPort _port;

    public string PortName { get; set; } = "COM3";
    public int BaudRate { get; set; } = 19200;
    public bool IsConnected { get; private set; }

    private const string CH = "01";
    private const string MSG_START = ":";
    private const string MSG_END = "\r\n";
    private const string FUNC_READ = "03";
    private const string FUNC_WRITE = "06";
    private const string FUNC_WRITE_CONT = "10";

    private const string ADDR_CONTROLLER = "10E0";
    private const string CONTROLLER_NAME = "TC-100";
    private const string ADDR_ALARM = "1005";
    private const string ALARM_NO_ALARM = "0000";
    private const string ALARM_EMG = "000B";
    private const string ADDR_SERVO_STATUS = "100C";
    private const string SERVO_ON = "0001";
    private const string SERVO_OFF = "0000";
    private const string ADDR_SERVO_ONOFF = "2011";
    private const string SERVO_CMD_ON = "0000";
    private const string SERVO_CMD_OFF = "0001";
    private const string ADDR_ERROR = "100D";
    private const string ERROR_SERVO_ONOFF = "0008";
    private const string ADDR_PORT_OUT_BASE = "1021";
    private const string PORT_OUT_ON = "0001";
    private const string ADDR_INP = "1001";
    private const string INP_ON = "0001";
    private const string ADDR_ECD = "100A";
    private const string ADDR_INC = "2000";
    private const string ADDR_ABS = "2002";
    private const string ADDR_SPEED = "2014";
    private const string ADDR_MOV_TYPE = "201E";
    private const string MOV_INC = "0000";
    private const string MOV_ABS = "0001";
    private const string MOV_ORG = "0003";
    private const string MOV_ALARM_RST = "0006";

    public async Task<bool> ConnectAsync() =>
        await Task.Run(() =>
        {
            try
            {
                _port = new SerialPort(PortName, BaudRate) { ReadTimeout = 500, WriteTimeout = 500 };
                _port.Open();

                if (!CheckController()) { _port.Close(); return false; }
                if (!HandleAlarmOnConnect()) { _port.Close(); return false; }
                if (!EnsureServoOn()) { _port.Close(); return false; }

                IsConnected = true;
                return true;
            }
            catch { IsConnected = false; return false; }
        });

    public void Disconnect()
    {
        try { if (_port?.IsOpen == true) _port.Close(); } catch { }
        IsConnected = false;
    }

    public async Task<bool> InitializeAsync() =>
        await Task.Run(() => ReturnToOriginInternal());

    /// <param name="speedPct">Speed 0–800.</param>
    public async Task<bool> SetSpeedAsync(ushort speedPct) =>
        await Task.Run(() => WriteReg(ADDR_SPEED, speedPct.ToString("X4")));

    /// <param name="amountUnits">Absolute position in 0.01mm units (mm × 100). Range 0–10,000.</param>
    public async Task<bool> MoveAbsAsync(int amountUnits) =>
        await Task.Run(() =>
        {
            if (amountUnits < 0 || amountUnits > 10_000) return false;
            return WriteRegCont(ADDR_ABS, "0002", amountUnits.ToString("X8"))
                && ExecuteMove(MOV_ABS);
        });

    /// <param name="amountUnits">Incremental distance in 0.01mm units. Range ±10,000.</param>
    public async Task<bool> MoveIncAsync(int amountUnits) =>
        await Task.Run(() =>
        {
            if (amountUnits < -10_000 || amountUnits > 10_000) return false;
            return WriteRegCont(ADDR_INC, "0002", amountUnits.ToString("X8"))
                && ExecuteMove(MOV_INC);
        });

    public async Task<bool> ReturnToOriginAsync() =>
        await Task.Run(ReturnToOriginInternal);

    public async Task<bool> GetPositionAsync(Action<int> onResult) =>
        await Task.Run(() =>
        {
            string data = null;
            if (!ReadReg(ADDR_ECD, "0002", ref data)) return false;
            onResult(Convert.ToInt32(data, 16));
            return true;
        });

    // ── internals ──────────────────────────────────────────────────────────────

    private bool ReturnToOriginInternal()
    {
        if (!WriteReg(ADDR_MOV_TYPE, MOV_ORG)) return false;
        for (int i = 0; i < 300; i++)
        {
            string s = null;
            if (ReadReg(ADDR_PORT_OUT_BASE, "0001", ref s) && s == PORT_OUT_ON) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    private bool ExecuteMove(string movType)
    {
        string portOut3 = null;
        if (!ReadReg((Convert.ToUInt16(ADDR_PORT_OUT_BASE, 16) + 2).ToString("X4"), "0001", ref portOut3)
            || portOut3 != PORT_OUT_ON)
            return false;

        if (!WriteReg(ADDR_MOV_TYPE, movType)) return false;

        for (int i = 0; i < 300; i++)
        {
            string p3 = null, inp = null;
            if (ReadReg((Convert.ToUInt16(ADDR_PORT_OUT_BASE, 16) + 2).ToString("X4"), "0001", ref p3) && p3 == PORT_OUT_ON &&
                ReadReg(ADDR_INP, "0001", ref inp) && inp == INP_ON)
                return true;
            Thread.Sleep(100);
        }
        return false;
    }

    private bool CheckController()
    {
        string data = null;
        if (!ReadReg(ADDR_CONTROLLER, "0003", ref data)) return false;
        return Encoding.ASCII.GetString(HexToBytes(data)) == CONTROLLER_NAME;
    }

    private bool HandleAlarmOnConnect()
    {
        string alarm = null;
        if (!ReadReg(ADDR_ALARM, "0001", ref alarm)) return false;
        if (alarm == ALARM_NO_ALARM) return true;
        if (alarm != ALARM_EMG) return false;

        if (!ResetAlarm())
        {
            string err = null;
            if (ReadReg(ADDR_ERROR, "0001", ref err) && err == ERROR_SERVO_ONOFF)
            {
                WriteReg(ADDR_SERVO_ONOFF, SERVO_CMD_OFF);
                if (!ResetAlarm()) return false;
            }
            else return false;
        }
        return true;
    }

    private bool EnsureServoOn()
    {
        string status = null;
        if (!ReadReg(ADDR_SERVO_STATUS, "0001", ref status)) return false;
        if (status == SERVO_OFF)
            return WriteReg(ADDR_SERVO_ONOFF, SERVO_CMD_ON);
        return true;
    }

    private bool ResetAlarm()
    {
        string status = null;
        if (!ReadReg(ADDR_SERVO_STATUS, "0001", ref status)) return false;
        if (status == SERVO_ON)
        {
            if (!WriteReg(ADDR_SERVO_ONOFF, SERVO_CMD_OFF)) return false;
            if (!ReadReg(ADDR_SERVO_STATUS, "0001", ref status) || status == SERVO_ON) return false;
        }
        return WriteReg(ADDR_MOV_TYPE, MOV_ALARM_RST);
    }

    private bool ReadReg(string addr, string wordCount, ref string data)
    {
        string payload = CH + FUNC_READ + addr + wordCount;
        string lrc = CalcLRC(payload);
        string msg = MSG_START + payload + lrc + MSG_END;

        try
        {
            _port.Write(msg);
            string response = _port.ReadLine();
            if (!response.StartsWith(MSG_START)) return false;
            byte byteCount = Convert.ToByte(response.Substring(5, 2), 16);
            data = response.Substring(7, byteCount * 2);
            return true;
        }
        catch { return false; }
    }

    private bool WriteReg(string addr, string value)
    {
        string payload = CH + FUNC_WRITE + addr + value;
        string lrc = CalcLRC(payload);
        string msg = MSG_START + payload + lrc + MSG_END;
        try
        {
            _port.Write(msg);
            _port.ReadLine();
            return true;
        }
        catch { return false; }
    }

    private bool WriteRegCont(string addr, string wordCount, string data)
    {
        int words = Convert.ToByte(wordCount, 16);
        string byteCountHex = (words * 2).ToString("X2");
        string payload = CH + FUNC_WRITE_CONT + addr + wordCount + byteCountHex + data;
        string lrc = CalcLRC(payload);
        string msg = MSG_START + payload + lrc + MSG_END;
        try
        {
            _port.Write(msg);
            _port.ReadLine();
            return true;
        }
        catch { return false; }
    }

    private static string CalcLRC(string hexStr)
    {
        byte[] bytes = HexToBytes(hexStr);
        byte sum = 0;
        foreach (var b in bytes) sum += b;
        return ((byte)(~sum + 1)).ToString("X2");
    }

    private static byte[] HexToBytes(string hex)
    {
        var result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return result;
    }

    public void Dispose() => Disconnect();
}
