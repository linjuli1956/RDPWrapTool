using System;
using System.IO;
using System.Reflection;

namespace RDPWrapTool.Core;

public static class AssetManager
{
    private const string DllResourceName = "RDPWrapTool.Assets.rdpwrap.dll";
    private const string IniResourceName = "RDPWrapTool.Assets.rdpwrap.ini";

    private static readonly string BaseDir = AppContext.BaseDirectory;
    private static readonly string ProgramDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RDPWrapTool");

    public static string ToolDataDir { get; } = ChooseWritableDir();
    public static string DllPath => Path.Combine(ToolDataDir, "rdpwrap.dll");
    public static string IniPath => Path.Combine(ToolDataDir, "rdpwrap.ini");

    public static void EnsureAssets()
    {
        ExtractIfMissing(DllResourceName, DllPath);
        ExtractIfMissing(IniResourceName, IniPath);
    }

    private static string ChooseWritableDir()
    {
        if (CanWrite(BaseDir))
            return BaseDir;

        Directory.CreateDirectory(ProgramDataDir);
        return ProgramDataDir;
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string test = Path.Combine(dir, ".rdpwraptool_write_test");
            File.WriteAllText(test, "ok");
            File.Delete(test);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ExtractIfMissing(string resourceName, string targetPath)
    {
        if (File.Exists(targetPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Embedded resource not found: {resourceName}");

        using var file = File.Create(targetPath);
        stream.CopyTo(file);
    }
}
