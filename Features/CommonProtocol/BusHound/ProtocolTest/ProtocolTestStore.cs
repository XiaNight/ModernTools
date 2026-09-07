namespace CommonProtocol.BusHound.ProtocolTest;

using System.Collections.Generic;
using Base.Core;

public static class ProtocolTestStore
{
	private const string KEY_SUITES = "BusHound.ProtocolTestSuites";

	public static List<TestSuite> GetAll()
		=> LocalAppDataStore.Instance.Get<List<TestSuite>>(KEY_SUITES) ?? new List<TestSuite>();

	public static void SaveAll(List<TestSuite> suites)
		=> LocalAppDataStore.Instance.Set(KEY_SUITES, suites ?? new List<TestSuite>());
}
