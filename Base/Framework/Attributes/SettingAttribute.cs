namespace Base.Core
{
	/// <summary>
	/// Marks a field or property on a <see cref="WpfBehaviourSingleton{T}"/> as an app / feature-wide
	/// <em>setting</em>. Settings differ from <see cref="ConfigAttribute"/> in scope and lifetime:
	/// where a <see cref="ConfigAttribute"/> is discovered on demand from the current page, settings
	/// are catalogued once at startup — right after the behaviour singletons are instantiated — by
	/// <see cref="Base.Components.SettingRegistry"/>, and are rendered lazily only when the Settings
	/// page is opened.
	/// <para>
	/// Presence of this attribute implies persistence: the value is loaded at startup and saved on
	/// application quit through <see cref="LocalAppDataStore"/> (keyed <c>"{OwnerType}.{Key}"</c>),
	/// so a separate <see cref="PersistAttribute"/> is not required.
	/// </para>
	/// <para>
	/// All presentation knobs (label, hint, header, help box, editor <see cref="FieldAttribute.Type"/>
	/// hint, validation bounds, <see cref="FieldAttribute.Changed"/> / <see cref="FieldAttribute.Condition"/>
	/// hooks) are inherited from <see cref="FieldAttribute"/> and behave exactly as they do for config.
	/// </para>
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
	public sealed class SettingAttribute : FieldAttribute
	{
		/// <summary>
		/// Optional section this setting is grouped under on the Settings page. When left empty the
		/// owning behaviour's type name is used as the section heading.
		/// </summary>
		public string Section { get; set; }

		/// <summary>
		/// Sort order within the section (ascending). Ties are broken by label. Sections themselves are
		/// ordered by the lowest order of their members.
		/// </summary>
		public int Order { get; set; } = 0;

		/// <param name="key">
		/// Logical key for the setting, also used as the persistence key suffix. Defaults to the
		/// member name when omitted or empty.
		/// </param>
		public SettingAttribute(string key = "") : base(key) { }
	}
}
