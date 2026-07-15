using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Controls;

namespace Base.Components;

/// <summary>
/// Enum editor rendered as a combo box of the enum's values. Each entry shows the value's
/// <see cref="DescriptionAttribute"/> text when present (so members can carry a friendly label with
/// spaces / punctuation), otherwise the raw member name. The underlying enum value is what is read
/// from and written back to the bound member.
/// </summary>
public partial class ConfigEnumField : UserControl, IConfigEditor
{
	/// <summary>A combo entry pairing the real enum value with its display label.</summary>
	private sealed class Option
	{
		public object Value { get; init; }
		public string Label { get; init; }
	}

	private ConfigItem _item;
	private bool _updating;

	public ConfigEnumField()
	{
		InitializeComponent();
	}

	public void Bind(ConfigItem item)
	{
		_item = item;

		Type type = item.UnderlyingType;
		List<Option> options = new();
		foreach (object value in Enum.GetValues(type))
			options.Add(new Option { Value = value, Label = DescribeValue(type, value) });

		Combo.ItemsSource = options;
		Combo.DisplayMemberPath = nameof(Option.Label);
		Combo.SelectedValuePath = nameof(Option.Value);
		Combo.SelectedValue = item.Get();
		Combo.SelectionChanged += OnSelectionChanged;
	}

	private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_updating || Combo.SelectedValue == null) return;

		_item.Set(Combo.SelectedValue);
		_updating = true;
		Combo.SelectedValue = _item.Get();
		_updating = false;
	}

	/// <summary>Returns the member's <see cref="DescriptionAttribute"/> text, or its name as a fallback.</summary>
	private static string DescribeValue(Type enumType, object value)
	{
		string name = value.ToString();
		FieldInfo field = enumType.GetField(name);
		DescriptionAttribute desc = field?.GetCustomAttribute<DescriptionAttribute>();
		return string.IsNullOrWhiteSpace(desc?.Description) ? name : desc.Description;
	}
}
