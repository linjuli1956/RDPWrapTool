using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace RDPWrapTool.Core;

/// <summary>
/// Collects every plausible INI section name for a termsrv.dll file.
/// rdpwrap.dll determines the section name from the version resource of the
/// LOADED module (GetModuleHandle + FindResource(1, RT_VERSION)), which can
/// disagree with the on-disk FileVersionInfo (observed: numeric 8875 vs
/// loaded-module 8737). Writing sections for all candidates makes the INI
/// immune to this discrepancy.
/// </summary>
public static class VersionHelper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResourceW(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;

    /// <summary>
    /// Returns distinct candidate version strings ("a.b.c.d"), most authoritative first.
    /// </summary>
    public static List<string> GetCandidateVersions(string filePath, Action<string>? log = null)
    {
        var result = new List<string>();
        void Add(string? v, string source)
        {
            if (string.IsNullOrEmpty(v)) return;
            if (!Regex.IsMatch(v, @"^\d+\.\d+\.\d+\.\d+$")) return;
            if (v == "0.0.0.0") return;
            if (!result.Contains(v))
            {
                result.Add(v);
                log?.Invoke($"[*] version candidate [{source}]: {v}");
            }
        }

        // 1. Raw version-resource read, replicating rdpwrap's GetModuleVersion exactly:
        //    FindResource(1, RT_VERSION), VS_FIXEDFILEINFO at struct offset 40,
        //    dwFileVersionMS @ +8, dwFileVersionLS @ +12.
        try
        {
            IntPtr hMod = LoadLibraryExW(filePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
            if (hMod != IntPtr.Zero)
            {
                try
                {
                    IntPtr hRes = FindResourceW(hMod, (IntPtr)1, (IntPtr)0x10);
                    if (hRes != IntPtr.Zero)
                    {
                        IntPtr hData = LoadResource(hMod, hRes);
                        IntPtr ptr = LockResource(hData);
                        if (ptr != IntPtr.Zero)
                        {
                            uint ms = (uint)Marshal.ReadInt32(ptr, 48);
                            uint ls = (uint)Marshal.ReadInt32(ptr, 52);
                            Add($"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}", "raw resource (rdpwrap-style)");
                        }
                    }
                }
                finally { FreeLibrary(hMod); }
            }
        }
        catch (Exception ex) { log?.Invoke($"[!] raw resource read failed: {ex.Message}"); }

        // 2. FileVersionInfo numeric fields
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(filePath);
            Add($"{vi.FileMajorPart}.{vi.FileMinorPart}.{vi.FileBuildPart}.{vi.FilePrivatePart}", "FileVersionInfo numeric");
        }
        catch (Exception ex) { log?.Invoke($"[!] FileVersionInfo failed: {ex.Message}"); }

        // 3. FileVersionInfo display string (leading x.x.x.x)
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(filePath);
            var m = Regex.Match(vi.FileVersion ?? "", @"^(\d+\.\d+\.\d+\.\d+)");
            if (m.Success) Add(m.Groups[1].Value, "FileVersionInfo string");
        }
        catch { }

        return result;
    }
}
