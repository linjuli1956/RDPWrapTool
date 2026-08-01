using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RDPWrapTool.Core;

/// <summary>
/// Manages the rdpwrap.ini configuration file.
/// Handles loading, saving, version checking, and section management.
/// </summary>
public class IniManager
{
    /// <summary>Known patch code hex payloads. Auto-added to [PatchCodes] when a generated section needs them.</summary>
    public static readonly Dictionary<string, string> KnownPatchCodes = new()
    {
        { "jmpshort", "EB" },
        { "Zero", "00" },
        { "nopjmp", "90E9" },
        { "mov_eax_1_nop_2", "B8010000009090" },
        { "CDefPolicy_Query_eax_rcx", "B80001000089813806000090" },
        { "CDefPolicy_Query_eax_rcx_jmp", "B80001000089813806000090EB" },
        { "CDefPolicy_Query_eax_rdi", "B80001000089873806000090" },
        { "CDefPolicy_Query_eax_rdi_jmp", "B80001000089873806000090EB" },
        { "CDefPolicy_Query_r9d_rdi_jmp", "C7873806000000010000EB" },
    };

    private readonly string _toolDir;
    private string _iniPath;

    public string IniPath => _iniPath;

    public event Action<string>? OnLog;
    private void Log(string msg) => OnLog?.Invoke(msg);

    public IniManager()
    {
        AssetManager.EnsureAssets();
        _toolDir = AssetManager.ToolDataDir;
        _iniPath = AssetManager.IniPath;
    }

    /// <summary>
    /// Set the INI file path (e.g., after importing from external source).
    /// </summary>
    public void SetIniPath(string path)
    {
        _iniPath = path;
    }

    /// <summary>
    /// Load the entire INI file as text.
    /// </summary>
    public string LoadText()
    {
        if (!File.Exists(_iniPath))
        {
            Log($"[-] INI file not found: {_iniPath}");
            return string.Empty;
        }
        return File.ReadAllText(_iniPath, Encoding.UTF8);
    }

    /// <summary>
    /// Save text to the INI file. Normalizes line endings to CRLF, guarantees a
    /// trailing CRLF (rdpwrap's parser drops the last line otherwise), and writes
    /// UTF-8 WITHOUT BOM so the first line stays a clean comment.
    /// </summary>
    public bool SaveText(string text)
    {
        try
        {
            text = NormalizeCrlf(text);
            File.WriteAllText(_iniPath, text, new UTF8Encoding(false));
            Log($"[+] INI saved to: {_iniPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[-] Save INI error: {ex.Message}");
            return false;
        }
    }

    /// <summary>Normalize to CRLF and ensure the file ends with CRLF.</summary>
    public static string NormalizeCrlf(string text)
    {
        text = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        if (!text.EndsWith("\r\n"))
            text += "\r\n";
        return text;
    }

    /// <summary>
    /// Import an INI file from an external path, replacing the current one.
    /// </summary>
    public bool ImportFrom(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                Log($"[-] Source file not found: {sourcePath}");
                return false;
            }
            File.Copy(sourcePath, _iniPath, true);
            Log($"[+] INI imported from: {sourcePath}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[-] Import INI error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if a specific termsrv.dll version is supported in the INI.
    /// </summary>
    public bool CheckVersionSupported(string versionString)
    {
        return CheckVersionSupported(_iniPath, versionString);
    }

    /// <summary>
    /// Static method to check if a version is supported in a given INI file.
    /// </summary>
    public static bool CheckVersionSupported(string iniPath, string versionString)
    {
        try
        {
            if (!File.Exists(iniPath)) return false;
            var text = File.ReadAllText(iniPath, Encoding.UTF8);
            string sectionHeader = $"[{versionString}]";
            return text.IndexOf(sectionHeader, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get all version sections in the INI (sections matching x.x.x.x pattern).
    /// </summary>
    public List<string> GetSupportedVersions()
    {
        var versions = new List<string>();
        if (!File.Exists(_iniPath)) return versions;

        try
        {
            var lines = File.ReadAllLines(_iniPath, Encoding.UTF8);
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && !trimmed.Contains("-"))
                {
                    string section = trimmed.Substring(1, trimmed.Length - 2);
                    // Check if it looks like a version (contains dots and numbers)
                    if (System.Text.RegularExpressions.Regex.IsMatch(section, @"^\d+\.\d+\.\d+\.\d+$"))
                    {
                        versions.Add(section);
                    }
                }
            }
        }
        catch { }
        return versions;
    }

    /// <summary>
    /// Get the termsrv.dll version string for the current system.
    /// </summary>
    public static string? GetCurrentTermsrvVersionString()
    {
        var ver = RDPWrapInstaller.GetTermsrvVersion();
        return ver?.ToString();
    }

    /// <summary>
    /// Check if the current system's termsrv.dll is supported by the INI.
    /// </summary>
    public (bool supported, string? version) CheckCurrentVersionSupport()
    {
        var ver = GetCurrentTermsrvVersionString();
        if (ver == null)
        {
            return (false, null);
        }
        return (CheckVersionSupported(ver), ver);
    }

    /// <summary>
    /// Add or replace a version section (plus its -SLInit pair) for MULTIPLE alias
    /// version names at once, and ensure required patch codes exist in [PatchCodes].
    /// </summary>
    public bool AddVersionSections(IReadOnlyList<string> aliases, string sectionContent, string? slInitContent, IEnumerable<string>? requiredPatchCodes = null)
    {
        try
        {
            var text = LoadText();
            if (string.IsNullOrEmpty(text))
            {
                Log("[-] Cannot add sections: INI file is empty.");
                return false;
            }

            text = EnsurePatchCodes(text, requiredPatchCodes);

            foreach (var name in aliases)
            {
                text = RemoveSection(text, name);
                text = RemoveSection(text, $"{name}-SLInit");
            }

            var sb = new StringBuilder(NormalizeCrlf(text));
            for (int i = 0; i < aliases.Count; i++)
            {
                sb.AppendLine();
                if (i > 0)
                    sb.AppendLine("; alias section - same binary, alternate version resource value");
                sb.AppendLine($"[{aliases[i]}]");
                sb.AppendLine(sectionContent.TrimEnd());
                if (!string.IsNullOrEmpty(slInitContent))
                {
                    sb.AppendLine();
                    sb.AppendLine($"[{aliases[i]}-SLInit]");
                    sb.AppendLine(slInitContent.TrimEnd());
                }
            }

            return SaveText(sb.ToString());
        }
        catch (Exception ex)
        {
            Log($"[-] AddVersionSections error: {ex.Message}");
            return false;
        }
    }

    /// <summary>Append missing entries to the [PatchCodes] section.</summary>
    public string EnsurePatchCodes(string text, IEnumerable<string>? requiredCodes)
    {
        if (requiredCodes == null) return text;
        var missing = new List<KeyValuePair<string, string>>();
        foreach (var code in requiredCodes.Distinct())
        {
            if (string.IsNullOrEmpty(code) || !KnownPatchCodes.TryGetValue(code, out var hex)) continue;
            // code present if a line "code=..." exists anywhere
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, $"(?m)^\\s*{System.Text.RegularExpressions.Regex.Escape(code)}\\s*="))
                missing.Add(new KeyValuePair<string, string>(code, hex));
        }
        if (missing.Count == 0) return text;

        foreach (var kv in missing)
            Log($"[+] adding patch code to [PatchCodes]: {kv.Key}");

        var lines = NormalizeCrlf(text).Split(new[] { "\r\n" }, StringSplitOptions.None).ToList();
        int idx = lines.FindIndex(l => l.Trim().Equals("[PatchCodes]", StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            // no PatchCodes section at all - create it at the end
            lines.Add("");
            lines.Add("[PatchCodes]");
            idx = lines.Count - 1;
        }
        // insert right after the section header
        int insertAt = idx + 1;
        foreach (var kv in missing)
            lines.Insert(insertAt++, $"{kv.Key}={kv.Value}");
        return string.Join("\r\n", lines);
    }

    /// <summary>
    /// Add or replace a version section in the INI file.
    /// Used by the auto-analysis feature to add new version support.
    /// </summary>
    public bool AddVersionSection(string versionString, string sectionContent, string? slInitContent = null)
    {
        try
        {
            var text = LoadText();
            if (string.IsNullOrEmpty(text))
            {
                Log("[-] Cannot add section: INI file is empty.");
                return false;
            }

            // Remove existing section if present
            text = RemoveSection(text, versionString);
            text = RemoveSection(text, $"{versionString}-SLInit");

            // Append new section(s) at the end
            var sb = new StringBuilder(text);
            if (!text.EndsWith("\n"))
                sb.AppendLine();

            sb.AppendLine($"[{versionString}]");
            sb.AppendLine(sectionContent);
            sb.AppendLine();

            if (!string.IsNullOrEmpty(slInitContent))
            {
                sb.AppendLine($"[{versionString}-SLInit]");
                sb.AppendLine(slInitContent);
                sb.AppendLine();
            }

            return SaveText(sb.ToString());
        }
        catch (Exception ex)
        {
            Log($"[-] AddVersionSection error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Remove a section and all its content from the INI text.
    /// </summary>
    private string RemoveSection(string text, string sectionName)
    {
        string sectionHeader = $"[{sectionName}]";
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var result = new List<string>();
        bool skipping = false;

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                skipping = trimmed.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase);
            }
            if (!skipping)
                result.Add(line);
        }

        return string.Join("\r\n", result);
    }

    /// <summary>
    /// Copy the INI file to System32 (used during installation).
    /// </summary>
    public bool CopyToSystem32()
    {
        try
        {
            string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string dst = Path.Combine(sys32, "rdpwrap.ini");
            File.Copy(_iniPath, dst, true);
            Log($"[+] INI copied to: {dst}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[-] CopyToSystem32 error: {ex.Message}");
            return false;
        }
    }
}
