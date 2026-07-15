using Base.Core;
using System.Reflection;

namespace Base.Components;

/// <summary>
/// Shared reflection helpers for turning a <see cref="FieldAttribute"/>-decorated member into an
/// editable binding: label resolution, visibility conditions, and change callbacks. Used by both the
/// per-page <see cref="ConfigDialog"/> discovery and the app-wide <see cref="SettingRegistry"/>, so
/// the two systems resolve <see cref="FieldAttribute.Condition"/> / <see cref="FieldAttribute.Changed"/>
/// identically.
/// </summary>
internal static class MemberBinding
{
	private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public |
									   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

	/// <summary>Label priority: explicit Name, then Key, then the member name.</summary>
	public static string ResolveLabel(FieldAttribute attr, string memberName)
	{
		if (!string.IsNullOrWhiteSpace(attr.Name)) return attr.Name;
		if (!string.IsNullOrWhiteSpace(attr.Key)) return attr.Key;
		return memberName;
	}

	/// <summary>
	/// Evaluates a <see cref="FieldAttribute.Condition"/> against the target. The condition names a
	/// bool field, readable bool property, or parameterless bool method (searched up the hierarchy,
	/// including non-public members). Returns <c>true</c> (show) when empty or unresolvable.
	/// </summary>
	public static bool EvaluateCondition(object target, string condition)
	{
		if (string.IsNullOrWhiteSpace(condition)) return true;

		for (Type t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
		{
			PropertyInfo prop = t.GetProperty(condition, Flags);
			if (prop != null && prop.CanRead && prop.PropertyType == typeof(bool))
				return (bool)prop.GetValue(target);

			FieldInfo field = t.GetField(condition, Flags);
			if (field != null && field.FieldType == typeof(bool))
				return (bool)field.GetValue(target);

			MethodInfo method = t.GetMethod(condition, Flags, binder: null, types: Type.EmptyTypes, modifiers: null);
			if (method != null && method.ReturnType == typeof(bool))
				return (bool)method.Invoke(target, null);
		}

		return true; // unresolved — default to showing the entry
	}

	/// <summary>
	/// Wraps a member's setter so that a <see cref="FieldAttribute.Changed"/> callback is invoked
	/// after the value is written — but only when the value actually changes (compared after any
	/// custom setter normalisation). When no callback is configured or it cannot be resolved, the
	/// original setter is returned unchanged.
	/// </summary>
	public static Action<object> WrapSet(object target, string changed, Func<object> get, Action<object> set)
	{
		if (string.IsNullOrWhiteSpace(changed)) return set;

		Action callback = ResolveChangedCallback(target, changed);
		if (callback == null) return set;

		return v =>
		{
			object before = get();
			set(v);
			if (!Equals(before, get()))
				callback();
		};
	}

	/// <summary>
	/// Resolves a <see cref="FieldAttribute.Changed"/> callback: a parameterless method on the target
	/// (searched up the hierarchy, including non-public members). Returns <c>null</c> when the name
	/// cannot be resolved.
	/// </summary>
	public static Action ResolveChangedCallback(object target, string name)
	{
		for (Type t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
		{
			MethodInfo method = t.GetMethod(name, Flags, binder: null, types: Type.EmptyTypes, modifiers: null);
			if (method != null)
				return () => method.Invoke(target, null);
		}

		return null;
	}
}
