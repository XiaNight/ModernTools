using System.IO.Ports;

namespace MouseATE.Hardware;

/// <summary>
/// Controls the HF-10 load cell / force gauge via serial ASCII protocol.
/// Used to find the zero-force reference point during Z-axis tests.
/// </summary>
public class HF10Controller : IDisposable
{
    private SerialPort _port;

    public string PortName { get; set; } = "COM5";
    public int BaudRate { get; set; } = 9600;
    public bool IsConnected { get; private set; }

    private const string CMD_TRACK = "01RETR\r\n";
    private const string CMD_TRACK_FAST = "01REFF\r\n";
    private const string CMD_ZERO = "01WRFZ\r\n";
    private const string RESP_OK = "OK\r";

    public async Task<bool> ConnectAsync() =>
        await Task.Run(() =>
        {
            try
            {
                _port = new SerialPort(PortName, BaudRate) { ReadTimeout = 500, WriteTimeout = 500 };
                _port.Open();
                if (!TareInternal()) { _port.Close(); return false; }
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

    /// <summary>Tare (zero) the load cell.</summary>
    public async Task<bool> TareAsync() => await Task.Run(TareInternal);

    /// <summary>Read current force value (raw integer from gauge).</summary>
    public async Task<(bool ok, int value)> ReadAsync() =>
        await Task.Run(() => ReadInternal(CMD_TRACK));

    /// <summary>Read at high speed (for actuation detection).</summary>
    public async Task<(bool ok, int value)> ReadFastAsync() =>
        await Task.Run(() => ReadInternal(CMD_TRACK_FAST));

    private bool TareInternal()
    {
        try
        {
            _port.Write(CMD_ZERO);
            string resp = _port.ReadLine();
            return resp.TrimEnd() == "OK";
        }
        catch { return false; }
    }

    private (bool ok, int value) ReadInternal(string cmd)
    {
        try
        {
            _port.Write(cmd);
            string line = _port.ReadLine();
            if (line == "NG" || line == "NO") return (false, 0);
            int v = Convert.ToInt32(line.Substring(0, 8));
            return (true, v);
        }
        catch { return (false, 0); }
    }

    public void Dispose() => Disconnect();
}
