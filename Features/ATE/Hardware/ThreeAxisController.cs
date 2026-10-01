namespace MouseATE.Hardware;

/// <summary>
/// Orchestrates the three-axis robot fixture.
/// Primary: JTB500 (XY, TCP) + TC100 (Z, serial).
/// Fallback: JTHS300 (XYZ all-in-one, TCP).
/// All public methods accept real-world mm values.
/// </summary>
public class ThreeAxisController : IDisposable
{
    public JTB500Controller JTB500 { get; } = new();
    public TC100Controller TC100 { get; } = new();
    public JTHS300Controller JTHS300 { get; } = new();
    public HF10Controller HF10 { get; } = new();

    public enum MachineType { JTB500_TC100, JTHS300 }
    public MachineType ActiveMachine { get; private set; } = MachineType.JTB500_TC100;
    public bool IsConnected => ActiveMachine == MachineType.JTHS300
        ? JTHS300.IsConnected
        : JTB500.IsConnected && TC100.IsConnected;

    public async Task<bool> ConnectAsync(MachineType preferredMachine = MachineType.JTB500_TC100)
    {
        if (preferredMachine == MachineType.JTB500_TC100)
        {
            bool jtbOk = await JTB500.ConnectAsync();
            bool tc100Ok = await TC100.ConnectAsync();
            if (jtbOk && tc100Ok)
            {
                ActiveMachine = MachineType.JTB500_TC100;
                return true;
            }
            // Fallback to all-in-one
            JTB500.Disconnect();
            TC100.Disconnect();
        }

        bool jthsOk = await JTHS300.ConnectAsync();
        if (jthsOk)
        {
            ActiveMachine = MachineType.JTHS300;
            return true;
        }
        return false;
    }

    public void Disconnect()
    {
        JTB500.Disconnect();
        TC100.Disconnect();
        JTHS300.Disconnect();
    }

    public async Task<bool> InitializeAsync()
    {
        if (ActiveMachine == MachineType.JTB500_TC100)
        {
            bool tc100Ok = await TC100.InitializeAsync();
            bool jtbOk = await JTB500.ReturnToOriginAsync();
            return tc100Ok && jtbOk;
        }
        return await JTHS300.ReturnToOriginAsync();
    }

    public async Task<bool> SwitchToManualAsync()
    {
        if (ActiveMachine == MachineType.JTB500_TC100)
            return await JTB500.SwitchToManualAsync();
        return await JTHS300.SwitchToManualAsync();
    }

    public async Task<bool> SwitchToAutoAsync()
    {
        if (ActiveMachine == MachineType.JTB500_TC100)
            return await JTB500.SwitchToAutoAsync();
        return await JTHS300.SwitchToAutoAsync();
    }

    public async Task<bool> AutoRunAsync(int procedure)
    {
        if (ActiveMachine == MachineType.JTB500_TC100)
            return await JTB500.AutoRunAsync(procedure);
        return await JTHS300.AutoRunAsync(procedure);
    }

    /// <summary>Move to absolute XY position.</summary>
    /// <param name="xMm">X in mm. JTB500: 0–500 mm. JTHS300: 0–300 mm.</param>
    /// <param name="yMm">Y in mm.</param>
    /// <param name="speed">Speed 0–800.</param>
    public async Task<bool> MoveXYAbsAsync(double xMm, double yMm, int speed)
    {
        int xUm = (int)(xMm * 1000);
        int yUm = (int)(yMm * 1000);
        if (ActiveMachine == MachineType.JTB500_TC100)
            return await JTB500.MoveAbsAsync(xUm, yUm, speed);
        return await JTHS300.MoveAbsAsync(xUm, yUm, 0, speed);
    }

    /// <summary>Move XY incrementally.</summary>
    public async Task<bool> MoveXYIncAsync(double dxMm, double dyMm, int speed)
    {
        int dxUm = (int)(dxMm * 1000);
        int dyUm = (int)(dyMm * 1000);
        if (ActiveMachine == MachineType.JTB500_TC100)
            return await JTB500.MoveIncAsync(dxUm, dyUm, speed);
        return await JTHS300.MoveIncAsync(dxUm, dyUm, 0, speed);
    }

    /// <summary>Move to absolute Z position.</summary>
    /// <param name="zMm">Z in mm. TC100: 0–100 mm. JTHS300 Z: 0–100 mm.</param>
    public async Task<bool> MoveZAbsAsync(double zMm, int speed)
    {
        if (ActiveMachine == MachineType.JTB500_TC100)
        {
            // TC100 uses 0.01mm units (mm × 100)
            int zUnits = (int)(zMm * 100);
            await TC100.SetSpeedAsync((ushort)speed);
            return await TC100.MoveAbsAsync(zUnits);
        }
        int zUm = (int)(zMm * 1000);
        return await JTHS300.MoveAbsAsync(0, 0, zUm, speed);
    }

    /// <summary>Move Z incrementally.</summary>
    public async Task<bool> MoveZIncAsync(double dzMm, int speed)
    {
        if (ActiveMachine == MachineType.JTB500_TC100)
        {
            int dz = (int)(dzMm * 100);
            await TC100.SetSpeedAsync((ushort)speed);
            return await TC100.MoveIncAsync(dz);
        }
        int dzUm = (int)(dzMm * 1000);
        return await JTHS300.MoveIncAsync(0, 0, dzUm, speed);
    }

    /// <summary>Move XY and Z simultaneously (JTHS300 only).</summary>
    public async Task<bool> MoveXYZAbsAsync(double xMm, double yMm, double zMm, int speed)
    {
        if (ActiveMachine == MachineType.JTHS300)
            return await JTHS300.MoveAbsAsync((int)(xMm * 1000), (int)(yMm * 1000), (int)(zMm * 1000), speed);

        // For JTB500+TC100, move XY and Z sequentially
        bool xyOk = await MoveXYAbsAsync(xMm, yMm, speed);
        bool zOk = await MoveZAbsAsync(zMm, speed);
        return xyOk && zOk;
    }

    public void Dispose()
    {
        JTB500.Dispose();
        TC100.Dispose();
        JTHS300.Dispose();
        HF10.Dispose();
    }
}
