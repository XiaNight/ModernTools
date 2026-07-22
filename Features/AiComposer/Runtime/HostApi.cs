using System.Windows;
using System.Windows.Threading;
using AiComposer.Contracts;
using Base.Services;

namespace AiComposer.Runtime;

/// <summary>
/// Default <see cref="IHostApi"/> implementation, one per materialized page. Bridges the curated
/// surface to Base services (logging, device selection) and hides the framework types from
/// generated code. Disposed when its page is destroyed so device-event handlers don't leak.
/// </summary>
internal sealed class HostApi : IHostApi, IDisposable
{
	private readonly string pageTitle;

	public HostApi(string pageTitle)
	{
		this.pageTitle = pageTitle;
		DeviceSelection.Instance.OnActiveDeviceConnected.AddListener(RaiseDeviceChanged);
		DeviceSelection.Instance.OnActiveDeviceDisconnected.AddListener(RaiseDeviceChanged);
	}

	public event Action ActiveDeviceChanged;

	private void RaiseDeviceChanged() => ActiveDeviceChanged?.Invoke();

	public void Log(string message) => Debug.Log($"[{pageTitle}] {message}");

	public IHostDevice ActiveDevice
	{
		get
		{
			DeviceSelection.Device dev = DeviceSelection.Instance.ActiveDevice;
			return dev == null ? null : new HostDevice(dev);
		}
	}

	public bool HasActiveDevice => DeviceSelection.Instance.ActiveDevice != null;

	public void RunOnUi(Action action)
	{
		if (action == null) return;

		Dispatcher disp = Application.Current?.Dispatcher;
		if (disp == null || disp.CheckAccess())
			action();
		else
			disp.Invoke(action);
	}

	public void Dispose()
	{
		DeviceSelection.Instance.OnActiveDeviceConnected.RemoveListener(RaiseDeviceChanged);
		DeviceSelection.Instance.OnActiveDeviceDisconnected.RemoveListener(RaiseDeviceChanged);
	}

	private sealed class HostDevice(DeviceSelection.Device device) : IHostDevice
	{
		public string Name => device.productName;
		public int Vid => device.VID;
		public int Pid => device.PID;
		public bool IsConnected => true;
	}
}
