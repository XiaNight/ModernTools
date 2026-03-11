using System.Reflection;

namespace Base.Core
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class PathAttribute(params string[] path) : Attribute
    {
        public string[] Path { get; } = path ?? [];

        public static string[] GetPath(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return [];

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = instance.GetType();

            var prop = type.GetProperty(memberName, flags);
            if (prop != null)
                return prop.GetCustomAttribute<PathAttribute>(true)?.Path ?? [];

            var field = type.GetField(memberName, flags);
            if (field != null)
                return field.GetCustomAttribute<PathAttribute>(true)?.Path ?? [];

            return [];
        }
    }
}
