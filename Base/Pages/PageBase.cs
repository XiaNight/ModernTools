using System.Windows;
using System.Windows.Controls;

namespace Base.Pages
{
    using Core;
    using Services;
    using Windows.Foundation.Metadata;

    public interface IPageBase
    {
        void Enable();
        void Disable();
        Grid Root { get; }
        string PageName { get; }
        string Description { get; }
    }

    /// <summary>
    /// This is a basic tab page.
    /// </summary>
    public abstract class PageBase : WpfBehaviour, IPageBase
    {
        public abstract string PageName { get; }
        public virtual string Glyph { get; } = "\uE878";
        public virtual string SecondaryGlyph { get; } = "";
        public virtual string ShortName { get; } = "";
        public virtual string Description { get; } = "There is no description for this page.";
        public virtual NavigationAlignment NavAlignment { get; } = NavigationAlignment.Front;
        public virtual int NavOrder { get; } = int.MaxValue;
        protected static DeviceSelection.Device ActiveDevice => DeviceSelection.Instance.ActiveDevice;

        [Deprecated("Use ActiveDevice instead.", DeprecationType.Deprecate, 0x0107)]
        protected static Base.Services.Peripheral.PeripheralInterface ActiveInterface => DeviceSelection.Instance.ActiveInterface;
        protected Grid root;
        Grid IPageBase.Root => root;
        
        private CancellationTokenSource cts;
        private Task updateLoop;

        public override void Awake()
        {
            base.Awake();
            FormPage();
        }

        public static T Create<T>() where T : PageBase, new()
        {
            return new T();
        }

        private void FormPage()
        {
            Content ??= new Grid();
            root = Content as Grid;
        }

        protected void StartLoop(double fps = 60)
        {
            if (updateLoop != null) return;

            cts = new CancellationTokenSource();
            updateLoop = Task.Run(() => LoopAsync(fps, cts.Token), cts.Token);
        }

        protected void StopLoop()
        {
            if (updateLoop != null) cts.Cancel();
            updateLoop = null;
        }

        private async Task LoopAsync(double fps, CancellationToken token)
        {
            try
            {
                var frameTime = TimeSpan.FromMilliseconds(1000.0 / fps);
                var stopwatch = new System.Diagnostics.Stopwatch();

                while (!token.IsCancellationRequested)
                {
                    // Reset the stopwatch
                    stopwatch.Restart();

                    // Call main loop
                    await Application.Current.Dispatcher.InvokeAsync(Update);

                    // Calculated delay to maintain frame rate
                    var elapsed = stopwatch.Elapsed;
                    var remaining = frameTime - elapsed;
                    if (remaining > TimeSpan.Zero)
                        await Task.Delay(remaining, token);
                }
            }
            catch (TaskCanceledException)
            {
                // Expected on cancellation: do nothing or log if needed
            }
            catch (Exception ex)
            {
                Debug.Log($"[Error] Page Update: {ex}");
            }
        }

        protected virtual void Update() { }

        public enum NavigationAlignment
        {
            Front,
            Back,
        }
    }
}
