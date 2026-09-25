using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using static TokenGauge.Native;

namespace TokenGauge;

public enum SegmentRole { Label, Value, Dim }

public sealed record Segment(string Text, SegmentRole Role, Level Level = Level.Normal);

/// <summary>
/// A transparent window placed inside the taskbar (as a child of Shell_TrayWnd), just left of the tray icons.
/// Nothing is injected into Explorer: if Explorer restarts, the window is destroyed with it and re-created on the next tick.
/// </summary>
internal sealed class TaskbarStrip : NativeWindow, IDisposable
{
    IntPtr _taskbar;
    IReadOnlyList<Segment> _segments = [];
    string _renderedKey = "";

    public event Action? LeftClick;
    public event Action? RightClick;

    public Rectangle ScreenBounds =>
        Handle != IntPtr.Zero && GetWindowRect(Handle, out var r)
            ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)
            : Rectangle.Empty;

    /// <summary>Re-attaches to the taskbar if needed, then repositions and redraws if anything changed.</summary>
    public void Update(IReadOnlyList<Segment> segments, Settings settings)
    {
        _segments = segments;
        if (!EnsureAttached()) return;

        var dpi = GetDpiForWindow(_taskbar);
        var scale = (dpi == 0 ? 96 : dpi) / 96f;
        var palette = Theme.Taskbar;

        GetClientRect(_taskbar, out var client);
        var rightEdge = RightEdge(client, scale, settings);

        // On Windows 11 the taskbar is drawn by a XAML child window covering the whole bar, so the strip
        // must sit above it in z-order. Explorer can reorder its children, so check on every tick.
        if (GetWindow(_taskbar, GW_CHILD) != Handle)
            SetWindowPos(Handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        var key = string.Join("|", segments) + $"|{dpi}|{palette == Palette.Light}|{client.Width}x{client.Height}|{rightEdge}";
        if (key == _renderedKey) return;

        using var font = new Font("Segoe UI", 12 * scale, GraphicsUnit.Pixel);
        var padding = (int)Math.Round(6 * scale);
        var widths = Measure(segments, font);
        var width = Math.Max(1, (int)Math.Ceiling(widths.Sum()) + padding * 2);
        var height = Math.Max(1, client.Height);

        SetWindowPos(Handle, HWND_TOP, rightEdge - width, 0, width, height, SWP_NOACTIVATE);
        using (var bitmap = Render(segments, widths, font, palette, width, height, padding))
            Present(bitmap);

        _renderedKey = key;
    }

    bool EnsureAttached()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            // Explorer isn't running; the child window went with it.
            _taskbar = IntPtr.Zero;
            return false;
        }

        if (taskbar != _taskbar || Handle == IntPtr.Zero || !IsWindow(Handle))
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
            _taskbar = taskbar;
            _renderedKey = "";
            try
            {
                CreateHandle(new CreateParams
                {
                    Caption = "TokenGauge",
                    Parent = taskbar,
                    Style = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                    ExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                    Width = 1,
                    Height = 1,
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Taskbar may be mid-restart; try again on the next tick.
                _taskbar = IntPtr.Zero;
                return false;
            }
        }
        return Handle != IntPtr.Zero;
    }

    /// <summary>The x position (in taskbar coordinates) the strip's right edge should sit at.</summary>
    int RightEdge(RECT client, float scale, Settings settings)
    {
        if (settings.OffsetFromRightPx is { } offset)
            return client.Width - (int)(offset * scale);

        var gap = (int)(settings.TrayGapPx * scale);
        var tray = FindWindowEx(_taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray != IntPtr.Zero && GetWindowRect(tray, out var r) && r.Width > 0)
        {
            MapWindowPoints(IntPtr.Zero, _taskbar, ref r, 2);
            if (r.Left > 0 && r.Left < client.Width) return r.Left - gap;
        }
        // Tray not found: leave room roughly the size of a typical tray and clock.
        return client.Width - (int)(320 * scale);
    }

    static float[] Measure(IReadOnlyList<Segment> segments, Font font)
    {
        using var probe = new Bitmap(1, 1);
        using var g = Graphics.FromImage(probe);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var format = TypographicFormat();
        return segments.Select(s => g.MeasureString(s.Text, font, PointF.Empty, format).Width).ToArray();
    }

    static Bitmap Render(IReadOnlyList<Segment> segments, float[] widths, Font font, Palette palette, int width, int height, int padding)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        // Near-invisible fill so clicks between letters still land on the strip instead of passing through.
        g.Clear(Color.FromArgb(1, 0, 0, 0));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // ClearType needs an opaque background; greyscale antialiasing works on a transparent one.
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var format = TypographicFormat();
        var y = (height - font.GetHeight(g)) / 2f;
        float x = padding;
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var color = s.Role switch
            {
                SegmentRole.Label => palette.Dim,
                SegmentRole.Dim => palette.Dim,
                _ => palette.For(s.Level),
            };
            using var brush = new SolidBrush(color);
            g.DrawString(s.Text, font, brush, new PointF(x, y), format);
            x += widths[i];
        }
        return bitmap;
    }

    static StringFormat TypographicFormat()
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return format;
    }

    void Present(Bitmap bitmap)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(memDc, hBitmap);
        try
        {
            var size = new SIZE { Width = bitmap.Width, Height = bitmap.Height };
            var source = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size, memDc, ref source, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                m.Result = MA_NOACTIVATE;
                return;
            case WM_LBUTTONUP:
                LeftClick?.Invoke();
                return;
            case WM_RBUTTONUP:
                RightClick?.Invoke();
                return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
