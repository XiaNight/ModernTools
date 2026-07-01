using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Base.Core
{
    public abstract class WpfBehaviourSingleton<T> : WpfBehaviour where T : WpfBehaviourSingleton<T>, new()
    {
        private static readonly Lazy<T> instance = new(() => new T());
        public static T Instance => instance.Value;

        public override void Awake()
        {
            base.Awake();
            Dispatcher.Invoke(Start, DispatcherPriority.Loaded);
        }

        protected WpfBehaviourSingleton() : base()
        {
            if (instance.IsValueCreated && instance.Value != this)
                throw new InvalidOperationException($"Only one instance of {typeof(T).Name} allowed.");
        }
    }

    public abstract class WpfBehaviour : UserControl
    {
        private bool isStarted = false;
        private bool isEnabled = false;
        public new bool IsEnabled => isEnabled;
        public virtual void OnApplicationQuit(System.ComponentModel.CancelEventArgs e)
        {
            SavePersistFields();
        }

        public virtual void Awake()
        {
            LoadPersistFields();
        }
        public virtual void Start() { }
        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
        public virtual void OnDestroy() { }
        public virtual void ThemeChanged() { }

        protected static MainWindow Main = Application.Current.MainWindow as MainWindow
                ?? throw new InvalidOperationException("MainWindow not found or invalid.");

        public void Enable()
        {
            if (!isStarted)
            {
                Start();
                isStarted = true;
            }
            if (!isEnabled) OnEnable();
            isEnabled = true;
        }
        public void Disable()
        {
            if (!isEnabled) return;

            OnDisable();
            isEnabled = false;
        }

        private static IEnumerable<FieldInfo> GetAllFields(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        private IEnumerable<(FieldInfo field, string key)> PersistFields()
        {
            foreach (var field in GetAllFields(GetType()))
            {
                var attr = field.GetCustomAttribute<PersistAttribute>(inherit: true);
                if (attr == null) continue;
                var suffix = string.IsNullOrWhiteSpace(attr.Key) ? field.Name : attr.Key;
                yield return (field, $"{GetType().Name}.{suffix}");
            }
        }

        private void LoadPersistFields()
        {
            if (!LocalAppDataStore.IsInitialised) return;
            foreach (var (field, key) in PersistFields())
            {
                var loaded = LocalAppDataStore.Instance.GetUntyped(key, field.FieldType);
                if (loaded != null) field.SetValue(this, loaded);
            }
        }

        private void SavePersistFields()
        {
            if (!LocalAppDataStore.IsInitialised) return;
            foreach (var (field, key) in PersistFields())
                LocalAppDataStore.Instance.SetUntyped(key, field.GetValue(this), field.FieldType);
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
