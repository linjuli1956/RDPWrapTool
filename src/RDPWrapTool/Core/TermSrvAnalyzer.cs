using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RDPWrapTool.Core;

/// <summary>
/// Auto-analyzes termsrv.dll to find patch offsets and generate INI configuration.
/// This enables support for new Windows versions not yet in the INI file.
/// </summary>
public class TermSrvAnalyzer
{
    public event Action<string>? OnLog;
    private void Log(string msg) => OnLog?.Invoke(msg);

    public class AnalysisResult
    {
        public string Version { get; set; } = "";
        public bool Success { get; set; }
        public uint DefPolicyOffset { get; set; }
        public uint SLInitOffset { get; set; }
        public uint SingleUserOffset { get; set; }
        public uint LocalOnlyOffset { get; set; }
        public uint BInitialized { get; set; }
        public uint BServerSku { get; set; }
        public uint LMaxUserSessions { get; set; }
        public uint BAppServerAllowed { get; set; }
        public uint BRemoteConnAllowed { get; set; }
        public uint BMultimonAllowed { get; set; }
        public uint UlMaxDebugSessions { get; set; }
        public uint BFUSEnabled { get; set; }
        public string Report { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Analyze termsrv.dll and generate patch offsets.
    /// </summary>
    public AnalysisResult Analyze(string? termsrvPath = null)
    {
        if (string.IsNullOrEmpty(termsrvPath))
            termsrvPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "termsrv.dll");

        var result = new AnalysisResult();
        var report = new StringBuilder();

        Log($"[*] Analyzing: {termsrvPath}");

        if (!File.Exists(termsrvPath))
        {
            Log("[-] termsrv.dll not found!");
            result.Success = false;
            result.Report = "termsrv.dll not found.";
            return result;
        }

        // Get version
        var ver = RDPWrapInstaller.GetFileVersion(termsrvPath);
        if (ver == null)
        {
            Log("[-] Cannot get termsrv.dll version.");
            result.Success = false;
            result.Report = "Cannot get termsrv.dll version.";
            return result;
        }

        result.Version = $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";
        Log($"[+] termsrv.dll version: {result.Version}");
        report.AppendLine($"termsrv.dll version: {result.Version}");

        using var pe = new PEAnalyzer(termsrvPath);
        Log($"[+] PE parsed: {pe.Sections.Count} sections, ImageBase=0x{pe.ImageBase:X}");

        // 1. Find CDefPolicy::Query (DefPolicyOffset)
        Log("[*] Searching for CDefPolicy::Query (offset 0x638 access)...");
        uint defPolicyOffset = FindDefPolicyOffset(pe);
        if (defPolicyOffset > 0)
        {
            result.DefPolicyOffset = defPolicyOffset;
            Log($"[+] Found DefPolicyOffset: 0x{defPolicyOffset:X}");
            report.AppendLine($"DefPolicyOffset: 0x{defPolicyOffset:X}");
        }
        else
        {
            Log("[-] Could not find CDefPolicy::Query.");
            result.Warnings.Add("DefPolicyOffset not found - DefPolicy patch will be skipped.");
        }

        // 2. Find CSLQuery::Initialize (SLInitOffset) and extract data offsets
        Log("[*] Searching for CSLQuery::Initialize...");
        var slInitResult = FindSLInitOffsets(pe);
        if (slInitResult.foundOffset > 0)
        {
            result.SLInitOffset = slInitResult.foundOffset;
            Log($"[+] Found SLInitOffset: 0x{slInitResult.foundOffset:X}");
            report.AppendLine($"SLInitOffset: 0x{slInitResult.foundOffset:X}");

            if (slInitResult.dataOffsets.Count > 0)
            {
                var offsets = slInitResult.dataOffsets;
                // Match offsets to variable names based on relative spacing
                MatchSlInitDataOffsets(offsets, result, report);
            }
            else
            {
                result.Warnings.Add("SLInit data offsets could not be extracted.");
            }
        }
        else
        {
            Log("[-] Could not find CSLQuery::Initialize.");
            result.Warnings.Add("SLInitOffset not found - SLInit hook will be skipped.");
        }

        // 3. Find SingleUserOffset
        Log("[*] Searching for SingleUserOffset...");
        uint singleUserOffset = FindSingleUserOffset(pe);
        if (singleUserOffset > 0)
        {
            result.SingleUserOffset = singleUserOffset;
            Log($"[+] Found SingleUserOffset: 0x{singleUserOffset:X}");
            report.AppendLine($"SingleUserOffset: 0x{singleUserOffset:X}");
        }
        else
        {
            Log("[-] Could not find SingleUserOffset.");
            result.Warnings.Add("SingleUserOffset not found - SingleUser patch will be skipped.");
        }

        // 4. Find LocalOnlyOffset
        Log("[*] Searching for LocalOnlyOffset...");
        uint localOnlyOffset = FindLocalOnlyOffset(pe);
        if (localOnlyOffset > 0)
        {
            result.LocalOnlyOffset = localOnlyOffset;
            Log($"[+] Found LocalOnlyOffset: 0x{localOnlyOffset:X}");
            report.AppendLine($"LocalOnlyOffset: 0x{localOnlyOffset:X}");
        }
        else
        {
            Log("[-] Could not find LocalOnlyOffset.");
            result.Warnings.Add("LocalOnlyOffset not found - LocalOnly patch will be skipped.");
        }

        result.Success = defPolicyOffset > 0 || slInitResult.foundOffset > 0;
        result.Report = report.ToString();
        return result;
    }

    /// <summary>
    /// Find CDefPolicy::Query by searching for access to offset 0x638 on a structure.
    /// The displacement 0x638 in x64 appears as bytes 38 06 00 00.
    /// </summary>
    private uint FindDefPolicyOffset(PEAnalyzer pe)
    {
        if (pe.TextSection == null) return 0;

        int codeStart = (int)pe.TextSection.RawDataOffset;
        int codeSize = (int)pe.TextSection.RawDataSize;

        // Search for [rcx+638h] access patterns:
        // 89 81 38 06 00 00 = mov [rcx+638h], eax
        // 89 89 38 06 00 00 = mov [rcx+638h], ecx
        // 8B 81 38 06 00 00 = mov eax, [rcx+638h]
        // 83 B9 38 06 00 00 = cmp dword [rcx+638h], XX

        // Pattern: XX 81/89/B9 38 06 00 00 (with wildcard for first byte)
        byte[] pattern = { 0xFF, 0x81, 0x38, 0x06, 0x00, 0x00 };
        var matches = pe.FindAllPatterns(pattern, codeStart, codeSize);

        foreach (var match in matches)
        {
            // Verify it's actually a mov/cmp instruction with rcx
            byte opcode = pe.ReadByte(match);
            if (opcode != 0x89 && opcode != 0x8B && opcode != 0x83)
            {
                // Also check 2-byte opcode variants
                pattern = new byte[] { 0xFF, 0x89, 0x38, 0x06, 0x00, 0x00 };
                matches = pe.FindAllPatterns(pattern, codeStart, codeSize);
                foreach (var m in matches)
                {
                    byte op = pe.ReadByte(m);
                    if (op == 0x89)
                    {
                        return FindFunctionStart(pe, m, codeStart);
                    }
                }
                continue;
            }

            return FindFunctionStart(pe, match, codeStart);
        }

        // Try alternative pattern: 89 89 38 06 00 00
        pattern = new byte[] { 0x89, 0x89, 0x38, 0x06, 0x00, 0x00 };
        matches = pe.FindAllPatterns(pattern, codeStart, codeSize);
        foreach (var match in matches)
        {
            return FindFunctionStart(pe, match, codeStart);
        }

        // Try: 83 B9 38 06 00 00
        pattern = new byte[] { 0x83, 0xB9, 0x38, 0x06, 0x00, 0x00 };
        matches = pe.FindAllPatterns(pattern, codeStart, codeSize);
        foreach (var match in matches)
        {
            return FindFunctionStart(pe, match, codeStart);
        }

        return 0;
    }

    /// <summary>
    /// Find the start of a function by searching backwards for a prologue or function boundary.
    /// </summary>
    private uint FindFunctionStart(PEAnalyzer pe, int refOffset, int searchStart)
    {
        // Strategy 1: Search backwards for common function prologues
        for (int i = refOffset; i >= Math.Max(searchStart, refOffset - 512); i--)
        {
            byte b0 = pe.ReadByte(i);
            byte b1 = (i + 1 < pe.DataLength) ? pe.ReadByte(i + 1) : (byte)0;
            byte b2 = (i + 2 < pe.DataLength) ? pe.ReadByte(i + 2) : (byte)0;

            // Common x64 function prologues
            if (b0 == 0x48 && b1 == 0x89 && (b2 == 0x5C || b2 == 0x6C || b2 == 0x0C))
                return pe.OffsetToRVA(i);
            if (b0 == 0x48 && b1 == 0x83 && b2 == 0xEC)
                return pe.OffsetToRVA(i);
            if (b0 == 0x40 && (b1 == 0x53 || b1 == 0x55 || b1 == 0x56 || b1 == 0x57))
                return pe.OffsetToRVA(i);
            if (b0 == 0x4C && b1 == 0x8B && b2 == 0xDC)
                return pe.OffsetToRVA(i);
            if (b0 == 0x48 && b1 == 0x8B && b2 == 0xEC)
                return pe.OffsetToRVA(i);
            if (b0 == 0x48 && b1 == 0x81 && b2 == 0xEC)
                return pe.OffsetToRVA(i);

            // int3 padding before function - most reliable boundary marker
            if (b0 == 0xCC && i + 1 <= refOffset)
            {
                byte next = pe.ReadByte(i + 1);
                if (next != 0xCC)
                    return pe.OffsetToRVA(i + 1);
            }
        }

        // Strategy 2: Fallback - search for CC padding or C3 ret as function boundary
        for (int i = refOffset - 1; i >= Math.Max(searchStart, refOffset - 512); i--)
        {
            byte b = pe.ReadByte(i);
            // CC (int3) padding - function starts right after
            if (b == 0xCC)
            {
                // Skip consecutive CC bytes
                while (i > searchStart && pe.ReadByte(i - 1) == 0xCC) i--;
                return pe.OffsetToRVA(i + 1);
            }
            // C3 (ret) from previous function - next byte might be function start
            if (b == 0xC3 && i > searchStart)
            {
                byte next = (i + 1 < pe.DataLength) ? pe.ReadByte(i + 1) : (byte)0;
                // Only accept if followed by CC padding or a known prologue
                if (next == 0xCC)
                {
                    while (i + 1 < pe.DataLength && pe.ReadByte(i + 1) == 0xCC) i++;
                    return pe.OffsetToRVA(i + 1);
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// Find CSLQuery::Initialize by searching for consecutive C7 05 (mov [rip+offset], imm32) instructions.
    /// </summary>
    private (uint foundOffset, List<(uint rva, uint value)> dataOffsets) FindSLInitOffsets(PEAnalyzer pe)
    {
        if (pe.TextSection == null) return (0, new List<(uint, uint)>());

        int codeStart = (int)pe.TextSection.RawDataOffset;
        int codeSize = (int)pe.TextSection.RawDataSize;

        // Search for C7 05 (mov dword [rip+disp32], imm32)
        byte[] pattern = { 0xC7, 0x05 };

        // Find all C7 05 locations
        var locations = pe.FindAllPatterns(pattern, codeStart, codeSize);
        Log($"[*] Found {locations.Count} C7 05 (mov [rip+disp], imm32) instructions.");

        // Collect ALL C7 05 target RVAs for clustering (not just consecutive ones)
        var allTargets = new List<(uint rva, uint value, int fileOffset)>();
        foreach (var loc in locations)
        {
            if (loc + 10 > codeStart + codeSize) continue;
            // Read displacement (signed int32) - C7 05 is 10 bytes total
            int disp = (int)pe.ReadUInt32(loc + 2);
            uint value = pe.ReadUInt32(loc + 6);
            // Calculate target RVA: C7 05 is 10 bytes, RIP = instRVA + 10
            uint instRVA = pe.OffsetToRVA(loc);
            uint targetRVA = (uint)(instRVA + 10 + disp);
            allTargets.Add((targetRVA, value, loc));
        }
        Log($"[*] Calculated {allTargets.Count} C7 05 target RVAs.");

        // Also search for 48 89 05 (mov [rip+disp], rax) - 7 bytes, stores pointer
        byte[] movRaxPat = { 0x48, 0x89, 0x05 };
        var raxLocs = pe.FindAllPatterns(movRaxPat, codeStart, codeSize);
        foreach (var loc in raxLocs)
        {
            if (loc + 7 > codeStart + codeSize) continue;
            int disp = (int)pe.ReadUInt32(loc + 3);
            uint instRVA = pe.OffsetToRVA(loc);
            uint targetRVA = (uint)(instRVA + 7 + disp);
            allTargets.Add((targetRVA, 0, loc));
        }
        Log($"[*] Total targets including 48 89 05: {allTargets.Count}");

        // Cluster targets by proximity (SLInit data is ~44 bytes, search within 60 bytes)
        allTargets.Sort((a, b) => a.rva.CompareTo(b.rva));
        for (int i = 0; i < allTargets.Count; i++)
        {
            var cluster = new List<(uint rva, uint value, int fileOffset)>();
            cluster.Add(allTargets[i]);
            for (int j = i + 1; j < allTargets.Count; j++)
            {
                if (allTargets[j].rva - allTargets[i].rva <= 60)
                    cluster.Add(allTargets[j]);
                else break;
            }

            if (cluster.Count >= 4)
            {
                // Try to match this cluster to the known SLInit layout
                int[] expectedDiffs = { 0, 4, 8, 16, 24, 28, 36, 40 };
                string[] varNames = { "bInitialized", "bServerSku", "lMaxUserSessions", "bAppServerAllowed",
                                      "bRemoteConnAllowed", "bMultimonAllowed", "ulMaxDebugSessions", "bFUSEnabled" };

                for (int baseIdx = 0; baseIdx < cluster.Count; baseIdx++)
                {
                    uint tryBase = cluster[baseIdx].rva;
                    var matches = new Dictionary<string, uint>();
                    foreach (var (rva, value, fo) in cluster)
                    {
                        int diff = (int)(rva - tryBase);
                        for (int k = 0; k < expectedDiffs.Length; k++)
                        {
                            if (diff == expectedDiffs[k]) { matches[varNames[k]] = rva; break; }
                        }
                    }

                    if (matches.Count >= 5) // At least 5 out of 8 matched
                    {
                        Log($"[+] Found SLInit data cluster with {matches.Count} matches");
                        var dataOffsets = cluster.Select(c => (c.rva, c.value)).ToList();
                        // Find function start from the first C7 05 in this cluster
                        int firstFileOffset = cluster.Min(c => c.fileOffset);
                        uint funcStart = FindFunctionStart(pe, firstFileOffset, codeStart);
                        Log($"    SLInitOffset (function start): 0x{funcStart:X}");
                        return (funcStart, dataOffsets);
                    }
                }
            }
        }

        return (0, new List<(uint, uint)>());
    }

    /// <summary>
    /// Match extracted SLInit data offsets to variable names based on relative spacing.
    /// Known layout (offsets from bInitialized):
    /// +0: bInitialized, +4: bServerSku, +8: lMaxUserSessions,
    /// +16: bAppServerAllowed, +24: bRemoteConnAllowed, +28: bMultimonAllowed,
    /// +36: ulMaxDebugSessions, +40: bFUSEnabled
    /// </summary>
    private void MatchSlInitDataOffsets(List<(uint rva, uint value)> offsets, AnalysisResult result, StringBuilder report)
    {
        if (offsets.Count < 4) return;

        // Sort by RVA
        offsets.Sort((a, b) => a.rva.CompareTo(b.rva));

        // The first offset should be bInitialized
        uint baseRVA = offsets[0].rva;
        Log($"[*] SLInit base RVA: 0x{baseRVA:X}");

        // Try to match each offset to the known layout
        // Expected differences from base: 0, 4, 8, 16, 24, 28, 36, 40
        int[] expectedDiffs = { 0, 4, 8, 16, 24, 28, 36, 40 };
        string[] varNames = { "bInitialized", "bServerSku", "lMaxUserSessions", "bAppServerAllowed",
                              "bRemoteConnAllowed", "bMultimonAllowed", "ulMaxDebugSessions", "bFUSEnabled" };

        // Try matching with different bases (the first offset might not be bInitialized)
        for (int baseIdx = 0; baseIdx < offsets.Count; baseIdx++)
        {
            uint tryBase = offsets[baseIdx].rva;
            var matches = new Dictionary<string, uint>();

            foreach (var (rva, value) in offsets)
            {
                int diff = (int)(rva - tryBase);
                for (int k = 0; k < expectedDiffs.Length; k++)
                {
                    if (diff == expectedDiffs[k])
                    {
                        matches[varNames[k]] = rva;
                        break;
                    }
                }
            }

            // If we matched at least 6 out of 8 variables, use this base
            if (matches.Count >= 6)
            {
                if (matches.TryGetValue("bInitialized", out uint bi)) result.BInitialized = bi;
                if (matches.TryGetValue("bServerSku", out uint bs)) result.BServerSku = bs;
                if (matches.TryGetValue("lMaxUserSessions", out uint lm)) result.LMaxUserSessions = lm;
                if (matches.TryGetValue("bAppServerAllowed", out uint ba)) result.BAppServerAllowed = ba;
                if (matches.TryGetValue("bRemoteConnAllowed", out uint br)) result.BRemoteConnAllowed = br;
                if (matches.TryGetValue("bMultimonAllowed", out uint bm)) result.BMultimonAllowed = bm;
                if (matches.TryGetValue("ulMaxDebugSessions", out uint ul)) result.UlMaxDebugSessions = ul;
                if (matches.TryGetValue("bFUSEnabled", out uint bf)) result.BFUSEnabled = bf;

                foreach (var kvp in matches)
                {
                    Log($"    {kvp.Key} = 0x{kvp.Value:X}");
                    report.AppendLine($"  {kvp.Key}: 0x{kvp.Value:X}");
                }
                return;
            }
        }

        // Fallback: just assign in order
        Log("[!] Could not match SLInit data to standard layout. Using sequential assignment.");
        if (offsets.Count >= 8)
        {
            result.BInitialized = offsets[0].rva;
            result.BServerSku = offsets[1].rva;
            result.LMaxUserSessions = offsets[2].rva;
            result.BAppServerAllowed = offsets[3].rva;
            result.BRemoteConnAllowed = offsets[4].rva;
            result.BMultimonAllowed = offsets[5].rva;
            result.UlMaxDebugSessions = offsets[6].rva;
            result.BFUSEnabled = offsets[7].rva;
        }
    }

    /// <summary>
    /// Find SingleUserOffset - the byte to patch in CSessionArbitrationHelper::IsSingleSessionPerUserEnabled.
    /// The patch changes a 01 byte to 00 (Zero), making the function return false instead of true.
    /// Pattern: B0 01 C3 (mov al, 1; ret) -> patch the 01 byte
    /// </summary>
    private uint FindSingleUserOffset(PEAnalyzer pe)
    {
        if (pe.TextSection == null) return 0;

        int codeStart = (int)pe.TextSection.RawDataOffset;
        int codeSize = (int)pe.TextSection.RawDataSize;

        // Strategy 1: Search for B0 01 C3 (mov al, 1; ret) preceded by function boundary
        // This is a small leaf function that returns true
        byte[] b001c3 = { 0xB0, 0x01, 0xC3 };
        var candidates = pe.FindAllPatterns(b001c3, codeStart, codeSize);
        Log($"[*] Found {candidates.Count} B0 01 C3 patterns");

        foreach (var candidate in candidates)
        {
            // Check if preceded by CC (int3 padding) or C3 (ret) - function boundary
            bool isFunctionBoundary = false;
            if (candidate > codeStart)
            {
                byte prev = pe.ReadByte(candidate - 1);
                if (prev == 0xCC) isFunctionBoundary = true;
                if (prev == 0xC3 && candidate > codeStart + 1)
                {
                    byte prev2 = pe.ReadByte(candidate - 2);
                    // Check if prev is actually a ret (not part of another instruction)
                    // Common patterns before ret: 48 83 C4 XX (add rsp, XX) or CC
                    if (prev2 == 0xC3 || prev2 == 0xCC || prev2 == 0x24 || prev2 == 0x28) isFunctionBoundary = true;
                }
            }

            if (isFunctionBoundary)
            {
                // The patch byte is the 01 at candidate+1
                uint patchRVA = pe.OffsetToRVA(candidate + 1);
                Log($"[+] Found SingleUserOffset: 0x{patchRVA:X} (B0 01 C3 at 0x{pe.OffsetToRVA(candidate):X})");
                return patchRVA;
            }
        }

        // Strategy 2: Search for B0 01 followed by C3 within 0-8 bytes (longer function)
        byte[] b001 = { 0xB0, 0x01 };
        var b001Matches = pe.FindAllPatterns(b001, codeStart, codeSize);
        foreach (var match in b001Matches)
        {
            // Check if C3 (ret) appears within 0-8 bytes after B0 01
            for (int i = 2; i <= 10 && match + i < codeStart + codeSize; i++)
            {
                if (pe.ReadByte(match + i) == 0xC3)
                {
                    // Found B0 01 ... C3 - check for function boundary before B0 01
                    if (match > codeStart)
                    {
                        byte prev = pe.ReadByte(match - 1);
                        if (prev == 0xCC)
                        {
                            uint patchRVA = pe.OffsetToRVA(match + 1);
                            Log($"[+] Found SingleUserOffset (strategy 2): 0x{patchRVA:X}");
                            return patchRVA;
                        }
                    }
                    break;
                }
                // Skip over common instructions between B0 01 and C3
                // e.g., 48 83 C4 28 (add rsp, 28h) before ret
            }
        }

        // Strategy 3: Search for 33 C0 40 C3 (xor eax, eax; inc eax; ret) - returns 1
        byte[] xorInc = { 0x33, 0xC0, 0x40, 0xC3 };
        var xorMatches = pe.FindAllPatterns(xorInc, codeStart, codeSize);
        foreach (var match in xorMatches)
        {
            if (match > codeStart && pe.ReadByte(match - 1) == 0xCC)
            {
                // The 40 (inc eax) byte should be patched to 90 (nop) to return 0
                // But SingleUserCode=Zero means patch to 00, so this pattern needs different handling
                // Skip for now
            }
        }

        Log("[-] SingleUserOffset not found via pattern search.");
        return 0;
    }

    /// <summary>
    /// Find LocalOnlyOffset - the conditional jump to patch in GetInstanceOfTSLicense.
    /// </summary>
    private uint FindLocalOnlyOffset(PEAnalyzer pe)
    {
        if (pe.TextSection == null) return 0;

        int codeStart = (int)pe.TextSection.RawDataOffset;
        int codeSize = (int)pe.TextSection.RawDataSize;

        // Search for "LocalOnly" or "TSLicense" strings
        string[] searchStrings = { "LocalOnly", "TSLicense", "GetInstanceOf" };

        foreach (var s in searchStrings)
        {
            byte[] searchString = Encoding.Unicode.GetBytes(s);
            int strPos = pe.FindPattern(searchString, 0);
            if (strPos < 0)
            {
                searchString = Encoding.ASCII.GetBytes(s);
                strPos = pe.FindPattern(searchString, 0);
            }

            if (strPos <= 0) continue;

            uint strRVA = pe.OffsetToRVA(strPos);
            Log($"[*] Found '{s}' string at RVA 0x{strRVA:X}");

            // Search for LEA references to this string
            byte[] leaPattern = { 0x48, 0x8D, 0x0D };
            var leaMatches = pe.FindAllPatterns(leaPattern, codeStart, codeSize);

            foreach (var match in leaMatches)
            {
                int disp = (int)pe.ReadUInt32(match + 3);
                uint instRVA = pe.OffsetToRVA(match);
                uint targetRVA = (uint)(instRVA + 7 + disp);

                if (targetRVA == strRVA)
                {
                    uint funcStart = FindFunctionStart(pe, match, codeStart);
                    if (funcStart > 0)
                    {
                        // Search for conditional jumps in this function
                        int funcOffset = (int)pe.RVAToOffset(funcStart);
                        for (int i = funcOffset; i < funcOffset + 512 && i < codeStart + codeSize; i++)
                        {
                            byte b = pe.ReadByte(i);
                            // Conditional jumps: 74 (jz), 75 (jnz), 84 (jz), 85 (jnz)
                            // These are 2-byte short jumps
                            if (b == 0x74 || b == 0x75)
                            {
                                // Found a conditional jump - this is a candidate for the patch
                                return pe.OffsetToRVA(i);
                            }
                            // Also check 0F 84/85 (long conditional jumps)
                            if (b == 0x0F && i + 1 < codeStart + codeSize)
                            {
                                byte b2 = pe.ReadByte(i + 1);
                                if (b2 == 0x84 || b2 == 0x85)
                                {
                                    return pe.OffsetToRVA(i);
                                }
                            }
                        }
                    }
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Generate INI section content from analysis result.
    /// </summary>
    public string GenerateIniSection(AnalysisResult result)
    {
        var sb = new StringBuilder();

        // Main version section
        sb.AppendLine($"[{result.Version}]");

        if (result.LocalOnlyOffset > 0)
        {
            sb.AppendLine("LocalOnlyPatch.x64=1");
            sb.AppendLine($"LocalOnlyOffset.x64={result.LocalOnlyOffset:X}");
            sb.AppendLine("LocalOnlyCode.x64=jmpshort");
        }

        if (result.SingleUserOffset > 0)
        {
            sb.AppendLine("SingleUserPatch.x64=1");
            sb.AppendLine($"SingleUserOffset.x64={result.SingleUserOffset:X}");
            sb.AppendLine("SingleUserCode.x64=Zero");
        }

        if (result.DefPolicyOffset > 0)
        {
            sb.AppendLine("DefPolicyPatch.x64=1");
            sb.AppendLine($"DefPolicyOffset.x64={result.DefPolicyOffset:X}");
            sb.AppendLine("DefPolicyCode.x64=CDefPolicy_Query_eax_rcx");
        }

        if (result.SLInitOffset > 0)
        {
            sb.AppendLine("SLInitHook.x64=1");
            sb.AppendLine($"SLInitOffset.x64={result.SLInitOffset:X}");
            sb.AppendLine("SLInitFunc.x64=New_CSLQuery_Initialize");
        }

        // SLInit data section
        if (result.BInitialized > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"[{result.Version}-SLInit]");
            sb.AppendLine($"bInitialized.x64 ={result.BInitialized:X}");
            sb.AppendLine($"bServerSku.x64 ={result.BServerSku:X}");
            sb.AppendLine($"lMaxUserSessions.x64 ={result.LMaxUserSessions:X}");
            sb.AppendLine($"bAppServerAllowed.x64 ={result.BAppServerAllowed:X}");
            sb.AppendLine($"bRemoteConnAllowed.x64={result.BRemoteConnAllowed:X}");
            sb.AppendLine($"bMultimonAllowed.x64 ={result.BMultimonAllowed:X}");
            sb.AppendLine($"ulMaxDebugSessions.x64={result.UlMaxDebugSessions:X}");
            sb.AppendLine($"bFUSEnabled.x64 ={result.BFUSEnabled:X}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Full analysis + INI generation in one step.
    /// </summary>
    public (bool success, string iniSection, string report) AnalyzeAndGenerate(string? termsrvPath = null)
    {
        var result = Analyze(termsrvPath);
        if (!result.Success && result.DefPolicyOffset == 0 && result.SLInitOffset == 0)
        {
            return (false, "", result.Report + "\n\nAuto-analysis failed. Could not find critical patch offsets.");
        }

        string iniSection = GenerateIniSection(result);
        return (true, iniSection, result.Report);
    }
}
