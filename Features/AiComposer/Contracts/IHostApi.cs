namespace AiComposer.Contracts;

/// <summary>
/// The curated surface that generated page logic is allowed to touch — and nothing else. Generated
/// C# is compiled with full trust (this is a local developer tool), so this interface is a
/// design-time guardrail, not a security boundary: it keeps generated pages pointed at supported
/// host services (device access, logging, UI marshalling) instead of poking at framework internals.
/// The syntax denylist (see the compiler) is the safety guardrail.
/// </summary>
public interface IHostApi
{
	/// <summary>Writes a line to the app's output log, prefixed with the page title.</summary>
	void Log(string message);

	/// <summary>The connected device, or null when nothing is connected.</summary>
	IHostDevice ActiveDevice { get; }

	/// <summary>True when a device is connected.</summary>
	bool HasActiveDevice { get; }

	/// <summary>Raised (on the UI thread) whenever the active device connects or disconnects.</summary>
	event Action ActiveDeviceChanged;

	/// <summary>Marshals <paramref name="action"/> onto the WPF UI thread.</summary>
	void RunOnUi(Action action);
}

/// <summary>Read-only identity of the connected device exposed to generated pages.</summary>
public interface IHostDevice
{
	string Name { get; }
	int Vid { get; }
	int Pid { get; }
	bool IsConnected { get; }
}
