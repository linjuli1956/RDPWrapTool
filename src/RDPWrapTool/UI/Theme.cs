using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RDPWrapTool.UI;

/// <summary>
/// Central palette, fonts and styling helpers for the Windows 11 flat look.
/// Every color and font lives here; the rest of the UI references these tokens
/// instead of hard-coded values, so the whole theme can be retuned in one place.
/// </summary>
internal static class Theme
{
    // --- Surfaces ---
    public static readonly Color Background = Color.FromArgb(0xF0, 0xF4, 0xF8); // soft page bg
    public static readonly Color Surface    = Color.FromArgb(0xFF, 0xFF, 0xFF); // card bg
    public static readonly Color SurfaceAlt = Color.FromArgb(0xF7, 0xFA, 0xFC); // inset bg
    public static readonly Color Border     = Color.FromArgb(0xDC, 0xE3, 0xEC);
    public static readonly Color Divider    = Color.FromArgb(0xE6, 0xEC, 0xF0);

    // --- Brand / accent ---
    public static readonly Color Primary      = Color.FromArgb(0x2C, 0x3E, 0x50);
    public static readonly Color PrimaryHover = Color.FromArgb(0x3A, 0x52, 0x6B);
    public static readonly Color Accent       = Color.FromArgb(0x34, 0x98, 0xDB);
    public static readonly Color AccentHover  = Color.FromArgb(0x2E, 0x86, 0xC1);

    // --- Status ---
    public static readonly Color Success = Color.FromArgb(0x27, 0xAE, 0x60);
    public static readonly Color Danger  = Color.FromArgb(0xE7, 0x4C, 0x3C);
    public static readonly Color Warning = Color.FromArgb(0xE6, 0x7E, 0x22);

    // --- Text ---
    public static readonly Color TextPrimary   = Color.FromArgb(0x2C, 0x3E, 0x50);
    public static readonly Color TextSecondary = Color.FromArgb(0x6B, 0x77, 0x85);
    public static readonly Color TextOnPrimary = Color.FromArgb(0xFF, 0xFF, 0xFF);
    public static readonly Color TextMuted     = Color.FromArgb(0x9A, 0xA5, 0xB1);

    // --- Dark log console ---
    public static readonly Color LogBackground  = Color.FromArgb(0x1E, 0x27, 0x2E);
    public static readonly Color LogForeground = Color.FromArgb(0xCD, 0xD6, 0xE0);
    public static readonly Color LogInfo  = Color.FromArgb(0xF1, 0xC4, 0x0F); // [*]
    public static readonly Color LogOk    = Color.FromArgb(0x7F, 0xDB, 0x9E);  // [+]
    public static readonly Color LogError = Color.FromArgb(0xF0, 0x71, 0x71);  // [-]

    // --- Fonts (Segoe UI for UI chrome; Consolas for mono content) ---
    public static readonly Font UIFont    = new("Segoe UI", 9F);
    public static readonly Font UIFontSb  = new("Segoe UI", 9F, FontStyle.Bold);
    public static readonly Font HeaderFont = new("Segoe UI", 11F, FontStyle.Bold);
    public static readonly Font TitleFont  = new("Segoe UI", 15F, FontStyle.Bold);
    public static readonly Font SubFont   = new("Segoe UI", 8F);
    public static readonly Font MonoFont   = new("Consolas", 9.5F);
    public static readonly Font MonoSmall  = new("Consolas", 8.5F);

    /// <summary>Builds a rounded-rectangle path. Caller is responsible for disposing it.</summary>
    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        int r = Math.Max(0, radius);
        var path = new GraphicsPath();
        if (r == 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }
        r = Math.Min(r, rect.Width / 2);
        r = Math.Min(r, rect.Height / 2);
        path.AddArc(rect.X, rect.Y, r, r, 180, 90);
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Lighten(Color c, float amount = 0.1f) => ControlPaint.Light(c, amount);
    public static Color Darken(Color c, float amount = 0.1f) => ControlPaint.Dark(c, amount);

    /// <summary>Applies the page background and base font to a form.</summary>
    public static void StyleForm(Form f)
    {
        f.BackColor = Background;
        f.Font = UIFont;
        f.ForeColor = TextPrimary;
        f.StartPosition = FormStartPosition.CenterScreen;
    }
}
