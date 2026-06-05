using System.Net.Http;
using System.Net.Http.Json;

namespace MouseATE.Hardware;

/// <summary>
/// Thin HTTP client that calls the CommonProtocol RelayControlPage API
/// running at localhost:2345. No direct HID access — relies on the
/// CommonProtocol page being loaded and its device connected.
/// </summary>
public class RelayApiClient
{
    private const string BaseUrl = "http://127.0.0.1:2345";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    // Relay slot labels shown in the UI: 0=A0 … 3=A3, 4=B0, 5=B1
    public static readonly string[] SlotLabels = ["A0", "A1", "A2", "A3", "B0", "B1"];

    // Slot → (key, idx) as used by the relay board protocol
    // Group A key=0x04 indices 0–3 ; Group B key=0x03 indices 0–1
    public static (byte key, byte idx) SlotToKeyIdx(int slot) => slot switch
    {
        0 => (4, 0),
        1 => (4, 1),
        2 => (4, 2),
        3 => (4, 3),
        4 => (3, 0),
        5 => (3, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    public async Task TurnOnAsync(int slot, CancellationToken ct = default)
    {
        var (key, idx) = SlotToKeyIdx(slot);
        var content = JsonContent.Create(new { key, idx });
        var resp = await _http.PostAsync(
            $"{BaseUrl}/CommonProtocol/RelayControlPage/TurnOnRelayAsync", content, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task TurnOffAsync(int slot, CancellationToken ct = default)
    {
        var (key, idx) = SlotToKeyIdx(slot);
        var content = JsonContent.Create(new { key, idx });
        var resp = await _http.PostAsync(
            $"{BaseUrl}/CommonProtocol/RelayControlPage/TurnOffRelayAsync", content, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task AllOffAsync(params int[] slots)
    {
        foreach (int slot in slots)
            try { await TurnOffAsync(slot); } catch { }
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/api/v1/listroute");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
