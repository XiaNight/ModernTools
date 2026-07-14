namespace Base.Core
{
    /// <summary>
    /// Optional presentation hint for a <see cref="ConfigAttribute"/> member, used to pick a more
    /// specific editor than the default one inferred from the member's type.
    /// </summary>
    public enum ConfigType
    {
        /// <summary>Pick the editor automatically from the member type (default).</summary>
        Auto = 0,

        /// <summary>
        /// Render a slider. Applies to numeric / floating members. Uses <see cref="ConfigAttribute.Min"/>
        /// and <see cref="ConfigAttribute.Max"/> as the slider range (defaults to 0..100 when unset).
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
    }

    /// <summary>
    /// Marks a field or property as user-configurable. Members decorated with this attribute are
    /// automatically discovered by <see cref="Base.Components.ConfigDialog"/> and rendered with an
    /// editor appropriate to their type (text box, numeric box, enum combo box, boolean toggle, ...).
    /// <para>
    /// The change is applied to the live member instantly; no explicit save step is required. Fields
    /// that additionally carry <see cref="PersistAttribute"/> are persisted through the normal persist
    /// pipeline on application quit.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ConfigAttribute : Attribute
    {
        /// <summary>
        /// Logical key for this configuration entry. Defaults to the member name when left empty.
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
        /// shown. Evaluated when the config dialog opens. May refer to a bool field, a readable bool
        /// property, or a parameterless method returning bool (searched up the type hierarchy,
        /// including non-public members). When the member cannot be resolved the entry is shown.
        /// <para>
        /// C# attributes cannot take a lambda, so pass the member name — ideally with
        /// <c>nameof</c>: <c>[Config(Condition = nameof(CanRecord))]</c>.
        /// </para>
        /// </summary>
        public string Condition { get; set; }

        /// <summary>
        /// Optional name of a parameterless method on the same object, invoked after the member's
        /// value is changed through the config dialog. Use it to react to edits (refresh a view,
        /// re-run a computation, ...). The callback fires only when the value actually changes, and
        /// after any custom setter normalisation has been applied. May refer to a parameterless
        /// method (searched up the type hierarchy, including non-public members). Unresolved names
        /// are ignored.
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
        public ConfigAttribute(string key = "") => Key = key;
    }
}
