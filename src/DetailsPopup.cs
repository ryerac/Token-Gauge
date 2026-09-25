using System.Drawing.Drawing2D;
using static TokenGauge.Native;

namespace TokenGauge;

/// <summary>Flyout shown above the strip with every limit, reset times and any problems.</summary>
internal sealed class DetailsPopup : Form
{
    readonly Settings _settings;
    IReadOnlyList<ServiceState> _states = [];
    Rectangle _anchor;
    DateTime _hiddenAt;

    public DetailsPopup(Settings settings)
    {
        _settings = settings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "TokenGauge";
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    /// <summary>True just after the popup closed, so the click that closed it doesn't immediately reopen it.</summary>
    public bool JustHidden => DateTime.UtcNow - _hiddenAt < TimeSpan.FromMilliseconds(300);

    public void SetStates(IReadOnlyList<ServiceState> states)
    {
        _states = states;
        if (Visible) Relayout();
    }

    public void ShowAt(Rectangle anchor)
    {
        _anchor = anchor;
        Relayout();
        Show();
        Update(); // Paint now rather than waiting for a queued WM_PAINT.
        Activate();
        SetForegroundWindow(Handle);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        HidePopup();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) HidePopup();
        base.OnKeyDown(e);
    }

    void HidePopup()
    {
        if (!Visible) return;
        Hide();
        _hiddenAt = DateTime.UtcNow;
    }

    void Relayout()
    {
        var scale = DeviceDpi / 96f;
        var width = (int)(320 * scale);
        var height = Draw(null, width, scale);

        var area = Screen.FromRectangle(_anchor).WorkingArea;
        var x = Math.Clamp(_anchor.Right - width, area.Left, area.Right - width);
        var y = Math.Min(_anchor.Top, area.Bottom) - height - (int)(8 * scale);
        Bounds = new Rectangle(x, Math.Max(area.Top, y), width, height);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Draw(e.Graphics, ClientSize.Width, DeviceDpi / 96f);
    }

    /// <summary>Lays out and (when <paramref name="g"/> is set) paints the contents; returns the height needed.</summary>
    int Draw(Graphics? g, int width, float scale)
    {
        var palette = Theme.Apps;
        var now = DateTimeOffset.Now;
        int S(float v) => (int)Math.Round(v * scale);

        using var title = new Font("Segoe UI Semibold", 14 * scale, GraphicsUnit.Pixel);
        using var body = new Font("Segoe UI", 13 * scale, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 12 * scale, GraphicsUnit.Pixel);

        if (g is not null)
        {
            g.Clear(palette.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        var pad = S(16);
        var inner = width - pad * 2;
        var y = pad;

        void Text(string text, Font font, Color color, int top, bool right = false)
        {
            if (g is null) return;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                        (right ? TextFormatFlags.Right : TextFormatFlags.Left);
            TextRenderer.DrawText(g, text, font, new Rectangle(pad, top, inner, font.Height), color, flags);
        }

        for (var i = 0; i < _states.Count; i++)
        {
            var state = _states[i];
            if (i > 0)
            {
                if (g is not null)
                {
                    using var pen = new Pen(palette.Separator, Math.Max(1, S(1)));
                    g.DrawLine(pen, pad, y, width - pad, y);
                }
                y += S(12);
            }

            var shown = state.LastGood ?? state.Latest;
            Text(state.Name, title, palette.Text, y);
            var status = state.Latest is null ? "checking…" : shown is null ? "" : $"as of {Theme.When(shown.FetchedAt, now)}";
            Text(status, small, palette.Dim, y + (title.Height - small.Height) / 2, right: true);
            y += title.Height + S(8);

            foreach (var w in shown?.Windows ?? [])
            {
                var pct = w.EffectivePercent(now);
                var level = Theme.LevelFor(pct, _settings);
                Text(w.Label, body, palette.Text, y);
                Text($"{pct:0}%", body, palette.For(level), y, right: true);
                y += body.Height + S(4);

                if (g is not null)
                {
                    var barHeight = S(4);
                    using var track = new SolidBrush(palette.Track);
                    using var fill = new SolidBrush(level == Level.Normal ? palette.Accent : palette.For(level));
                    g.FillRectangle(track, pad, y, inner, barHeight);
                    var filled = (int)(inner * Math.Clamp(pct, 0, 100) / 100);
                    if (filled > 0) g.FillRectangle(fill, pad, y, filled, barHeight);
                }
                y += S(4) + S(4);

                var notes = new List<string>();
                if (w.Detail is not null) notes.Add(w.Detail);
                if (w.ResetsAt is { } reset)
                    notes.Add(reset <= now ? "reset" : $"resets in {Theme.Until(reset, now)} ({Theme.When(reset, now)})");
                foreach (var note in notes)
                {
                    Text(note, small, palette.Dim, y);
                    y += small.Height + S(2);
                }
                y += S(10);
            }

            var problem = state.Latest is { Status: not UsageStatus.Ok } latest ? latest.Message : null;
            var info = state.Latest is { Status: UsageStatus.Ok } ok ? ok.Message : null;
            if (problem is not null)
            {
                var color = state.Latest!.Status == UsageStatus.NotConfigured ? palette.Dim : palette.Amber;
                Text(state.IsStale ? $"Showing last reading. {problem}" : problem, small, color, y);
                y += small.Height + S(10);
            }
            else if (info is not null)
            {
                Text(info, small, palette.Dim, y);
                y += small.Height + S(10);
            }
        }

        return y - S(10) + pad;
    }
}
