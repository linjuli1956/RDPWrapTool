using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using RDPWrapTool.Forms;

string outDir = args.Length > 0 ? args[0] : ".";
using var form = new MainForm();
form.Show();
Application.DoEvents();
// Walk into the tab control via reflection to capture each tab page.
var tabField = typeof(MainForm).GetField("_tabControl", BindingFlags.NonPublic | BindingFlags.Instance);
var tabs = (TabControl?)tabField?.GetValue(form);
if (tabs == null) { Console.WriteLine("no tab control"); return; }
for (int i = 0; i < tabs.TabCount; i++)
{
    tabs.SelectedIndex = i;
    Application.DoEvents();
    System.Threading.Thread.Sleep(120);
    using var bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
    form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
    string path = System.IO.Path.Combine(outDir, $"tab{i}.png");
    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    Console.WriteLine($"saved {path} ({bmp.Width}x{bmp.Height})");
}
form.Close();
Console.WriteLine("RENDER OK");
