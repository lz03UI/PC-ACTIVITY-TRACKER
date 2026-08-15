using PcActivityTracker.Core.Tracking;
using PcActivityTracker.Windows;
using Xunit;

namespace PcActivityTracker.Windows.UnitTests;

public sealed class WindowsCollectorTests
{
    [Fact] public async Task HookIsRemovedAtStop() { var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native); await collector.StartAsync(); await collector.StopAsync(); Assert.Equal(1, native.UnhookCount); }
    [Fact] public async Task DuplicateStartInstallsSingleHook() { var native = new FakeNative(); await using var collector = new WindowsTrackingCollector(native); await collector.StartAsync(); await collector.StartAsync(); Assert.Equal(1, native.HookCount); }
    [Fact] public void SessionMessagesMapLockAndUnlock() { Assert.Equal(TrackingSignalKind.Locked, WindowsMessageMapper.Map(WindowsMessageMapper.WmWtsSessionChange, 0x7)); Assert.Equal(TrackingSignalKind.Unlocked, WindowsMessageMapper.Map(WindowsMessageMapper.WmWtsSessionChange, 0x8)); }
    [Fact] public void PowerMessagesMapSuspendAndResume() { Assert.Equal(TrackingSignalKind.Suspended, WindowsMessageMapper.Map(WindowsMessageMapper.WmPowerBroadcast, 0x4)); Assert.Equal(TrackingSignalKind.Resumed, WindowsMessageMapper.Map(WindowsMessageMapper.WmPowerBroadcast, 0x12)); }
    [Fact] public async Task SnapshotProviderUsesNativeFacade() { var native = new FakeNative { Snapshot = new(7, "test") }; await using var collector = new WindowsTrackingCollector(native); Assert.Equal(native.Snapshot, await collector.GetCurrentAsync()); }

    private sealed class FakeNative : IWindowsNativeFacade
    {
        public int HookCount { get; private set; }
        public int UnhookCount { get; private set; }
        public ForegroundSnapshot? Snapshot { get; init; }
        public nint SetForegroundHook(WinEventCallback callback) { HookCount++; return 1; }
        public bool Unhook(nint hook) { UnhookCount++; return true; }
        public nint GetForegroundWindow() => 42;
        public ForegroundSnapshot? ReadForeground(nint window) => Snapshot;
        public TimeSpan GetIdleDuration() => TimeSpan.Zero;
    }
}
