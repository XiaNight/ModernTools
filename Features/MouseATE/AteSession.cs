using MouseATE.Hardware;

namespace MouseATE;

/// <summary>
/// Shared singleton state for the ATE feature.
/// The Fixture Control page sets <see cref="Arm"/> on connect;
/// all test pages read from it.
/// </summary>
public static class AteSession
{
    public static ThreeAxisController Arm { get; set; }
}
