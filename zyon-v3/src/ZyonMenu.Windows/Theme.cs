// C# / src/ZyonMenu.Windows/Theme.cs / .NET 8 WinForms / Windows 10+ x64.
using System.Drawing.Drawing2D;

namespace ZyonMenu.Windows;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(15, 17, 21);
    public static readonly Color Sidebar = Color.FromArgb(11, 13, 17);
    public static readonly Color Card = Color.FromArgb(24, 27, 33);
    public static readonly Color Field = Color.FromArgb(32, 36, 44);
    public static readonly Color Line = Color.FromArgb(42, 47, 57);
    public static readonly Color Accent = Color.FromArgb(25, 184, 233);
    public static readonly Color Text = Color.FromArgb(231, 235, 241);
    public static readonly Color Muted = Color.FromArgb(135, 145, 163);
    public static readonly Color Warning = Color.FromArgb(238, 181, 85);
    public static readonly Color Success = Color.FromArgb(87, 213, 156);

    public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Height = 36,
            Width = 150,
            ForeColor = primary ? Background : Text,
            BackColor = primary ? Accent : Field,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Margin = new Padding(0, 4, 12, 4)
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Line;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(65, 203, 242) : Color.FromArgb(43, 48, 57);
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(15, 151, 197) : Color.FromArgb(30, 34, 42);
        return button;
    }

    public static Label Label(string text, bool muted = false) => new()
    {
        Text = text,
        ForeColor = muted ? Muted : Text,
        Font = new Font("Segoe UI", 9.5f),
        AutoSize = true,
        BackColor = Color.Transparent,
        Margin = new Padding(0)
    };

    public static TextBox TextBox(bool secret = false) => new()
    {
        BackColor = Field,
        ForeColor = Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 11f),
        UseSystemPasswordChar = secret,
        Height = 30,
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 6, 0, 18)
    };
}

internal sealed class RoundedCard : Panel
{
    public RoundedCard()
    {
        DoubleBuffered = true;
        BackColor = Theme.Background;
        Padding = new Padding(20, 18, 20, 16);
        Margin = new Padding(0, 0, 18, 18);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using var fill = new SolidBrush(Theme.Card);
        using var border = new Pen(Theme.Line);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }
}

internal sealed class ToggleSwitch : Control
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            AccessibleDescription = value ? "Ativado" : "Desativado";
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        Size = new Size(42, 24);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
        SetStyle(ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRectangle(new Rectangle(1, 3, Width - 3, Height - 7), 9);
        using var background = new SolidBrush(Checked ? Color.FromArgb(25, 58, 74) : Theme.Field);
        using var outline = new Pen(Checked ? Theme.Accent : Theme.Line, 1.2f);
        e.Graphics.FillPath(background, path);
        e.Graphics.DrawPath(outline, path);
        using var dot = new SolidBrush(Checked ? Theme.Accent : Theme.Muted);
        e.Graphics.FillEllipse(dot, Checked ? Width - 17 : 5, 6, 11, 11);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Theme.Accent, Theme.Card);
        base.OnPaint(e);
    }
}

internal sealed class AccentSlider : Control
{
    private int _value;
    private bool _dragging;
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (_value == clamped) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public AccentSlider()
    {
        Size = new Size(190, 26);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.Slider;
        SetStyle(ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            _dragging = true;
            Capture = true;
            SetFromMouse(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromMouse(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = Math.Max(1, (Maximum - Minimum) / 100);
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.Down: Value -= step; break;
            case Keys.Right:
            case Keys.Up: Value += step; break;
            case Keys.Home: Value = Minimum; break;
            case Keys.End: Value = Maximum; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
        base.OnKeyDown(e);
    }

    private void SetFromMouse(int x)
    {
        var fraction = Math.Clamp((x - 8d) / Math.Max(1, Width - 16), 0d, 1d);
        Value = Minimum + (int)Math.Round(fraction * (Maximum - Minimum));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var y = Height / 2;
        var range = Math.Max(1, Maximum - Minimum);
        var x = 8 + (Width - 16) * (Value - Minimum) / range;
        using var track = new Pen(Theme.Line, 3);
        using var fill = new Pen(Theme.Accent, 3);
        using var dot = new SolidBrush(Theme.Accent);
        e.Graphics.DrawLine(track, 8, y, Width - 8, y);
        e.Graphics.DrawLine(fill, 8, y, x, y);
        e.Graphics.FillEllipse(dot, x - 6, y - 6, 12, 12);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Theme.Accent, Theme.Card);
        base.OnPaint(e);
    }
}
