using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiComposer.Compilation;

/// <summary>
/// A best-effort, syntax-level guardrail walked over generated C# before it is compiled. This is a
/// local developer tool with a full-trust model — the denylist is a tripwire that keeps generated
/// pages away from process launching, arbitrary file IO, reflection, unsafe code, and P/Invoke, not
/// a security sandbox. It errs on the side of rejecting: a few false positives (e.g. a member named
/// "Process") are preferable to letting a dangerous call through.
/// </summary>
internal static class SyntaxDenylist
{
	// Namespaces generated code may not reference, by using-directive or fully-qualified name.
	private static readonly string[] BannedNamespaces =
	[
		"System.IO",
		"System.Reflection",
		"System.Runtime.InteropServices",
		"System.Runtime.CompilerServices",
		"Microsoft.Win32",
	];

	// Fully-qualified types banned even though their namespace is otherwise allowed.
	private static readonly string[] BannedQualifiedTypes =
	[
		"System.Diagnostics.Process",
		"System.Diagnostics.ProcessStartInfo",
		"System.AppDomain",
		"System.Activator",
		"System.Environment",
	];

	// Simple identifiers that are strong danger signals regardless of qualification.
	private static readonly HashSet<string> BannedSimpleNames = new(StringComparer.Ordinal)
	{
		"Process", "ProcessStartInfo",
		"Activator", "AppDomain",
		"Marshal", "NativeLibrary", "GCHandle",
		"Assembly", "AssemblyLoadContext",
		"Environment",
	};

	/// <summary>Returns a list of human-readable violations; empty means the tree passed.</summary>
	public static List<string> Check(SyntaxNode root)
	{
		List<string> violations = new();

		void Add(string message, SyntaxNode at)
		{
			int line = at.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
			string entry = $"{message} (line {line})";
			if (!violations.Contains(entry))
				violations.Add(entry);
		}

		foreach (SyntaxNode node in root.DescendantNodesAndSelf())
		{
			switch (node)
			{
				case UsingDirectiveSyntax u when u.Name is not null:
					string ns = u.Name.ToString();
					if (BannedNamespaces.Any(b => ns == b || ns.StartsWith(b + ".", StringComparison.Ordinal)))
						Add($"Disallowed namespace import '{ns}'", node);
					break;

				case QualifiedNameSyntax q:
					string full = q.ToString();
					if (BannedNamespaces.Any(b => full.StartsWith(b + ".", StringComparison.Ordinal))
						|| BannedQualifiedTypes.Any(t => full == t || full.StartsWith(t + ".", StringComparison.Ordinal)))
						Add($"Disallowed type reference '{full}'", node);
					break;

				case IdentifierNameSyntax id when BannedSimpleNames.Contains(id.Identifier.ValueText):
					Add($"Disallowed API '{id.Identifier.ValueText}'", node);
					break;

				case UnsafeStatementSyntax:
					Add("Unsafe code is not allowed", node);
					break;

				case PointerTypeSyntax:
				case FunctionPointerTypeSyntax:
					Add("Pointer types are not allowed", node);
					break;

				case StackAllocArrayCreationExpressionSyntax:
					Add("stackalloc is not allowed", node);
					break;

				case FixedStatementSyntax:
					Add("fixed statements are not allowed", node);
					break;

				case AttributeSyntax attr when IsPInvokeAttribute(attr.Name.ToString()):
					Add($"P/Invoke attribute '{attr.Name}' is not allowed", node);
					break;

				case MethodDeclarationSyntax m when m.Modifiers.Any(t => t.ValueText == "extern"):
					Add("extern methods (P/Invoke) are not allowed", node);
					break;
			}
		}

		return violations;
	}

	private static bool IsPInvokeAttribute(string name)
	{
		string simple = name.Split('.').Last();
		return simple is "DllImport" or "DllImportAttribute" or "LibraryImport" or "LibraryImportAttribute";
	}
}
