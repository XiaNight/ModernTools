using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;

namespace Base.Core
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class AppMenuItemAttribute : Attribute
    {
        public string Path { get; }
        public bool StayOpen { get; init; } = false;

        public Key Key { get; init; } = Key.None;
        public ModifierKeys ModifierKeys { get; init; } = ModifierKeys.None;

        // Positional arguments (match parameter order)
        public object[] Args { get; init; }

        // Named arguments (match by parameter name; names are case-insensitive)
        public string[] Names { get; init; }
        public object[] Values { get; init; }

        public AppMenuItemAttribute(string path) => Path = path;
        public AppMenuItemAttribute(string path, bool stayOpen, Key key = Key.None, ModifierKeys modifierKeys = ModifierKeys.None)
        {
            Path = path;
            StayOpen = stayOpen;
            Key = key;
            ModifierKeys = modifierKeys;
        }

        public AppMenuItemAttribute(string path, params object[] args)
        {
            Path = path;
            Args = args;
        }
        public AppMenuItemAttribute(string path, bool stayOpen = false, Key key = Key.None, ModifierKeys modifierKeys = ModifierKeys.None, params object[] args)
        {
            Path = path;
            StayOpen = stayOpen;
            Key = key;
            ModifierKeys = modifierKeys;
            Args = args;
        }

        public AppMenuItemAttribute(string path, string[] names, object[] values)
        {
            Path = path;
            Names = names;
            Values = values;
        }
        public AppMenuItemAttribute(string path, bool stayOpen, string[] names, object[] values)
        {
            Path = path;
            StayOpen = stayOpen;
            Names = names;
            Values = values;
        }

        public static List<AppMenuItemRegestry> GetAppMenuItemRegestry(object targetOrType)
        {
            if (targetOrType is null) throw new ArgumentNullException(nameof(targetOrType));

            var type = targetOrType as Type ?? targetOrType.GetType();
            var isStaticOnly = targetOrType is Type;
            var results = new List<AppMenuItemRegestry>();

            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                              .Where(m => m.IsDefined(typeof(AppMenuItemAttribute), false));

            foreach (var m in methods)
            {
                if (isStaticOnly && !m.IsStatic) continue;

                var attrs = (AppMenuItemAttribute[])m.GetCustomAttributes(typeof(AppMenuItemAttribute), false);
                foreach (var attr in attrs)
                {
                    var values = BuildArgumentArray(m, attr);

                    Action action = () =>
                    {
                        var instance = m.IsStatic ? null :
                            (isStaticOnly ? throw new InvalidOperationException($"Instance method '{m.Name}' requires a target instance.") : targetOrType);

                        var result = m.Invoke(instance, values);

                        if (result is Task task)
                        {
                            // Fire-and-forget
                            _ = task;
                        }
                    };

                    results.Add(new AppMenuItemRegestry(attr.Path, attr.StayOpen, action, attr.Key, attr.ModifierKeys));
                }
            }

            return results;
        }

        private static object?[] BuildArgumentArray(MethodInfo method, AppMenuItemAttribute attr)
        {
            var ps = method.GetParameters();
            var args = new object?[ps.Length];

            // Fill with defaults (if any)
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].HasDefaultValue)
                {
                    args[i] = ps[i].DefaultValue;
                }
                else if (ps[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(ps[i].ParameterType) == null)
                {
                    args[i] = Activator.CreateInstance(ps[i].ParameterType);
                }
                else
                {
                    args[i] = null;
                }
            }

            // Apply positional
            if (attr.Args is { Length: > 0 })
            {
                if (attr.Args.Length > ps.Length)
                    throw new TargetParameterCountException($"Too many positional args for method '{method.Name}'.");

                for (int i = 0; i < attr.Args.Length; i++)
                    args[i] = ConvertToParameterType(attr.Args[i], ps[i].ParameterType);
            }

            // Apply named
            if ((attr.Names is { Length: > 0 }) || (attr.Values is { Length: > 0 }))
            {
                if (attr.Names is null || attr.Values is null || attr.Names.Length != attr.Values.Length)
                    throw new ArgumentException($"Named argument arrays must be same length for method '{method.Name}'.");

                for (int i = 0; i < attr.Names.Length; i++)
                {
                    var name = attr.Names[i];
                    var idx = Array.FindIndex(ps, p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0)
                        throw new ArgumentException($"No parameter named '{name}' on method '{method.DeclaringType?.Name}.{method.Name}'.");

                    args[idx] = ConvertToParameterType(attr.Values[i], ps[idx].ParameterType);
                }
            }

            return args;
        }

        private static object? ConvertToParameterType(object? value, Type parameterType)
        {
            if (value is null) return null;

            var t = Nullable.GetUnderlyingType(parameterType) ?? parameterType;

            // Enum from string or numeric
            if (t.IsEnum)
            {
                if (value is string s) return Enum.Parse(t, s, ignoreCase: true);
                return Enum.ToObject(t, System.Convert.ChangeType(value, Enum.GetUnderlyingType(t)));
            }

            // Type match
            if (t.IsInstanceOfType(value)) return value;

            // Special case: char from string
            if (t == typeof(char) && value is string str && str.Length == 1) return str[0];

            // Convert.ChangeType for primitives
            return System.Convert.ChangeType(value, t);
        }
    }

    public record AppMenuItemRegestry(string Path, bool StayOpen, Action Action, Key key = Key.None, ModifierKeys modifierKeys = ModifierKeys.None);
}
