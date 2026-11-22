using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WowFisher;

internal sealed class FishingApplication : ApplicationContext
{
    private const int RecastDelayMilliseconds = 2000;
    private const int CastSettlingMilliseconds = 1500;

    private readonly OverlayForm _overlay = new();
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _record = new("Record sample");
    private readonly ToolStripMenuItem _select = new("Select bobber area…");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 25 };
    private readonly ScreenFrame _screen = new();
    private readonly BobberDetector _detector = new();
    private readonly string _settingsPath;
    private readonly string _capturesFolder;
    private readonly RecordingHotkey _hotkey;
    private CaptureArea? _area;
    private CaptureWindow? _fishingWindow;
    private CaptureWindow? _recordingWindow;
    private SampleRecorder? _recorder;
    private Rectangle _lastBounds;
    private long? _castAt;
    private long _watchFrom;
    private long _recordFrom;
    private long _recordDeadline;
    private bool _selecting;

    public FishingApplication(string dataFolder)
    {
        _settingsPath = Path.Combine(dataFolder, "area.json");
        _capturesFolder = Path.Combine(dataFolder, "Captures");
        _area = CaptureArea.Load(_settingsPath);

        _overlay.StartFishing += (_, _) => StartFishing();
        _overlay.StopFishing += (_, _) => StopFishing();
        _timer.Tick += (_, _) => Tick();

        _select.Click += (_, _) => SelectArea();
        _record.Click += (_, _) => ToggleRecording();
        _menu.Items.AddRange([_status, new ToolStripSeparator(), _select, _record,
            new ToolStripMenuItem("Open captures folder", null, (_, _) => OpenCapturesFolder()),
            new ToolStripSeparator(), new ToolStripMenuItem("Exit", null, (_, _) => ExitThread())]);
        _tray.Icon = SystemIcons.Application;
        _tray.ContextMenuStrip = _menu;
        _tray.DoubleClick += (_, _) => SelectArea();
        _tray.Visible = true;

        _hotkey = new RecordingHotkey(ToggleRecording);
        if (_hotkey.Registered)
        {
            _record.ShortcutKeyDisplayString = "Ctrl+Shift+R";
        }

        SetStatus(IdleStatus);
        _overlay.Show();
    }

    private string IdleStatus => _area is null ? "Select a bobber area to begin" : "Ready to fish";

    private void SelectArea()
    {
        if (_selecting || _recordingWindow.HasValue)
        {
            return;
        }

        _recordDeadline = 0;
        StopFishing();
        _selecting = true;
        _overlay.Hide();
        try
        {
            using var selector = new AreaSelector();
            if (selector.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            Rectangle bounds = selector.ScreenSelection;
            if (CaptureArea.FromSelection(bounds) is not { } area)
            {
                Notify("Select a rectangle completely inside the game's window on one display.");
                return;
            }

            _area = area;
            UpdateBounds(bounds);
            SetStatus(IdleStatus);
            try
            {
                area.Save(_settingsPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Notify("The area is selected for this session. Windows could not save the setting.");
            }
        }
        finally
        {
            _selecting = false;
            _overlay.Show();
        }
    }

    private void StartFishing()
    {
        if (_area is null)
        {
            Notify("Open the WowFisher tray menu and choose Select bobber area.");
            return;
        }

        if (_area.BindForeground() is not { } window)
        {
            Notify("Bring the game to the front, then click Start Fishing.");
            return;
        }

        Rectangle bounds = _area.ScreenBounds(window);
        if (bounds.IsEmpty)
        {
            Notify("Select the bobber area again after moving or resizing the game.");
            return;
        }

        _fishingWindow = window;
        _castAt = Environment.TickCount64;
        UpdateBounds(bounds);
        SetStatus("Waiting to cast");
    }

    private void StopFishing()
    {
        _fishingWindow = null;
        _castAt = null;
        _detector.Reset();
        SetStatus(IdleStatus);
    }

    private void ToggleRecording()
    {
        if (_selecting)
        {
            return;
        }

        if (_recorder is not null)
        {
            FinishRecording();
            return;
        }

        if (_recordDeadline != 0)
        {
            _recordDeadline = 0;
            SetStatus(IdleStatus);
            return;
        }

        if (_area is null)
        {
            Notify("Select a bobber area before recording a sample.");
            return;
        }

        StopFishing();
        long now = Environment.TickCount64;
        _recordFrom = now + 350;
        _recordDeadline = now + 10_000;
        SetStatus("Bring the game forward to record");
    }

    private void Tick()
    {
        long now = Environment.TickCount64;
        if (_recordDeadline != 0)
        {
            TryStartRecording(now);
        }

        if (_recorder is not null)
        {
            RecordFrame(now);
        }
        else if (_fishingWindow is { } window)
        {
            WatchBobber(window, now);
        }
    }

    private void TryStartRecording(long now)
    {
        if (now >= _recordDeadline)
        {
            _recordDeadline = 0;
            SetStatus(IdleStatus);
            Notify("Recording timed out. Bring your game forward and try again.");
            return;
        }

        if (now < _recordFrom || _area!.BindForeground() is not { } window)
        {
            return;
        }

        _recordDeadline = 0;
        Rectangle bounds = _area.ScreenBounds(window);
        if (bounds.IsEmpty)
        {
            SetStatus(IdleStatus);
            Notify("Select the bobber area again before recording.");
            return;
        }

        try
        {
            _recorder = new SampleRecorder(_capturesFolder, bounds.Size, now);
            _recordingWindow = window;
            UpdateBounds(bounds);
            _overlay.Hide();
            SetStatus("Recording the bobber area");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException)
        {
            SetStatus(IdleStatus);
            Notify($"Could not start recording. {exception.Message}");
        }
    }

    private void RecordFrame(long now)
    {
        CaptureWindow window = _recordingWindow!.Value;
        Rectangle bounds = _area!.ScreenBounds(window);
        if (!window.IsForeground || bounds.IsEmpty)
        {
            FinishRecording();
            return;
        }

        try
        {
            if (_recorder!.WantsFrame(now))
            {
                _recorder.AddFrame(_screen.Read(bounds), now);
            }

            if (_recorder.IsFull)
            {
                FinishRecording();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or ExternalException)
        {
            FinishRecording(error: $"Could not record the game area. {exception.Message}");
        }
    }

    private void FinishRecording(bool closing = false, string? error = null)
    {
        using SampleRecorder? recorder = _recorder;
        _recorder = null;
        _recordingWindow = null;
        try
        {
            string? path = error is null ? recorder?.Save() : null;
            if (!closing)
            {
                Notify(error ?? (path is null ? "No frames were captured." :
                    $"Saved {Path.GetFileName(path)}. Open the captures folder from the tray."));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!closing)
            {
                Notify($"Could not save the recording. {exception.Message}");
            }
        }
        finally
        {
            if (!closing)
            {
                SetStatus(IdleStatus);
                _overlay.Show();
            }
        }
    }

    private void WatchBobber(CaptureWindow window, long now)
    {
        if (!window.IsOpen)
        {
            StopFishing();
            return;
        }

        if (!window.IsForeground)
        {
            _detector.Reset();
            SetStatus("Paused while the game is in the background");
            return;
        }

        Rectangle bounds = _area!.ScreenBounds(window);
        if (bounds.IsEmpty)
        {
            StopFishing();
            Notify("The game window changed size or moved off-screen. Select the bobber area again.");
            return;
        }

        if (bounds != _lastBounds)
        {
            _detector.Reset();
            UpdateBounds(bounds);
        }

        if (_castAt is { } castAt)
        {
            if (now < castAt)
            {
                SetStatus("Waiting to cast again");
            }
            else if (TrySendFishingKey(window))
            {
                _castAt = null;
                _watchFrom = Environment.TickCount64 + CastSettlingMilliseconds;
                _detector.Reset();
                SetStatus("Waiting for the bobber to settle");
            }
            else if (_fishingWindow.HasValue)
            {
                SetStatus("Waiting to cast");
            }

            return;
        }

        if (now < _watchFrom)
        {
            SetStatus("Waiting for the bobber to settle");
            return;
        }

        SetStatus("Watching the bobber");
        try
        {
            if (FishingInputInUse())
            {
                return;
            }

            var clientSize = new Size(_area.ClientWidth, _area.ClientHeight);
            if (_detector.Analyze(_screen.Read(bounds), clientSize, now) && TrySendFishingKey(window))
            {
                _castAt = Environment.TickCount64 + RecastDelayMilliseconds;
                SetStatus("Waiting to cast again");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or ExternalException)
        {
            StopFishing();
            Notify($"Could not capture the game area. Try windowed or borderless mode. {exception.Message}");
        }
    }

    private bool TrySendFishingKey(CaptureWindow window)
    {
        if (!window.IsForeground || FishingInputInUse())
        {
            return false;
        }

        if (NativeMethods.TryPressSlash())
        {
            return true;
        }

        StopFishing();
        Notify("Windows blocked the / key press. Run the game and overlay at the same privilege level.");
        return false;
    }

    private static bool FishingInputInUse() => NativeMethods.IsModifierDown() || NativeMethods.IsSlashDown();

    private void UpdateBounds(Rectangle bounds)
    {
        _lastBounds = bounds;
        _overlay.PositionNear(bounds, _area!.ClientBoundsAt(bounds));
    }

    private void SetStatus(string status)
    {
        if (_status.Text == status)
        {
            return;
        }

        bool fishing = _fishingWindow.HasValue;
        bool recording = _recordDeadline != 0 || _recorder is not null;
        _overlay.SetState(fishing, recording);
        _select.Enabled = !recording;
        _record.Text = recording ? "Stop recording" : "Record sample";
        _timer.Enabled = fishing || recording;
        _status.Text = status;
        _tray.Text = $"WowFisher - {status}";
    }

    private void Notify(string message) => _tray.ShowBalloonTip(3500, "WowFisher", message, ToolTipIcon.Info);

    private void OpenCapturesFolder()
    {
        try
        {
            Directory.CreateDirectory(_capturesFolder);
            Process.Start(new ProcessStartInfo(_capturesFolder) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            Notify($"Could not open the captures folder. {exception.Message}");
        }
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        FinishRecording(closing: true);
        _tray.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkey.Dispose();
            _timer.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _overlay.Dispose();
            _screen.Dispose();
            _detector.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class RecordingHotkey : NativeWindow, IDisposable
    {
        private const int HotkeyId = 1;
        private readonly Action _action;

        internal RecordingHotkey(Action action)
        {
            _action = action;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
            Registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, NativeMethods.ControlShiftNoRepeat, (uint)Keys.R);
        }

        internal bool Registered { get; }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.HotkeyMessage && message.WParam == HotkeyId)
            {
                _action();
            }

            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (Registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            }

            DestroyHandle();
        }
    }
}
