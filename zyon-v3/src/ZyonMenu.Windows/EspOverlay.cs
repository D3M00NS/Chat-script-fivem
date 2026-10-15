// C# / src/ZyonMenu.Windows/EspOverlay.cs / .NET 8 WinForms / Windows 10 x64 game-window overlay.
using System.Drawing.Drawing2D;
using System.Diagnostics;
using ZyonMenu.Core;

namespace ZyonMenu.Windows;

internal sealed class EspOverlay : Form
{
    private readonly Func<OverlayFrame> _readFrame;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 16 };
    private static readonly TimeSpan MaximumFrameAge = TimeSpan.FromMilliseconds(200);
    private OverlayFrame _frame = OverlayFrame.Empty;
    private static readonly (int Start, int End)[] Edges = [(0, 7), (7, 8), (7, 5), (7, 6), (8, 3), (3, 1), (8, 4), (4, 2)];

    public EspOverlay(Func<OverlayFrame> readFrame)
    {
        _readFrame = readFrame;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
        _refresh.Tick += (_, _) => RefreshFrame();
        _refresh.Start();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x00000020 | 0x00080000 | 0x00000080; return value; }
    }

    private void RefreshFrame()
    {
        _frame = _readFrame();
        bool draw = Fresh(_frame) && _frame.Foreground && _frame.Bounds.Width > 0 &&
            (_frame.Settings.Esp.Enabled || _frame.Settings.Aim.DrawFov);
        if (!draw) { if (Visible) Hide(); return; }
        if (Bounds != _frame.Bounds) Bounds = _frame.Bounds;
        if (!Visible) Show();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!Fresh(_frame)) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var settings = _frame.Settings;
        using var aimPen = new Pen(Color.FromArgb(0, 191, 236), 1.3f);
        if (settings.Aim.DrawFov)
        {
            int radius = settings.Aim.Fov;
            e.Graphics.DrawEllipse(aimPen, ClientSize.Width / 2f - radius, ClientSize.Height / 2f - radius, radius * 2, radius * 2);
        }
        if (!settings.Esp.Enabled) return;
        foreach (var actor in _frame.Actors)
        {
            if (actor.Bones.Length != 9 || actor.Distance > settings.Esp.MaxDistance || settings.Esp.OnlyPlayers && !actor.Player) continue;
            var bones = actor.Bones;
            using var pen = new Pen(actor.Player ? Color.Cyan : Color.Gold, 1.2f);
            if (settings.Esp.Skeletons)
                foreach (var edge in Edges)
                    if (bones[edge.Start] is { } start && bones[edge.End] is { } end)
                        e.Graphics.DrawLine(pen, start.X, start.Y, end.X, end.Y);
            if (!EspGeometry.TryGetBounds(bones, ClientSize.Height, out var bounds)) continue;
            if (settings.Esp.Boxes)
                e.Graphics.DrawRectangle(pen, bounds.CenterX - bounds.Width / 2, bounds.Top, bounds.Width, bounds.Height);
            if (settings.Esp.Distance)
            {
                string text = $"{actor.Distance:F0} m";
                var size = e.Graphics.MeasureString(text, Font);
                e.Graphics.DrawString(text, Font, Brushes.Black, bounds.CenterX - size.Width / 2 + 1, bounds.Bottom + 2);
                e.Graphics.DrawString(text, Font, Brushes.White, bounds.CenterX - size.Width / 2, bounds.Bottom + 1);
            }
        }
    }

    private static bool Fresh(OverlayFrame frame) => OverlayFrameTiming.IsFresh(frame.CapturedTimestamp,
        Stopwatch.GetTimestamp(), Stopwatch.Frequency, MaximumFrameAge);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refresh.Dispose();
        base.Dispose(disposing);
    }
}
