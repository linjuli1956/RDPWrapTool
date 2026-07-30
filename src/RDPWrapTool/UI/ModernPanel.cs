using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RDPWrapTool.UI;

/// <summary>
/// A flat "card" container that replaces GroupBox: a rounded surface with a
/// bold title, an accent underline and generous inner padding. Child controls
/// are placed inside the padded content area below the header.
/// </summary>
public class ModernPanel : Panel
{
    private const int HeaderHeight = 38;
    private const int Radius = 8;

    public string Title { get; set; } = "";
    public Color AccentColor { get; set; } = Theme.Accent;

    public ModernPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        BackColor = Theme.Surface;
        // Top padding reserves room for the header; sides/bottom give breathing room.
        Padding = new Padding(16, HeaderHeight, 16, 14);
        Font = Theme.UIFont;
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        BackColor = Theme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Mask square corners against the parent's background for clean rounded edges.
        Color parentBg = Parent?.BackColor ?? Theme.Background;
        using (var pm = new SolidBrush(parentBg))
            g.FillRectangle(pm, ClientRectangle);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, Radius);

        // Card body.
        using (var sb = new SolidBrush(Theme.Surface))
            g.FillPath(sb, path);
        using (var bpen = new Pen(Theme.Border, 1f))
            g.DrawPath(bpen, path);

        // Title.
        if (!string.IsNullOrEmpty(Title))
        {
            var titleRect = new Rectangle(Padding.Left, 10, Width - Padding.Left - 16, HeaderHeight - 10);
            TextRenderer.DrawText(g, Title, Theme.HeaderFont, titleRect, Theme.Primary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            // Accent underline beneath the title.
            int lineY = HeaderHeight - 2;
            using var apen = new Pen(AccentColor, 2f);
            g.DrawLine(apen, Padding.Left, lineY, Padding.Left + 34, lineY);
        }
    }
}
