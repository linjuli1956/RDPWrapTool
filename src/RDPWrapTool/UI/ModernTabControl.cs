using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RDPWrapTool.UI;

/// <summary>
/// TabControl with owner-drawn flat Windows 11 style tabs. Keeps the full
/// TabControl API (SelectedIndex, SelectedIndexChanged, TabPages, Add order)
/// so MainForm logic is unchanged; only the chrome is repainted via UserPaint.
/// </summary>
public class ModernTabControl : TabControl
{
    private const int TabH = 42;
    private const int TabPad = 16;

    public Color ActiveAccent { get; set; } = Theme.Accent;

    public ModernTabControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        Alignment = TabAlignment.Top;
        Appearance = TabAppearance.Normal;
        ItemSize = new Size(0, TabH);
        SizeMode = TabSizeMode.Normal;
        Padding = new Point(TabPad, 6);
        Font = Theme.UIFontSb;
        Margin = new Padding(0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Strip + page background behind the tabs.
        using (var bb = new SolidBrush(Theme.Background))
            g.FillRectangle(bb, ClientRectangle);

        for (int i = 0; i < TabPages.Count; i++)
        {
            var r = GetTabRect(i);
            if (r.Width <= 0 || r.Height <= 0) continue;
            bool active = i == SelectedIndex;
            DrawTab(g, TabPages[i], r, active);
        }
    }

    private void DrawTab(Graphics g, TabPage page, Rectangle r, bool active)
    {
        var textRect = new Rectangle(r.X, r.Y, r.Width, r.Height - 2);

        if (active)
        {
            // Rounded active "pill" surface behind the label.
            var pill = new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8);
            using var path = Theme.RoundedRect(pill, 8);
            using var ab = new SolidBrush(Theme.Surface);
            g.FillPath(ab, path);

            // Accent underline tab indicator.
            var line = new Rectangle(r.X + 10, r.Bottom - 5, r.Width - 20, 3);
            using var lp = Theme.RoundedRect(line, 2);
            using var abrush = new SolidBrush(ActiveAccent);
            g.FillPath(abrush, lp);
        }

        var color = active ? Theme.Primary : Theme.TextSecondary;
        TextRenderer.DrawText(g, page.Text, Font, textRect, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}

