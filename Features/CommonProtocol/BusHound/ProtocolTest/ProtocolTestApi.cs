namespace CommonProtocol.BusHound;

using Base.Services.APIService;
using Base.Services.Peripheral;
using BusHound.ProtocolTest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class ASUSBusHoundPage
{
	[GET("~/bushound/suites", requireMainThread: true,
		Summary = "List all test suites.",
		Description = "Lists every saved test suite with its steps and last-run verdict.")]
	public ApiResponse ApiListSuites()
		=> Ok(suitesList.Select(SuiteToDto).ToList());

	[GET("~/bushound/suites/detail", requireMainThread: true,
		Summary = "Get one test suite by id.",
		Description = "Returns a single suite by id, including steps and last-run details. Query: ?id=<suite id>.")]
	public ApiResponse ApiGetSuite(string id)
	{
		TestSuite suite = suitesList.FirstOrDefault(s => s.Id == id);
		return suite == null ? NotFound(id) : Ok(SuiteToDto(suite));
	}

	[POST("~/bushound/suites", requireMainThread: true,
		Summary = "Create a new test suite.",
		Description = "Creates a new test suite. JSON body: { name, description, resetWhenDone, stopOnFirstFailure, steps[] }.")]
	public ApiResponse ApiCreateSuite(TestSuite suite)
	{
		if (suite == null) return Bad("Missing suite body.");

		suite.Id = Guid.NewGuid().ToString("N");
		if (string.IsNullOrWhiteSpace(suite.Name)) suite.Name = "New suite";
		suite.Steps ??= new List<TestStep>();
		foreach (TestStep step in suite.Steps)
			step.Id = Guid.NewGuid().ToString("N");

		suitesList.Add(suite);
		SaveSuites();
		BuildSuiteRows();
		return Ok(SuiteToDto(suite));
	}

	[POST("~/bushound/suites/update", requireMainThread: true,
		Summary = "Update a test suite.",
		Description = "Updates a suite's properties (name, description, resetWhenDone, stopOnFirstFailure). " +
			"Does not replace steps — use the step endpoints for that.")]
	public ApiResponse ApiUpdateSuite(TestSuite patch)
	{
		if (patch == null || string.IsNullOrWhiteSpace(patch.Id)) return Bad("Missing suite id.");

		TestSuite existing = suitesList.FirstOrDefault(s => s.Id == patch.Id);
		if (existing == null) return NotFound(patch.Id);

		if (!string.IsNullOrWhiteSpace(patch.Name)) existing.Name = patch.Name;
		if (patch.Description != null) existing.Description = patch.Description;
		existing.ResetWhenDone = patch.ResetWhenDone;
		existing.StopOnFirstFailure = patch.StopOnFirstFailure;

		SaveSuites();
		BuildSuiteRows();
		return Ok(SuiteToDto(existing));
	}

	[POST("~/bushound/suites/delete", requireMainThread: true,
		Summary = "Delete a test suite.",
		Description = "Deletes the suite with the given id. Query: ?id=<suite id>.")]
	public ApiResponse ApiDeleteSuite(string id)
	{
		if (string.IsNullOrWhiteSpace(id)) return Bad("Missing suite id.");
		if (suitesList.RemoveAll(s => s.Id == id) == 0) return NotFound(id);

		SaveSuites();
		BuildSuiteRows();
		return Ok(new { deleted = id });
	}

	[POST("~/bushound/suites/steps/add", requireMainThread: true,
		Summary = "Add a step to a suite.",
		Description = "Adds a step to the end of the given suite. Query: ?suiteId=<suite id>. Body: the step JSON.")]
	public ApiResponse ApiAddStep(string suiteId, TestStep step)
	{
		if (string.IsNullOrWhiteSpace(suiteId)) return Bad("Missing suiteId.");
		if (step == null) return Bad("Missing step body.");

		TestSuite suite = suitesList.FirstOrDefault(s => s.Id == suiteId);
		if (suite == null) return NotFound(suiteId);

		step.Id = Guid.NewGuid().ToString("N");
		suite.Steps.Add(step);
		SaveSuites();
		if (suiteRows.TryGetValue(suite, out TestSuiteEntry row)) row.RebuildStepRows();
		UpdateTestsSummary();
		return Ok(StepToDto(step));
	}

	[POST("~/bushound/suites/steps/delete", requireMainThread: true,
		Summary = "Delete a step from a suite.",
		Description = "Removes a step by id from a suite. Query: ?suiteId=<suite id>&stepId=<step id>.")]
	public ApiResponse ApiDeleteStep(string suiteId, string stepId)
	{
		if (string.IsNullOrWhiteSpace(suiteId)) return Bad("Missing suiteId.");
		if (string.IsNullOrWhiteSpace(stepId)) return Bad("Missing stepId.");

		TestSuite suite = suitesList.FirstOrDefault(s => s.Id == suiteId);
		if (suite == null) return NotFound(suiteId);

		if (suite.Steps.RemoveAll(s => s.Id == stepId) == 0)
			return NotFound(stepId);

		SaveSuites();
		if (suiteRows.TryGetValue(suite, out TestSuiteEntry row)) row.RebuildStepRows();
		UpdateTestsSummary();
		return Ok(new { deleted = stepId });
	}

	[POST("~/bushound/suites/run", requireMainThread: true,
		Summary = "Run a single test suite.",
		Description = "Runs all steps of a suite against the connected device. Query: ?id=<suite id>.")]
	public async Task<ApiResponse> ApiRunSuite(string id)
	{
		if (string.IsNullOrWhiteSpace(id)) return Bad("Missing suite id.");
		if (testsRunning) return Busy();

		TestSuite suite = suitesList.FirstOrDefault(s => s.Id == id);
		if (suite == null) return NotFound(id);

		testsRunning = true;
		SetTestsBusy(true);
		try
		{
			PeripheralInterface targetInterface = ResolveTestInterface();
			if (targetInterface == null)
				return Bad("Not connected — start capturing a device first.");

			TestSuiteRunner runner = new(targetInterface);
			SuiteRunResult result = await runner.RunAsync(suite);
			suiteResults[suite] = result;

			if (suiteRows.TryGetValue(suite, out TestSuiteEntry row))
				row.ShowSummary(result);

			return Ok(SuiteResultToDto(suite, result));
		}
		finally
		{
			testsRunning = false;
			SetTestsBusy(false);
		}
	}

	[POST("~/bushound/suites/runall", requireMainThread: true,
		Summary = "Run all test suites.",
		Description = "Runs every suite in order against the connected device.")]
	public async Task<ApiResponse> ApiRunAllSuites()
	{
		if (testsRunning) return Busy();

		testsRunning = true;
		SetTestsBusy(true);
		try
		{
			PeripheralInterface targetInterface = ResolveTestInterface();
			if (targetInterface == null)
				return Bad("Not connected — start capturing a device first.");

			List<object> results = new();
			TestSuiteRunner runner = new(targetInterface);

			foreach (TestSuite suite in suitesList)
			{
				SuiteRunResult result = await runner.RunAsync(suite);
				suiteResults[suite] = result;
				if (suiteRows.TryGetValue(suite, out TestSuiteEntry row))
					row.ShowSummary(result);
				results.Add(SuiteResultToDto(suite, result));
			}

			return Ok(results);
		}
		finally
		{
			testsRunning = false;
			SetTestsBusy(false);
		}
	}

	[POST("~/bushound/suites/examples", requireMainThread: true,
		Summary = "Load example test suites.",
		Description = "Appends four example suites demonstrating all step types (Send, SendAndWait, SendAndAssert, Sleep) " +
			"and suite features (stopOnFirstFailure, resetWhenDone). Returns the created suites.")]
	public ApiResponse ApiLoadExamples()
	{
		LoadExampleSuites();
		return Ok(suitesList.Select(SuiteToDto).ToList());
	}

	// ---- DTOs ----

	private object SuiteToDto(TestSuite suite)
	{
		SuiteRunResult lastResult = suiteResults.GetValueOrDefault(suite);
		return new
		{
			id = suite.Id,
			name = suite.Name,
			description = suite.Description,
			resetWhenDone = suite.ResetWhenDone,
			stopOnFirstFailure = suite.StopOnFirstFailure,
			steps = suite.Steps.Select(StepToDto).ToList(),
			lastVerdict = lastResult?.Verdict.ToString(),
			lastMessage = lastResult?.Message,
			lastTotalElapsedMs = lastResult?.TotalElapsedMs,
		};
	}

	private static object StepToDto(TestStep step)
	{
		return step switch
		{
			SendStep send => new
			{
				id = step.Id,
				name = step.Name,
				type = "send",
				requestHex = send.RequestHex,
			},
			SendAndWaitStep wait => new
			{
				id = step.Id,
				name = step.Name,
				type = "send_wait",
				requestHex = wait.RequestHex,
				totalTimeoutMs = wait.TotalTimeoutMs,
				expectedCount = wait.ExpectedCount,
			},
			SendAndAssertStep assert => (object)new
			{
				id = step.Id,
				name = step.Name,
				type = "send_assert",
				requestHex = assert.RequestHex,
				expectedLines = assert.ExpectedLines,
				totalTimeoutMs = assert.TotalTimeoutMs,
				allowTrailingWildcard = assert.AllowTrailingWildcard,
			},
			SleepStep sleep => new
			{
				id = step.Id,
				name = step.Name,
				type = "sleep",
				durationMs = sleep.DurationMs,
			},
			_ => new { id = step.Id, name = step.Name, type = "unknown" },
		};
	}

	private static object SuiteResultToDto(TestSuite suite, SuiteRunResult result)
		=> new
		{
			id = suite.Id,
			name = suite.Name,
			verdict = result.Verdict.ToString(),
			message = result.Message,
			totalElapsedMs = result.TotalElapsedMs,
			stepResults = result.StepResults.Select((sr, i) => new
			{
				stepIndex = i,
				stepName = i < suite.Steps.Count ? suite.Steps[i].Name : null,
				verdict = sr.Verdict.ToString(),
				message = sr.Message,
				elapsedMs = sr.ElapsedMs,
				received = HexList(sr.Received),
			}).ToList(),
		};

	private static List<string> HexList(IEnumerable<byte[]> packets)
	{
		List<string> list = new();
		if (packets == null) return list;
		foreach (byte[] packet in packets)
			list.Add(ByteToString(packet, false));
		return list;
	}

	private static ApiResponse Ok(object data) => new() { Status = 200, Data = data };
	private static ApiResponse Bad(string message) => new() { Status = 400, Data = new { error = message } };
	private static ApiResponse NotFound(string id) => new() { Status = 404, Data = new { error = $"No item with id '{id}'." } };
	private static ApiResponse Busy() => new() { Status = 409, Data = new { error = "A test run is already in progress." } };
}
