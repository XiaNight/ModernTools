using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Base.Core;

namespace QuickScan;

/// <summary>
/// Loads and persists QuickScan scenarios. Built-in scenarios ship in code; user edits
/// and new scenarios are stored under %AppData% via <see cref="LocalAppDataStore"/>. A
/// user scenario whose Id matches a built-in overrides it.
/// </summary>
public static class QuickScanStore
{
	private const string KEY_USER_SCENARIOS = "QuickScan.UserScenarios";

	private static readonly JsonSerializerOptions FileJson = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private static List<QuickScanScenario> BuiltIns()
		=> new() { M708Scenario.Create() };

	private static List<QuickScanScenario> UserScenarios()
		=> LocalAppDataStore.Instance.Get<List<QuickScanScenario>>(KEY_USER_SCENARIOS) ?? new();

	private static void SaveUserScenarios(List<QuickScanScenario> scenarios)
		=> LocalAppDataStore.Instance.Set(KEY_USER_SCENARIOS, scenarios);

	/// <summary>
	/// All scenarios: built-ins, with a same-Id user copy taking precedence — unless that
	/// copy is older than the built-in's <see cref="QuickScanScenario.Version"/>, in which
	/// case it is stale (saved by an earlier build) and dropped so the updated built-in
	/// structure is used. Stale copies are pruned from storage so this self-heals.
	/// </summary>
	public static List<QuickScanScenario> GetAll()
	{
		List<QuickScanScenario> user = UserScenarios();
		List<QuickScanScenario> all = new();
		bool pruned = false;

		foreach (QuickScanScenario builtin in BuiltIns())
		{
			QuickScanScenario overridden = user.FirstOrDefault(s => s.Id == builtin.Id);
			if (overridden != null && overridden.Version < builtin.Version)
			{
				user.RemoveAll(s => s.Id == builtin.Id);
				overridden = null;
				pruned = true;
			}
			all.Add(overridden ?? builtin);
		}

		foreach (QuickScanScenario scenario in user)
		{
			if (all.All(s => s.Id != scenario.Id))
				all.Add(scenario);
		}

		if (pruned) SaveUserScenarios(user);

		return all;
	}

	/// <summary>Best scenario for the connected device, or the first available one.</summary>
	public static QuickScanScenario SelectForDevice(ushort vid, ushort pid, string productName)
	{
		List<QuickScanScenario> all = GetAll();
		QuickScanScenario best = null;
		int bestScore = 0;

		foreach (QuickScanScenario scenario in all)
		{
			int score = scenario.MatchScore(vid, pid, productName);
			if (score > bestScore)
			{
				bestScore = score;
				best = scenario;
			}
		}

		return best ?? all.FirstOrDefault();
	}

	/// <summary>Inserts or replaces a scenario (matched by Id) in the user store.</summary>
	public static void Save(QuickScanScenario scenario)
	{
		if (scenario == null) return;
		if (string.IsNullOrEmpty(scenario.Id)) scenario.Id = Guid.NewGuid().ToString("N");

		List<QuickScanScenario> user = UserScenarios();
		int index = user.FindIndex(s => s.Id == scenario.Id);
		if (index >= 0) user[index] = scenario;
		else user.Add(scenario);

		SaveUserScenarios(user);
	}

	/// <summary>
	/// Removes a user scenario. A user override of a built-in reverts to the built-in;
	/// a pure built-in cannot be removed.
	/// </summary>
	public static void Delete(string id)
	{
		List<QuickScanScenario> user = UserScenarios();
		if (user.RemoveAll(s => s.Id == id) > 0)
			SaveUserScenarios(user);
	}

	public static void Export(QuickScanScenario scenario, string path)
	{
		string json = JsonSerializer.Serialize(scenario, FileJson);
		File.WriteAllText(path, json);
	}

	/// <summary>Loads a scenario from JSON and saves it. Returns the imported scenario.</summary>
	public static QuickScanScenario Import(string path)
	{
		string json = File.ReadAllText(path);
		QuickScanScenario scenario = JsonSerializer.Deserialize<QuickScanScenario>(json, FileJson)
			?? throw new InvalidDataException("File did not contain a QuickScan scenario.");

		if (string.IsNullOrEmpty(scenario.Id)) scenario.Id = Guid.NewGuid().ToString("N");
		// An imported scenario is a user scenario, never a protected built-in.
		scenario.BuiltIn = false;
		Save(scenario);
		return scenario;
	}
}
