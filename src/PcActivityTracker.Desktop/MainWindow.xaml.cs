using Microsoft.UI.Xaml;
using PcActivityTracker.Core.Domain;
using PcActivityTracker.Core.Tracking;
using PcActivityTracker.Windows;
using WinRT.Interop;

namespace PcActivityTracker.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly TrackingCoordinator coordinator;
    private readonly WindowsTrackingCollector collector;
    private WindowsLifecycleRegistration? lifecycle;

    public MainWindow(TrackingCoordinator coordinator, WindowsTrackingCollector collector)
    {
        this.coordinator = coordinator; this.collector = collector;
        InitializeComponent();
        coordinator.StatusChanged += (_, status) => DispatcherQueue.TryEnqueue(() => Render(status));
        collector.Signal += Collector_Signal;
        Closed += Window_Closed;
    }
    public void AttachLifecycle() => lifecycle = new(WindowNative.GetWindowHandle(this), collector.EmitLifecycle);
    private async void Collector_Signal(object? sender, TrackingSignal signal)
    {
        try { await coordinator.HandleAsync(signal); if (signal.Kind == TrackingSignalKind.Stop) await collector.StopAsync(); }
        catch (Exception) { DispatcherQueue.TryEnqueue(() => HealthText.Text = "Collector degradato: errore operativo"); }
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try { await collector.StartAsync(); await coordinator.HandleAsync(Create(TrackingSignalKind.Start)); }
        catch (Exception) { HealthText.Text = "Collector degradato: avvio non riuscito"; }
    }
    private async void Pause_Click(object sender, RoutedEventArgs e) => await coordinator.HandleAsync(Create(coordinator.Status == TrackingStatus.Paused ? TrackingSignalKind.Resume : TrackingSignalKind.Pause));
    private async void Private_Click(object sender, RoutedEventArgs e) => await coordinator.HandleAsync(Create(coordinator.Status == TrackingStatus.Private ? TrackingSignalKind.ExitPrivate : TrackingSignalKind.EnterPrivate));
    private async void Stop_Click(object sender, RoutedEventArgs e) { await coordinator.HandleAsync(Create(TrackingSignalKind.Stop)); await collector.StopAsync(); }
    private async void Window_Closed(object sender, WindowEventArgs args) { lifecycle?.Dispose(); await coordinator.HandleAsync(Create(TrackingSignalKind.Stop)); await collector.DisposeAsync(); }
    private static TrackingSignal Create(TrackingSignalKind kind) => new(kind, UtcInstant.Now(TimeProvider.System), new(TimeProvider.System.GetTimestamp()));
    private void Render(TrackingStatus status)
    {
        StatusText.Text = status.ToString(); StartButton.IsEnabled = status == TrackingStatus.Stopped; StopButton.IsEnabled = status != TrackingStatus.Stopped;
        PauseButton.IsEnabled = status is TrackingStatus.Running or TrackingStatus.Paused; PauseButton.Content = status == TrackingStatus.Paused ? "Resume" : "Pause";
        PrivateButton.IsEnabled = status is TrackingStatus.Running or TrackingStatus.Paused or TrackingStatus.Private; PrivateButton.Content = status == TrackingStatus.Private ? "Private mode off" : "Private mode on";
        HealthText.Text = collector.IsDegraded ? $"Collector degradato; segnali persi: {collector.DroppedSignalCount}" : "Collector operativo";
    }
}
