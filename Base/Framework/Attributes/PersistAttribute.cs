namespace Base.Core
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class PersistAttribute : Attribute
    {
        /// <summary>
        /// The key suffix used in LocalAppDataStore. Full key = "{ClassName}.{Key}".
        /// Defaults to the field name if empty.
        /// </summary>
        public string Key { get; }
        public PersistAttribute(string key = "") => Key = key;
    }
}
