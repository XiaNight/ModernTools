namespace CommonProtocol.BusHound.ProtocolTest;

using System.Collections.Generic;
using Base.Core;

/// <summary>
/// Loads and persists Bus Hound protocol tests. The list is stored under %AppData% via
/// <see cref="LocalAppDataStore"/>, mirroring the QuickScan persistence pattern. There are
/// no built-in tests — the store starts empty until the user adds one.
/// </summary>
public static class ProtocolTestStore
{
	private const string KEY_TESTS = "BusHound.ProtocolTests";

	public static List<TestProtocol> GetAll()
		=> LocalAppDataStore.Instance.Get<List<TestProtocol>>(KEY_TESTS) ?? new List<TestProtocol>();

	public static void SaveAll(List<TestProtocol> tests)
		=> LocalAppDataStore.Instance.Set(KEY_TESTS, tests ?? new List<TestProtocol>());
}