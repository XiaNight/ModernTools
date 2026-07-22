using System.IO;
using System.Windows.Threading;
using AiComposer.Compilation;
using AiComposer.Model;
using AiComposer.Persistence;
using Base.Components;
using Base.Core;
using Base.Pages;
using Base.Services;

namespace AiComposer.Runtime;

/// <summary>
/// Owns the lifecycle of every generated page: startup registration, create / update / delete, the
/// compiled-module cache, and Roslyn warm-up. Registered as a startup singleton so it runs before
/// the user can navigate. At startup it only reads manifests and registers nav entries from
/// metadata — no XAML is parsed and no C# is compiled until a page is first shown (that work lives
/// in <see cref="GeneratedPageHost"/>). The persisted source is the single source of truth; the
/// compiled assembly is a cached projection keyed by source hash, unloaded when its last user goes
/// away. Edits are destroy-and-recreate: the host instance and nav slot stay put while the
/// materialized content and its collectible ALC are rebuilt.
/// </summary>
public sealed class GeneratedPageManager : WpfBehaviourSingleton<GeneratedPageManager>
{
	private const string SampleSeededKey = "AiComposer.SampleSeeded";

	private readonly Dictionary<string, RegisteredPage> pages = new();

	private readonly object moduleLock = new();
	private readonly Dictionary<string, CompiledModule> compiledByHash = new();

	private GeneratedPageStore store;
	private bool started;

	/// <summary>Raised whenever the set of pages changes, so the composer page can refresh its list.</summary>
	public event Action PagesChanged;

	public GeneratedPageStore Store => store;

	public override void Start()
	{
		base.Start();
		if (started) return;
		started = true;

		string root = Path.Combine(LocalAppDataStore.Instance.RootDirectory, "GeneratedPages");
		store = new GeneratedPageStore(root);

		// Metadata-only registration: parse/compile is deferred to first navigation.
		foreach (GeneratedPageDefinition def in store.LoadAllManifests())
			RegisterInternal(def);

		SeedSampleIfNeeded();

		ScheduleWarmup();
		PagesChanged?.Invoke();
	}

	// ---- queries ----

	/// <summary>Metadata for every registered page (source not loaded), for the composer list.</summary>
	public IReadOnlyList<GeneratedPageDefinition> GetDefinitions()
		=> pages.Values.Select(p => p.Definition).OrderBy(d => d.Title).ToList();

	/// <summary>Full definition (metadata + source) read fresh from disk — the source of truth.</summary>
	public GeneratedPageDefinition LoadDefinition(string id)
		=> store?.LoadFull(id);

	// ---- create / update / delete ----

	/// <summary>Persists a new page, registers its nav entry, and returns the stored definition.</summary>
	public GeneratedPageDefinition Create(GeneratedPageDefinition def)
	{
		if (string.IsNullOrWhiteSpace(def.Id)) def.Id = Guid.NewGuid().ToString("N");
		DateTime now = DateTime.UtcNow;
		def.CreatedUtc = now;
		def.ModifiedUtc = now;
		def.SchemaVersion = GeneratedPageDefinition.CurrentSchemaVersion;

		store.Save(def);
		RegisterInternal(def);
		PagesChanged?.Invoke();
		return def;
	}

	/// <summary>
	/// Overwrites an existing page (same Id) and destroys+recreates its content. The nav slot and
	/// host instance stay stable; the tab's title/glyph update in place. If the page id is unknown
	/// this falls back to <see cref="Create"/>.
	/// </summary>
	public GeneratedPageDefinition Update(GeneratedPageDefinition def)
	{
		if (!pages.TryGetValue(def.Id, out RegisteredPage reg))
			return Create(def);

		def.CreatedUtc = reg.Definition.CreatedUtc;
		def.ModifiedUtc = DateTime.UtcNow;
		def.SchemaVersion = GeneratedPageDefinition.CurrentSchemaVersion;

		store.Save(def);
		reg.Definition = def;

		// Refresh the nav entry in place (slot stays stable). Group/Order changes take effect on the
		// next app restart — moving a tab between groups is intentionally left to the nav rebuild.
		reg.Tab.Text = def.Title;
		reg.Tab.Glyph = def.Glyph;

		// Destroy + recreate the materialized content against the new source.
		reg.Host.Rematerialize();

		PagesChanged?.Invoke();
		return def;
	}

	/// <summary>Removes a page: nav tab, host instance, compiled module, and stored files.</summary>
	public void Delete(string id)
	{
		if (!pages.Remove(id, out RegisteredPage reg)) return;

		Main.UnregisterDynamicPage(reg.Host);
		reg.Host.TearDown();
		store.Delete(id);
		PagesChanged?.Invoke();
	}

	/// <summary>Navigates to a page by id (used by the composer after create/update).</summary>
	public void Navigate(string id)
	{
		if (pages.TryGetValue(id, out RegisteredPage reg))
			reg.Tab.Click();
	}

	private void RegisterInternal(GeneratedPageDefinition def)
	{
		if (pages.ContainsKey(def.Id)) return;

		GeneratedPageHost host = new(def.Id);
		INavigationItem tab = Main.RegisterDynamicPage(
			host,
			text: def.Title,
			path: def.NavPath,
			glyph: def.Glyph,
			order: def.Order);

		pages[def.Id] = new RegisteredPage { Definition = def, Host = host, Tab = tab };
	}

	// ---- compiled-module cache (source-hash keyed, reference counted) ----

	/// <summary>
	/// Returns the compiled logic type for the given source, compiling off the caller's thread on a
	/// cache miss. Reference-counted: each successful acquire must be matched by a
	/// <see cref="ReleaseModule"/> so the collectible ALC can unload when the last page drops it.
	/// </summary>
	internal CompileOutcome AcquireModule(string hash, string csharp, string assemblyName)
	{
		lock (moduleLock)
		{
			if (compiledByHash.TryGetValue(hash, out CompiledModule cached))
			{
				cached.RefCount++;
				return CompileOutcome.Ok(cached.LogicType);
			}
		}

		CompileResult result = RoslynCompiler.Compile(csharp, assemblyName);
		if (!result.Success)
			return CompileOutcome.Failed(result.Errors);

		lock (moduleLock)
		{
			if (compiledByHash.TryGetValue(hash, out CompiledModule raced))
			{
				// Another thread compiled the same source first; discard ours.
				result.LoadContext?.Unload();
				raced.RefCount++;
				return CompileOutcome.Ok(raced.LogicType);
			}

			compiledByHash[hash] = new CompiledModule
			{
				Alc = result.LoadContext,
				LogicType = result.LogicType,
				RefCount = 1,
			};
			return CompileOutcome.Ok(result.LogicType);
		}
	}

	/// <summary>Drops one reference to a compiled module, unloading its ALC when none remain.</summary>
	internal void ReleaseModule(string hash)
	{
		lock (moduleLock)
		{
			if (!compiledByHash.TryGetValue(hash, out CompiledModule module)) return;

			module.RefCount--;
			if (module.RefCount > 0) return;

			compiledByHash.Remove(hash);
			try { module.Alc?.Unload(); }
			catch (Exception ex) { Debug.Log($"[AiComposer] ALC unload failed: {ex.Message}"); }
		}
	}

	// ---- warm-up ----

	private void ScheduleWarmup()
	{
		// After the window is idle (off the startup critical path), warm Roslyn once with a throwaway
		// compile so the first real materialization is fast. Deliberately no eager compile of saved
		// pages — those compile lazily, one at a time, on first navigation.
		Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
		{
			Task.Run(() =>
			{
				try { RoslynCompiler.Warmup(); }
				catch (Exception ex) { Debug.Log($"[AiComposer] Roslyn warm-up failed: {ex.Message}"); }
			});
		});
	}

	// ---- built-in sample ----

	private void SeedSampleIfNeeded()
	{
		if (pages.ContainsKey(BuiltInSample.SampleId)) return;
		if (LocalAppDataStore.Instance.Get<bool>(SampleSeededKey)) return;

		try
		{
			GeneratedPageDefinition sample = BuiltInSample.Create();
			store.Save(sample);
			RegisterInternal(sample);
			Debug.Log("[AiComposer] Seeded built-in sample page.");
		}
		catch (Exception ex)
		{
			Debug.Log($"[AiComposer] Failed to seed sample: {ex.Message}");
		}
		finally
		{
			LocalAppDataStore.Instance.Set(SampleSeededKey, true);
		}
	}

	private sealed class RegisteredPage
	{
		public GeneratedPageDefinition Definition { get; set; }
		public GeneratedPageHost Host { get; init; }
		public INavigationItem Tab { get; init; }
	}

	private sealed class CompiledModule
	{
		public GeneratedLoadContext Alc { get; init; }
		public Type LogicType { get; init; }
		public int RefCount { get; set; }
	}
}
