using System.Reflection;
using System.Runtime.Loader;

namespace AiComposer.Compilation;

/// <summary>
/// A collectible load context for one generated assembly. Returning null from <see cref="Load"/>
/// makes every dependency (System.*, WPF, Base, AiComposer) resolve against the default context, so
/// shared types like <c>IGeneratedLogic</c> / <c>IHostApi</c> keep a single identity. Being
/// collectible lets the host <see cref="AssemblyLoadContext.Unload"/> the old code on edit/delete.
/// </summary>
internal sealed class GeneratedLoadContext : AssemblyLoadContext
{
	public GeneratedLoadContext(string name)
		: base(name, isCollectible: true)
	{
	}

	protected override Assembly Load(AssemblyName assemblyName) => null;
}