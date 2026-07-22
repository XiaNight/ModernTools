using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Base.Pages
{
    using Core;
    using Services;
    using System.Windows.Media;

    internal interface IPageBase
    {
        void Enable();
        void Disable();
        string PageName { get; }
        string Description { get; }
        bool ShowDeviceSelection { get; }
    }

    /// <summary>
    /// This is a basic tab page.
    /// </summary>
    public abstract class PageBase : WpfBehaviour, IPageBase
    {
        /// <summary>
        /// Navigation metadata, resolved once from the <see cref="PageInfoAttribute"/> on the
        /// concrete page type. Every concrete page must be decorated with [PageInfo].
        /// </summary>
        private readonly PageInfoAttribute _info;

        protected PageBase()
        {
            _info = GetType().GetCustomAttribute<PageInfoAttribute>(inherit: false)
                ?? throw new InvalidOperationException(
                    $"{GetType().FullName} must be decorated with [PageInfo(...)].");
        }

        // Read-only metadata sourced from [PageInfo]. These are intentionally non-virtual:
        // pages declare their metadata via the attribute, not by overriding properties.
        public string PageName => _info.PageName;
        public string Glyph => _info.Glyph;
        public string SecondaryGlyph => _info.SecondaryGlyph;
        public string ShortName => _info.ShortName;
        public string Description => _info.Description;
        public NavigationAlignment NavAlignment =>
            _info.NavAlignment == 1 ? NavigationAlignment.Back : NavigationAlignment.Front;
        public int NavOrder => _info.NavOrder;
        public bool ShowDeviceSelection => _info.ShowDeviceSelection;
        // Named NavPath (not Path) to avoid colliding with System.IO.Path inside page classes.
        public string[] NavPath => _info.Path;
        protected static DeviceSelection.Device ActiveDevice => DeviceSelection.Instance.ActiveDevice;

        protected Grid root;

        private CancellationTokenSource cts;
        private Task updateLoop;

        public override void Awake()
        {
            base.Awake();
            FormPage();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            CompositionTarget.Rendering += OnRendering;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            Update();
        }

        public static T Create<T>() where T : PageBase, new()
        {
            return new T();
        }

        [Obsolete("FormPage is deprecated and will be removed in a future release.")]
        private void FormPage()
        {
            Content ??= new Grid();
            root = Content as Grid;
        }

        protected virtual void Update() { }

        public enum NavigationAlignment
        {
            Front,
            Back,
        }
    }
}
