namespace Base.UI.Pages;

using Base.Core;
using Base.Framework.Utilities;
using Base.Pages;
using Base.Services;
using Base.Services.APIService;
using Base.Services.Peripheral;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static Base.UI.Pages.ASUSBusHoundPage.BusListener;

public partial class ASUSBusHoundPage : PageBase, INotifyPropertyChanged
{
    public override string PageName => "Bus Hound";
    public override string Glyph => "\uEE6F";

    public override string Description =>
        "ASUS Bus Hound is a software that allows you to control your ASUS laptop's fans and performance modes. This page provides integration with ASUS Hound, allowing you to monitor and adjust your laptop's performance settings directly from this application.";

    private const int MaxVisibleItems = 20000;
    private const int MaxBatchSizePerTick = 2000;

    // Logs
    private readonly ConcurrentQueue<LogRow> pendingLogs = new();
    private ScrollViewer scrollViewer;
    private bool stickToBottom = true;

    // Interface Connection
    private List<PeripheralInterface> connectedInterfaces;

    // Track interval between received packets (shared across all connected interfaces)
    private long lastReceivedTicksShared;

    // Track History Sent Commands
    private List<string> historyCommands;
    private int currentSelectedHistoryIndex;
    private string currentBufferCommand;

    public BulkObservableCollection<LogRow> Items { get; } = new();

    public event PropertyChangedEventHandler PropertyChanged;

    // Keep track of event handlers so we can unhook them on Stop without disposing interfaces.
    private readonly Dictionary<PeripheralInterface, Action<ReadOnlyMemory<byte>, DateTime>> dataReceivedHandlers = new();
    private readonly Dictionary<PeripheralInterface, Action<ReadOnlyMemory<byte>, DateTime>> dataSentHandlers = new();

    // ── Quick Action ──
    private const string QuickActionStoreKey = "BusHound_QuickActions";
    private List<QuickActionEntryData> quickActionEntries = new();
    private bool isQuickActionPanelOpen = false;

    // Debug
    //private readonly CancellationTokenSource usbpcapcts;

    public ASUSBusHoundPage()
    {
        InitializeComponent();
        DataContext = this;

        StartBtn.Click += StartBtn_Click;
        StopBtn.Click += StopBtn_Click;

        SendBtn.Click += SendBtn_Click;
        CmdInputbox.PreviewKeyDown += CmdInputbox_KeyDown;
        CmdInputbox.TextChanged += CmdInputbox_Validation;
        CmdInputbox.PreviewTextInput += CmdInputbox_ValidateInputCharacter;
        LogGrid.CopyingRowClipboardContent += CopyingRowClipboardContent;

        // Quick Action buttons
        QuickActionBtn.Click += (_, _) => ToggleQuickActionPanel();
        QuickActionCloseBtn.Click += (_, _) => SetQuickActionPanelOpen(false);
        QuickActionAddBtn.Click += (_, _) => AddNewQuickActionEntry();
        QuickActionImportBtn.Click += (_, _) => ImportQuickActionFile();

        //USBPCapHandler usbpcap = new();
        //usbpcap.DebugPrintDeviceTree(8);

        //usbpcap.OnPacketCaptured += Usbpcap_OnPacketCaptured;

        //usbpcapcts = new CancellationTokenSource();
        //Task.Run(() =>
        //{
        //    _ = usbpcap.StartCaptureToDebugAsync(@"\\.\USBPcap1", deviceAddress: 16, cancellationToken: usbpcapcts.Token);
        //});
        //Start8KTest();
        //polling();
    }

    //DispatcherTimer testTimer;

    //private void Start8KTest()
    //{
    //    testTimer = new DispatcherTimer
    //    {
    //        Interval = TimeSpan.FromMicroseconds(1/8000f)
    //    };
    //    testTimer.Tick += (_, _) =>
    //    {
    //        byte[] data = new byte[64];
    //        new Random().NextBytes(data);
    //        EnqueueLog("TestDevice", "IN", data, "1s");
    //    };
    //    testTimer.Start();
    //}

    //DispatcherTimer pollTimer;

    //private void polling()
    //{
    //    pollTimer = new DispatcherTimer
    //    {
    //        Interval = TimeSpan.FromSeconds(0.5f)
    //    };
    //    pollTimer.Tick += (_, _) =>
    //    {
    //        FlushPendingLogs();
    //    };
    //    pollTimer.Start();
    //}

    //private void Usbpcap_OnPacketCaptured(ref USBPCapHandler.PcapRecordHeader pcapHeader, ref USBPCapHandler.UsbPcapPacket packet)
    //{
    //    //if (packet.Payload.Length == 0) return;
    //    //string direction = packet.Direction switch
    //    //{
    //    //    USBPCapHandler.UsbPcapEndpointDirection.OUT => "OUT",
    //    //    USBPCapHandler.UsbPcapEndpointDirection.IN => "IN",
    //    //    _ => "UNKNOWN"
    //    //};
    //    //byte[] data = packet.Payload.ToArray();
    //    //StartEnqueueLog();
    //    EnqueueLog(
    //        $"1",
    //        "IN",
    //        [0x01],
    //        $"{pcapHeader.TsSec}.{pcapHeader.TsUsec}");
    //}

    //private void StartEnqueueLog()
    //{
    //    _ = Task.Run(() =>
    //    {
    //        int count = 0;
    //        byte[] data = new byte[64];
    //        new Random().NextBytes(data);
    //        EnqueueLog("AsyncTestDevice", Phase.IN, data, $"{count++}");
    //    });
    //}

    protected override void Update()
    {
        base.Update();
        FlushPendingLogs();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        // Persist quick action entries on destroy
        SaveQuickActionEntries();
        //usbpcapcts.Cancel();
    }

    public override void Start()
    {
        base.Start();
        connectedInterfaces = [];
        dataReceivedHandlers.Clear();
        dataSentHandlers.Clear();
        scrollViewer = FindVisualChild<ScrollViewer>(LogGrid);
        historyCommands = [];
        currentSelectedHistoryIndex = -1;

        if (scrollViewer is not null)
        {
            scrollViewer.ScrollChanged += OnScrollChanged;
            UpdateStickToBottom(scrollViewer);
        }

        // Load quick action entries from persistent storage
        LoadQuickActionEntries();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        DeviceSelection.Instance.OnActiveDeviceConnected += ConnectAndStart;
        DeviceSelection.Instance.OnActiveDeviceDisconnected += Stop;

        if (ActiveDevice != null)
        {
            ConnectAndStart();
        }

        if (Items.Count > 0)
        {
            Dispatcher.InvokeAsync(() => LogGrid.ScrollIntoView(Items[^1]), DispatcherPriority.Background);
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        DeviceSelection.Instance.OnActiveDeviceConnected -= ConnectAndStart;
        DeviceSelection.Instance.OnActiveDeviceDisconnected -= Stop;
    }

    // ----------------------------
    // External Listeners
    // ----------------------------

    public List<BusListener> busListeners = new();

    [POST]
    public void UnregisterListener(string name)
    {
        var listener = busListeners.FirstOrDefault(l => l.Name == name);
        if (listener != null)
        {
            busListeners.Remove(listener);
        }
    }

    [POST]
    public bool RegisterNewListener(string name, int bufferSize, int bucketSize)
    {
        if (busListeners.Any(l => l.Name == name))
        {
            return false; // Name already exists
        }
        busListeners.Add(new BusListener(name, bufferSize, bucketSize));
        return true;
    }

    [GET]
    public ApiResponse ReadOne(string name)
    {
        var listener = busListeners.FirstOrDefault(l => l.Name == name);
        if (listener != null)
        {
            if (!listener.ReceivedData.TryDequeue(out MemoryData item)) return null;
            string byteAsString = ByteToString(item.bucket.AsSpan(0, item.length).ToArray(), false);
            return new ApiResponse
            {
                Data = new
                {
                    Bytes = byteAsString,
                    Phase = Enum.GetName(item.phase),
                    Delta = FormatInterval(TimeSpan.FromTicks(item.delta))
                },
                Status = 200,
            };
        }
        return new ApiResponse
        {
            Status = 200,
            Data = null
        };
    }

    [GET]
    public ApiResponse ReadAll(string name)
    {
        var listener = busListeners.FirstOrDefault(l => l.Name == name);
        if (listener != null)
        {
            List<object> allData = new();
            while (listener.ReceivedData.TryDequeue(out MemoryData item))
            {
                string byteAsString = ByteToString(item.bucket.AsSpan(0, item.length).ToArray(), false);
                allData.Add(
                    new
                    {
                        Bytes = byteAsString,
                        Phase = Enum.GetName(item.phase),
                        Delta = FormatInterval(TimeSpan.FromTicks(item.delta))
                    });
            }
            return new ApiResponse
            {
                Data = allData,
                Status = 200,
            };
        }
        return new ApiResponse
        {
            Status = 200,
            Data = null
        };
    }

    public class BusListener
    {
        public string Name { get; }
        public readonly RingBuffer<MemoryData> ReceivedData;
        public readonly int bufferSize;

        public BusListener(string name, int bufferSize, int bucketSize)
        {
            Name = name;
            ReceivedData = new RingBuffer<MemoryData>(bufferSize);
            ReceivedData.Fill((ref MemoryData md) =>
            {
                md = new MemoryData
                {
                    bucket = new byte[bucketSize],
                    length = 0
                };
            });
            this.bufferSize = bufferSize;
        }

        public void EnqueueData(byte[] data, Phase phase, long delta)
        {
            ReceivedData.EnqueueValue((ref MemoryData memoryData) =>
            {
                int length = Math.Min(data.Length, bufferSize);
                Array.Copy(data, memoryData.bucket, length);
                memoryData.length = length;
                memoryData.phase = phase;
                memoryData.delta = delta;
            });
        }

        public class MemoryData
        {
            public byte[] bucket;
            public int length;
            public Phase phase;
            public long delta;
        }
    }

    // ----------------------------
    // Quick Action Panel
    // ----------------------------

    private void ToggleQuickActionPanel()
    {
        SetQuickActionPanelOpen(!isQuickActionPanelOpen);
    }

    private void SetQuickActionPanelOpen(bool open)
    {
        isQuickActionPanelOpen = open;
        if (open)
        {
            QuickActionPanel.Visibility = Visibility.Visible;
            QuickActionColumnDef.Width = new GridLength(380);
        }
        else
        {
            QuickActionPanel.Visibility = Visibility.Collapsed;
            QuickActionColumnDef.Width = new GridLength(0);
        }
    }

    private void LoadQuickActionEntries()
    {
        try
        {
            var stored = LocalAppDataStore.Instance.Get<List<QuickActionEntryData>>(QuickActionStoreKey);
            quickActionEntries = stored ?? new List<QuickActionEntryData>();
        }
        catch
        {
            quickActionEntries = new List<QuickActionEntryData>();
        }

        RebuildQuickActionUI();
    }

    private void SaveQuickActionEntries()
    {
        try
        {
            LocalAppDataStore.Instance.Set(QuickActionStoreKey, quickActionEntries);
        }
        catch
        {
            // Best-effort persist
        }
    }

    private void RebuildQuickActionUI()
    {
        QuickActionEntriesPanel.Children.Clear();

        foreach (var entryData in quickActionEntries)
        {
            var control = CreateEntryControl(entryData);
            QuickActionEntriesPanel.Children.Add(control);
        }
    }

    private QuickActionEntryControl CreateEntryControl(QuickActionEntryData entryData)
    {
        var control = new QuickActionEntryControl(entryData);
        control.DataChanged += SaveQuickActionEntries;
        control.SendRequested += OnQuickActionSendRequested;
        control.DeleteRequested += OnQuickActionDeleteRequested;
        return control;
    }

    private void AddNewQuickActionEntry()
    {
        var newEntry = new QuickActionEntryData();
        quickActionEntries.Add(newEntry);
        var control = CreateEntryControl(newEntry);
        QuickActionEntriesPanel.Children.Add(control);
        SaveQuickActionEntries();
    }

    private void OnQuickActionDeleteRequested(QuickActionEntryControl control)
    {
        var result = MessageBox.Show(
            $"Delete quick action \"{control.EntryData.Name}\"?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        quickActionEntries.Remove(control.EntryData);
        QuickActionEntriesPanel.Children.Remove(control);
        SaveQuickActionEntries();
    }

    private async void OnQuickActionSendRequested(QuickActionEntryData entry)
    {
        if (connectedInterfaces is null || connectedInterfaces.Count == 0)
        {
            MessageBox.Show("No device connected. Start a connection first.", "Not Connected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Determine the target interface from the combo box selection
        PeripheralInterface targetInterface = null;
        if (UsageSelectionBox.SelectedItem is AvailableDeviceItem t)
        {
            targetInterface = connectedInterfaces.Find(
                i => i.ProductInfo.Usage == t.usage && i.ProductInfo.UsagePage == t.usagePage);
        }
        targetInterface ??= connectedInterfaces.FirstOrDefault();
        if (targetInterface is null) return;

        var commands = entry.Commands;
        if (commands.Count == 0) return;

        for (int i = 0; i < commands.Count; i++)
        {
            byte[] bytes = QuickActionEntryData.HexToBytes(commands[i]);
            if (bytes is null || bytes.Length == 0) continue;

            ProtocolService.AppendCmd(targetInterface, bytes);

            // Wait interval between commands (except after the last one)
            if (i < commands.Count - 1 && entry.IntervalMs > 0)
            {
                await Task.Delay(entry.IntervalMs);
            }
        }
    }

    #region Import Quick Action

    private void ImportQuickActionFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import Quick Action Commands",
            Filter = "All supported|*.bin;*.txt|Binary files (*.bin)|*.bin|Text files (*.txt)|*.txt",
            Multiselect = false
        };

        if (dlg.ShowDialog() != true) return;

        string path = dlg.FileName;
        string ext = Path.GetExtension(path).ToLowerInvariant();

        try
        {
            QuickActionEntryData imported = ext switch
            {
                ".bin" => ImportBinFile(path),
                ".txt" => ImportTxtFile(path),
                _ => throw new NotSupportedException($"Unsupported file type: {ext}")
            };

            if (imported.Commands.Count == 0)
            {
                MessageBox.Show("No valid commands found in the file.", "Import Result",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            quickActionEntries.Add(imported);
            var control = CreateEntryControl(imported);
            QuickActionEntriesPanel.Children.Add(control);
            SaveQuickActionEntries();

            // Auto-open the panel if it's hidden
            if (!isQuickActionPanelOpen)
                SetQuickActionPanelOpen(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to import file:\n{ex.Message}", "Import Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Import a .bin file: reads raw bytes and chunks them into 64-byte commands.
    /// </summary>
    private static QuickActionEntryData ImportBinFile(string path)
    {
        byte[] rawBytes = File.ReadAllBytes(path);
        var entry = new QuickActionEntryData
        {
            Name = Path.GetFileNameWithoutExtension(path)
        };

        // Chunk the raw bytes into commands of up to 64 bytes each
        int offset = 0;
        while (offset < rawBytes.Length)
        {
            int chunkSize = Math.Min(QuickActionEntryData.MaxCommandBytes, rawBytes.Length - offset);
            byte[] chunk = new byte[chunkSize];
            Array.Copy(rawBytes, offset, chunk, 0, chunkSize);
            entry.Commands.Add(QuickActionEntryData.BytesToHex(chunk));
            offset += chunkSize;
        }

        return entry;
    }

    /// <summary>
    /// Import a .txt file: each line is parsed as one hex command.
    /// Lines starting with non-alphanumeric characters (#, %, @, etc.) are treated as comments and skipped.
    /// </summary>
    private static QuickActionEntryData ImportTxtFile(string path)
    {
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        var entry = new QuickActionEntryData
        {
            Name = Path.GetFileNameWithoutExtension(path)
        };

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Lines starting with non-alphanumeric characters are comments
            char first = line[0];
            if (!char.IsLetterOrDigit(first))
                continue;

            byte[] bytes = QuickActionEntryData.HexToBytes(line);
            if (bytes is not null && bytes.Length > 0)
                entry.Commands.Add(QuickActionEntryData.BytesToHex(bytes));
        }

        return entry;
    }
    #endregion

    private void CopyingRowClipboardContent(object sender, DataGridRowClipboardEventArgs e)
    {
        e.ClipboardRowContent.Clear();

        var item = (LogRow)e.Item;

        e.ClipboardRowContent.Add(new DataGridClipboardCellContent(
            e.Item,
            LogGrid.Columns[1],
            $"{Enum.GetName(item.Phase)}\t"
        ));

        e.ClipboardRowContent.Add(new DataGridClipboardCellContent(
            e.Item,
            LogGrid.Columns[2],
            $"{ByteToString(item.Data, false)}\t"
        ));
        e.ClipboardRowContent.Add(new DataGridClipboardCellContent(
            e.Item,
            LogGrid.Columns[3],
            $"{item.Description}"
        ));
        e.ClipboardRowContent.Add(new DataGridClipboardCellContent(
            e.Item,
            LogGrid.Columns[4],
            $"{item.Delta}"
        ));
    }

    private void CmdInputbox_ValidateInputCharacter(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // Allow only hex characters and whitespace.
        foreach (char c in e.Text)
        {
            if (!IsHex(c) && !char.IsWhiteSpace(c))
            {
                e.Handled = true;
                break;
            }
        }
        static bool IsHex(char c)
            => (uint)(c - '0') <= 9
            || (uint)((c | 32) - 'a') <= 5;
    }

    private void CmdInputbox_Validation(object sender, TextChangedEventArgs e)
    {
        byte[] parsedCmd = ParseCommand(CmdInputbox.Text);
        CmdInputbox.BorderBrush = parsedCmd == null
            ? ResourceBrush.Find("SystemControlErrorTextForegroundBrush")
            : ResourceBrush.Find("SystemControlBackgroundBaseHighRevealBorderBrush");
    }

    private void CmdInputbox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter:
                SendBtn_Click(sender, e);
                e.Handled = true;
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Up:
                if (currentSelectedHistoryIndex < 0) currentBufferCommand = CmdInputbox.Text;
                if (historyCommands.Count > currentSelectedHistoryIndex + 1)
                {
                    CmdInputbox.Text = historyCommands[++currentSelectedHistoryIndex];
                }
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Down:
                if (currentSelectedHistoryIndex > 0)
                {
                    CmdInputbox.Text = historyCommands[--currentSelectedHistoryIndex];
                }
                else
                {
                    CmdInputbox.Text = currentBufferCommand;
                    currentSelectedHistoryIndex = -1;
                }
                e.Handled = true;
                break;
        }
    }

    private void SendBtn_Click(object sender, RoutedEventArgs e)
    {
        if (UsageSelectionBox.SelectedItem is AvailableDeviceItem t)
        {
            string inputText = CmdInputbox.Text;
            SendCmd(t.usage, t.usagePage, inputText);

            // Update history: only add if it's not the same as the last command.
            currentSelectedHistoryIndex = -1;
            currentBufferCommand = null;
            string last = historyCommands.FirstOrDefault();
            if (string.IsNullOrEmpty(last))
            {
                historyCommands.Insert(0, inputText);
                return;
            }
            else
            {
                if (string.Equals(last, inputText)) return;
                else historyCommands.Insert(0, inputText);
            }
        }
    }

    [POST]
    private bool SendCmd(ushort usage, ushort usagePage, string cmdText)
    {
        if (connectedInterfaces is null || connectedInterfaces.Count == 0) return false;
        if(string.Compare(cmdText, "04 05") == 0)
        {
            Debug.Log("Exit Hall Sensor");
        }
        PeripheralInterface targetInterface = connectedInterfaces.Find(
            i => i.ProductInfo.Usage == usage && i.ProductInfo.UsagePage == usagePage);
        return targetInterface == null ? false : SendCmd(targetInterface, cmdText);
    }

    private bool SendCmd(PeripheralInterface pInterface, string cmdText)
    {
        byte[] parsedCmd = ParseCommand(cmdText);
        if (parsedCmd == null || parsedCmd.Length == 0) return false;
        ProtocolService.AppendCmd(pInterface, parsedCmd);
        return true;
    }

    /// <summary>
    /// Writes a hex command to the specified interface and waits for a response within the given timeout.
    /// </summary>
    /// <param name="usage">HID Usage of the target interface.</param>
    /// <param name="usagePage">HID Usage Page of the target interface.</param>
    /// <param name="cmdText">Hex string command (e.g. "02 00 B5 00").</param>
    /// <param name="timeoutMs">Timeout in milliseconds to wait for a response.</param>
    [POST]
    public async Task<ApiResponse> WriteAndRead(ushort usage, ushort usagePage, string cmdText, int timeoutMs = 1000)
    {
        if (connectedInterfaces is null || connectedInterfaces.Count == 0)
            return new ApiResponse { Status = 503, Data = "No device connected." };

        PeripheralInterface targetInterface = connectedInterfaces.Find(
            i => i.ProductInfo.Usage == usage && i.ProductInfo.UsagePage == usagePage)
            ?? connectedInterfaces.FirstOrDefault();

        if (targetInterface is null)
            return new ApiResponse { Status = 503, Data = "No matching interface found." };

        byte[] parsedCmd = ParseCommand(cmdText);
        if (parsedCmd is null || parsedCmd.Length == 0)
            return new ApiResponse { Status = 400, Data = "Invalid command string." };

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            byte[] response = await targetInterface.WriteAndReadAsync(parsedCmd, cts.Token);
            return new ApiResponse
            {
                Status = 200,
                Data = new
                {
                    Bytes = ByteToString(response, false),
                    Length = response.Length
                }
            };
        }
        catch (OperationCanceledException)
        {
            return new ApiResponse { Status = 408, Data = $"Read timed out after {timeoutMs} ms." };
        }
        catch (Exception ex)
        {
            return new ApiResponse { Status = 500, Data = ex.Message };
        }
    }

    private void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        ConnectAndStart();
    }

    private void ConnectAndStart()
    {
        if (ActiveDevice == null) return;

        // Clear previous session state before connecting.
        Stop();
        ClearLogs();
        ClearPendingLogs();
        Interlocked.Exchange(ref lastReceivedTicksShared, 0);

        foreach (var deviceInterface in ActiveDevice.interfaces)
        {
            try
            {
                var newInterface = deviceInterface.Connect();
                if (newInterface == null) continue;

                connectedInterfaces.Add(newInterface);

                Action<ReadOnlyMemory<byte>, DateTime> receivedHandler = (data, time) =>
                {
                    long delta = 0;

                    // Shared interval across all interfaces.
                    long previousTicks = Interlocked.Exchange(ref lastReceivedTicksShared, time.Ticks);
                    if (previousTicks != 0)
                    {
                        long diffTicks = time.Ticks - previousTicks;
                        if (diffTicks >= 0)
                        {
                            delta = diffTicks;
                        }
                    }

                    EnqueueLog(
                        deviceInterface.UsagePage.ToString("X4"),
                        Phase.IN,
                        data[1..].ToArray(),
                        delta);
                };

                Action<ReadOnlyMemory<byte>, DateTime> sentHandler = (data, time) =>
                {
                    long delta = 0;

                    // Shared interval across all interfaces.
                    long previousTicks = Interlocked.Exchange(ref lastReceivedTicksShared, time.Ticks);
                    if (previousTicks != 0)
                    {
                        long diffTicks = time.Ticks - previousTicks;
                        if (diffTicks >= 0)
                        {
                            delta = diffTicks;
                        }
                    }

                    EnqueueLog(
                        deviceInterface.UsagePage.ToString("X4"),
                        Phase.OUT,
                        data.ToArray(),
                        delta);
                };

                // Avoid double-subscribing if Start is pressed multiple times.
                if (dataReceivedHandlers.TryGetValue(newInterface, out var existingRx))
                {
                    try { newInterface.OnDataReceived -= existingRx; } catch { }
                    dataReceivedHandlers.Remove(newInterface);
                }

                if (dataSentHandlers.TryGetValue(newInterface, out var existingTx))
                {
                    try { newInterface.OnDataSent -= existingTx; } catch { }
                    dataSentHandlers.Remove(newInterface);
                }

                dataReceivedHandlers[newInterface] = receivedHandler;
                newInterface.OnDataReceived += receivedHandler;

                dataSentHandlers[newInterface] = sentHandler;
                newInterface.OnDataSent += sentHandler;
            }
            catch
            {
                // Ignore individual interface connection/subscription errors.
            }
        }
        PopulateComboBox();

        StartBtn.IsEnabled = false;
        StopBtn.IsEnabled = true;
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        Stop();
    }

    private void Stop()
    {
        if (connectedInterfaces is null || connectedInterfaces.Count == 0)
        {
            return;
        }

        foreach (var iface in connectedInterfaces)
        {
            if (iface is null)
            {
                continue;
            }

            try
            {
                if (dataReceivedHandlers.TryGetValue(iface, out var handler))
                {
                    iface.OnDataReceived -= handler;
                    dataReceivedHandlers.Remove(iface);
                }

                if (dataSentHandlers.TryGetValue(iface, out var sentHandler))
                {
                    iface.OnDataSent -= sentHandler;
                    dataSentHandlers.Remove(iface);
                }

                // Best-effort: stop any queued RX so we don't immediately flush stale data on next Start.
                iface.ClearPendingReports();
            }
            catch
            {
                // Best-effort stop listening; ignore individual failures.
            }
        }

        connectedInterfaces.Clear();

        Dispatcher.Invoke(() =>
        {
            ClearComboBox();
            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
        });
    }

    private class AvailableDeviceItem(ushort usage, ushort usagePage)
    {
        public ushort usage = usage;
        public ushort usagePage = usagePage;

        public override string ToString()
        {
            return $"{usage:X4}, {usagePage:X4}";
        }
    }

    private void PopulateComboBox()
    {
        ClearComboBox();

        var items = connectedInterfaces
            .Select(ci => new AvailableDeviceItem(
                ci.ProductInfo.Usage,
                ci.ProductInfo.UsagePage))
            .OrderBy(i => i.usage)
            .ThenBy(i => i.usagePage);

        foreach (var item in items)
            UsageSelectionBox.Items.Add(item);

        // Select first item that UsagePage is greater or equal to 0xFF00
        for (int i = 0; i < UsageSelectionBox.Items.Count; i++)
        {
            if (UsageSelectionBox.Items[i] is AvailableDeviceItem adi && adi.usagePage >= 0xFF00)
            {
                UsageSelectionBox.SelectedIndex = i;
                break;
            }
        }
    }

    private void ClearComboBox()
    {
        UsageSelectionBox.Items.Clear();
    }

    private static string FormatInterval(TimeSpan interval)
    {
        double totalMicroseconds = interval.TotalMilliseconds * 1000.0;
        if (totalMicroseconds < 1000.0)
        {
            return $"{FormatNumber(totalMicroseconds)}us";
        }

        double totalMilliseconds = interval.TotalMilliseconds;
        return totalMilliseconds < 1000.0
            ? $"{FormatNumber(totalMilliseconds)}ms"
            : $"{FormatNumber(interval.TotalSeconds)}s";

        static string FormatNumber(double value)
        {
            if (value == 0) return "0";

            double abs = Math.Abs(value);
            int digits = (int)Math.Floor(Math.Log10(abs)) + 1;
            int decimals = Math.Max(0, 3 - digits);

            double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            return rounded.ToString($"0.{new string('#', decimals)}");
        }
    }

    public void EnqueueLog(string device, Phase phase, byte[] data, long delta)
    {
        string diff = FormatInterval(TimeSpan.FromTicks(delta));
        pendingLogs.Enqueue(new LogRow(device, phase, data, ByteToString(data), "", diff));

        EnqueueToListeners(data, phase, delta);
    }

    private void EnqueueToListeners(byte[] data, Phase phase, long delta)
    {
        foreach (var listener in busListeners)
        {
            listener.EnqueueData(data, phase, delta);
        }
    }

    public void ClearLogs()
    {
        Dispatcher.Invoke(() => Items.ClearAll());
    }

    private void ClearPendingLogs()
    {
        while (pendingLogs.TryDequeue(out _))
        {
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            UpdateStickToBottom(sv);
        }
    }

    private void UpdateStickToBottom(ScrollViewer sv)
    {
        stickToBottom = sv.ScrollableHeight <= 0 || sv.VerticalOffset >= sv.ScrollableHeight - 1.0;
    }

    readonly List<LogRow> batch = new(Math.Min(MaxBatchSizePerTick, 1024));

    private void FlushPendingLogs()
    {
        if (pendingLogs.IsEmpty)
        {
            return;
        }

        batch.Clear();
        while (batch.Count < MaxBatchSizePerTick && pendingLogs.TryDequeue(out LogRow row))
        {
            batch.Add(row);
        }

        if (batch.Count == 0)
        {
            return;
        }

        Items.AddRange(batch);

        int overflow = Items.Count - MaxVisibleItems;
        if (overflow > 0)
        {
            Items.TrimStart(overflow);
        }

        if (stickToBottom && Items.Count > 0)
        {
            LogGrid.ScrollIntoView(Items[^1]);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
            {
                return match;
            }

            T? descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static byte[] ParseCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<byte>();

        ReadOnlySpan<char> s = text.AsSpan();

        // First pass: count hex digits
        int hexCount = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (IsHex(c))
                hexCount++;
            else if (!char.IsWhiteSpace(c))
                return null;
        }

        if ((hexCount & 1) != 0)
            return null;

        byte[] result = new byte[hexCount >> 1];

        // Second pass: parse
        int ri = 0;
        int hi = -1;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (!IsHex(c))
                continue;

            int val = HexVal(c);

            if (hi < 0)
            {
                hi = val;
            }
            else
            {
                result[ri++] = (byte)((hi << 4) | val);
                hi = -1;
            }
        }

        return result;

        static bool IsHex(char c)
            => (uint)(c - '0') <= 9
            || (uint)((c | 32) - 'a') <= 5;

        static int HexVal(char c)
            => (uint)(c - '0') <= 9
                ? c - '0'
                : ((c | 32) - 'a' + 10);
    }

    private static string ByteToString(byte[] bytes, bool newLine = true)
    {
        int n = bytes.Length;
        if (n == 0)
            return string.Empty;

        int m = n - 1;
        int doubleSpaces = (m >> 2) - (m >> 4);
        int length = (n << 1) + m + doubleSpaces;

        return string.Create(length, (bytes, newLine), static (dst, state) =>
        {
            var (src, nl) = state;

            const string Hex = "0123456789ABCDEF";
            int pos = 0;

            for (int i = 0; i < src.Length; i++)
            {
                if (i != 0)
                {
                    if ((i & 15) == 0)
                    {
                        dst[pos++] = nl ? '\n' : ' ';
                    }
                    else
                    {
                        dst[pos++] = ' ';
                        if ((i & 3) == 0)
                            dst[pos++] = ' ';
                    }
                }

                byte b = src[i];
                dst[pos++] = Hex[b >> 4];
                dst[pos++] = Hex[b & 0x0F];
            }
        });
    }

    private static string ByteToDescription(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        int lines = (bytes.Length - 1) >> 4;
        int length = bytes.Length + lines;

        return string.Create(length, bytes, static (dst, src) =>
        {
            int di = 0;

            for (int i = 0; i < src.Length; i++)
            {
                if (i != 0 && (i & 15) == 0)
                    dst[di++] = '\n';

                byte b = src[i];
                dst[di++] = IsDisplayableAscii(b) ? (char)b : '.';
            }

            static bool IsDisplayableAscii(byte b)
                => b is >= 0x20 and <= 0x7E;
        });
    }
}

public enum Phase
{
    IN,
    OUT,
    CTL
}

public sealed record LogRow(
    string Device,
    Phase Phase,
    byte[] Data,
    string DataString,
    string Description,
    string Delta);

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool suppressNotification;

    public void AddRange(IEnumerable<T> items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        IList<T> materialized = items as IList<T> ?? items.ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        suppressNotification = true;
        try
        {
            foreach (T item in materialized)
            {
                Items.Add(item);
            }
        }
        finally
        {
            suppressNotification = false;
        }

        RaiseReset();
    }

    public void TrimStart(int count)
    {
        if (count <= 0 || Items.Count == 0)
        {
            return;
        }

        if (count >= Items.Count)
        {
            ClearAll();
            return;
        }

        suppressNotification = true;
        try
        {
            for (int i = 0; i < count; i++)
            {
                Items.RemoveAt(0);
            }
        }
        finally
        {
            suppressNotification = false;
        }

        RaiseReset();
    }

    public void ClearAll()
    {
        if (Items.Count == 0)
        {
            return;
        }

        suppressNotification = true;
        try
        {
            Items.Clear();
        }
        finally
        {
            suppressNotification = false;
        }

        RaiseReset();
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!suppressNotification)
        {
            base.OnCollectionChanged(e);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!suppressNotification)
        {
            base.OnPropertyChanged(e);
        }
    }

    private void RaiseReset()
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}