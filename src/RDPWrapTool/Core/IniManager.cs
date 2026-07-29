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
    private readonly string _toolDir;
    private string _iniPath;

    public string IniPath => _iniPath;

    public event Action<string>? OnLog;
    private void Log(string msg) => OnLog?.Invoke(msg);

    public IniManager()
    {
        _toolDir = AppContext.BaseDirectory;
        _iniPath = Path.Combine(_toolDir, "rdpwrap.ini");
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
    /// Save text to the INI file.
    /// </summary>
    public bool SaveText(string text)
    {
        try
        {
            File.WriteAllText(_iniPath, text, Encoding.UTF8);
            Log($"[+] INI saved to: {_iniPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[-] Save INI error: {ex.Message}");
            return false;
        }
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
            return text.Contains(sectionHeader, StringComparison.OrdinalIgnoreCase);
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
