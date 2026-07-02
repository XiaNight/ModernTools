namespace Base.Components;

/// <summary>
/// Implemented by every per-type configuration editor control. The <see cref="ConfigDialog"/>
/// instantiates the appropriate editor, calls <see cref="Bind"/>, and drops the control into the
/// row; all value get/set/validate/read-back logic lives inside the control itself.
/// </summary>
public interface IConfigEditor
{
    /// <summary>Wires the editor to the supplied member and initialises its displayed value.</summary>
    void Bind(ConfigItem item);
}
