using Base.Services;
using MouseATE.Hardware;
using MouseATE.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MouseATE.Pages;

public partial class FixtureControlView : UserControl
{
    internal ThreeAxisController Arm { get; private set; }

    public FixtureControlView()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadStoredConnectionSettings();
    }

    private void LoadStoredConnectionSettings()
    {
        var conn = AteSettingsStore.Connection;
        RobotIpBox.Text = conn.RobotIpAddress;
        RobotPortBox.Text = conn.RobotPort.ToString();
        TC100PortBox.Text = conn.Tc100Port;
        TC100BaudBox.Text = conn.Tc100BaudRate.ToString();
        HF10PortBox.Text = conn.Hf10Port;
        MachineTypeCombo.SelectedIndex = conn.UseJths300 ? 1 : 0;
    }

    private static void AppendLog(string msg) => Debug.Log("[ATE/Fixture] " + msg);

    private void SetConnectedState(bool connected)
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var btn in new Button[] {
                InitBtn, MoveXYAbsBtn, MoveZAbsBtn,
                JogXPlusBtn, JogXMinusBtn, JogYPlusBtn, JogYMinusBtn,
                JogZUpBtn, JogZDownBtn, AutoRunBtn })
                btn.IsEnabled = connected;

            ConnectBtn.IsEnabled = !connected;
            DisconnectBtn.IsEnabled = connected;

            ConnectionStatus.Text = connected ? "● Connected" : "● Disconnected";
            ConnectionStatus.Foreground = connected
                ? new SolidColorBrush(Colors.LimeGreen)
                : new SolidColorBrush(Colors.Red);
        });
    }

    private int Speed => int.TryParse(SpeedBox.Text, out int v) ? Math.Clamp(v, 0, 800) : 100;
    private double JogStep => double.TryParse(JogStepBox.Text, out double v) ? v : 10.0;
    private double ZJogStep => double.TryParse(ZJogStepBox.Text, out double v) ? v : 1.0;

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        ConnectBtn.IsEnabled = false;

        var machineType = MachineTypeCombo.SelectedIndex == 0
            ? ThreeAxisController.MachineType.JTB500_TC100
            : ThreeAxisController.MachineType.JTHS300;

        int port = int.TryParse(RobotPortBox.Text, out int p) ? p : 5000;
        int baud = int.TryParse(TC100BaudBox.Text, out int b) ? b : 19200;

        Arm = new ThreeAxisController
        {
            JTB500  = { IpAddress = RobotIpBox.Text, Port = port },
            TC100   = { PortName  = TC100PortBox.Text, BaudRate = baud },
            JTHS300 = { IpAddress = RobotIpBox.Text, Port = port },
            HF10    = { PortName  = HF10PortBox.Text }
        };

        AppendLog($"Connecting ({machineType})...");
        bool ok = await Arm.ConnectAsync(machineType);
        if (ok)
        {
            AppendLog($"Connected via {Arm.ActiveMachine}.");
            AteSession.Arm = Arm;

            // Persist connection settings on successful connect
            AteSettingsStore.Connection = new AteConnectionSettings
            {
                RobotIpAddress = RobotIpBox.Text,
                RobotPort      = port,
                Tc100Port      = TC100PortBox.Text,
                Tc100BaudRate  = baud,
                Hf10Port       = HF10PortBox.Text,
                UseJths300     = MachineTypeCombo.SelectedIndex == 1
            };
        }
        else
        {
            AppendLog("Connection failed. Check IP/COM settings.");
        }

        SetConnectedState(ok);
        if (!ok) ConnectBtn.IsEnabled = true;
    }

    private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        Arm?.Disconnect();
        AteSession.Arm = null;
        AppendLog("Disconnected.");
        SetConnectedState(false);
    }

    private async void InitBtn_Click(object sender, RoutedEventArgs e)
    {
        InitStatus.Text = "Homing...";
        bool ok = await Arm.InitializeAsync();
        InitStatus.Text = ok ? "Done." : "Home failed!";
        InitStatus.Foreground = ok
            ? new SolidColorBrush(Colors.LimeGreen)
            : new SolidColorBrush(Colors.Red);
        AppendLog(ok ? "Initialized (homed)." : "Initialization failed.");
    }

    private async void MoveXYAbsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(AbsXBox.Text, out double x) || !double.TryParse(AbsYBox.Text, out double y))
        { AppendLog("Invalid XY coordinates."); return; }

        await Arm.SwitchToManualAsync();
        bool ok = await Arm.MoveXYAbsAsync(x, y, Speed);
        AppendLog(ok ? $"Moved to X={x} Y={y} mm." : "XY move failed.");
    }

    private async void MoveZAbsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(AbsZBox.Text, out double z))
        { AppendLog("Invalid Z coordinate."); return; }

        bool ok = await Arm.MoveZAbsAsync(z, Speed);
        AppendLog(ok ? $"Moved to Z={z} mm." : "Z move failed.");
    }

    private async void JogXPlus_Click(object sender, RoutedEventArgs e)
    {
        await Arm.SwitchToManualAsync();
        await Arm.MoveXYIncAsync(JogStep, 0, Speed);
        AppendLog($"Jog X +{JogStep} mm.");
    }

    private async void JogXMinus_Click(object sender, RoutedEventArgs e)
    {
        await Arm.SwitchToManualAsync();
        await Arm.MoveXYIncAsync(-JogStep, 0, Speed);
        AppendLog($"Jog X -{JogStep} mm.");
    }

    private async void JogYPlus_Click(object sender, RoutedEventArgs e)
    {
        await Arm.SwitchToManualAsync();
        await Arm.MoveXYIncAsync(0, JogStep, Speed);
        AppendLog($"Jog Y +{JogStep} mm.");
    }

    private async void JogYMinus_Click(object sender, RoutedEventArgs e)
    {
        await Arm.SwitchToManualAsync();
        await Arm.MoveXYIncAsync(0, -JogStep, Speed);
        AppendLog($"Jog Y -{JogStep} mm.");
    }

    private async void JogZUp_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await Arm.MoveZIncAsync(ZJogStep, Speed);
        AppendLog($"Jog Z +{ZJogStep} mm" + (ok ? "." : " — failed."));
    }

    private async void JogZDown_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await Arm.MoveZIncAsync(-ZJogStep, Speed);
        AppendLog($"Jog Z -{ZJogStep} mm" + (ok ? "." : " — failed."));
    }

    private async void AutoRunBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AutoProcBox.Text, out int proc))
        { AppendLog("Invalid procedure index."); return; }

        AutoStatus.Text = "Running...";
        await Arm.SwitchToAutoAsync();
        bool ok = await Arm.AutoRunAsync(proc);
        AutoStatus.Text = ok ? "Done." : "Failed.";
        AppendLog(ok ? $"Auto procedure {proc} complete." : $"Auto procedure {proc} failed.");
    }
}
