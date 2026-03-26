using System.Windows.Controls;

namespace Base.UI.Controls;
/// <summary>
/// Interaction logic for BatteryIndicator.xaml
/// </summary>
public partial class BatteryIndicator : UserControl
{
    public const ushort batteryGlyphOffset = 0xEBA0;
    public const ushort normalOffset = 0;
    public const ushort chargingOffset = 11;
    public const ushort ecoOffset = 22;

    public bool isCharging = false;
    public byte[] level;

    public BatteryIndicator()
    {
        InitializeComponent();
    }

    public void SetBatteryStatus(bool isCharging)
    {
        this.isCharging = isCharging;
        UpdateVisual();
    }

    public void SetBatteryLevel(byte[] level)
    {
        this.level = level;
        UpdateVisual();
    }

    public void Reset()
    {
        level = null;
        isCharging = false;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (level == null || level.Length == 0)
        {
            Visibility = System.Windows.Visibility.Collapsed;
            return;
        }

        int levelPercentage = level[0] / 10;

        BatteryIcon.Glyph = ((char)(batteryGlyphOffset + GetOffset() + levelPercentage)).ToString();

        float levelPercent = level[0] / 100f;
        BatteryText.Text = levelPercent.ToString("P0");
        Visibility = System.Windows.Visibility.Visible;
    }

    private ushort GetOffset()
    {
        if (isCharging)
        {
            return chargingOffset;
        }
        else
        {
            return normalOffset;
        }
    }
}
