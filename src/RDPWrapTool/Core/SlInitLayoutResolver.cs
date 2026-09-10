using System;
using System.Collections.Generic;
using System.Linq;

namespace RDPWrapTool.Core;

/// <summary>How the value of a CSLQuery::Initialize slot is produced in termsrv.dll.</summary>
public enum SlInitSlotKind
{
    Unknown = 0,
    /// <summary>`mov dword [rip+x], 1` - the bInitialized flag written last.</summary>
    ImmOne,
    /// <summary>Policy result normalised to 0/1 (`cmp [v],0` + store 0 or 1).</summary>
    Bool,
    /// <summary>Raw policy value stored as-is (a session count).</summary>
    Count
}

/// <summary>One write to a .data slot performed inside CSLQuery::Initialize.</summary>
public sealed class SlInitStore
{
    public uint Rva;                 // target .data RVA
    public int Delta;                // Rva - bInitialized RVA
    public SlInitSlotKind Kind;
    public int StoreInsOffset;       // raw file offset of the storing instruction
    public string? WppName;          // WPP trace name feeding this slot (null if unnamed)
    public int WppLeaOffset;         // raw file offset of the lea that loads the WPP string (-1 if none)

    public string StoreRvaHex => "0x" + ((uint)StoreInsOffset).ToString("X");
}

/// <summary>
/// Result of deriving the SLInit (CSLQuery::Initialize) data block from termsrv.dll.
/// The layout is DERIVED from the binary, never guessed from the build number.
/// </summary>
public sealed class SlInitResolution
{
    public bool Success;
    public string Reason = "";
    public string Strategy = "";
    public uint HookRva;
    public uint FunctionEndRva;

    /// <summary>Every .data slot written by the hooked function (diagnostics + validation).</summary>
    public List<SlInitStore> AllStores { get; } = new();

    /// <summary>Slots that could be named from the embedded WPP trace strings.</summary>
    public List<SlInitStore> NamedSlots { get; } = new();

    /// <summary>Name -&gt; RVA for the 8 variables of the INI's -SLInit section (when Success).</summary>
    public Dictionary<string, uint> Addresses { get; } = new();

    /// <summary>Human readable evidence lines (also printed in the analysis report).</summary>
    public List<string> Evidence { get; } = new();

    /// <summary>e.g. "0,4,8,10,1C,20,28,2C"; empty when unresolved.</summary>
    public string LayoutString { get; set; } = "";

    public bool MatchesKnownLayout { get; set; }
    public SlInitStore? StoreFor(uint rva) => AllStores.FirstOrDefault(s => s.Rva == rva);
}

/// <summary>
/// Derives the 8 data addresses of the rdpwrap SLInit override block from termsrv.dll.
///
/// Why this exists: the previous implementation picked a hard-coded delta table based on
/// the build number and then "verified" it against the very same table (circular), so a
/// wrong layout was always accepted. Real Windows builds use at least three different
/// layouts, and Windows 11 24H2+/25H2/26H1 builds (e.g. 10.0.28000.2952) use a layout
/// that no hard-coded table contained.
///
/// The derivation uses two independent signals taken from the binary itself:
///  1. WPP/ETW trace strings embedded in termsrv.dll read
///     "CSLQuery::Initialize - SLGetWindowsInformationDWORD for &lt;VariableName&gt;".
///     The lea of such a string is followed (in the same block) by the store of that
///     variable's value, so name -&gt; address can be recovered exactly.
///  2. The shape of each store: a boolean slot is written through the
///     "cmp [v],0 / mov [t],1 / mov [t],0 / mov eax,[t]" idiom, a count slot stores the
///     raw queried value. lMaxUserSessions/ulMaxDebugSessions are counts, the four
///     Allowed/FUS flags are booleans - this rejects wrong layouts by itself.
/// </summary>
public static class SlInitLayoutResolver
{
    /// <summary>
    /// Literal prefix of the WPP trace strings. The API name is embedded in the format
    /// string, so this is stable across builds and locales (it is not localised).
    /// </summary>
    public const string WppPrefix = "CSLQuery::Initialize - SLGetWindowsInformationDWORD for ";

    /// <summary>Variables that must exist in every -SLInit section (bInitialized comes from the imm=1 anchor).</summary>
    public static readonly string[] NamedVariables =
    {
        "bServerSku", "lMaxUserSessions", "bAppServerAllowed",
        "bRemoteConnAllowed", "bMultimonAllowed", "ulMaxDebugSessions", "bFUSEnabled"
    };

    /// <summary>
    /// Layouts observed in community-verified rdpwrap.ini files (delta from bInitialized).
    /// Used only as a cross-check / fallback - never as the primary source of truth.
    /// </summary>
    public static readonly (string tag, int bServerSku, int lMaxUserSessions, int bAppServerAllowed,
        int bRemoteConnAllowed, int bMultimonAllowed, int ulMaxDebugSessions, int bFUSEnabled)[] KnownLayouts =
    {
        ("L1/20348+26100-early+28000-early", 0x4, 0x8, 0x10, 0x18, 0x1C, 0x24, 0x28),
        ("L2/26100-late+28000.1516+",        0x4, 0x8, 0x10, 0x1C, 0x20, 0x28, 0x2C),
        ("L3/win10-19041",                   0x4, 0x8, 0x10, 0x18, 0x1C, 0x20, 0x24)
    };

    // ---------------------------------------------------------------------
    // Public entry
    // ---------------------------------------------------------------------

    /// <summary>
    /// Resolve the SLInit data block for the CSLQuery::Initialize function that starts at
    /// 'hookRva' and ends at 'functionEndRva'.
    /// </summary>
    public static SlInitResolution Resolve(PEAnalyzer pe, uint hookRva, uint functionEndRva, Action<string>? log = null,
        bool allowWppNames = true)
    {
        var res = new SlInitResolution { HookRva = hookRva, FunctionEndRva = functionEndRva };

        int startOff = (int)pe.RVAToOffset(hookRva);
        if (startOff <= 0)
        {
            res.Reason = $"hook RVA 0x{hookRva:X} is not backed by file data";
            return res;
        }
        int endOff = functionEndRva > 0 ? (int)pe.RVAToOffset(functionEndRva - 1) + 1 : 0;
        if (endOff <= startOff) endOff = pe.DataLength;

        if (pe.DataSection == null)
        {
            res.Reason = "no .data section";
            return res;
        }

        CollectStores(pe, startOff, endOff, res);
        res.Evidence.Add($"[i] {res.AllStores.Count} .data store(s) found inside the hooked function (0x{hookRva:X}..0x{functionEndRva:X})");
        if (res.AllStores.Count < 8)
        {
            res.Reason = $"only {res.AllStores.Count} .data stores inside the hook function, expected >= 8";
            return res;
        }

        CollectWppNames(pe, startOff, endOff, res, allowWppNames);

        // ---- primary path: WPP names ----
        string primaryFailure = allowWppNames ? "" : "WPP name matching disabled by caller";
        if (allowWppNames && TryBuildFromNames(pe, res, out primaryFailure))
        {
            res.Success = true;
            res.Strategy = "wpp-named + store-shape";
            return res;
        }

        // ---- fallback: known layout + type signature ----
        string fallbackFailure = "";
        if (TryBuildFromKnownLayouts(pe, res, out fallbackFailure))
        {
            res.Success = true;
            res.Strategy = "known-layout + store-shape type signature (no WPP names available)";
            res.Evidence.Add("[i] WPP name path unavailable: " + primaryFailure);
            return res;
        }

        res.Reason = $"WPP path: {primaryFailure}; fallback path: {fallbackFailure}";
        return res;
    }

    // ---------------------------------------------------------------------
    // Step 1: every .data store inside the hook function
    // ---------------------------------------------------------------------

    private static void CollectStores(PEAnalyzer pe, int startOff, int endOff, SlInitResolution res)
    {
        var data = pe.DataSection!;
        int limit = Math.Min(endOff - 10, pe.DataLength - 10);

        for (int i = startOff; i < limit; i++)
        {
            byte c = pe.ReadByte(i);
            byte b1 = pe.ReadByte(i + 1);
            bool isStore = false;
            uint target = 0;
            SlInitSlotKind kind = SlInitSlotKind.Count;
            uint insRva = pe.OffsetToRVA(i);
            if (insRva == 0) continue;

            if (c == 0xC7 && b1 == 0x05)
            {
                target = (uint)(insRva + 10 + (int)pe.ReadUInt32(i + 2));
                kind = pe.ReadUInt32(i + 6) == 1 ? SlInitSlotKind.ImmOne : SlInitSlotKind.Count;
                isStore = true;
            }
            else if ((c == 0x48 || c == 0x4C) && b1 == 0x89 && pe.ReadByte(i + 2) == 0x05)
            {
                target = (uint)(insRva + 7 + (int)pe.ReadUInt32(i + 3));
                isStore = true;
            }
            else if (c == 0x89 && b1 == 0x05 && (i == 0 || (pe.ReadByte(i - 1) != 0x48 && pe.ReadByte(i - 1) != 0x4C)))
            {
                target = (uint)(insRva + 6 + (int)pe.ReadUInt32(i + 2));
                isStore = true;
            }
            else if (c == 0x88 && b1 == 0x05)
            {
                target = (uint)(insRva + 6 + (int)pe.ReadUInt32(i + 2));
                isStore = true;
            }
            else if (c == 0xC6 && b1 == 0x05)
            {
                target = (uint)(insRva + 7 + (int)pe.ReadUInt32(i + 2));
                isStore = true;
            }

            if (!isStore) continue;
            if (!data.ContainsRVA(target)) continue;
            if (res.AllStores.Any(s => s.Rva == target && s.StoreInsOffset == i)) continue;

            if (kind == SlInitSlotKind.Count && IsBooleanStoreIdiom(pe, i))
                kind = SlInitSlotKind.Bool;

            res.AllStores.Add(new SlInitStore { Rva = target, Kind = kind, StoreInsOffset = i, WppLeaOffset = -1 });
        }
    }

    /// <summary>
    /// The compiler emits boolean policy results as:
    ///   cmp dword [rsp+X],0 / jcc / mov dword [rsp+Y],1 / jmp / mov dword [rsp+Y],0
    ///   mov eax,[rsp+Y] / mov [global],eax
    /// Detect the "mov [rsp+Y],1" + "mov [rsp+Y],0" pair just before the store.
    /// </summary>
    private static bool IsBooleanStoreIdiom(PEAnalyzer pe, int storeOffset)
    {
        int oneSlot = -1, zeroSlot = -1;
        for (int j = storeOffset - 4; j >= Math.Max(0, storeOffset - 64); j--)
        {
            if (pe.ReadByte(j) != 0xC7 || pe.ReadByte(j + 1) != 0x44 || pe.ReadByte(j + 2) != 0x24) continue;
            byte slot = pe.ReadByte(j + 3);
            uint imm = pe.ReadUInt32(j + 4);
            if (imm == 1) oneSlot = slot;
            else if (imm == 0) zeroSlot = slot;
        }
        return oneSlot >= 0 && zeroSlot >= 0 && oneSlot == zeroSlot;
    }

    // ---------------------------------------------------------------------
    // Step 2: recover variable names from the embedded WPP trace strings
    // ---------------------------------------------------------------------

    private static void CollectWppNames(PEAnalyzer pe, int startOff, int endOff, SlInitResolution res, bool allowWppNames)
    {
        if (!allowWppNames)
        {
            res.Evidence.Add("[i] WPP trace string matching disabled (test mode)");
            return;
        }
        var strings = pe.FindAnsiStrings(WppPrefix);
        if (strings.Count == 0)
        {
            res.Evidence.Add("[i] no WPP trace strings found (older/stripped build)");
            return;
        }
        res.Evidence.Add($"[i] {strings.Count} WPP trace string(s) naming CSLQuery::Initialize variables");

        var candidates = new List<SlInitStore>();
        foreach (var (strRva, text) in strings)
        {
            string name = text.Substring(WppPrefix.Length).Trim();
            if (name.Length == 0) continue;

            var targets = new List<(SlInitStore store, int lea)>();
            // find lea instructions inside the function that load this string
            for (int i = startOff; i < endOff - 7; i++)
            {
                byte c = pe.ReadByte(i);
                if (c != 0x48 && c != 0x4C) continue;
                if (pe.ReadByte(i + 1) != 0x8D) continue;
                if ((pe.ReadByte(i + 2) & 0xC7) != 0x05) continue;
                uint insRva = pe.OffsetToRVA(i);
                if (insRva == 0) continue;
                uint dst = (uint)(insRva + 7 + (int)pe.ReadUInt32(i + 3));
                if (dst != strRva) continue;

                SlInitStore? store = res.AllStores
                    .Where(s => s.StoreInsOffset > i && s.StoreInsOffset - i <= 1200)
                    .OrderBy(s => s.StoreInsOffset)
                    .FirstOrDefault();
                if (store != null) targets.Add((store, i));
            }

            if (targets.Count == 0)
            {
                res.Evidence.Add($"[!] WPP '{name}': no following store found");
                continue;
            }
            if (targets.Select(t => t.store.Rva).Distinct().Count() > 1)
            {
                res.Evidence.Add($"[!] WPP '{name}': ambiguous targets " +
                    string.Join(", ", targets.Select(t => "0x" + t.store.Rva.ToString("X")).Distinct()));
                continue;
            }

            var win = targets[0].store;
            if (res.NamedSlots.Any(s => s.Rva == win.Rva && s.WppName != null)) continue;
            candidates.Add(new SlInitStore
            {
                Rva = win.Rva,
                Kind = win.Kind,
                StoreInsOffset = win.StoreInsOffset,
                WppName = name,
                WppLeaOffset = targets[0].lea
            });
        }

        // Outlier rejection: all named globals of CSLQuery::Initialize live in one small block.
        if (candidates.Count > 0)
        {
            var chosen = candidates
                .OrderBy(c => candidates.Count(o => Math.Abs((long)o.Rva - c.Rva) <= 0x40))
                .ThenBy(c => c.Rva)
                .Last();
            var cluster = candidates.Where(c => Math.Abs((long)c.Rva - chosen.Rva) <= 0x40).ToList();
            foreach (var dropped in candidates.Except(cluster))
                res.Evidence.Add($"[!] WPP '{dropped.WppName}' -> 0x{dropped.Rva:X} rejected (outside the SLInit block)");
            res.NamedSlots.AddRange(cluster.OrderBy(c => c.Rva));
        }

        foreach (var s in res.NamedSlots)
            res.Evidence.Add($"[i] WPP name: {s.WppName,-34} -> 0x{s.Rva:X8}  ({s.Kind}, store @0x{s.StoreInsOffset:X}, lea @0x{s.WppLeaOffset:X})");
    }

    // ---------------------------------------------------------------------
    // Step 3a: build the layout from WPP names
    // ---------------------------------------------------------------------

    private static bool TryBuildFromNames(PEAnalyzer pe, SlInitResolution res, out string failure)
    {
        failure = "no WPP-named slots recovered";
        if (res.NamedSlots.Count < 4) return false;

        // bInitialized: the imm=1 store written last inside the SLInit block.
        var immOnes = res.AllStores
            .Where(s => s.Kind == SlInitSlotKind.ImmOne)
            .Where(s => res.NamedSlots.Any(n => Math.Abs((long)n.Rva - s.Rva) <= 0x80))
            .OrderBy(s => s.StoreInsOffset)
            .ToList();
        if (immOnes.Count == 0)
        {
            failure = "no `mov dword [rip+x],1` anchor found inside the SLInit block";
            return false;
        }
        var bInit = immOnes.Last();

        uint baseRva = bInit.Rva;
        var byName = new Dictionary<string, SlInitStore>(StringComparer.Ordinal);
        foreach (var n in res.NamedSlots)
            if (n.WppName != null && !byName.ContainsKey(n.WppName)) byName[n.WppName] = n;

        var addresses = new Dictionary<string, uint> { ["bInitialized"] = baseRva };

        // bServerSku never has a WPP trace string; it is the boolean slot right after bInitialized.
        var sku = res.AllStores.FirstOrDefault(s => s.Rva == baseRva + 4
                                                    && !byName.Values.Any(v => v.Rva == s.Rva));
        if (sku == null)
        {
            failure = $"no store at bInitialized+4 (0x{baseRva + 4:X}) for bServerSku";
            return false;
        }
        addresses["bServerSku"] = sku.Rva;

        var expected = new (string name, SlInitSlotKind kind)[]
        {
            ("lMaxUserSessions", SlInitSlotKind.Count),
            ("bAppServerAllowed", SlInitSlotKind.Bool),
            ("bRemoteConnAllowed", SlInitSlotKind.Bool),
            ("bMultimonAllowed", SlInitSlotKind.Bool),
            ("ulMaxDebugSessions", SlInitSlotKind.Count),
            ("bFUSEnabled", SlInitSlotKind.Bool)
        };
        foreach (var (name, kind) in expected)
        {
            if (!byName.TryGetValue(name, out var slot))
            {
                failure = $"WPP name '{name}' not recovered";
                return false;
            }
            if (slot.Kind != kind)
            {
                failure = $"'{name}' at 0x{slot.Rva:X} has store shape {slot.Kind}, expected {kind}";
                return false;
            }
            if (addresses.ContainsValue(slot.Rva))
            {
                failure = $"duplicate address 0x{slot.Rva:X} for '{name}'";
                return false;
            }
            addresses[name] = slot.Rva;
        }

        return Finish(pe, res, addresses, baseRva, out failure);
    }

    // ---------------------------------------------------------------------
    // Step 3b: fallback - pick the unique known layout whose type signature fits
    // ---------------------------------------------------------------------

    private static bool TryBuildFromKnownLayouts(PEAnalyzer pe, SlInitResolution res, out string failure)
    {
        failure = "no `mov dword [rip+x],1` anchor found";
        var bInit = res.AllStores.Where(s => s.Kind == SlInitSlotKind.ImmOne)
                                 .OrderBy(s => s.StoreInsOffset)
                                 .LastOrDefault();
        if (bInit == null) return false;

        uint baseRva = bInit.Rva;
        var passing = new List<string>();
        Dictionary<string, uint>? winner = null;

        foreach (var l in KnownLayouts)
        {
            var map = new Dictionary<string, uint>
            {
                ["bInitialized"] = baseRva,
                ["bServerSku"] = baseRva + (uint)l.bServerSku,
                ["lMaxUserSessions"] = baseRva + (uint)l.lMaxUserSessions,
                ["bAppServerAllowed"] = baseRva + (uint)l.bAppServerAllowed,
                ["bRemoteConnAllowed"] = baseRva + (uint)l.bRemoteConnAllowed,
                ["bMultimonAllowed"] = baseRva + (uint)l.bMultimonAllowed,
                ["ulMaxDebugSessions"] = baseRva + (uint)l.ulMaxDebugSessions,
                ["bFUSEnabled"] = baseRva + (uint)l.bFUSEnabled
            };
            bool ok = true;
            foreach (var kv in map)
            {
                if (kv.Key == "bInitialized") continue;
                var store = res.StoreFor(kv.Value);
                if (store == null) { ok = false; break; }
                if (kv.Key is "lMaxUserSessions" or "ulMaxDebugSessions")
                {
                    if (store.Kind != SlInitSlotKind.Count) { ok = false; break; }
                }
                else if (store.Kind != SlInitSlotKind.Bool) { ok = false; break; }
            }
            if (!ok) continue;
            passing.Add(l.tag);
            winner ??= map;
        }

        if (passing.Count != 1)
        {
            failure = passing.Count == 0
                ? "no known layout matches the store shapes of this build"
                : $"ambiguous: {passing.Count} known layouts match ({string.Join(", ", passing)})";
            return false;
        }

        failure = "";
        res.Evidence.Add($"[i] fallback selected known layout {passing[0]}");
        return Finish(pe, res, winner!, baseRva, out failure);
    }

    // ---------------------------------------------------------------------
    // Step 4: validation shared by both paths
    // ---------------------------------------------------------------------

    private static bool Finish(PEAnalyzer pe, SlInitResolution res, Dictionary<string, uint> addresses,
        uint baseRva, out string failure)
    {
        failure = "";
        var data = pe.DataSection!;

        foreach (var required in NamedVariables.Prepend("bInitialized"))
        {
            if (!addresses.ContainsKey(required))
            {
                failure = $"missing address for '{required}'";
                return false;
            }
        }

        var seen = new HashSet<uint>();
        foreach (var kv in addresses)
        {
            uint rva = kv.Value;
            if (!data.ContainsRVA(rva)) { failure = $"{kv.Key} RVA 0x{rva:X} outside .data"; return false; }
            if ((rva & 3) != 0) { failure = $"{kv.Key} RVA 0x{rva:X} not 4-aligned"; return false; }
            if (rva < baseRva) { failure = $"{kv.Key} RVA 0x{rva:X} below bInitialized 0x{baseRva:X}"; return false; }
            if (!seen.Add(rva)) { failure = $"duplicate RVA 0x{rva:X}"; return false; }
            if (res.StoreFor(rva) == null)
            {
                failure = $"{kv.Key} RVA 0x{rva:X} is never written inside the hooked function";
                return false;
            }
        }

        var deltas = NamedVariables.Prepend("bInitialized")
            .Select(n => (name: n, delta: (int)(addresses[n] - baseRva)))
            .OrderBy(t => t.delta)
            .ToList();

        res.Addresses.Clear();
        foreach (var kv in addresses) res.Addresses[kv.Key] = kv.Value;

        res.LayoutString = string.Join(",", deltas.Select(d => d.delta.ToString("X")));

        var tag = KnownLayouts.FirstOrDefault(l =>
            l.bServerSku == addresses["bServerSku"] - baseRva &&
            l.lMaxUserSessions == addresses["lMaxUserSessions"] - baseRva &&
            l.bAppServerAllowed == addresses["bAppServerAllowed"] - baseRva &&
            l.bRemoteConnAllowed == addresses["bRemoteConnAllowed"] - baseRva &&
            l.bMultimonAllowed == addresses["bMultimonAllowed"] - baseRva &&
            l.ulMaxDebugSessions == addresses["ulMaxDebugSessions"] - baseRva &&
            l.bFUSEnabled == addresses["bFUSEnabled"] - baseRva).tag;
        res.MatchesKnownLayout = tag != null;
        res.Evidence.Add($"[+] resolved layout (delta from bInitialized 0x{baseRva:X}): {res.LayoutString}" +
                         (tag != null ? $"  == known layout {tag}" : "  (not in the known-layout table)"));

        foreach (var d in deltas)
            res.Evidence.Add($"[+] {d.name,-20} = 0x{addresses[d.name]:X}  (+0x{d.delta:X})" +
                             $"  store {res.StoreFor(addresses[d.name])!.Kind} @0x{res.StoreFor(addresses[d.name])!.StoreInsOffset:X}");
        return true;
    }
}
