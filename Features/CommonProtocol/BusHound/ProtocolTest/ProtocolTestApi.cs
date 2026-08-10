namespace CommonProtocol.BusHound;

using Base.Services.APIService;
using BusHound.ProtocolTest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// HTTP API surface for the Bus Hound protocol tests (create / read / update / delete / run). Every
/// endpoint runs on the UI thread (<c>requireMainThread</c>) because it touches the shared test list,
/// the on-screen rows, and the device interface. Persistence and the visible panel stay in sync with
/// every change. Routes:
///   GET  /bushound/tests            list all tests (with each one's last verdict)
///   GET  /bushound/tests/detail     ?id=  one test with its last run details
///   POST /bushound/tests            create (JSON body: name, requestHex, expectedLines, totalTimeoutMs, allowTrailingWildcard)
///   POST /bushound/tests/update     update (JSON body incl. id)
///   POST /bushound/tests/delete     ?id=  delete
///   POST /bushound/tests/run        ?id=  run one, returns verdict/elapsed/received
///   POST /bushound/tests/runall     run all in order
/// </summary>
public partial class ASUSBusHoundPage
{
	[GET("~/bushound/tests", requireMainThread: true,
		Summary = "List all Bus Hound protocol tests.",
		Description = "Lists every saved Bus Hound protocol test. Takes no parameters. Each entry includes the " +
			"test's definition (request bytes, expected packets, timeout) plus the result of its most recent " +
			"run (last verdict, elapsed time, message and received packets), if it has been run this session.")]
	public ApiResponse ApiListTests()
		=> Ok(testsList.Select(TestToDto).ToList());

	[GET("~/bushound/tests/detail", requireMainThread: true,
		Summary = "Get one protocol test with its last-run details.",
		Description = "Returns a single saved test by id, including its last-run details. Query: ?id=<test id> " +
			"(as returned by the list/create endpoints). Responds 404 if no test with that id exists.")]
	public ApiResponse ApiGetTest(string id)
	{
		TestProtocol test = testsList.FirstOrDefault(t => t.Id == id);
		return test == null ? NotFound(id) : Ok(TestToDto(test));
	}

	[POST("~/bushound/tests", requireMainThread: true,
		Summary = "Create a new protocol test.",
		Description = "Creates a new protocol test from a JSON body and persists it. The server assigns the id " +
			"(any id in the body is ignored) and returns the created test. JSON body: { \"name\": string, " +
			"\"requestHex\": string (request bytes as hex, e.g. \"02 00 B5 00\"), \"expectedLines\": string[] " +
			"(one expected reply packet per entry, hex where any nibble may be the wildcard X), " +
			"\"totalTimeoutMs\": integer (budget to receive and match all packets), \"allowTrailingWildcard\": " +
			"boolean (when true, extra trailing bytes on a received packet are ignored) }. Responds 400 if the " +
			"request hex or an expected line is malformed, or if no expected lines are supplied.")]
	public ApiResponse ApiCreateTest(TestProtocol test)
	{
		if (test == null) return Bad("Missing test body.");

		test.Id = Guid.NewGuid().ToString("N");
		if (string.IsNullOrWhiteSpace(test.Name)) test.Name = "New test";
		test.ExpectedLines ??= new List<string>();
		if (!ValidateTest(test, out string error)) return Bad(error);

		testsList.Add(test);
		SaveTests();
		BuildTestRows();
		return Ok(TestToDto(test));
	}

	[POST("~/bushound/tests/update", requireMainThread: true,
		Summary = "Update an existing protocol test.",
		Description = "Updates an existing test in place, matched by the id in the body, then persists it. JSON " +
			"body is the full test definition including \"id\" (the test to replace); uses the same fields as " +
			"create (name, requestHex, expectedLines, totalTimeoutMs, allowTrailingWildcard). Omitting name " +
			"keeps the existing name. Responds 400 if the id is missing or the definition is invalid, and 404 " +
			"if no test with that id exists.")]
	public ApiResponse ApiUpdateTest(TestProtocol test)
	{
		if (test == null || string.IsNullOrWhiteSpace(test.Id)) return Bad("Missing test id.");

		int index = testsList.FindIndex(t => t.Id == test.Id);
		if (index < 0) return NotFound(test.Id);

		if (string.IsNullOrWhiteSpace(test.Name)) test.Name = testsList[index].Name;
		test.ExpectedLines ??= new List<string>();
		if (!ValidateTest(test, out string error)) return Bad(error);

		testsList[index] = test;
		SaveTests();
		BuildTestRows();
		return Ok(TestToDto(test));
	}

	[POST("~/bushound/tests/delete", requireMainThread: true,
		Summary = "Delete a protocol test.",
		Description = "Deletes the test with the given id and persists the change. Body or query: id=<test id>. " +
			"Responds 400 if the id is missing and 404 if no test with that id exists.")]
	public ApiResponse ApiDeleteTest(string id)
	{
		if (string.IsNullOrWhiteSpace(id)) return Bad("Missing test id.");
		if (testsList.RemoveAll(t => t.Id == id) == 0) return NotFound(id);

		SaveTests();
		BuildTestRows();
		return Ok(new { deleted = id });
	}

	[POST("~/bushound/tests/run", requireMainThread: true,
		Summary = "Run a single protocol test.",
		Description = "Runs one test against the connected device: sends its request frame and matches the reply " +
			"against the expected packets. Body or query: id=<test id>. Returns the outcome — verdict, message, " +
			"measured response time (elapsedMs) and the received packets as hex strings. Responds 400 if the " +
			"id is missing, 404 if the test is not found, and 409 if a test run is already in progress.")]
	public async Task<ApiResponse> ApiRunTest(string id)
	{
		if (string.IsNullOrWhiteSpace(id)) return Bad("Missing test id.");
		if (testsRunning) return Busy();

		TestProtocol test = testsList.FirstOrDefault(t => t.Id == id);
		if (test == null) return NotFound(id);

		testsRunning = true;
		SetTestsBusy(true);
		try
		{
			TestRunResult result = await RunTestAsync(test, testRows.GetValueOrDefault(test));
			return Ok(ResultToDto(test, result));
		}
		finally
		{
			testsRunning = false;
			SetTestsBusy(false);
		}
	}

	[POST("~/bushound/tests/runall", requireMainThread: true,
		Summary = "Run all protocol tests in order.",
		Description = "Runs every saved test in order against the connected device and returns an array of " +
			"per-test results (id, name, verdict, message, elapsedMs, received). Takes no parameters. Responds " +
			"409 if a test run is already in progress.")]
	public async Task<ApiResponse> ApiRunAll()
	{
		if (testsRunning) return Busy();

		testsRunning = true;
		SetTestsBusy(true);
		try
		{
			List<object> results = new();
			foreach (TestProtocol test in testsList)
			{
				TestRunResult result = await RunTestAsync(test, testRows.GetValueOrDefault(test));
				results.Add(ResultToDto(test, result));
			}

			return Ok(results);
		}
		finally
		{
			testsRunning = false;
			SetTestsBusy(false);
		}
	}

	// ---- DTOs / helpers ----

	private object TestToDto(TestProtocol test)
	{
		ProtocolTestEntry row = testRows.GetValueOrDefault(test);
		return new
		{
			id = test.Id,
			name = test.Name,
			requestHex = test.RequestHex,
			expectedLines = test.ExpectedLines,
			totalTimeoutMs = test.TotalTimeoutMs,
			allowTrailingWildcard = test.AllowTrailingWildcard,
			lastVerdict = row?.LastVerdict?.ToString(),
			lastElapsedMs = row?.LastElapsedMs,
			lastMessage = row?.LastMessage,
			lastReceived = row == null ? null : HexList(row.LastReceived),
		};
	}

	private static object ResultToDto(TestProtocol test, TestRunResult result)
		=> new
		{
			id = test.Id,
			name = test.Name,
			verdict = result.Verdict.ToString(),
			message = result.Message,
			elapsedMs = result.ElapsedMs,
			received = HexList(result.Received),
		};

	private static List<string> HexList(IEnumerable<byte[]> packets)
	{
		List<string> list = new();
		if (packets == null) return list;
		foreach (byte[] packet in packets)
			list.Add(ByteToString(packet, false));
		return list;
	}

	private static bool ValidateTest(TestProtocol test, out string error)
	{
		error = null;

		if (!HexBytes.TryParse(test.RequestHex, out _, out string requestError))
		{
			error = $"Request bytes: {requestError}";
			return false;
		}

		if (test.ExpectedLines == null || test.ExpectedLines.Count == 0)
		{
			error = "At least one expected line is required.";
			return false;
		}

		for (int i = 0; i < test.ExpectedLines.Count; i++)
		{
			if (!ExpectedPacket.TryParse(test.ExpectedLines[i], out _, out string lineError))
			{
				error = $"Expected line {i + 1}: {lineError}";
				return false;
			}
		}

		return true;
	}

	private static ApiResponse Ok(object data) => new() { Status = 200, Data = data };
	private static ApiResponse Bad(string message) => new() { Status = 400, Data = new { error = message } };
	private static ApiResponse NotFound(string id) => new() { Status = 404, Data = new { error = $"No test with id '{id}'." } };
	private static ApiResponse Busy() => new() { Status = 409, Data = new { error = "A test run is already in progress." } };
}