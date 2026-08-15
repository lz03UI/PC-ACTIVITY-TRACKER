using Microsoft.UI.Xaml;
using PcActivityTracker.Core.Tracking;
using PcActivityTracker.Windows;
using WinRT.Interop;

namespace PcActivityTracker.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly TrackingCoordinator coordinator;
    private readonly WindowsTrackingCollector collector;
    private readonly CancellationTokenSource lifetime = new();
    private WindowsLifecycleRegistration? lifecycle;
    private Task? coordinatorWorker;

    public MainWindow(TrackingCoordinator coordinator, WindowsTrackingCollector collector)
    {
        this.coordinator = coordinator; this.collector = collector;
        InitializeComponent();
        coordinator.StatusChanged += (_, status) => DispatcherQueue.TryEnqueue(() => Render(status));
        Closed += Window_Closed;
    }
    public void AttachLifecycle() => lifecycle = new(WindowNative.GetWindowHandle(this), collector.TryPublish);

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await collector.StartAsync(lifetime.Token);
            coordinatorWorker = RunCoordinatorAsync();
            _ = collector.TryPublish(TrackingSignalKind.Start);
        }
        catch (Exception) { HealthText.Text = "Collector degradato: avvio non riuscito"; }
    }
    private void Pause_Click(object sender, RoutedEventArgs e) =>
        _ = collector.TryPublish(coordinator.Status == TrackingStatus.Paused ? TrackingSignalKind.Resume : TrackingSignalKind.Pause);
    private void Private_Click(object sender, RoutedEventArgs e) =>
        _ = collector.TryPublish(coordinator.Status == TrackingStatus.Private ? TrackingSignalKind.ExitPrivate : TrackingSignalKind.EnterPrivate);
    private void Stop_Click(object sender, RoutedEventArgs e) => _ = collector.TryPublish(TrackingSignalKind.Stop);

    private async Task RunCoordinatorAsync()
    {
        try { await coordinator.RunAsync(lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { await collector.StopAsync(); }
    }
    private async void Window_Closed(object sender, WindowEventArgs args)
    {
        lifecycle?.Dispose(); _ = collector.TryPublish(TrackingSignalKind.Stop); lifetime.Cancel();
        if (coordinatorWorker is { } worker)
            try { await worker; } catch (OperationCanceledException) { }
        await collector.DisposeAsync(); lifetime.Dispose();
    }
    private void Render(TrackingStatus status)
    {
        StatusText.Text = status.ToString(); StartButton.IsEnabled = status is TrackingStatus.Stopped or TrackingStatus.Faulted; StopButton.IsEnabled = status is not TrackingStatus.Stopped;
        PauseButton.IsEnabled = status is TrackingStatus.Running or TrackingStatus.Paused; PauseButton.Content = status == TrackingStatus.Paused ? "Resume" : "Pause";
        PrivateButton.IsEnabled = status is TrackingStatus.Running or TrackingStatus.Paused or TrackingStatus.Private; PrivateButton.Content = status == TrackingStatus.Private ? "Private mode off" : "Private mode on";
        HealthText.Text = status == TrackingStatus.Faulted ? "Collector arrestato: errore di persistenza" :
            collector.IsDegraded ? $"Collector degradato; segnali persi: {collector.DroppedSignalCount}" : "Collector operativo";
    }
}
