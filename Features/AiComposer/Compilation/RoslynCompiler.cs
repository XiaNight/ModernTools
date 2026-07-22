using System.IO;
using System.Reflection;
using AiComposer.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace AiComposer.Compilation;

/// <summary>Outcome of compiling a generated page's C#.</summary>
internal sealed class CompileResult
{
	public bool Success { get; init; }
	public IReadOnlyList<string> Errors { get; init; } = [];
	public Type LogicType { get; init; }
	public GeneratedLoadContext LoadContext { get; init; }

	public static CompileResult Fail(IReadOnlyList<string> errors) => new() { Success = false, Errors = errors };
}

/// <summary>
/// Compiles generated C# into a collectible assembly. Runs the syntax denylist first, then a
/// <see cref="CSharpCompilation"/> emitted to memory and loaded via a <see cref="GeneratedLoadContext"/>.
/// Compilation is thread-agnostic and meant to run off the UI thread; the caller instantiates the
/// returned type and calls Initialize on the dispatcher. Failures return diagnostics rather than
/// throwing so the host can render them in its error panel.
/// </summary>
internal static class RoslynCompiler
{
	// Global usings injected into every generated assembly so page logic needs no boilerplate. IO,
	// reflection, diagnostics and interop are deliberately absent — unqualified dangerous types then
	// fail to resolve, complementing the syntax denylist.
	private const string GlobalUsings = """
		global using System;
		global using System.Collections.Generic;
		global using System.Collections.ObjectModel;
		global using System.Linq;
		global using System.Threading.Tasks;
		global using System.Windows;
		global using System.Windows.Controls;
		global using System.Windows.Input;
		global using AiComposer.Contracts;
		global using Base.Helpers;
		""";

	private static readonly CSharpParseOptions ParseOptions =
		new(LanguageVersion.CSharp12);

	// Per-file cache of metadata references so repeated compiles don't re-read DLLs, while the
	// reference set is still re-enumerated each compile to pick up assemblies loaded after warm-up.
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, MetadataReference> ReferenceCache =
		new(StringComparer.OrdinalIgnoreCase);

	public static CompileResult Compile(string csharp, string assemblyName)
	{
		if (string.IsNullOrWhiteSpace(csharp))
			return CompileResult.Fail(["No C# source provided."]);

		SyntaxTree userTree = CSharpSyntaxTree.ParseText(csharp, ParseOptions);

		List<string> violations = SyntaxDenylist.Check(userTree.GetRoot());
		if (violations.Count > 0)
			return CompileResult.Fail(violations.Select(v => $"Guardrail: {v}").ToList());

		SyntaxTree usingsTree = CSharpSyntaxTree.ParseText(GlobalUsings, ParseOptions);

		CSharpCompilation compilation = CSharpCompilation.Create(
			assemblyName,
			[usingsTree, userTree],
			BuildReferences(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
				optimizationLevel: OptimizationLevel.Release));

		using MemoryStream ms = new();
		EmitResult emit = compilation.Emit(ms);
		if (!emit.Success)
		{
			List<string> errors = emit.Diagnostics
				.Where(d => d.Severity == DiagnosticSeverity.Error)
				.Select(d => d.ToString())
				.ToList();
			if (errors.Count == 0) errors.Add("Compilation failed with no error diagnostics.");
			return CompileResult.Fail(errors);
		}

		ms.Seek(0, SeekOrigin.Begin);
		GeneratedLoadContext alc = new(assemblyName);
		Assembly assembly = alc.LoadFromStream(ms);

		Type logicType = assembly.GetTypes().FirstOrDefault(t =>
			typeof(IGeneratedLogic).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });

		if (logicType == null)
		{
			alc.Unload();
			return CompileResult.Fail(["No public type implementing IGeneratedLogic was found."]);
		}

		return new CompileResult { Success = true, LogicType = logicType, LoadContext = alc };
	}

	// Reference every loaded assembly with a real file location. That covers System.*, WPF,
	// ModernWpf, Base and AiComposer without hand-maintaining a list.
	private static List<MetadataReference> BuildReferences()
	{
		List<MetadataReference> refs = new();
		foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (asm.IsDynamic) continue;

			string location;
			try { location = asm.Location; }
			catch { continue; }

			if (string.IsNullOrEmpty(location) || !File.Exists(location)) continue;

			MetadataReference reference = ReferenceCache.GetOrAdd(location, static path =>
			{
				try { return MetadataReference.CreateFromFile(path); }
				catch { return null; }
			});
			if (reference != null) refs.Add(reference);
		}

		return refs;
	}

	/// <summary>Compiles a trivial program to force Roslyn's JIT/type caches to warm up.</summary>
	public static void Warmup()
	{
		CompileResult result = Compile(
			"public sealed class Warmup : IGeneratedLogic { public void Initialize(IHostApi host, FrameworkElement root) { } }",
			"AiComposer.Warmup");
		result.LoadContext?.Unload();
	}
}