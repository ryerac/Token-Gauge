namespace TokenGauge;

/// <summary>
/// Per-tool status, visibility, intervals and setup, plus display options. Every change applies (and saves) immediately.
/// </summary>
internal sealed class SettingsForm : Form
{
    static readonly Color Good = Color.FromArgb(0x0F, 0x7B, 0x0F);
    static readonly Color Warn = Color.FromArgb(0x9D, 0x5D, 0x00);
    static readonly Color Bad = Color.FromArgb(0xC4, 0x2B, 0x1C);

    readonly Settings _settings;
    readonly Action _changed;
    readonly Action<ServiceState, SetupAction> _runSetup;
    readonly Func<ServiceState, bool> _isSetupRunning;
    readonly List<(ServiceState State, Label Status, Button Setup)> _rows = [];
    readonly TableLayoutPanel _grid;

    public SettingsForm(
        Settings settings,
        IReadOnlyList<ServiceState> states,
        Action changed,
        Action refreshNow,
        Action<ServiceState, SetupAction> runSetup,
        Func<ServiceState, bool> isSetupRunning)
    {
        _settings = settings;
        _changed = changed;
        _runSetup = runSetup;
        _isSetupRunning = isSetupRunning;

        Text = "TokenGauge settings";
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 8, 16, 12);

        _grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        foreach (var state in states) AddTool(state);
        AddDisplay();
        AddGeneral();
        AddButtons(refreshNow);

        Controls.Add(_grid);
        UpdateStatus();
    }

    void AddTool(ServiceState state)
    {
        var tool = state.Provider.Settings;
        AddHeader(state.Name);

        var status = new Label { AutoSize = true, MaximumSize = new Size(Scale(420), 0), Margin = new Padding(3, 0, 3, 6) };
        AddFull(status);

        var setup = new Button { AutoSize = true, Visible = false, Margin = new Padding(3, 0, 3, 8) };
        setup.Click += (_, _) => { if (setup.Tag is SetupAction a) _runSetup(state, a); };
        AddFull(setup);

        var show = new CheckBox { Text = "Show on the taskbar", AutoSize = true, Checked = tool.Enabled };
        show.CheckedChanged += (_, _) => { tool.Enabled = show.Checked; _changed(); };
        AddFull(show);

        var minutes = Number(1, 60, Math.Clamp(tool.IntervalSeconds / 60, 1, 60));
        minutes.ValueChanged += (_, _) => { tool.IntervalSeconds = (int)minutes.Value * 60; _changed(); };
        AddRow("Check every", minutes, "minutes");

        _rows.Add((state, status, setup));
    }

    void AddDisplay()
    {
        AddHeader("Display");

        var amber = Number(1, 100, (decimal)_settings.AmberAtPercent);
        amber.ValueChanged += (_, _) => { _settings.AmberAtPercent = (double)amber.Value; _changed(); };
        AddRow("Amber at", amber, "% used");

        var red = Number(1, 100, (decimal)_settings.RedAtPercent);
        red.ValueChanged += (_, _) => { _settings.RedAtPercent = (double)red.Value; _changed(); };
        AddRow("Red at", red, "% used");

        // Two radio buttons in different containers don't group automatically, so they're toggled by hand.
        var nextToTray = new RadioButton { Text = "Next to the tray icons", AutoSize = true, AutoCheck = false };
        var fixedOffset = new RadioButton { Text = "Fixed distance from the right edge:", AutoSize = true, AutoCheck = false };
        var offset = Number(0, 4000, _settings.OffsetFromRightPx ?? 400);

        void SetPosition(bool useFixed)
        {
            nextToTray.Checked = !useFixed;
            fixedOffset.Checked = useFixed;
            offset.Enabled = useFixed;
        }
        SetPosition(_settings.OffsetFromRightPx is not null);

        nextToTray.Click += (_, _) => { SetPosition(false); _settings.OffsetFromRightPx = null; _changed(); };
        fixedOffset.Click += (_, _) => { SetPosition(true); _settings.OffsetFromRightPx = (int)offset.Value; _changed(); };
        offset.ValueChanged += (_, _) =>
        {
            if (!fixedOffset.Checked) return;
            _settings.OffsetFromRightPx = (int)offset.Value;
            _changed();
        };

        AddFull(nextToTray);
        AddFull(Inline(fixedOffset, offset, Caption("px")));
    }

    void AddGeneral()
    {
        AddHeader("General");
        var startup = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = Startup.IsEnabled };
        startup.CheckedChanged += (_, _) => Startup.IsEnabled = startup.Checked;
        AddFull(startup);
    }

    void AddButtons(Action refreshNow)
    {
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        var refresh = new Button { Text = "Refresh now", AutoSize = true };
        refresh.Click += (_, _) => refreshNow();
        CancelButton = close;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0),
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(refresh);
        AddFull(buttons);
    }

    /// <summary>Refreshes each tool's status line and setup button from its latest check.</summary>
    public void UpdateStatus()
    {
        var now = DateTimeOffset.Now;
        foreach (var (state, status, setup) in _rows)
        {
            var (text, color) = Describe(state, now);
            if (status.Text != text) status.Text = text;
            status.ForeColor = color;

            var action = state.Latest?.Setup ?? SetupAction.None;
            var running = _isSetupRunning(state);
            setup.Visible = action != SetupAction.None;
            setup.Tag = action;
            setup.Enabled = !running;
            setup.Text = running ? "Waiting for the setup window to close…" : SetupRunner.Label(action);
        }
    }

    static (string Text, Color Color) Describe(ServiceState state, DateTimeOffset now)
    {
        var latest = state.Latest;
        if (latest is null) return ("Checking…", SystemColors.GrayText);

        var hidden = state.Provider.Settings.Enabled ? "" : " Hidden from the taskbar.";
        switch (latest.Status)
        {
            case UsageStatus.Ok:
                var headline = latest.Headline(now);
                var summary = headline is null
                    ? latest.Message ?? "Working."
                    : $"Working. {headline.Label}: {headline.EffectivePercent(now):0}% used, as of {Theme.When(latest.FetchedAt, now)}.";
                return (summary + hidden, Good);
            case UsageStatus.Stale:
                return ($"Showing last reading. {latest.Message}" + hidden, Warn);
            case UsageStatus.Error:
                return ($"Last check failed: {latest.Message}" + hidden, Bad);
            default:
                return ((latest.Message ?? "Not set up.") + hidden, SystemColors.GrayText);
        }
    }

    void AddHeader(string text)
    {
        var margin = _grid.RowCount == 0 ? 8 : 18;
        AddFull(new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.25f, FontStyle.Bold),
            Margin = new Padding(0, margin, 3, 6),
        });
    }

    void AddFull(Control control)
    {
        _grid.RowCount++;
        _grid.Controls.Add(control, 0, _grid.RowCount - 1);
        _grid.SetColumnSpan(control, 2);
    }

    void AddRow(string label, Control input, string? suffix)
    {
        _grid.RowCount++;
        _grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, _grid.RowCount - 1);
        _grid.Controls.Add(suffix is null ? input : Inline(input, Caption(suffix)), 1, _grid.RowCount - 1);
    }

    static FlowLayoutPanel Inline(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        panel.Controls.AddRange(controls);
        return panel;
    }

    static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 3, 0) };

    NumericUpDown Number(decimal min, decimal max, decimal value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = Scale(64),
    };

    int Scale(int px) => (int)(px * DeviceDpi / 96f);
}
