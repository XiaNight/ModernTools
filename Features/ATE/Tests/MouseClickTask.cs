using MouseATE.Hardware;

namespace MouseATE.Tests;

public record ClickCycleProgress(int Attempted, int Pass, int Miss, int Extra, int Total);

public record ClickTestResult(int Attempted, int Pass, int Miss, int Extra)
{
    public double PassRatePct => Attempted > 0 ? (double)Pass / Attempted * 100.0 : 100.0;
    public bool   IsPassed    => Miss == 0 && Extra == 0;
}

public class MouseClickTask
{
    private readonly MouseButton    _button;
    private readonly int            _relaySlot;
    private readonly RelayApiClient _relay;
    private readonly MouseHookService _hook;

    public int TotalClicks   { get; set; } = 1000;
    public int SolenoidOnMs  { get; set; } = 100;
    public int ClickWindowMs { get; set; } = 50;
    public int CoolDownMs    { get; set; } = 200;

    public IProgress<ClickCycleProgress> Progress { get; set; }
    public IProgress<string>             Log      { get; set; }

    public MouseClickTask(MouseButton button, int relaySlot, RelayApiClient relay, MouseHookService hook)
    {
        _button    = button;
        _relaySlot = relaySlot;
        _relay     = relay;
        _hook      = hook;
    }

    public async Task<ClickTestResult> RunAsync(CancellationToken ct)
    {
        int pass = 0, miss = 0, extra = 0;

        for (int i = 1; i <= TotalClicks; i++)
        {
            ct.ThrowIfCancellationRequested();

            _hook.ResetCount(_button);

            await _relay.TurnOnAsync(_relaySlot, ct);
            await Task.Delay(SolenoidOnMs, ct);
            await _relay.TurnOffAsync(_relaySlot, ct);
            await Task.Delay(ClickWindowMs, ct);

            int count = _hook.GetCount(_button);

            if (count == 1)      { pass++; }
            else if (count == 0) { miss++;  Log?.Report($"  [MISS] Cycle {i}: no click"); }
            else                 { extra++; Log?.Report($"  [EXTRA] Cycle {i}: {count} clicks"); }

            Progress?.Report(new ClickCycleProgress(i, pass, miss, extra, TotalClicks));

            if (i < TotalClicks)
                await Task.Delay(CoolDownMs, ct);
        }

        return new ClickTestResult(TotalClicks, pass, miss, extra);
    }
}
