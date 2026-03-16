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
        Grid Root { get; }
        string PageName { get; }
        string Description { get; }
        bool ShowDeviceSelection { get; }
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
        public virtual bool ShowDeviceSelection { get; } = true;
        protected static DeviceSelection.Device ActiveDevice => DeviceSelection.Instance.ActiveDevice;

        protected Grid root;
        Grid IPageBase.Root => root;
        
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
