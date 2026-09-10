using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RDPWrapTool.Core;

/// <summary>
/// Auto-analyzes termsrv.dll to find patch offsets and generate INI configuration.
///
/// Strategy (learned from community-verified configs):
/// - Every patch point has known byte-shape variants (old pre-24H2 shape and new
///   24H2/25H2/26H2 shape). Each variant is searched by exact signature; the
///   winning shape also determines the patch CODE.
/// - SLInit data is located via RIP-relative-write anchors (mov dword[rip],imm=1
///   -> bInitialized), the hook function is found by intra-function writes or by
///   caller cross-reference, with ambiguity scoring.
/// - EVERY result is byte-level verified by OffsetVerifier before being accepted.
///   Strict policy: if any of the four patch points cannot be verified, the whole
///   analysis fails and nothing is written.
/// </summary>
public class TermSrvAnalyzer
{
    public event Action<string>? OnLog;
    private void Log(string msg) => OnLog?.Invoke(msg);

    /// <summary>
    /// Test/diagnostic switch: ignore the WPP trace string names embedded in termsrv.dll and
    /// fall back to known-layout + store-shape selection. Used by the regression CLI.
    /// </summary>
    public bool DisableWppNames { get; set; }

    public class AnalysisResult
    {
        public string Version { get; set; } = "";
        public List<string> Aliases { get; } = new();
        public bool Success { get; set; }

        public uint LocalOnlyOffset { get; set; }
        public string LocalOnlyCode { get; set; } = "";
        public bool LocalOnlyVerified { get; set; }

        public uint SingleUserOffset { get; set; }
        public string SingleUserCode { get; set; } = "";
        public bool SingleUserVerified { get; set; }

        public uint DefPolicyOffset { get; set; }
        public string DefPolicyCode { get; set; } = "";
        public bool DefPolicyVerified { get; set; }

        public uint SLInitOffset { get; set; }
        public string SlInitStrategy { get; set; } = "";
        public bool SlInitVerified { get; set; }
        public SlInitResolution? SlInitResolution { get; set; }

        public uint BInitialized { get; set; }
        public uint BServerSku { get; set; }
        public uint LMaxUserSessions { get; set; }
        public uint BAppServerAllowed { get; set; }
        public uint BRemoteConnAllowed { get; set; }
        public uint BMultimonAllowed { get; set; }
        public uint UlMaxDebugSessions { get; set; }
        public uint BFUSEnabled { get; set; }

        public List<string> Errors { get; } = new();
        public string Report { get; set; } = "";
    }

    private class RipRef
    {
        public int FileOffset;
        public uint TargetRva;
        public uint Imm;
        public string Kind = "";
    }

    // =====================================================================
    // Main entry
    // =====================================================================

    public AnalysisResult Analyze(string? termsrvPath = null)
    {
        if (string.IsNullOrEmpty(termsrvPath))
            termsrvPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "termsrv.dll");

        var result = new AnalysisResult();
        var report = new StringBuilder();
        void Rep(string s) { Log(s); report.AppendLine(s); }

        Rep($"[*] Analyzing: {termsrvPath}");

        if (!File.Exists(termsrvPath))
        {
            Rep("[-] termsrv.dll not found!");
            result.Errors.Add("termsrv.dll not found");
            result.Report = report.ToString();
            return result;
        }

        // ---- version candidates (section naming) ----
        var versions = VersionHelper.GetCandidateVersions(termsrvPath, msg => Rep(msg));
        if (versions.Count == 0)
        {
            Rep("[-] Cannot determine termsrv.dll version.");
            result.Errors.Add("version unknown");
            result.Report = report.ToString();
            return result;
        }
        result.Version = versions[0];
        result.Aliases.AddRange(versions);
        Rep($"[+] primary version: {result.Version} ({versions.Count} section name(s) will be written)");

        using var pe = new PEAnalyzer(termsrvPath);
        if (pe.TextSection == null || pe.DataSection == null)
        {
            Rep("[-] PE has no .text/.data section.");
            result.Errors.Add("bad PE");
            result.Report = report.ToString();
            return result;
        }
        Rep($"[+] PE parsed: {pe.Sections.Count} sections, .text=0x{pe.TextSection.VirtualAddress:X}/0x{pe.TextSection.VirtualSize:X}, .data=0x{pe.DataSection.VirtualAddress:X}/0x{pe.DataSection.VirtualSize:X}");

        int codeStart = (int)pe.TextSection.RawDataOffset;
        int codeSize = (int)Math.Min(pe.TextSection.RawDataSize, pe.DataLength - codeStart);

        // ---- 1. LocalOnly ----
        Rep("");
        Rep("[*] --- LocalOnly (CEnforcementCore::GetInstanceOfTSLicense) ---");
        FindLocalOnly(pe, codeStart, codeSize, result, Rep);

        // ---- 2. SingleUser ----
        Rep("");
        Rep("[*] --- SingleUser (CSessionArbitrationHelper::IsSingleSessionPerUserEnabled) ---");
        FindSingleUser(pe, codeStart, codeSize, result, Rep);

        // ---- 3. DefPolicy ----
        Rep("");
        Rep("[*] --- DefPolicy (CDefPolicy::Query) ---");
        FindDefPolicy(pe, codeStart, codeSize, result, Rep);

        // ---- 4. SLInit ----
        Rep("");
        Rep("[*] --- SLInit (CSLQuery::Initialize + data block) ---");
        FindSLInit(pe, codeStart, codeSize, result, Rep);

        // ---- final verdict ----
        Rep("");
        result.Success = result.LocalOnlyVerified && result.SingleUserVerified
                      && result.DefPolicyVerified && result.SlInitVerified;
        Rep(result.Success
            ? "[+] ALL FOUR patch points found and byte-verified. Safe to write INI."
            : "[-] STRICT MODE: not all patch points verified. NOTHING will be written.");
        foreach (var e in result.Errors) Rep($"    missing: {e}");
        if (!result.Success)
            Rep("[!] Diagnostic: hex dumps above show rejected candidates. Send this full report to update the rule library for this Windows build.");

        result.Report = report.ToString();
        return result;
    }

    // =====================================================================
    // LocalOnly
    // =====================================================================

    private void FindLocalOnly(PEAnalyzer pe, int codeStart, int codeSize, AnalysisResult result, Action<string> Rep)
    {
        // New shape (24H2+/25H2/26H2, community-verified on 26100 & 28000):
        //   74 44                jz short +0x44          <- patch this byte (jmpshort)
        //   83 3D xx xx xx xx 02 cmp dword [rip+x], 2
        var sig = new byte?[] { 0x74, 0x44, 0x83, 0x3D, null, null, null, null, 0x02 };
        var matches = pe.FindAllPatterns(sig, codeStart, codeSize);
        Rep($"[*] new-shape signature (74 44 83 3D .. 02): {matches.Count} match(es)");

        if (matches.Count == 1)
        {
            uint rva = pe.OffsetToRVA(matches[0]);
            if (VerifyLocalOnlyAt(pe, rva, result, Rep)) return;
        }
        else if (matches.Count > 1)
        {
            Rep("[!] signature ambiguous, trying per-candidate verification...");
            foreach (var m in matches)
            {
                uint rva = pe.OffsetToRVA(m);
                // extra context: the byte after jz's operand should be 83 3D (already in sig) — all pass;
                // cannot disambiguate -> strict fail handled below if none unique
                if (OffsetVerifier.VerifyLocalOnly(pe, rva, out _))
                {
                    Rep($"    candidate 0x{rva:X} verifies, but ambiguity remains");
                }
            }
            result.Errors.Add("LocalOnly ambiguous");
            Rep("[-] LocalOnly: ambiguous signature matches, rejected (strict).");
            return;
        }

        // Old-shape fallback: xref to "LocalOnly" string, then first jz in the function
        Rep("[*] trying old-shape fallback (string xref)...");
        foreach (var s in new[] { "LocalOnly", "TSLicense" })
        {
            byte[] strBytes = Encoding.Unicode.GetBytes(s);
            int strPos = pe.FindPattern(strBytes, 0);
            if (strPos < 0)
            {
                strBytes = Encoding.ASCII.GetBytes(s);
                strPos = pe.FindPattern(strBytes, 0);
            }
            if (strPos <= 0) continue;
            uint strRVA = pe.OffsetToRVA(strPos);
            Rep($"[*] found '{s}' string at RVA 0x{strRVA:X}");

            foreach (var leaMatch in pe.FindAllPatterns(new byte?[] { 0x48, 0x8D, 0x0D }, codeStart, codeSize))
            {
                int disp = (int)pe.ReadUInt32(leaMatch + 3);
                uint instRVA = pe.OffsetToRVA(leaMatch);
                if ((uint)(instRVA + 7 + disp) != strRVA) continue;

                uint funcStart = FindFunctionStart(pe, leaMatch, codeStart);
                if (funcStart == 0) continue;
                int funcOff = (int)pe.RVAToOffset(funcStart);
                for (int i = funcOff; i < funcOff + 512 && i + 1 < codeStart + codeSize; i++)
                {
                    if (pe.ReadByte(i) != 0x74) continue;
                    uint rva = pe.OffsetToRVA(i);
                    if (VerifyLocalOnlyAt(pe, rva, result, Rep)) return;
                }
            }
        }

        result.Errors.Add("LocalOnly");
        Rep("[-] LocalOnly: not found.");
    }

    private bool VerifyLocalOnlyAt(PEAnalyzer pe, uint rva, AnalysisResult result, Action<string> Rep)
    {
        if (OffsetVerifier.VerifyLocalOnly(pe, rva, out string detail))
        {
            result.LocalOnlyOffset = rva;
            result.LocalOnlyCode = OffsetVerifier.CodeJmpShort;
            result.LocalOnlyVerified = true;
            Rep($"[+] LocalOnlyOffset = 0x{rva:X} ({OffsetVerifier.CodeJmpShort}) VERIFIED: {detail}");
            return true;
        }
        Rep($"[-] LocalOnly candidate 0x{rva:X} failed verification: {detail}");
        return false;
    }

    // =====================================================================
    // SingleUser
    // =====================================================================

    private void FindSingleUser(PEAnalyzer pe, int codeStart, int codeSize, AnalysisResult result, Action<string> Rep)
    {
        // Unified shape (byte-verified on 19041, 26100 & 28000):
        //   8D 57 40               lea edx, [rdi+40h]
        //   48 8D 4D 90            lea rcx, [rbp-70h]
        //   48 FF 15 xx xx xx xx   call qword [rip+x]   <- patch 7 bytes (mov eax,1 + nop*2)
        //   0F 1F 44 00 00         nop dword [rax+0]
        //   85 C0                  test eax, eax
        //   0F 84/85 ..            jz/jnz near (jz on Win11, jnz on Win10)
        var sig = new byte?[] { 0x8D, 0x57, 0x40, 0x48, 0x8D, 0x4D, 0x90, 0x48, 0xFF, 0x15, null, null, null, null, 0x0F, 0x1F, 0x44, 0x00, 0x00, 0x85, 0xC0, 0x0F, null };
        var matches = pe.FindAllPatterns(sig, codeStart, codeSize)
            .Where(m => m + 23 <= codeStart + codeSize && (pe.ReadByte(m + 22) == 0x84 || pe.ReadByte(m + 22) == 0x85))
            .ToList();
        Rep($"[*] unified signature (lea edx/rcx; call [rip]; nop; test eax,eax; jz/jnz): {matches.Count} match(es)");

        if (matches.Count == 1)
        {
            uint rva = pe.OffsetToRVA(matches[0] + 7); // the call [rip] instruction
            if (VerifySingleUserAt(pe, rva, OffsetVerifier.CodeMovEax1Nop2, result, Rep)) return;
        }
        else if (matches.Count > 1)
        {
            result.Errors.Add("SingleUser ambiguous");
            Rep("[-] SingleUser: ambiguous signature matches, rejected (strict).");
            return;
        }

        // Old shape: B0 01 C3 (mov al,1; ret) leaf function at CC boundary -> patch the 01 with 00
        Rep("[*] trying old-shape fallback (B0 01 C3 leaf)...");
        var leaf = new byte?[] { 0xB0, 0x01, 0xC3 };
        var candidates = pe.FindAllPatterns(leaf, codeStart, codeSize);
        Rep($"[*] B0 01 C3 occurrences: {candidates.Count}");
        int accepted = 0;
        uint acceptedRva = 0;
        foreach (var c in candidates)
        {
            if (c <= codeStart) continue;
            if (pe.ReadByte(c - 1) != 0xCC) continue; // must start a function
            accepted++;
            acceptedRva = pe.OffsetToRVA(c + 1);
        }
        if (accepted == 1)
        {
            if (VerifySingleUserAt(pe, acceptedRva, OffsetVerifier.CodeZero, result, Rep)) return;
        }
        else if (accepted > 1)
        {
            Rep($"[!] {accepted} boundary-aligned B0 01 C3 candidates, ambiguous (strict).");
        }

        result.Errors.Add("SingleUser");
        Rep("[-] SingleUser: not found.");
    }

    private bool VerifySingleUserAt(PEAnalyzer pe, uint rva, string code, AnalysisResult result, Action<string> Rep)
    {
        if (OffsetVerifier.VerifySingleUser(pe, rva, code, out string detail))
        {
            result.SingleUserOffset = rva;
            result.SingleUserCode = code;
            result.SingleUserVerified = true;
            Rep($"[+] SingleUserOffset = 0x{rva:X} ({code}) VERIFIED: {detail}");
            return true;
        }
        Rep($"[-] SingleUser candidate 0x{rva:X} failed verification: {detail}");
        return false;
    }

    // =====================================================================
    // DefPolicy
    // =====================================================================

    private void FindDefPolicy(PEAnalyzer pe, int codeStart, int codeSize, AnalysisResult result, Action<string> Rep)
    {
        // Shape families, in priority order. Each tuple: signature, patch code, description.
        // Multiple raw matches are expected; the verifier's mandatory suffix check
        // (cmp r8d,r9d; jcc) filters them down to the one true CDefPolicy::Query site.
        var families = new (byte?[] sig, string code, string desc)[]
        {
            // mov r9d, [rdi+638h]  (25H2 verified)
            (new byte?[] { 0x44, 0x8B, 0x8F, 0x38, 0x06, 0x00, 0x00 }, OffsetVerifier.CodeDefPolicyR9dRdiJmp, "mov r9d,[rdi+638h]"),
            // mov r8d, [rdi+63Ch]  (26H2 verified)
            (new byte?[] { 0x44, 0x8B, 0x87, 0x3C, 0x06, 0x00, 0x00 }, OffsetVerifier.CodeDefPolicyR9dRdiJmp, "mov r8d,[rdi+63Ch]"),
            // mov r8d, [rdi+638h]
            (new byte?[] { 0x44, 0x8B, 0x87, 0x38, 0x06, 0x00, 0x00 }, OffsetVerifier.CodeDefPolicyR9dRdiJmp, "mov r8d,[rdi+638h]"),
            // mov r9d, [rdi+63Ch]
            (new byte?[] { 0x44, 0x8B, 0x8F, 0x3C, 0x06, 0x00, 0x00 }, OffsetVerifier.CodeDefPolicyR9dRdiJmp, "mov r9d,[rdi+63Ch]"),
        };

        foreach (var (sig, code, desc) in families)
        {
            var matches = pe.FindAllPatterns(sig, codeStart, codeSize);
            Rep($"[*] {desc}: {matches.Count} raw match(es)");
            uint acceptedRva = 0;
            int accepted = 0;
            foreach (var m in matches)
            {
                uint rva = pe.OffsetToRVA(m);
                if (OffsetVerifier.VerifyDefPolicy(pe, rva, code, out string vd))
                {
                    accepted++;
                    acceptedRva = rva;
                    Rep($"    candidate 0x{rva:X} passes byte verification ({vd})");
                }
                else
                {
                    Rep($"    candidate 0x{rva:X} rejected ({vd})");
                    DumpAt(pe, rva, 20, "DefPolicy", Rep);
                }
            }
            if (accepted == 1)
            {
                if (VerifyDefPolicyAt(pe, acceptedRva, code, result, Rep)) return;
            }
            else if (accepted > 1)
            {
                Rep($"[!] {desc}: {accepted} verified candidates, ambiguous, trying next shape...");
            }
        }

        // Old shape (rdpwrap KB, Win10 19041 verified): mov eax,[rcx+638h]; cmp [rcx+63Ch],eax
        // Dispatch on the jcc that follows: far (0F 84/85) -> patch the CMP (match+6)
        // with 12-byte eax_rcx; short (74/75) -> patch the MOV (match) with 13-byte
        // eax_rcx_jmp whose trailing EB reuses the short jcc displacement.
        var oldSig = new byte?[] { 0x8B, 0x81, 0x38, 0x06, 0x00, 0x00, 0x39, 0x81, 0x3C, 0x06, 0x00, 0x00 };
        var oldMatches = pe.FindAllPatterns(oldSig, codeStart, codeSize);
        Rep($"[*] old-shape (mov eax,[rcx+638h]; cmp [rcx+63Ch],eax): {oldMatches.Count} raw match(es)");
        int oldAcc = 0; uint oldRva = 0; string oldCode = "";
        foreach (var m in oldMatches)
        {
            byte j0 = pe.ReadByte(m + 12), j1 = pe.ReadByte(m + 13);
            uint rva; string code;
            if (j0 == 0x0F && (j1 == 0x84 || j1 == 0x85)) { rva = pe.OffsetToRVA(m + 6); code = OffsetVerifier.CodeDefPolicyEaxRcx; }
            else if (j0 == 0x74 || j0 == 0x75) { rva = pe.OffsetToRVA(m); code = OffsetVerifier.CodeDefPolicyEaxRcxJmp; }
            else { Rep($"    old-shape candidate at 0x{pe.OffsetToRVA(m):X}: unknown jcc {j0:X2} {j1:X2}"); DumpAt(pe, pe.OffsetToRVA(m), 20, "old-shape", Rep); continue; }
            if (OffsetVerifier.VerifyDefPolicy(pe, rva, code, out string vd))
            {
                oldAcc++; oldRva = rva; oldCode = code;
                Rep($"    old-shape candidate 0x{rva:X} ({code}) passes ({vd})");
            }
            else { Rep($"    old-shape candidate 0x{rva:X} ({code}) rejected ({vd})"); DumpAt(pe, rva, 20, "old-shape", Rep); }
        }
        if (oldAcc == 1) { if (VerifyDefPolicyAt(pe, oldRva, oldCode, result, Rep)) return; }
        else if (oldAcc > 1) Rep($"[!] old-shape: {oldAcc} verified candidates, ambiguous (strict).");

        result.Errors.Add("DefPolicy");
        Rep("[-] DefPolicy: not found.");
    }

    private bool VerifyDefPolicyAt(PEAnalyzer pe, uint rva, string code, AnalysisResult result, Action<string> Rep)
    {
        if (OffsetVerifier.VerifyDefPolicy(pe, rva, code, out string detail))
        {
            result.DefPolicyOffset = rva;
            result.DefPolicyCode = code;
            result.DefPolicyVerified = true;
            Rep($"[+] DefPolicyOffset = 0x{rva:X} ({code}) VERIFIED: {detail}");
            return true;
        }
        Rep($"[-] DefPolicy candidate 0x{rva:X} failed verification: {detail}");
        return false;
    }

    // =====================================================================
    // SLInit
    // =====================================================================

    private void FindSLInit(PEAnalyzer pe, int codeStart, int codeSize, AnalysisResult result, Action<string> Rep)
    {
        // E8-anchored function map (probe4-finalized, byte-verified on 19041 & 25H2):
        // function starts = distinct E8 call targets inside .text; a body spans
        // [start, min(next start, first >=6-byte CC run after start+16, .text end)).
        // CC bytes INSIDE instructions (disp32 etc.) are never mistaken for padding
        // because only E8 TARGETS can become starts.
        var fnSet = new SortedSet<uint>();
        int e8End = codeStart + codeSize - 5;
        for (int i = codeStart; i < e8End; i++)
        {
            if (pe.ReadByte(i) != 0xE8) continue;
            int rel = (int)pe.ReadUInt32(i + 1);
            uint tgt = (uint)(pe.OffsetToRVA(i) + 5 + rel);
            if (pe.TextSection != null && pe.TextSection.ContainsRVA(tgt))
                fnSet.Add(tgt);
        }
        var fns = fnSet.ToList();
        Rep($"[*] E8-anchored function starts in .text: {fns.Count}");
        if (fns.Count == 0) { result.Errors.Add("SLInit"); Rep("[-] SLInit: no E8 targets (unexpected)."); return; }

        var bodies = new List<(int start, int end)>();
        for (int idx = 0; idx < fns.Count; idx++)
        {
            int fo = (int)pe.RVAToOffset(fns[idx]);
            if (fo <= 0) continue;
            int nxt = idx + 1 < fns.Count ? (int)pe.RVAToOffset(fns[idx + 1]) : codeStart + codeSize;
            bodies.Add((fo, Math.Min(Math.Min(nxt, FirstCcRun(pe, fo + 16, codeStart + codeSize)), codeStart + codeSize)));
        }
        var bodyStarts = bodies.Select(bd => bd.start).ToList();
        int Owner(int off)
        {
            int idx = bodyStarts.BinarySearch(off);
            if (idx < 0) idx = ~idx - 1;
            return idx >= 0 && off < bodies[idx].end ? idx : -1;
        }

        // Broad RIP-relative reference scan (reads+writes, probe4 byte-walk).
        var refs = ScanBroadRipRefs(pe, codeStart, codeSize);
        Rep($"[*] broad RIP refs near .data: {refs.Count}");

        // Anchors: mov dword [rip]->.data, imm=1 (bInitialized candidates).
        var anchors = ScanRipWrites(pe, codeStart, codeSize)
            .Where(w => w.Kind == "C7" && w.Imm == 1 && InData(pe, w.TargetRva))
            .Select(w => w.TargetRva).Distinct().OrderBy(x => x).ToList();
        Rep($"[*] anchor blocks (C7 05 imm=1 -> .data): {anchors.Count}");

        // Score: for each anchor block, the function referencing the MOST DISTINCT
        // slot deltas (0..0x28 step 4) IS CSLQuery::Initialize.
        var scored = new List<(int score, uint block, uint hook, HashSet<uint> slots)>();
        foreach (var blk in anchors)
        {
            var per = new Dictionary<int, HashSet<uint>>();
            foreach (var (off, tgt) in refs)
            {
                uint delta = tgt - blk;  // wraps for tgt < blk; huge values filtered below
                if (delta > 0x28 || (delta & 3) != 0) continue;
                int o = Owner(off);
                if (o < 0) continue;
                if (!per.TryGetValue(o, out var s)) per[o] = s = new HashSet<uint>();
                s.Add(delta);
            }
            if (per.Count == 0) continue;
            var top = per.OrderByDescending(kv => kv.Value.Count).First();
            uint hook = pe.OffsetToRVA(bodies[top.Key].start);
            scored.Add((top.Value.Count, blk, hook, top.Value));
            Rep($"    block 0x{blk:X}: topFunc=0x{hook:X} distinctSlots={top.Value.Count} hasZeroSlot={top.Value.Contains(0)}");
        }

        var ranked = scored.OrderByDescending(s => s.score).ToList();
        if (ranked.Count > 0)
        {
            int best = ranked[0].score;
            var leaders = ranked.Where(s => s.score == best).ToList();
            if (leaders.Count > 1)
            {
                // tie-break: the TRUE block's slot set contains delta=0 (bInitialized
                // is read at the block head); the overlapping decoy block's does not.
                var withZero = leaders.Where(s => s.slots.Contains(0u)).ToList();
                if (withZero.Count > 0) leaders = withZero;
            }
            if (leaders.Count == 1 && best >= 7)
            {
                var win = leaders[0];
                Rep($"[+] SLInit winner: block=0x{win.block:X} hook=0x{win.hook:X} (distinctSlots={best}, unique)");

                // The data block is DERIVED from this very function (never guessed from the
                // build number): WPP trace strings name each global, and the store shape of
                // every slot tells booleans from counts. See SlInitLayoutResolver.
                int bodyIdx = bodies.FindIndex(bd => pe.OffsetToRVA(bd.start) == win.hook);
                uint funcEndRva = bodyIdx >= 0 && bodies[bodyIdx].end > bodies[bodyIdx].start
                    ? pe.OffsetToRVA(bodies[bodyIdx].end - 1) + 1
                    : 0;
                if (funcEndRva == 0)
                    Rep("[!] could not determine the hooked function's end; using .text bounds");

                var resolution = SlInitLayoutResolver.Resolve(pe, win.hook, funcEndRva, Rep, allowWppNames: !DisableWppNames);
                foreach (var line in resolution.Evidence) Rep("    " + line);
                if (resolution.Success && AcceptSLInit(pe, win.hook, resolution, result, Rep)) return;

                if (!resolution.Success)
                    Rep($"[-] SLInit layout could not be derived: {resolution.Reason}");
            }
            else
            {
                Rep($"[!] no unique SLInit leader (best={best}, leaders={leaders.Count}, need unique & >=7)");
                foreach (var l in leaders.Take(3))
                    DumpAt(pe, l.hook, 16, $"leader hook for block 0x{l.block:X}", Rep);
            }
        }
        else
        {
            Rep("[!] no anchor block scored at all");
        }

        result.Errors.Add("SLInit");
        Rep("[-] SLInit: not found.");
    }

    /// <summary>First run of >=6 consecutive CC (int3 padding) bytes at/after 'from'; end if none.</summary>
    private static int FirstCcRun(PEAnalyzer pe, int from, int end)
    {
        for (int j = Math.Max(from, 0); j < end - 6; j++)
        {
            if (pe.ReadByte(j) == 0xCC && pe.ReadByte(j + 1) == 0xCC && pe.ReadByte(j + 2) == 0xCC &&
                pe.ReadByte(j + 3) == 0xCC && pe.ReadByte(j + 4) == 0xCC && pe.ReadByte(j + 5) == 0xCC)
                return j;
        }
        return end;
    }

    /// <summary>
    /// Broad RIP-relative reference scan (probe4 byte-walk): mov/lea/cmp/add forms
    /// with ModRM mod=00 rm=101, plus C7 05 / C6 05 immediate writes. Returns file
    /// offset + decoded target RVA for refs landing near .data.
    /// </summary>
    private List<(int off, uint target)> ScanBroadRipRefs(PEAnalyzer pe, int codeStart, int codeSize)
    {
        var list = new List<(int, uint)>();
        uint dva = pe.DataSection?.VirtualAddress ?? 0;
        uint dend = dva + (pe.DataSection?.VirtualSize ?? 0);
        int n = codeStart + codeSize - 10;
        for (int i = codeStart; i < n; i++)
        {
            byte c = pe.ReadByte(i);
            byte b1 = pe.ReadByte(i + 1);
            byte b2 = pe.ReadByte(i + 2);
            uint tgt;
            if ((c == 0x48 || c == 0x4C) &&
                (b1 == 0x89 || b1 == 0x8B || b1 == 0x8D || b1 == 0x39 || b1 == 0x3B || b1 == 0x01 || b1 == 0x03) &&
                (b2 & 0xC7) == 0x05)
                tgt = (uint)(pe.OffsetToRVA(i) + 7 + (int)pe.ReadUInt32(i + 3));
            else if ((c == 0x89 || c == 0x8B || c == 0x88 || c == 0x39 || c == 0x3B) && (b1 & 0xC7) == 0x05)
                tgt = (uint)(pe.OffsetToRVA(i) + 6 + (int)pe.ReadUInt32(i + 2));
            else if (c == 0x83 && (b1 & 0xC7) == 0x05)
                tgt = (uint)(pe.OffsetToRVA(i) + 7 + (int)pe.ReadUInt32(i + 2));
            else if (c == 0xC7 && b1 == 0x05)
                tgt = (uint)(pe.OffsetToRVA(i) + 10 + (int)pe.ReadUInt32(i + 2));
            else if (c == 0xC6 && b1 == 0x05)
                tgt = (uint)(pe.OffsetToRVA(i) + 7 + (int)pe.ReadUInt32(i + 2));
            else continue;
            if (tgt >= dva - 0x100 && tgt < dend + 0x200)
                list.Add((i, tgt));
        }
        return list;
    }

    /// <summary>Build number from a "a.b.c.d" version string (0 if unparseable).</summary>
    private static int ParseBuild(string version)
    {
        var parts = (version ?? "").Split('.');
        return parts.Length >= 3 && int.TryParse(parts[2], out int b) ? b : 0;
    }

    /// <summary>Hex-dump bytes at an RVA into the report (diagnostic fallback).</summary>
    private void DumpAt(PEAnalyzer pe, uint rva, int len, string label, Action<string> Rep)
    {
        uint off = pe.RVAToOffset(rva);
        if (off == 0 || off >= (uint)pe.DataLength) return;
        Rep($"    [dump] {label} @0x{rva:X}: {OffsetVerifier.Hex(pe, (int)off, len)}");
    }

    private bool AcceptSLInit(PEAnalyzer pe, uint hookRva, SlInitResolution resolution,
        AnalysisResult result, Action<string> Rep)
    {
        if (!OffsetVerifier.VerifySLInit(pe, hookRva, resolution, out string detail))
        {
            Rep($"[-] SLInit candidate hook=0x{hookRva:X} failed verification: {detail}");
            DumpAt(pe, hookRva, 16, "SLInit hook", Rep);
            return false;
        }

        var data = resolution.Addresses;
        result.SLInitOffset = hookRva;
        result.SlInitStrategy = resolution.Strategy;
        result.SlInitResolution = resolution;
        result.BInitialized = data["bInitialized"];
        result.BServerSku = data["bServerSku"];
        result.LMaxUserSessions = data["lMaxUserSessions"];
        result.BAppServerAllowed = data["bAppServerAllowed"];
        result.BRemoteConnAllowed = data["bRemoteConnAllowed"];
        result.BMultimonAllowed = data["bMultimonAllowed"];
        result.UlMaxDebugSessions = data["ulMaxDebugSessions"];
        result.BFUSEnabled = data["bFUSEnabled"];
        result.SlInitVerified = true;
        Rep($"[+] SLInitOffset = 0x{hookRva:X} ({resolution.Strategy}) VERIFIED: {detail}");
        Rep($"    layout deltas: {resolution.LayoutString}" +
            (resolution.MatchesKnownLayout ? "  (matches a community-verified layout)" : "  (derived, not in the known table)"));
        Rep($"    bInitialized=0x{result.BInitialized:X} bServerSku=0x{result.BServerSku:X} lMaxUserSessions=0x{result.LMaxUserSessions:X}");
        Rep($"    bAppServerAllowed=0x{result.BAppServerAllowed:X} bRemoteConnAllowed=0x{result.BRemoteConnAllowed:X} bMultimonAllowed=0x{result.BMultimonAllowed:X}");
        Rep($"    ulMaxDebugSessions=0x{result.UlMaxDebugSessions:X} bFUSEnabled=0x{result.BFUSEnabled:X}");
        return true;
    }

    // =====================================================================
    // Binary scanning helpers
    // =====================================================================

    private List<RipRef> ScanRipWrites(PEAnalyzer pe, int codeStart, int codeSize)
    {
        var list = new List<RipRef>();

        // C7 05 disp32 imm32 : mov dword [rip+disp], imm  (10 bytes)
        foreach (var loc in pe.FindAllPatterns(new byte?[] { 0xC7, 0x05 }, codeStart, codeSize))
        {
            if (loc + 10 > codeStart + codeSize) continue;
            int disp = (int)pe.ReadUInt32(loc + 2);
            uint instRva = pe.OffsetToRVA(loc);
            list.Add(new RipRef { FileOffset = loc, TargetRva = (uint)(instRva + 10 + disp), Imm = pe.ReadUInt32(loc + 6), Kind = "C7" });
        }
        // 48 89 05 disp32 : mov [rip+disp], rax  (7 bytes)
        foreach (var loc in pe.FindAllPatterns(new byte?[] { 0x48, 0x89, 0x05 }, codeStart, codeSize))
        {
            if (loc + 7 > codeStart + codeSize) continue;
            int disp = (int)pe.ReadUInt32(loc + 3);
            uint instRva = pe.OffsetToRVA(loc);
            list.Add(new RipRef { FileOffset = loc, TargetRva = (uint)(instRva + 7 + disp), Kind = "W" });
        }
        // 89 05 disp32 : mov [rip+disp], eax (6 bytes) -- skip if actually 48 89 05
        foreach (var loc in pe.FindAllPatterns(new byte?[] { 0x89, 0x05 }, codeStart, codeSize))
        {
            if (loc + 6 > codeStart + codeSize) continue;
            if (loc > 0 && pe.ReadByte(loc - 1) == 0x48) continue;
            int disp = (int)pe.ReadUInt32(loc + 2);
            uint instRva = pe.OffsetToRVA(loc);
            list.Add(new RipRef { FileOffset = loc, TargetRva = (uint)(instRva + 6 + disp), Kind = "W" });
        }
        // 88 05 disp32 : mov [rip+disp], al (6 bytes)
        foreach (var loc in pe.FindAllPatterns(new byte?[] { 0x88, 0x05 }, codeStart, codeSize))
        {
            if (loc + 6 > codeStart + codeSize) continue;
            int disp = (int)pe.ReadUInt32(loc + 2);
            uint instRva = pe.OffsetToRVA(loc);
            list.Add(new RipRef { FileOffset = loc, TargetRva = (uint)(instRva + 6 + disp), Kind = "W" });
        }
        // C6 05 disp32 imm8 : mov byte [rip+disp], imm8 (7 bytes)
        foreach (var loc in pe.FindAllPatterns(new byte?[] { 0xC6, 0x05 }, codeStart, codeSize))
        {
            if (loc + 7 > codeStart + codeSize) continue;
            int disp = (int)pe.ReadUInt32(loc + 2);
            uint instRva = pe.OffsetToRVA(loc);
            list.Add(new RipRef { FileOffset = loc, TargetRva = (uint)(instRva + 7 + disp), Imm = pe.ReadByte(loc + 6), Kind = "C6" });
        }
        return list;
    }

    private bool InData(PEAnalyzer pe, uint rva) => pe.DataSection != null && pe.DataSection.ContainsRVA(rva);

    // BlockInData removed: superseded by E8-anchored SLInit scoring (block end check done in VerifySLInit).

    /// <summary>
    /// Find the start of the function containing refOffset by scanning backwards
    /// for a prologue or an int3-padded function boundary.
    /// </summary>
    private uint FindFunctionStart(PEAnalyzer pe, int refOffset, int searchStart)
    {
        for (int i = refOffset; i >= Math.Max(searchStart, refOffset - 512); i--)
        {
            // int3 padding run followed by a valid prologue = strongest boundary signal
            if (OffsetVerifier.IsValidPrologue(pe, i))
            {
                // prefer prologue directly after CC padding
                if (i > searchStart && pe.ReadByte(i - 1) == 0xCC)
                    return pe.OffsetToRVA(i);
            }
        }
        // second pass: any prologue
        for (int i = refOffset; i >= Math.Max(searchStart, refOffset - 512); i--)
        {
            if (OffsetVerifier.IsValidPrologue(pe, i))
                return pe.OffsetToRVA(i);
        }
        return 0;
    }

    // =====================================================================
    // INI generation
    // =====================================================================

    /// <summary>Generate INI sections for EVERY version alias (section-name robustness).</summary>
    public string GenerateIniSection(AnalysisResult result)
    {
        var sb = new StringBuilder();
        var names = result.Aliases.Count > 0 ? result.Aliases : new List<string> { result.Version };
        for (int i = 0; i < names.Count; i++)
        {
            if (i > 0) sb.AppendLine($"; alias section - same binary, alternate version resource value");
            AppendOneSection(sb, names[i], result);
        }
        return sb.ToString();
    }

    private void AppendOneSection(StringBuilder sb, string version, AnalysisResult result)
    {
        var (body, slInit) = BuildSectionBodies(result);
        sb.AppendLine($"[{version}]");
        sb.AppendLine(body);
        sb.AppendLine();
        sb.AppendLine($"[{version}-SLInit]");
        sb.AppendLine(slInit);
        sb.AppendLine();
    }

    /// <summary>Section bodies WITHOUT headers - used by IniManager.AddVersionSections.</summary>
    public (string sectionContent, string slInitContent) BuildSectionBodies(AnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LocalOnlyPatch.x64=1");
        sb.AppendLine($"LocalOnlyOffset.x64={result.LocalOnlyOffset:X}");
        sb.AppendLine($"LocalOnlyCode.x64={result.LocalOnlyCode}");
        sb.AppendLine("SingleUserPatch.x64=1");
        sb.AppendLine($"SingleUserOffset.x64={result.SingleUserOffset:X}");
        sb.AppendLine($"SingleUserCode.x64={result.SingleUserCode}");
        sb.AppendLine("DefPolicyPatch.x64=1");
        sb.AppendLine($"DefPolicyOffset.x64={result.DefPolicyOffset:X}");
        sb.AppendLine($"DefPolicyCode.x64={result.DefPolicyCode}");
        sb.AppendLine("SLInitHook.x64=1");
        sb.AppendLine($"SLInitOffset.x64={result.SLInitOffset:X}");
        sb.Append("SLInitFunc.x64=New_CSLQuery_Initialize");

        var sd = new StringBuilder();
        sd.AppendLine($"bInitialized.x64      ={result.BInitialized:X}");
        sd.AppendLine($"bServerSku.x64        ={result.BServerSku:X}");
        sd.AppendLine($"lMaxUserSessions.x64  ={result.LMaxUserSessions:X}");
        sd.AppendLine($"bAppServerAllowed.x64 ={result.BAppServerAllowed:X}");
        sd.AppendLine($"bRemoteConnAllowed.x64={result.BRemoteConnAllowed:X}");
        sd.AppendLine($"bMultimonAllowed.x64  ={result.BMultimonAllowed:X}");
        sd.AppendLine($"ulMaxDebugSessions.x64={result.UlMaxDebugSessions:X}");
        sd.Append($"bFUSEnabled.x64       ={result.BFUSEnabled:X}");
        return (sb.ToString(), sd.ToString());
    }

    /// <summary>Patch codes required by this analysis result (for [PatchCodes] section).</summary>
    public List<string> GetRequiredPatchCodes(AnalysisResult result)
    {
        var codes = new List<string> { result.LocalOnlyCode, result.SingleUserCode, result.DefPolicyCode };
        return codes.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
    }

    /// <summary>Full analysis + INI generation in one step.</summary>
    public (bool success, string iniSection, string report) AnalyzeAndGenerate(string? termsrvPath = null)
    {
        var result = Analyze(termsrvPath);
        if (!result.Success)
        {
            return (false, "", result.Report + "\nAuto-analysis failed strict verification. INI was NOT modified.");
        }
        string iniSection = GenerateIniSection(result);
        return (true, iniSection, result.Report);
    }

    /// <summary>Analyze and return the rich result object (used by the one-click flow).</summary>
    public AnalysisResult AnalyzeEx(string? termsrvPath = null) => Analyze(termsrvPath);
}
