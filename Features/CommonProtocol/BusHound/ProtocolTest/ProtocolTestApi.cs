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
	[GET("~/bushound/tests", requireMainThread: true)]
	public ApiResponse ApiListTests()
		=> Ok(testsList.Select(TestToDto).ToList());

	[GET("~/bushound/tests/detail", requireMainThread: true)]
	public ApiResponse ApiGetTest(string id)
	{
		TestProtocol test = testsList.FirstOrDefault(t => t.Id == id);
		return test == null ? NotFound(id) : Ok(TestToDto(test));
	}

	[POST("~/bushound/tests", requireMainThread: true)]
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

	[POST("~/bushound/tests/update", requireMainThread: true)]
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

	[POST("~/bushound/tests/delete", requireMainThread: true)]
	public ApiResponse ApiDeleteTest(string id)
	{
		if (string.IsNullOrWhiteSpace(id)) return Bad("Missing test id.");
		if (testsList.RemoveAll(t => t.Id == id) == 0) return NotFound(id);

		SaveTests();
		BuildTestRows();
		return Ok(new { deleted = id });
	}

	[POST("~/bushound/tests/run", requireMainThread: true)]
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

	[POST("~/bushound/tests/runall", requireMainThread: true)]
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