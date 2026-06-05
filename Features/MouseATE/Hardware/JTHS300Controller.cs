using System.Net.Sockets;
using System.Text;

namespace MouseATE.Hardware;

/// <summary>
/// Controls the JTHS300 all-in-one XYZ robot arm via TCP.
/// Accepts coordinates in µm (mm × 1000). XY range: 0–300,000 µm. Z range: 0–100,000 µm.
/// NOTE: This hardware has a ×0.5 scale modification — commands are divided by 2 before
/// being sent to the controller. Do NOT account for this externally.
/// </summary>
public class JTHS300Controller : IDisposable
{
    private TcpClient _client;
    private NetworkStream _stream;

    public string IpAddress { get; set; } = "192.168.0.100";
    public int Port { get; set; } = 5000;
    public bool IsConnected { get; private set; }

    private const string MSG_END = "\r\n";
    private const string MSG_CMD_VER = "@?VER\r\n";
    private const string MSG_CMD_ABSRST = "@ABSRST\r\n";
    private const string MSG_DATA_ABSRST = "ORG_0.00 0.00 0.00 0.00\r\n";
    private const string MSG_CMD_MOVE_L = "@MOVE L";
    private const string MSG_CMD_MOVEI_L = "@MOVEI L";
    private const string MSG_CMD_MODE_MANUAL = "@MANUAL\r\n";
    private const string MSG_CMD_MODE_AUTO = "@AUTO\r\n";
    private const string MSG_CMD_AUTO_RUN = "@RUN";
    private const string MSG_CMD_READ_OUTPUT = "@?OUT\r\n";
    private const string MSG_CMD_WHERE = "@?WHERE\r\n";

    public async Task<bool> ConnectAsync() =>
        await Task.Run(() =>
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(IpAddress, Port);
                _stream = _client.GetStream();
                _stream.ReadTimeout = 30000;
                _stream.WriteTimeout = 30000;

                // Read welcome (accept any non-empty response)
                var buf = new byte[256];
                int n = _stream.Read(buf, 0, buf.Length);
                if (n == 0) return false;

                // Check version (accept any non-empty response)
                string ver = null;
                if (!ReadCmd(MSG_CMD_VER, ref ver) || string.IsNullOrEmpty(ver)) return false;

                IsConnected = true;
                return true;
            }
            catch { IsConnected = false; return false; }
        });

    public void Disconnect()
    {
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        IsConnected = false;
    }

    public async Task<bool> SwitchToManualAsync() =>
        await Task.Run(() => WriteCmd(MSG_CMD_MODE_MANUAL));

    public async Task<bool> SwitchToAutoAsync() =>
        await Task.Run(() => WriteCmd(MSG_CMD_MODE_AUTO));

    public async Task<bool> AutoRunAsync(int procedure) =>
        await Task.Run(() =>
        {
            if (!WriteCmd(MSG_CMD_AUTO_RUN + " " + procedure + MSG_END)) return false;
            for (int i = 0; i < 60 * 2; i++)
            {
                byte status = 0;
                if (ReadOutputIO(4, ref status) && status == 0) return true;
                Thread.Sleep(500);
            }
            return false;
        });

    /// <param name="xUm">X in µm (mm × 1000). Range: 0–300,000.</param>
    /// <param name="yUm">Y in µm (mm × 1000). Range: 0–300,000.</param>
    /// <param name="zUm">Z in µm (mm × 1000). Range: 0–100,000.</param>
    public async Task<bool> MoveAbsAsync(int xUm, int yUm, int zUm, int speed) =>
        await Task.Run(() =>
        {
            if (xUm < 0 || xUm > 300_000 || yUm < 0 || yUm > 300_000
                || zUm < 0 || zUm > 100_000 || speed < 0 || speed > 800)
                return false;
            // Hardware ×0.5 scale modification: divide by 2 before sending
            return WriteCmd($"{MSG_CMD_MOVE_L} {xUm / 2} {yUm / 2} {zUm / 2} 0, V={speed}{MSG_END}");
        });

    /// <param name="dxUm">Incremental X in µm. Range: ±300,000.</param>
    /// <param name="dyUm">Incremental Y in µm. Range: ±300,000.</param>
    /// <param name="dzUm">Incremental Z in µm. Range: ±100,000.</param>
    public async Task<bool> MoveIncAsync(int dxUm, int dyUm, int dzUm, int speed) =>
        await Task.Run(() =>
        {
            if (dxUm < -300_000 || dxUm > 300_000 || dyUm < -300_000 || dyUm > 300_000
                || dzUm < -100_000 || dzUm > 100_000 || speed < 0 || speed > 800)
                return false;
            return WriteCmd($"{MSG_CMD_MOVEI_L} {dxUm / 2} {dyUm / 2} {dzUm / 2} 0, V={speed}{MSG_END}");
        });

    public async Task<bool> ReturnToOriginAsync() =>
        await Task.Run(() =>
        {
            string data = null;
            return ReadCmd(MSG_CMD_ABSRST, ref data) && data == MSG_DATA_ABSRST;
        });

    public async Task<string[]> GetPositionAsync() =>
        await Task.Run(() =>
        {
            string data = null;
            if (!ReadCmd(MSG_CMD_WHERE, ref data)) return null;
            return data.Split(' ');
        });

    private bool ReadOutputIO(int bitIndex, ref byte status)
    {
        string data = null;
        if (!ReadCmd(MSG_CMD_READ_OUTPUT, ref data) || data.Length < 17) return false;
        status = (byte)(Convert.ToUInt16(data.Substring(15, 2) + data.Substring(12, 2), 16) >> (bitIndex - 1) & 0x0001);
        return true;
    }

    private bool ReadCmd(string cmd, ref string response)
    {
        try
        {
            _stream.Write(Encoding.ASCII.GetBytes(cmd));
            var buf = new byte[256];
            int n = _stream.Read(buf, 0, buf.Length);
            response = Encoding.ASCII.GetString(buf, 0, n);
            return true;
        }
        catch { return false; }
    }

    private bool WriteCmd(string cmd)
    {
        string _ = null;
        return ReadCmd(cmd, ref _);
    }

    public void Dispose() => Disconnect();
}
