using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RDPWrapTool.UI;

/// <summary>Visual role of a ModernButton. Drives the color pair used to paint it.</summary>
public enum ButtonRole
{
    Primary,
    Accent,
    Success,
    Danger,
    Warning,
    Subtle
}

/// <summary>
/// Owner-drawn flat button with rounded corners and hover/press feedback.
/// Pure WinForms (no third-party). Use Role to switch the color scheme;
/// Subtle renders as an outline button, the rest render as filled.
/// </summary>
public class ModernButton : Button
{
    private int _radius = 6;
    private bool _hover;
    private bool _pressed;

    public ButtonRole Role { get; set; } = ButtonRole.Primary;

    public int CornerRadius
    {
        get => _radius;
        set { _radius = Math.Max(0, value); Invalidate(); }
    }

    public ModernButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseDownBackColor = Color.Empty;
        FlatAppearance.MouseOverBackColor = Color.Empty;

        Font = Theme.UIFontSb;
        ForeColor = Theme.TextOnPrimary;
        Cursor = Cursors.Hand;
        TextAlign = ContentAlignment.MiddleCenter;
        Padding = new Padding(14, 8, 14, 8);
        BackColor = Theme.Surface;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    private (Color back, Color fore) Palette()
    {
        return Role switch
        {
            ButtonRole.Primary => (Theme.Primary, Theme.TextOnPrimary),
            ButtonRole.Accent   => (Theme.Accent, Theme.TextOnPrimary),
            ButtonRole.Success  => (Theme.Success, Theme.TextOnPrimary),
            ButtonRole.Danger   => (Theme.Danger, Theme.TextOnPrimary),
            ButtonRole.Warning  => (Theme.Warning, Theme.TextOnPrimary),
            ButtonRole.Subtle   => (Theme.Surface, Theme.Primary),
            _ => (Theme.Primary, Theme.TextOnPrimary),
        };
    }

    private static readonly TextFormatFlags Tff =
        TextFormatFlags.HorizontalCenter |
        TextFormatFlags.VerticalCenter |
        TextFormatFlags.NoPadding |
        TextFormatFlags.EndEllipsis;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = Theme.RoundedRect(rect, _radius);
        var (baseBack, fore) = Palette();

        if (!Enabled)
        {
            using var db = new SolidBrush(Theme.Divider);
            g.FillPath(db, path);
            TextRenderer.DrawText(g, Text, Font, rect, Theme.TextMuted, Tff);
            return;
        }

        if (Role == ButtonRole.Subtle)
        {
            // Outline button: surface fill, colored border, tint on hover.
            using var sb = new SolidBrush(_hover ? Theme.Divider : Theme.Surface);
            g.FillPath(sb, path);
            using var pen = new Pen(_pressed ? Theme.PrimaryHover : Theme.Primary, 1.5f);
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Text, Font, rect,
                _pressed ? Theme.PrimaryHover : Theme.Primary, Tff);
        }
        else
        {
            Color back = baseBack;
            if (_pressed) back = Theme.Darken(baseBack, 0.12f);
            else if (_hover) back = Theme.Lighten(baseBack, 0.18f);

            using var bb = new SolidBrush(back);
            g.FillPath(bb, path);
            TextRenderer.DrawText(g, Text, Font, rect, fore, Tff);
        }

        // Focus affordance.
        if (Focused)
        {
            var inner = rect;
            inner.Inflate(-3, -3);
            using var fpath = Theme.RoundedRect(inner, Math.Max(0, _radius - 2));
            using var fpen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f);
            g.DrawPath(fpen, fpath);
        }
    }
}
