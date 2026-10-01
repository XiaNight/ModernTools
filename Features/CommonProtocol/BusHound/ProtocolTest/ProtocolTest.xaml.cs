using Base.Services;
using Base.Services.APIService;
using Base.Services.Peripheral;
using CommonProtocol.BusHound.ProtocolTest;
using System.Windows;
using static Base.Services.DeviceSelection;

namespace CommonProtocol.BusHound;
using Debug = Base.Services.Debug;

public partial class ASUSBusHoundPage
{
    private bool testsRunning;
    private List<TestSuite> suitesList = [];
    private readonly Dictionary<TestSuite, TestSuiteEntry> suiteRows = [];
    private readonly Dictionary<TestSuite, SuiteRunResult> suiteResults = [];
    private readonly Dictionary<TestStep, StepRunResult> stepResults = [];

    private bool recording;
    private StepEditDialog recordDialog;
    private PeripheralInterface recordingInterface;
    private Action<ReadOnlyMemory<byte>, DateTime> recordHandler;

    private void InitTestsPanel()
    {
        TestsBtn.Click += (_, _) => ShowTestsPanel();
        TestsAddSuiteBtn.Click += (_, _) => AddSuite();
        TestsRunAllBtn.Click += (_, _) => RunAllSuites();
        TestsImportBtn.Click += (_, _) => ImportSuites();
        TestsExportBtn.Click += (_, _) => ExportSuites();
        TestsLoadExampleBtn.Click += (_, _) => LoadExampleSuites();
        LoadSuites();
    }

    private void ShowTestsPanel()
    {
        devicePanelVisible = false;
        DevicePanel.Visibility = Visibility.Collapsed;
        CapturePanel.Visibility = Visibility.Collapsed;
        TestsPanel.Visibility = Visibility.Visible;
    }

    // ---- loading / persistence ----

    private void LoadSuites()
    {
        suitesList = ProtocolTestStore.GetAll();
        BuildSuiteRows();
    }

    private void SaveSuites() => ProtocolTestStore.SaveAll(suitesList);

    private void BuildSuiteRows()
    {
        SuitesListPanel.Children.Clear();
        suiteRows.Clear();

        foreach (TestSuite suite in suitesList)
        {
            TestSuiteEntry row = new();
            row.Bind(suite);
            row.RunRequested += (sender, _) => RunSingleSuite(sender as TestSuiteEntry);
            row.EditRequested += (sender, _) => EditSuite(sender as TestSuiteEntry);
            row.DeleteRequested += (sender, _) => DeleteSuite(sender as TestSuiteEntry);
            row.AddStepRequested += (sender, _) => AddStep(sender as TestSuiteEntry);
            row.StepEditRequested += (sender, e) => EditStep(sender as TestSuiteEntry, e.Entry);
            row.StepDeleteRequested += (sender, e) => DeleteStep(sender as TestSuiteEntry, e.Entry);
            row.StepViewRequested += (sender, e) => ViewStep(e.Entry);

            suiteRows[suite] = row;
            SuitesListPanel.Children.Add(row);
        }

        UpdateTestsSummary();
    }

    private void UpdateTestsSummary()
    {
        int suiteCount = suitesList.Count;
        int stepCount = 0;
        foreach (TestSuite s in suitesList)
            stepCount += s.Steps?.Count ?? 0;

        TestsSummaryText.Text = suiteCount == 0
            ? string.Empty
            : $"{suiteCount} suite(s), {stepCount} step(s)";
        TestsEmptyText.Visibility = suiteCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        TestsRunAllBtn.IsEnabled = suiteCount > 0 && !testsRunning;
        TestsExportBtn.IsEnabled = suiteCount > 0;
    }

    #region ---- API ----

    [POST("DeleteAllSuites")]
    private ApiResponse DeleteAllSuitesApi()
    {
        DeleteAllSuites();

        return new ApiResponse
        {
            Status = 200,
            Data = new { message = "All test suites deleted." } 
        };
    }

    [POST("LoadSuites")]
    private ApiResponse LoadSuitesApi(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new ApiResponse
            {
                Status = 400,
                Data = new { message = "File path is required." }
            };
        }
        bool success = LoadSuiteFromFile(filePath);
        if (success)
        {
            return new ApiResponse
            {
                Status = 200,
                Data = new { message = "Test suites loaded successfully." }
            };
        }
        else
        {
            return new ApiResponse
            {
                Status = 500,
                Data = new { message = "Failed to load test suites from the specified file." }
            };
        }
    }

    #endregion

    #region ---- suite CRUD ----

    private async void AddSuite()
    {
        TestSuite suite = new();
        SuiteEditDialog dialog = new();
        if (await dialog.EditAsync(suite))
        {
            suitesList.Add(suite);
            SaveSuites();
            BuildSuiteRows();
        }
    }

    private async void EditSuite(TestSuiteEntry row)
    {
        if (row?.Suite == null) return;

        SuiteEditDialog dialog = new();
        if (await dialog.EditAsync(row.Suite))
        {
            SaveSuites();
            row.Bind(row.Suite);
            UpdateTestsSummary();
        }
    }

    private void DeleteAllSuites()
    {
        if (testsRunning) return;
        suitesList.Clear();
        SaveSuites();
        BuildSuiteRows();
    }

    private void DeleteSuite(TestSuiteEntry row)
    {
        if (row?.Suite == null) return;

        suitesList.Remove(row.Suite);
        SaveSuites();
        BuildSuiteRows();
    }

    private void LoadExampleSuites()
    {
        suitesList.AddRange(CreateExampleSuites());
        SaveSuites();
        BuildSuiteRows();
    }

    private bool LoadSuiteFromFile(string filePath)
    {
        try
        {
            if (!System.IO.File.Exists(filePath))
            {
                Debug.Log($"[BusHound.Tests] File not found: {filePath}");
                return false;
            }

            string content = System.IO.File.ReadAllText(filePath);
            string extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            List<TestSuite> imported;

            if (extension is ".yaml" or ".yml")
            {
                imported = ParseTestSuitesFromYaml(content);
            }
            else
            {
                System.Text.Json.JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
                TestDocument doc = System.Text.Json.JsonSerializer.Deserialize<TestDocument>(content, options);
                imported = doc?.Suites;
            }

            if (imported == null || imported.Count == 0)
            {
                Debug.Log($"[BusHound.Tests] No suites found in {filePath}");
                return false;
            }

            foreach (TestSuite suite in imported)
            {
                suite.Id = Guid.NewGuid().ToString("N");
                foreach (TestStep step in suite.Steps)
                    step.Id = Guid.NewGuid().ToString("N");
            }

            suitesList.AddRange(imported);
            SaveSuites();
            BuildSuiteRows();
            Debug.Log($"[BusHound.Tests] Loaded {imported.Count} suite(s) from {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.Log($"[BusHound.Tests] LoadSuiteFromFile failed: {ex.Message}");
            return false;
        }
    }

    private bool LoadSuitesFromYaml(string rawYaml)
    {
        try
        {
            List<TestSuite> parsedSuites = ParseTestSuitesFromYaml(rawYaml);
            if (parsedSuites == null || parsedSuites.Count == 0)
            {
                Debug.Log("[BusHound.Tests] Import: no suites found in YAML.");
                return false;
            }
            foreach (TestSuite suite in parsedSuites)
            {
                suite.Id = Guid.NewGuid().ToString("N");
                foreach (TestStep step in suite.Steps)
                    step.Id = Guid.NewGuid().ToString("N");
            }
            suitesList.AddRange(parsedSuites);
            SaveSuites();
            BuildSuiteRows();
            Debug.Log($"[BusHound.Tests] Imported {parsedSuites.Count} suite(s) from YAML.");
            return true;
        }
        catch (Exception ex)
        {
            Debug.Log($"[BusHound.Tests] Import failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Parses a simple YAML format into test suites. Supports the structure:
    /// <code>
    /// suites:
    ///   - name: My Suite
    ///     description: Optional description
    ///     resetWhenDone: false
    ///     stopOnFirstFailure: true
    ///     steps:
    ///       - type: send
    ///         name: Fire command
    ///         requestHex: "02 00 B3 01"
    ///       - type: send_assert
    ///         name: Check response
    ///         requestHex: "02 00 B0 01"
    ///         expectedLines:
    ///           - "02 00 B0 01 XX XX"
    ///         totalTimeoutMs: 100
    ///         allowTrailingWildcard: true
    ///       - type: send_wait
    ///         name: Collect packets
    ///         requestHex: "02 00 B5 00"
    ///         totalTimeoutMs: 200
    ///         expectedCount: 2
    ///       - type: sleep
    ///         name: Pause
    ///         durationMs: 100
    /// </code>
    /// </summary>
    private static List<TestSuite> ParseTestSuitesFromYaml(string rawYaml)
    {
        List<TestSuite> suites = new();
        if (string.IsNullOrWhiteSpace(rawYaml)) return suites;

        string[] lines = rawYaml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        TestSuite currentSuite = null;
        TestStep currentStep = null;
        bool inSteps = false;
        bool inExpectedLines = false;

        foreach (string rawLine in lines)
        {
            string trimmed = rawLine.TrimEnd();
            if (trimmed.Length == 0 || trimmed.TrimStart().StartsWith('#'))
                continue;

            int indent = trimmed.Length - trimmed.TrimStart().Length;
            string content = trimmed.TrimStart();

            if (content == "suites:" || content == "suites: []")
                continue;

            // Suite-level list item: "- name: ..."
            if (indent <= 2 && content.StartsWith("- "))
            {
                inExpectedLines = false;
                inSteps = false;
                currentStep = null;
                currentSuite = new TestSuite();
                suites.Add(currentSuite);
                content = content[2..].TrimStart();
                if (content.Length > 0)
                    ApplySuiteProperty(currentSuite, content);
                continue;
            }

            // Suite property at indent 4 (or similar)
            if (currentSuite != null && !inSteps && !content.StartsWith("- ") && content.Contains(':'))
            {
                if (content == "steps:" || content == "steps: []")
                {
                    inSteps = true;
                    inExpectedLines = false;
                    continue;
                }
                ApplySuiteProperty(currentSuite, content);
                continue;
            }

            // Step list item inside steps
            if (inSteps && content.StartsWith("- "))
            {
                inExpectedLines = false;
                content = content[2..].TrimStart();
                currentStep = ParseStepStart(content);
                if (currentStep != null && currentSuite != null)
                    currentSuite.Steps.Add(currentStep);
                continue;
            }

            // Step property or expectedLines entries
            if (inSteps && currentStep != null)
            {
                if (content == "expectedLines:" || content == "expectedLines: []")
                {
                    inExpectedLines = true;
                    continue;
                }

                if (inExpectedLines && content.StartsWith("- "))
                {
                    string lineValue = UnquoteYaml(content[2..].Trim());
                    if (currentStep is SendAndAssertStep assertStep)
                        assertStep.ExpectedLines.Add(lineValue);
                    continue;
                }

                if (content.Contains(':'))
                {
                    inExpectedLines = false;
                    ApplyStepProperty(currentStep, content);
                }
            }
        }

        return suites;
    }

    private static TestStep ParseStepStart(string firstProperty)
    {
        string typeValue = null;

        if (firstProperty.StartsWith("type:"))
            typeValue = UnquoteYaml(firstProperty["type:".Length..].Trim());

        TestStep step = (typeValue?.ToLowerInvariant()) switch
        {
            "send" => new SendStep(),
            "send_wait" or "sendandwait" => new SendAndWaitStep(),
            "send_assert" or "sendandassert" => new SendAndAssertStep(),
            "sleep" => new SleepStep(),
            _ => new SendAndAssertStep(),
        };

        if (typeValue == null && firstProperty.Contains(':'))
            ApplyStepProperty(step, firstProperty);

        return step;
    }

    private static void ApplySuiteProperty(TestSuite suite, string kvLine)
    {
        int colonIndex = kvLine.IndexOf(':');
        if (colonIndex < 0) return;

        string key = kvLine[..colonIndex].Trim().ToLowerInvariant();
        string value = UnquoteYaml(kvLine[(colonIndex + 1)..].Trim());

        switch (key)
        {
            case "name":
                suite.Name = value;
                break;
            case "description":
                suite.Description = value;
                break;
            case "resetwhendone":
                suite.ResetWhenDone = IsTruthy(value);
                break;
            case "stoponFirstfailure" or "stoponfirstfailure":
                suite.StopOnFirstFailure = IsTruthy(value);
                break;
        }
    }

    private static void ApplyStepProperty(TestStep step, string kvLine)
    {
        int colonIndex = kvLine.IndexOf(':');
        if (colonIndex < 0) return;

        string key = kvLine[..colonIndex].Trim().ToLowerInvariant();
        string value = UnquoteYaml(kvLine[(colonIndex + 1)..].Trim());

        switch (key)
        {
            case "name":
                step.Name = value;
                break;
            case "requesthex" when step is SendStep send:
                send.RequestHex = value;
                break;
            case "requesthex" when step is SendAndWaitStep wait:
                wait.RequestHex = value;
                break;
            case "requesthex" when step is SendAndAssertStep assert:
                assert.RequestHex = value;
                break;
            case "totaltimeoutms" when step is SendAndWaitStep waitTimeout:
                if (int.TryParse(value, out int waitMs)) waitTimeout.TotalTimeoutMs = waitMs;
                break;
            case "totaltimeoutms" when step is SendAndAssertStep assertTimeout:
                if (int.TryParse(value, out int assertMs)) assertTimeout.TotalTimeoutMs = assertMs;
                break;
            case "expectedcount" when step is SendAndWaitStep waitCount:
                if (int.TryParse(value, out int count)) waitCount.ExpectedCount = count;
                break;
            case "allowtrailingwildcard" when step is SendAndAssertStep assertTrailing:
                assertTrailing.AllowTrailingWildcard = IsTruthy(value);
                break;
            case "durationms" when step is SleepStep sleep:
                if (int.TryParse(value, out int ms)) sleep.DurationMs = ms;
                break;
        }
    }

    private static string UnquoteYaml(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
                return value[1..^1];
        }
        return value;
    }

    private static bool IsTruthy(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase)
           || value == "1"
           || value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static List<TestSuite> CreateExampleSuites()
    {
        TestSuite getInfoSuite = new()
        {
            Name = "Get Device Info",
            Description = "Queries basic device information and verifies the response structure.",
            StopOnFirstFailure = true,
            ResetWhenDone = false,
            Steps =
            [
                new SendAndAssertStep
                {
                    Name = "Get firmware version",
                    RequestHex = "02 00 B0 01",
                    ExpectedLines = ["02 00 B0 01 XX XX XX XX"],
                    TotalTimeoutMs = 100,
                    AllowTrailingWildcard = true,
                },
                new SleepStep
                {
                    Name = "Wait between queries",
                    DurationMs = 50,
                },
                new SendAndAssertStep
                {
                    Name = "Get device model",
                    RequestHex = "02 00 B0 02",
                    ExpectedLines = ["02 00 B0 02 XX XX XX XX"],
                    TotalTimeoutMs = 100,
                    AllowTrailingWildcard = true,
                },
            ],
        };

        TestSuite sendOnlySuite = new()
        {
            Name = "Fire-and-Forget Commands",
            Description = "Demonstrates Send steps that don't wait for a response.",
            StopOnFirstFailure = false,
            ResetWhenDone = false,
            Steps =
            [
                new SendStep
                {
                    Name = "Set LED mode (static)",
                    RequestHex = "02 00 B3 01 00",
                },
                new SleepStep
                {
                    Name = "Hold LED state",
                    DurationMs = 200,
                },
                new SendStep
                {
                    Name = "Set LED mode (breathing)",
                    RequestHex = "02 00 B3 01 01",
                },
            ],
        };

        TestSuite waitSuite = new()
        {
            Name = "Send and Wait Demo",
            Description = "Sends a command and waits for response packets without asserting their content.",
            StopOnFirstFailure = true,
            ResetWhenDone = false,
            Steps =
            [
                new SendAndWaitStep
                {
                    Name = "Query and collect 1 reply",
                    RequestHex = "02 00 B5 00",
                    TotalTimeoutMs = 200,
                    ExpectedCount = 1,
                },
                new SleepStep
                {
                    Name = "Pause before next query",
                    DurationMs = 100,
                },
                new SendAndWaitStep
                {
                    Name = "Query and collect 2 replies",
                    RequestHex = "02 00 B5 01",
                    TotalTimeoutMs = 500,
                    ExpectedCount = 2,
                },
            ],
        };

        TestSuite resetSuite = new()
        {
            Name = "Full Sequence with Reset",
            Description = "Runs a full send → assert → sleep → send → assert cycle, then resets the device.",
            StopOnFirstFailure = true,
            ResetWhenDone = true,
            Steps =
            [
                new SendAndAssertStep
                {
                    Name = "Write config",
                    RequestHex = "02 00 B4 01 FF",
                    ExpectedLines = ["02 00 B4 01 00"],
                    TotalTimeoutMs = 200,
                    AllowTrailingWildcard = true,
                },
                new SleepStep
                {
                    Name = "Wait for config apply",
                    DurationMs = 300,
                },
                new SendAndAssertStep
                {
                    Name = "Verify config written",
                    RequestHex = "02 00 B4 02",
                    ExpectedLines = ["02 00 B4 02 FF"],
                    TotalTimeoutMs = 200,
                    AllowTrailingWildcard = true,
                },
                new SendStep
                {
                    Name = "Trigger save",
                    RequestHex = "02 00 B4 03",
                },
                new SleepStep
                {
                    Name = "Wait for save",
                    DurationMs = 500,
                },
            ],
        };

        return [getInfoSuite, sendOnlySuite, waitSuite, resetSuite];
    }

    #endregion

    #region ---- step CRUD ----

    private async void AddStep(TestSuiteEntry suiteRow)
    {
        if (suiteRow?.Suite == null) return;

        StepEditDialog dialog = new();
        dialog.RecordStartRequested += (_, _) => StartRecording(dialog);
        dialog.RecordStopRequested += (_, _) => StopRecording();
        try
        {
            TestStep step = await dialog.CreateNewAsync();
            if (step != null)
            {
                suiteRow.Suite.Steps.Add(step);
                SaveSuites();
                suiteRow.RebuildStepRows();
                UpdateTestsSummary();
            }
        }
        finally
        {
            StopRecording();
        }
    }

    private async void EditStep(TestSuiteEntry suiteRow, TestStepEntry stepRow)
    {
        if (suiteRow?.Suite == null || stepRow?.Step == null) return;

        StepEditDialog dialog = new();
        dialog.RecordStartRequested += (_, _) => StartRecording(dialog);
        dialog.RecordStopRequested += (_, _) => StopRecording();
        try
        {
            if (await dialog.EditAsync(stepRow.Step))
            {
                SaveSuites();
                stepRow.Bind(stepRow.Step);
                suiteRow.UpdateStepCount();
            }
        }
        finally
        {
            StopRecording();
        }
    }

    private void DeleteStep(TestSuiteEntry suiteRow, TestStepEntry stepRow)
    {
        if (suiteRow?.Suite == null || stepRow?.Step == null) return;

        suiteRow.Suite.Steps.Remove(stepRow.Step);
        SaveSuites();
        suiteRow.RebuildStepRows();
        UpdateTestsSummary();
    }

    private async void ViewStep(TestStepEntry stepRow)
    {
        if (stepRow?.Step == null) return;

        StepRunResult result = stepRow.Step != null ? stepResults.GetValueOrDefault(stepRow.Step) : null;
        ProtocolTestViewDialog dialog = new();
        await dialog.ShowForStepAsync(stepRow.Step, result);
    }

    #endregion

    #region ---- running ----

    private async void RunSingleSuite(TestSuiteEntry row)
    {
        if (testsRunning || row?.Suite == null) return;

        testsRunning = true;
        SetTestsBusy(true);
        try
        {
            await RunSuiteAsync(row.Suite, row);
        }
        finally
        {
            testsRunning = false;
            SetTestsBusy(false);
        }
    }

    private async void RunAllSuites()
    {
        if (testsRunning || suitesList.Count == 0) return;

        testsRunning = true;
        SetTestsBusy(true);
        try
        {
            foreach (TestSuite suite in suitesList)
            {
                if (suiteRows.TryGetValue(suite, out TestSuiteEntry row))
                {
                    row.SetExpanded(true);
                    await RunSuiteAsync(suite, row);
                }
            }
        }
        finally
        {
            testsRunning = false;
            SetTestsBusy(false);
        }
    }

    private void SetTestsBusy(bool busy)
    {
        TestsAddSuiteBtn.IsEnabled = !busy;
        TestsRunAllBtn.IsEnabled = !busy && suitesList.Count > 0;
        TestsImportBtn.IsEnabled = !busy;
        TestsExportBtn.IsEnabled = !busy && suitesList.Count > 0;
    }

    private async System.Threading.Tasks.Task RunSuiteAsync(TestSuite suite, TestSuiteEntry row)
    {
        if (suite == null) return;

        row?.ShowRunning();

        PeripheralInterface targetInterface = ResolveTestInterface();
        if (targetInterface == null)
        {
            SuiteRunResult errorResult = new()
            {
                Verdict = TestVerdict.Error,
                Message = "Not connected — start capturing a device first.",
            };
            row?.ShowSummary(errorResult);
            return;
        }

        IReadOnlyList<TestStepEntry> stepEntries = row?.StepEntries ?? Array.Empty<TestStepEntry>();
        foreach (TestStepEntry stepEntry in stepEntries)
            stepEntry.ShowRunning();

        TestSuiteRunner runner = new(targetInterface);
        SuiteRunResult result = await runner.RunAsync(
            suite,
            onStepDone: (index, stepResult) =>
            {
                if (index < suite.Steps.Count)
                    stepResults[suite.Steps[index]] = stepResult;

                if (index < stepEntries.Count)
                {
                    TestStepEntry entry = stepEntries[index];
                    entry.ShowReceived(stepResult.Received);
                    entry.ShowResult(stepResult.Verdict, stepResult.Message,
                        TimeSpan.FromMilliseconds(stepResult.ElapsedMs));
                }
            },
            onPackets: (index, packets) =>
            {
                if (index < stepEntries.Count)
                    stepEntries[index].ShowReceived(packets);
            });

        suiteResults[suite] = result;
        row?.ShowSummary(result);
    }

    private PeripheralInterface ResolveTestInterface()
    {
        Device device = DeviceSelection.Instance.ActiveDevice;
        if (device == null || device.interfaces.Count == 0)
            return null;

        int[] usagePages = [0xFF01, 0xFF02, 0xFF00];

        IPeripheralDetail deviceInterface = usagePages
            .Select(usagePage => device.interfaces.FirstOrDefault(
                @interface => @interface.UsagePage == usagePage &&
                              @interface.Usage == 1))
            .FirstOrDefault(@interface => @interface != null);

        if (deviceInterface == null)
            return null;

        return deviceInterface.Connect(true);
    }

    #endregion

    #region ---- recording ----

    private void StartRecording(StepEditDialog dialog)
    {
        if (recording) return;

        PeripheralInterface targetInterface = ResolveTestInterface();
        if (targetInterface == null)
        {
            dialog.NotifyRecordingStopped("Not connected — start capturing a device first.");
            return;
        }

        recordDialog = dialog;
        recordingInterface = targetInterface;
        recording = true;
        dialog.ClearExpectedForRecording();

        recordHandler = (data, time) =>
        {
            byte[] actual = data.ToArray();
            byte[] line = actual.Length > 0 ? actual[1..] : actual;
            string text = ByteToString(line, false);
            Dispatcher.InvokeAsync(() => recordDialog?.AppendRecordedPacket(text));
        };
        targetInterface.OnDataReceived += recordHandler;

        byte[] request = ParseCommand(dialog.CurrentRequestHex);
        if (request != null && request.Length > 0)
            _ = targetInterface.WriteAsync(request, CancellationToken.None);
    }

    private void StopRecording()
    {
        if (!recording) return;
        recording = false;

        if (recordingInterface != null && recordHandler != null)
            recordingInterface.OnDataReceived -= recordHandler;

        recordHandler = null;
        recordingInterface = null;
        recordDialog?.NotifyRecordingStopped("Recording stopped.");
        recordDialog = null;
    }

    #endregion

    #region ---- import / export ----

    private async void ImportSuites()
    {
        if (testsRunning) return;

        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Import Test Suites",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            string json = await System.IO.File.ReadAllTextAsync(dialog.FileName);
            TestDocument doc = System.Text.Json.JsonSerializer.Deserialize<TestDocument>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (doc?.Suites == null || doc.Suites.Count == 0)
            {
                Debug.Log("[BusHound.Tests] Import: no suites found in file.");
                return;
            }

            foreach (TestSuite suite in doc.Suites)
            {
                suite.Id = Guid.NewGuid().ToString("N");
                foreach (TestStep step in suite.Steps)
                    step.Id = Guid.NewGuid().ToString("N");
            }

            suitesList.AddRange(doc.Suites);
            SaveSuites();
            BuildSuiteRows();
            Debug.Log($"[BusHound.Tests] Imported {doc.Suites.Count} suite(s) from {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Debug.Log($"[BusHound.Tests] Import failed: {ex.Message}");
        }
    }

    private async void ExportSuites()
    {
        if (testsRunning || suitesList.Count == 0) return;

        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "Export Test Suites",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = "protocol-tests.json",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            TestDocument doc = new() { Suites = suitesList };
            string json = System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            });
            await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
            Debug.Log($"[BusHound.Tests] Exported {suitesList.Count} suite(s) to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Debug.Log($"[BusHound.Tests] Export failed: {ex.Message}");
        }
    }

    #endregion
}
