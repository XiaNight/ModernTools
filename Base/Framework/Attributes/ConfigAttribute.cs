namespace Base.Core
{
	/// <summary>
	/// Optional presentation hint for a <see cref="FieldAttribute"/> member, used to pick a more
	/// specific editor than the default one inferred from the member's type.
	/// </summary>
	public enum ConfigType
	{
		/// <summary>Pick the editor automatically from the member type (default).</summary>
		Auto = 0,

		/// <summary>
		/// Render a slider. Applies to numeric / floating members. Uses <see cref="FieldAttribute.Min"/>
		/// and <see cref="FieldAttribute.Max"/> as the slider range (defaults to 0..100 when unset).
		/// </summary>
		Slider,

		/// <summary>
		/// Render a hexadecimal text box. Applies to integer members (byte, short, int, long, ...).
		/// The user may type with or without a <c>0x</c> prefix; the prefix is inserted automatically
		/// when the field loses focus. The display is zero-padded to a full byte — 2 hex digits — so
		/// values keep at least one leading zero (e.g. <c>0x00</c>, <c>0x01</c>, <c>0xFF</c>).
		/// </summary>
		Hex,

		/// <summary>
		/// Like <see cref="Hex"/>, but the display is zero-padded to 4 hex digits so leading zeros are
		/// preserved (e.g. <c>0x0000</c> stays <c>0x0000</c>). Applies to integer members.
		/// </summary>
		Short,

		/// <summary>
		/// Like <see cref="Hex"/>, but the display is zero-padded to 6 hex digits, matching a packed
		/// <c>0xRRGGBB</c> color. Leading zeros are preserved (e.g. <c>0x0000FF</c> stays
		/// <c>0x0000FF</c>). Applies to integer members.
		/// </summary>
		Hex_RGB,

		/// <summary>
		/// Like <see cref="Hex"/>, but the display is zero-padded to 8 hex digits, matching a packed
		/// <c>0xRRGGBBAA</c> color. Leading zeros are preserved (e.g. <c>0x000000FF</c> stays
		/// <c>0x000000FF</c>). Applies to integer members.
		/// </summary>
		Hex_RGBA,

		/// <summary>
		/// Render a text box with a <c>Browse…</c> button that opens a single-selection file picker.
		/// The chosen file's full path is written back to the member. Applies to string members.
		/// </summary>
		File,

		/// <summary>
		/// Render a text box with a <c>Browse…</c> button that opens a single-selection folder picker.
		/// The chosen folder's full path is written back to the member. Applies to string members.
		/// </summary>
		Folder,
	}

	/// <summary>
	/// Marks a field or property as user-configurable <em>for the page it lives on</em>. Members
	/// decorated with this attribute are discovered by <see cref="Base.Components.ConfigDialog"/> when
	/// the config button is pressed and rendered with an editor appropriate to their type (text box,
	/// numeric box, enum combo box, boolean toggle, ...).
	/// <para>
	/// The change is applied to the live member instantly; no explicit save step is required. Fields
	/// that additionally carry <see cref="PersistAttribute"/> are persisted through the normal persist
	/// pipeline on application quit. For app / feature-wide options that live on a behaviour singleton
	/// and are edited from the Settings page, use <see cref="SettingAttribute"/> instead.
	/// </para>
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
	public sealed class ConfigAttribute : FieldAttribute
	{
		/// <param name="key">
		/// Logical key for the entry. Defaults to the member name when omitted or empty.
		/// </param>
		public ConfigAttribute(string key = "") : base(key) { }
	}
}
