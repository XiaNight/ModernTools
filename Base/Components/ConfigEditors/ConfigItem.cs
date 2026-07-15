using Base.Core;

namespace Base.Components;

/// <summary>
/// A single discovered <see cref="FieldAttribute"/> member (field or property) on a target object,
/// abstracted so editor controls can read / write it without caring whether it is a field or a
/// property, and reflect any custom setter behaviour by reading the value back. The same item type
/// backs both the per-page config dialog (<see cref="ConfigAttribute"/>) and the app-wide settings
/// page (<see cref="SettingAttribute"/>).
/// </summary>
public sealed class ConfigItem
{
    /// <summary>The attribute declared on the member (a <see cref="ConfigAttribute"/> or <see cref="SettingAttribute"/>).</summary>
    public FieldAttribute Attr { get; init; }

    /// <summary>The declared member type (may be a <see cref="System.Nullable{T}"/>).</summary>
    public Type ValueType { get; init; }

    /// <summary>The resolved display label for the member.</summary>
    public string Label { get; init; }

    /// <summary>Reads the current value from the target.</summary>
    public Func<object> Get { get; init; }

    /// <summary>Writes a value to the target.</summary>
    public Action<object> Set { get; init; }

    /// <summary>The underlying (non-nullable) value type.</summary>
    public Type UnderlyingType => Nullable.GetUnderlyingType(ValueType) ?? ValueType;
}
