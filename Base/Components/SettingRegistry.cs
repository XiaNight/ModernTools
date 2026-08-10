using Base.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Base.Components;

/// <summary>
/// A startup-built catalogue of every <see cref="SettingAttribute"/>-decorated member found on the
/// behaviour singletons. It is populated once — right after the singletons are instantiated — and only
/// stores lightweight descriptors (owner reference, delegates, metadata); it deliberately builds no
/// WPF controls. The Settings page materialises editors from this catalogue lazily, the first time it
/// is opened, so cataloguing adds negligible startup cost.
/// <para>
/// Because a <see cref="SettingAttribute"/> implies persistence, the persisted value for each member is
/// loaded here (during <see cref="Build"/>) and written back on quit via <see cref="SaveAll"/>, using
/// the same <see cref="LocalAppDataStore"/> key scheme as the <c>[Persist]</c> pipeline.
/// </para>
/// </summary>
public sealed class SettingRegistry
{
	private static readonly Lazy<SettingRegistry> _instance = new(() => new SettingRegistry());
	public static SettingRegistry Instance => _instance.Value;

	private SettingRegistry() { }

	/// <summary>A single catalogued setting member, ready to be turned into an editor row on demand.</summary>
	public sealed class SettingEntry
	{
		/// <summary>The behaviour singleton the setting lives on.</summary>
		public object Owner { get; init; }

		/// <summary>The declaring attribute.</summary>
		public SettingAttribute Attr { get; init; }

		/// <summary>The declared member type (may be a <see cref="System.Nullable{T}"/>).</summary>
		public Type ValueType { get; init; }

		/// <summary>Resolved display label.</summary>
		public string Label { get; init; }

		/// <summary>Resolved section heading (attribute section, or the owner type name).</summary>
		public string Section { get; init; }

		/// <summary>Sort order within the section.</summary>
		public int Order { get; init; }

		/// <summary>Persistence key, <c>"{OwnerType}.{Key}"</c>.</summary>
		public string PersistKey { get; init; }

		/// <summary>Reads the live value.</summary>
		public Func<object> Get { get; init; }

		/// <summary>Writes the live value (wrapped to fire the <see cref="FieldAttribute.Changed"/> hook).</summary>
		public Action<object> Set { get; init; }

		/// <summary>Adapts this entry to the shared editor field system.</summary>
		public ConfigItem ToConfigItem() => new()
		{
			Attr = Attr,
			ValueType = ValueType,
			Label = Label,
			Get = Get,
			Set = Set,
		};
	}

	private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public |
											 BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

	private readonly List<SettingEntry> _entries = new();
	private bool _built;

	/// <summary>All catalogued settings, in discovery order.</summary>
	public IReadOnlyList<SettingEntry> Entries => _entries;

	/// <summary><c>true</c> once at least one setting has been catalogued.</summary>
	public bool HasEntries => _entries.Count > 0;

	/// <summary>
	/// Reflects every <see cref="SettingAttribute"/> member off the supplied owners (the behaviour
	/// singletons) and loads any persisted values. Safe to call once; subsequent calls are ignored.
	/// </summary>
	public void Build(IEnumerable<WpfBehaviour> owners)
	{
		if (_built) return;
		_built = true;

		if (owners == null) return;

		foreach (WpfBehaviour owner in owners)
		{
			if (owner == null) continue;

			HashSet<string> seen = new();
			for (Type t = owner.GetType(); t != null && t != typeof(object); t = t.BaseType)
			{
				foreach (FieldInfo field in t.GetFields(MemberFlags))
				{
					SettingAttribute attr = field.GetCustomAttribute<SettingAttribute>(inherit: true);
					if (attr == null || !seen.Add(field.Name)) continue;

					Add(owner, attr, field.FieldType, field.Name,
						() => field.GetValue(owner), v => field.SetValue(owner, v));
				}

				foreach (PropertyInfo prop in t.GetProperties(MemberFlags))
				{
					SettingAttribute attr = prop.GetCustomAttribute<SettingAttribute>(inherit: true);
					if (attr == null || !seen.Add(prop.Name)) continue;
					if (!prop.CanRead || !prop.CanWrite) continue;
					if (prop.GetIndexParameters().Length > 0) continue;

					Add(owner, attr, prop.PropertyType, prop.Name,
						() => prop.GetValue(owner), v => prop.SetValue(owner, v));
				}
			}
		}
	}

	private void Add(WpfBehaviour owner, SettingAttribute attr, Type valueType, string memberName,
					 Func<object> get, Action<object> set)
	{
		string suffix = string.IsNullOrWhiteSpace(attr.Key) ? memberName : attr.Key;
		string persistKey = $"{owner.GetType().Name}.{suffix}";

		// Apply the persisted value before the UI ever binds. Set directly (no Changed callback),
		// mirroring the [Persist] pipeline which restores fields without side effects.
		if (LocalAppDataStore.IsInitialised)
		{
			object loaded = LocalAppDataStore.Instance.GetUntyped(persistKey, valueType);
			if (loaded != null) set(loaded);
		}

		_entries.Add(new SettingEntry
		{
			Owner = owner,
			Attr = attr,
			ValueType = valueType,
			Label = MemberBinding.ResolveLabel(attr, memberName),
			Section = string.IsNullOrWhiteSpace(attr.Section) ? owner.GetType().Name : attr.Section,
			Order = attr.Order,
			PersistKey = persistKey,
			Get = get,
			Set = MemberBinding.WrapSet(owner, attr.Changed, get, set),
		});
	}

	/// <summary>
	/// Returns the catalogued settings grouped into sections, sections ordered by their lowest member
	/// order then name, and members within a section ordered by <see cref="SettingAttribute.Order"/>
	/// then label. Entries whose <see cref="FieldAttribute.Condition"/> currently evaluates false are
	/// omitted, so the page reflects the live state each time it is opened.
	/// </summary>
	public List<IGrouping<string, SettingEntry>> GetVisibleSections()
	{
		return _entries
			.Where(e => MemberBinding.EvaluateCondition(e.Owner, e.Attr.Condition))
			.OrderBy(e => e.Order)
			.ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
			.GroupBy(e => e.Section)
			.OrderBy(g => g.Min(e => e.Order))
			.ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>Persists the current value of every catalogued setting. Called on application quit.</summary>
	public void SaveAll()
	{
		if (!LocalAppDataStore.IsInitialised) return;

		foreach (SettingEntry entry in _entries)
			LocalAppDataStore.Instance.SetUntyped(entry.PersistKey, entry.Get(), entry.ValueType);
	}
}
