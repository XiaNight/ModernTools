using System.Windows;
using System.Windows.Controls;

namespace Base.Core
{
    public abstract class WpfBehaviourSingleton<T> : WpfBehaviour where T : WpfBehaviourSingleton<T>, new()
    {
        private static readonly Lazy<T> instance = new(() => new T());
        public static T Instance => instance.Value;

        protected WpfBehaviourSingleton() : base()
        {
            if (instance.IsValueCreated && instance.Value != this)
                throw new InvalidOperationException($"Only one instance of {typeof(T).Name} allowed.");
        }
    }

    public abstract class WpfBehaviour : UserControl
    {
        private bool isEnabled = false;
        public new bool IsEnabled => isEnabled;
        public virtual void OnApplicationQuit(System.ComponentModel.CancelEventArgs e) { }
        public virtual void Awake() {}
        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
        public virtual void OnDestroy() { }
        public virtual void ThemeChanged() { }

        protected static MainWindow Main = Application.Current.MainWindow as MainWindow
                ?? throw new InvalidOperationException("MainWindow not found or invalid.");

        public void Enable()
        {
            if (!isEnabled) OnEnable();
            isEnabled = true;
        }
        public void Disable()
        {
            if (!isEnabled) return;

            OnDisable();
            isEnabled = false;
        }

        public WpfBehaviour()
        {
            if (Application.Current.MainWindow == null)
            {
                throw new InvalidOperationException("MainWindow not found or invalid.");
            }
            Main.RegisterWpfObject(this);
        }
    }
}
