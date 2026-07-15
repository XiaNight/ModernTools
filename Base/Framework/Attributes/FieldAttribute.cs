namespace Base.Core
{
	/// <summary>
	/// Shared base for reflection-driven, editor-backed attributes such as
	/// <see cref="ConfigAttribute"/> (per-page configuration) and <see cref="SettingAttribute"/>
	/// (app / feature-wide settings). It holds every presentation knob the editor controls understand
	/// (label, tooltip, header, help box, editor hint, validation bounds, change / condition hooks).
	/// <para>
	/// The two systems differ in <em>discovery</em> and <em>scope</em>, not in how a member is rendered —
	/// so both funnel through the same editor controls by exposing their knobs here. This type is
	/// abstract and is never applied directly; apply <see cref="ConfigAttribute"/> or
	/// <see cref="SettingAttribute"/> instead.
	/// </para>
	/// </summary>
	public abstract class FieldAttribute : Attribute
	{
		/// <summary>
		/// Logical key for this entry. Defaults to the member name when left empty. For settings this
		/// also forms the persistence key.
		/// </summary>
		public string Key { get; set; }

		/// <summary>
		/// Display label shown next to the editor. Defaults to <see cref="Key"/> (and therefore the
		/// member name) when left empty.
		/// </summary>
		public string Name { get; set; }

		/// <summary>Optional descriptive text shown as a tooltip on the row's label.</summary>
		public string Description { get; set; }

		/// <summary>
		/// Optional header rendered as a title directly above this field (similar to Unity's
		/// <c>[Header]</c> attribute). Purely decorative — it does not group members.
		/// </summary>
		public string Header { get; set; }

		/// <summary>
		/// Optional hint shown as a native tooltip when the cursor hovers anywhere over the row.
		/// </summary>
		public string Hint { get; set; }

		/// <summary>
		/// Optional placeholder text shown inside the input box while it is empty. Applies to the
		/// single-line text editor (string / numeric / hex fields).
		/// </summary>
		public string Placeholder { get; set; }

		/// <summary>
		/// Optional plain-text help box rendered directly beneath this field (similar to Unity's
		/// <c>EditorGUI.HelpBox</c>). No icon is shown — just the text.
		/// </summary>
		public string HelpBox { get; set; }

		/// <summary>
		/// Optional presentation hint that overrides the editor chosen from the member type
		/// (e.g. <see cref="ConfigType.Slider"/> or <see cref="ConfigType.Hex"/>). Ignored when the
		/// hint does not apply to the member's type.
		/// </summary>
		public ConfigType Type { get; set; } = ConfigType.Auto;

		/// <summary>
		/// Optional name of a boolean member on the same object that gates whether this entry is
		/// shown. May refer to a bool field, a readable bool property, or a parameterless method
		/// returning bool (searched up the type hierarchy, including non-public members). When the
		/// member cannot be resolved the entry is shown.
		/// <para>
		/// C# attributes cannot take a lambda, so pass the member name — ideally with
		/// <c>nameof</c>: <c>[Config(Condition = nameof(CanRecord))]</c>.
		/// </para>
		/// </summary>
		public string Condition { get; set; }

		/// <summary>
		/// Optional name of a parameterless method on the same object, invoked after the member's
		/// value is changed through the editor. The callback fires only when the value actually
		/// changes, and after any custom setter normalisation has been applied. May refer to a
		/// parameterless method (searched up the type hierarchy, including non-public members).
		/// Unresolved names are ignored.
		/// <para>
		/// C# attributes cannot take a delegate, so pass the method name — ideally with
		/// <c>nameof</c>: <c>[Config(Changed = nameof(OnPortChanged))]</c>.
		/// </para>
		/// </summary>
		public string Changed { get; set; }

		/// <summary>
		/// Optional regular expression used to validate string values. Ignored for non-string members.
		/// </summary>
		public string Regex { get; set; }

		/// <summary>
		/// Minimum allowed value. For numeric members this bounds the value; for string members this
		/// bounds the string length. Use <see cref="double.NaN"/> (the default) to leave unbounded.
		/// </summary>
		public double Min { get; set; } = double.NaN;

		/// <summary>
		/// Maximum allowed value. For numeric members this bounds the value; for string members this
		/// bounds the string length. Use <see cref="double.NaN"/> (the default) to leave unbounded.
		/// </summary>
		public double Max { get; set; } = double.NaN;

		/// <summary><c>true</c> when a minimum bound has been supplied.</summary>
		public bool HasMin => !double.IsNaN(Min);

		/// <summary><c>true</c> when a maximum bound has been supplied.</summary>
		public bool HasMax => !double.IsNaN(Max);

		/// <param name="key">
		/// Logical key for the entry. Defaults to the member name when omitted or empty.
		/// </param>
		protected FieldAttribute(string key = "") => Key = key;
	}
}
