using Microsoft.UI.Xaml;
using PcActivityTracker.Core.Domain;
using PcActivityTracker.Core.Tracking;
using PcActivityTracker.Data;
using PcActivityTracker.Windows;

namespace PcActivityTracker.Desktop;

public partial class App : Application
{
    private Window? window;
    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcActivityTracker");
        Directory.CreateDirectory(dataDirectory);
        var database = new SqliteDatabase(Path.Combine(dataDirectory, "activity.db"));
        await database.InitializeAsync();
        var store = new SqliteActivityStore(database);
        var exclusions = await store.GetExclusionsAsync();
        var native = new WindowsNativeFacade();
        var metrics = new RuntimeMetrics();
        var collector = new WindowsTrackingCollector(native, runtimeMetrics: metrics);
        var machine = new TrackingStateMachine(new RuleExclusionEvaluator(exclusions), CurrentLocalTime);
        var coordinator = new TrackingCoordinator(machine, store, collector, metrics);
        var main = new MainWindow(coordinator, collector);
        window = main; window.Activate(); main.AttachLifecycle();
    }
    private static LocalTimeContext CurrentLocalTime()
    {
        var zone = TimeZoneInfo.Local; return new(zone.Id, zone.GetUtcOffset(DateTimeOffset.UtcNow));
    }
}
