using System.ComponentModel;
using TokenGauge.Providers;

namespace TokenGauge;

internal sealed class GaugeContext : ApplicationContext
{
    readonly Settings _settings = Settings.Load();
    readonly SynchronizationContext _ui;
    readonly HttpClient _http;
    readonly UsageMonitor _monitor;
    readonly TaskbarStrip _strip = new();
    readonly DetailsPopup _popup;
    readonly ContextMenuStrip _menu;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    readonly HashSet<ServiceState> _setupRunning = [];
    readonly RegisteredWaitHandle _showSettingsWait;
    SettingsForm? _settingsForm;

    public GaugeContext(WaitHandle showSettingsSignal)
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _ui = SynchronizationContext.Current!;
        _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(showSettingsSignal,
            (_, _) => _ui.Post(_ => OpenSettings(), null), null, Timeout.Infinite, executeOnlyOnce: false);

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TokenGauge/0.1");

        _monitor = new UsageMonitor(
        [
            new ClaudeProvider(_http, _settings.Claude),
            new CopilotProvider(_http, _settings.Copilot),
            new CodexProvider(_settings.Codex),
        ]);

        _popup = new DetailsPopup(_settings);
        _ = _popup.Handle; // Created up front so the context menu has a window to take focus with.

        _menu = BuildMenu();
        _strip.LeftClick += OnLeftClick;
        _strip.RightClick += ShowMenu;
        _monitor.Changed += Render;
        _timer.Tick += (_, _) => Render();

        Render();
        _timer.Start();
        _monitor.Start();

        Startup.UpdatePathIfMoved();
        // First run: open Settings so the setup buttons and Start with Windows are easy to find.
        if (_settings.IsFirstRun) _ui.Post(_ => OpenSettings(), null);
    }

    /// <summary>Tools the user wants shown and that a check has confirmed are set up.</summary>
    List<ServiceState> Shown => _monitor.Services.Where(s => s.Provider.Settings.Enabled && s.IsAvailable).ToList();

    void Render()
    {
        var shown = Shown;
        _strip.Update(BuildSegments(shown, DateTimeOffset.Now), _settings);
        _popup.SetStates(shown);
        if (_popup.Visible) _popup.Invalidate();
        _settingsForm?.UpdateStatus();
    }

    List<Segment> BuildSegments(IReadOnlyList<ServiceState> shown, DateTimeOffset now)
    {
        var segments = new List<Segment>();
        if (shown.Count == 0)
        {
            // Keep something on the taskbar so there's always a way into settings.
            var checking = _monitor.Services.Any(s => s.Provider.Settings.Enabled && s.Latest is null);
            segments.Add(new Segment(checking ? "TokenGauge …" : "TokenGauge: click to set up", SegmentRole.Dim));
            return segments;
        }

        foreach (var state in shown)
        {
            if (segments.Count > 0) segments.Add(new Segment("  ·  ", SegmentRole.Dim));
            segments.Add(new Segment(state.Name + " ", SegmentRole.Label));

            if (state.LastGood?.Headline(now) is { } headline)
            {
                var pct = headline.EffectivePercent(now);
                var text = $"{pct:0}%" + (state.IsStale ? "?" : "");
                segments.Add(new Segment(text, SegmentRole.Value, Theme.LevelFor(pct, _settings)));
            }
            else
            {
                segments.Add(new Segment("–", SegmentRole.Dim));
            }
        }
        return segments;
    }

    void OnLeftClick()
    {
        if (Shown.Count == 0)
        {
            OpenSettings();
            return;
        }
        if (_popup.Visible) _popup.Hide();
        else if (!_popup.JustHidden) _popup.ShowAt(_strip.ScreenBounds);
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh now", null, (_, _) => _monitor.RefreshNow());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    void ShowMenu()
    {
        Native.SetForegroundWindow(_popup.Handle);
        _menu.Show(Cursor.Position);
    }

    void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings, _monitor.Services, OnSettingsChanged, _monitor.RefreshNow,
            RunSetup, _setupRunning.Contains);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    void OnSettingsChanged()
    {
        _settings.Save();
        _monitor.Reschedule();
        Render();
    }

    void RunSetup(ServiceState state, SetupAction action)
    {
        if (action == SetupAction.None || !_setupRunning.Add(state)) return;

        System.Diagnostics.Process process;
        try
        {
            process = SetupRunner.Start(action);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _setupRunning.Remove(state);
            MessageBox.Show($"Couldn't open the setup window: {ex.Message}", "TokenGauge",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => _ui.Post(_ =>
        {
            _setupRunning.Remove(state);
            process.Dispose();
            _monitor.Refresh(state);
            Render();
        }, null);
        Render();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _showSettingsWait.Unregister(null);
        _monitor.Dispose();
        _settingsForm?.Close();
        _strip.Dispose();
        _popup.Dispose();
        _menu.Dispose();
        _http.Dispose();
        base.ExitThreadCore();
    }
}
