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

    /// <summary>
    /// Legacy behaviour: only extract when the target file is missing. The working INI is
    /// edited by the tool itself (auto-analysis, online update), so it must never be
    /// overwritten automatically - that would silently delete locally added sections.
    /// </summary>
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

    /// <summary>
    /// True when the INI embedded in this exe differs from the extracted working copy
    /// (e.g. the exe ships a newer community INI than the one on disk). Reported so the
    /// user can decide whether to overwrite the working copy.
    /// </summary>
    public static bool EmbeddedIniDiffersFromWorking()
    {
        if (!File.Exists(IniPath)) return true;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IniResourceName);
            if (stream == null) return false;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] embedded = ms.ToArray();
            byte[] working = File.ReadAllBytes(IniPath);
            if (embedded.Length != working.Length) return true;
            for (int i = 0; i < embedded.Length; i++)
                if (embedded[i] != working[i]) return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Overwrite the extracted working files with the embedded resources.</summary>
    public static void ExtractOverwrite()
    {
        Directory.CreateDirectory(ToolDataDir);
        foreach (var (resource, target) in new[]
                 {
                     (DllResourceName, DllPath),
                     (IniResourceName, IniPath)
                 })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream == null) continue;
            using var file = File.Create(target);
            stream.CopyTo(file);
        }
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
}
