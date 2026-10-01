using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CameraPreview.Capture;
using CameraPreview.Imaging;

namespace CameraPreview.UI;

public sealed class CameraViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ICameraService _cameraService;
    private readonly FramePipeline _pipeline;
    private readonly PreviewRenderer _previewRenderer;

    private CameraDevice? _selectedCamera;
    private BitmapSource? _previewSource;
    private CancellationTokenSource? _captureCancellation;

    public CameraViewModel(
        ICameraService cameraService,
        FramePipeline pipeline,
        PreviewRenderer previewRenderer)
    {
        _cameraService = cameraService;
        _pipeline = pipeline;
        _previewRenderer = previewRenderer;

        RefreshCommand = new AsyncCommand(RefreshAsync);
        StartCommand = new AsyncCommand(StartAsync, () => SelectedCamera is not null);
        StopCommand = new AsyncCommand(StopAsync);

        _cameraService.FrameReceived += OnFrameReceived;
    }

    public ObservableCollection<CameraDevice> Cameras { get; } = [];

    public CameraDevice? SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (SetField(ref _selectedCamera, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public BitmapSource? PreviewSource
    {
        get => _previewSource;
        private set => SetField(ref _previewSource, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var devices = await _cameraService.GetDevicesAsync();

        Cameras.Clear();
        foreach (var device in devices)
            Cameras.Add(device);

        SelectedCamera ??= Cameras.FirstOrDefault();
    }

    private async Task StartAsync()
    {
        if (SelectedCamera is null)
            return;

        await StopAsync();

        _captureCancellation = new CancellationTokenSource();
        await _cameraService.StartAsync(SelectedCamera, _captureCancellation.Token);
    }

    private async Task StopAsync()
    {
        if (_captureCancellation is not null)
        {
            await _captureCancellation.CancelAsync();
            _captureCancellation.Dispose();
            _captureCancellation = null;
        }

        await _cameraService.StopAsync();
    }

    private void OnFrameReceived(object? sender, VideoFrame frame)
    {
        _ = ProcessFrameAsync(frame);
    }

    private async Task ProcessFrameAsync(VideoFrame frame)
    {
        try
        {
            await _pipeline.ProcessAsync(frame, CancellationToken.None);

            var preview = await _previewRenderer.RenderAsync(frame);
            PreviewSource = preview;
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cameraService.FrameReceived -= OnFrameReceived;
        await StopAsync();
        await _cameraService.DisposeAsync();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private sealed class AsyncCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;

        public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
            => !_isExecuting && (_canExecute?.Invoke() ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
                return;

            try
            {
                _isExecuting = true;
                CommandManager.InvalidateRequerySuggested();
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
