using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RDPWrapTool.Core;

/// <summary>
/// Byte-level verification of patch offsets BEFORE they are written to the INI.
/// Every candidate offset must match an exact byte fingerprint learned from
/// community-verified configurations. Strict policy: anything unverified is rejected.
/// </summary>
public static class OffsetVerifier
{
    // Patch code names (must exist in [PatchCodes] section of the INI)
    public const string CodeJmpShort = "jmpshort";
    public const string CodeZero = "Zero";
    public const string CodeMovEax1Nop2 = "mov_eax_1_nop_2";
    public const string CodeDefPolicyEaxRcx = "CDefPolicy_Query_eax_rcx";
    public const string CodeDefPolicyEaxRcxJmp = "CDefPolicy_Query_eax_rcx_jmp";
    public const string CodeDefPolicyR9dRdiJmp = "CDefPolicy_Query_r9d_rdi_jmp";

    // SLInit data layout: offset of each variable relative to bInitialized.
    // LayoutA: Win11 (build >= 22000), byte-verified on 26100.8737 & 28000.2336.
    public static readonly (string name, int delta)[] SlInitLayoutA =
    {
        ("bInitialized", 0), ("bServerSku", 4), ("lMaxUserSessions", 8),
        ("bAppServerAllowed", 16), ("bRemoteConnAllowed", 24), ("bMultimonAllowed", 28),
        ("ulMaxDebugSessions", 36), ("bFUSEnabled", 40)
    };
    // LayoutB: Win10 (build < 22000), byte-verified on 19041.6456.
    public static readonly (string name, int delta)[] SlInitLayoutB =
    {
        ("bInitialized", 0), ("bServerSku", 4), ("lMaxUserSessions", 8),
        ("bAppServerAllowed", 12), ("bRemoteConnAllowed", 24), ("bMultimonAllowed", 28),
        ("ulMaxDebugSessions", 32), ("bFUSEnabled", 36)
    };
    public const int SlInitBlockSizeA = 44; // bFUSEnabled + 4
    public const int SlInitBlockSizeB = 40;
    // Legacy aliases (default = Win11 layout)
    public static readonly (string name, int delta)[] SlInitLayout = SlInitLayoutA;
    public const int SlInitBlockSize = SlInitBlockSizeA;

    public static string Hex(PEAnalyzer pe, int fileOff, int len)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < len && fileOff + i < pe.DataLength; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(pe.ReadByte(fileOff + i).ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>LocalOnly: jmpshort (0xEB) overwrites a short JZ, so byte must be 0x74.</summary>
    public static bool VerifyLocalOnly(PEAnalyzer pe, uint rva, out string detail)
    {
        detail = "";
        int off = CheckedOffset(pe, rva, 1, out detail);
        if (off < 0) return false;
        byte b = pe.ReadByte(off);
        if (b != 0x74)
        {
            detail = $"expect 74 (jz short), got {b:X2}";
            return false;
        }
        detail = $"jz short found ({Hex(pe, off, 2)})";
        return true;
    }

    /// <summary>SingleUser: fingerprint depends on the selected patch code.</summary>
    public static bool VerifySingleUser(PEAnalyzer pe, uint rva, string code, out string detail)
    {
        detail = "";
        if (code == CodeMovEax1Nop2)
        {
            int off = CheckedOffset(pe, rva, 3, out detail);
            if (off < 0) return false;
            // 48 FF 15 = call qword [rip+disp32]; patch replaces all 7 bytes with mov eax,1 + nop*2
            if (pe.ReadByte(off) == 0x48 && pe.ReadByte(off + 1) == 0xFF && pe.ReadByte(off + 2) == 0x15)
            {
                detail = $"call [rip] found ({Hex(pe, off, 3)})";
                return true;
            }
            detail = $"expect 48 FF 15 (call [rip]), got {Hex(pe, off, 3)}";
            return false;
        }
        if (code == CodeZero)
        {
            // patch writes 0x00 over the '01' of 'B0 01 C3' (mov al,1; ret)
            int off = CheckedOffset(pe, rva, 2, out detail);
            if (off < 0) return false;
            if (pe.ReadByte(off) == 0x01 && off > 0 && pe.ReadByte(off - 1) == 0xB0)
            {
                detail = $"mov al,1 found ({Hex(pe, off - 1, 3)})";
                return true;
            }
            detail = $"expect B0 01 (mov al,1) before offset, got {Hex(pe, Math.Max(0, off - 1), 3)}";
            return false;
        }
        detail = $"unknown SingleUser code '{code}'";
        return false;
    }

    /// <summary>DefPolicy: fingerprint depends on the selected patch code.</summary>
    public static bool VerifyDefPolicy(PEAnalyzer pe, uint rva, string code, out string detail)
    {
        detail = "";

        if (code == CodeDefPolicyR9dRdiJmp)
        {
            // 44 8B 8F/87 38/3C 06 00 00 = mov r9d/r8d,[rdi+638h/63Ch]
            // The 11-byte patch ends with EB which overwrites the following jcc opcode
            // and REUSES its displacement, so the suffix 45 3B ?? 74/75 is MANDATORY.
            int off = CheckedOffset(pe, rva, 12, out detail);
            if (off < 0) return false;
            if (!(pe.ReadByte(off) == 0x44 && pe.ReadByte(off + 1) == 0x8B &&
                  (pe.ReadByte(off + 2) == 0x8F || pe.ReadByte(off + 2) == 0x87) &&
                  (pe.ReadByte(off + 3) == 0x38 || pe.ReadByte(off + 3) == 0x3C) &&
                  pe.ReadByte(off + 4) == 0x06 && pe.ReadByte(off + 5) == 0x00 && pe.ReadByte(off + 6) == 0x00))
            {
                detail = $"expect 44 8B 8F/87 38/3C 06 00 00, got {Hex(pe, off, 7)}";
                return false;
            }
            if (!(pe.ReadByte(off + 7) == 0x45 && pe.ReadByte(off + 8) == 0x3B &&
                  (pe.ReadByte(off + 10) == 0x74 || pe.ReadByte(off + 10) == 0x75)))
            {
                detail = $"missing '45 3B .. 74/75' (cmp r8d,r9d; jcc) suffix, got {Hex(pe, off + 7, 5)}";
                return false;
            }
            detail = $"mov r9d/r8d,[rdi+63xh] + cmp/jcc suffix found ({Hex(pe, off, 12)})";
            return true;
        }
        if (code == CodeDefPolicyEaxRcxJmp)
        {
            // 8B 81 38 06 00 00  mov eax,[rcx+638h]
            // 39 81 3C 06 00 00  cmp [rcx+63Ch],eax
            // 74/75 xx           jcc — the 13-byte patch's trailing EB reuses its displacement
            int off = CheckedOffset(pe, rva, 14, out detail);
            if (off < 0) return false;
            if (!(pe.ReadByte(off) == 0x8B && pe.ReadByte(off + 1) == 0x81 &&
                  pe.ReadByte(off + 2) == 0x38 && pe.ReadByte(off + 3) == 0x06 &&
                  pe.ReadByte(off + 4) == 0x00 && pe.ReadByte(off + 5) == 0x00 &&
                  pe.ReadByte(off + 6) == 0x39 && pe.ReadByte(off + 7) == 0x81 &&
                  pe.ReadByte(off + 8) == 0x3C && pe.ReadByte(off + 9) == 0x06 &&
                  pe.ReadByte(off + 10) == 0x00 && pe.ReadByte(off + 11) == 0x00))
            {
                detail = $"expect 8B 81 38 06 00 00 39 81 3C 06 00 00, got {Hex(pe, off, 12)}";
                return false;
            }
            if (pe.ReadByte(off + 12) != 0x74 && pe.ReadByte(off + 12) != 0x75)
            {
                detail = $"missing jcc after cmp, got {Hex(pe, off + 12, 2)}";
                return false;
            }
            detail = $"old-shape mov/cmp + jcc found ({Hex(pe, off, 14)})";
            return true;
        }
        if (code == CodeDefPolicyEaxRcx)
        {
            // Win10 19041 shape: patch point is the CMP instruction (the preceding
            // mov eax,[rcx+638h] 6 bytes earlier is left intact and becomes harmless).
            //   39 81 3C 06 00 00   cmp [rcx+63Ch],eax
            //   0F 84/85 xxxxxxxx   jz/jnz near — fully covered by the 12-byte patch
            int off = CheckedOffset(pe, rva, 14, out detail);
            if (off < 0) return false;
            if (!(pe.ReadByte(off) == 0x39 && pe.ReadByte(off + 1) == 0x81 &&
                  pe.ReadByte(off + 2) == 0x3C && pe.ReadByte(off + 3) == 0x06 &&
                  pe.ReadByte(off + 4) == 0x00 && pe.ReadByte(off + 5) == 0x00))
            {
                detail = $"expect 39 81 3C 06 00 00 (cmp [rcx+63Ch],eax), got {Hex(pe, off, 6)}";
                return false;
            }
            if (!(pe.ReadByte(off + 6) == 0x0F && (pe.ReadByte(off + 7) == 0x84 || pe.ReadByte(off + 7) == 0x85)))
            {
                detail = $"missing far jcc (0F 84/85) after cmp, got {Hex(pe, off + 6, 2)}";
                return false;
            }
            // optional context: the mov 6 bytes before should be 8B 81 38 06 00 00
            if (off >= 6 &&
                !(pe.ReadByte(off - 6) == 0x8B && pe.ReadByte(off - 5) == 0x81 &&
                  pe.ReadByte(off - 4) == 0x38 && pe.ReadByte(off - 3) == 0x06))
            {
                detail = $"cmp found but preceding mov eax,[rcx+638h] missing ({Hex(pe, off - 6, 6)})";
                return false;
            }
            detail = $"cmp [rcx+63Ch],eax + far jcc found ({Hex(pe, off, 8)})";
            return true;
        }
        detail = $"unknown DefPolicy code '{code}'";
        return false;
    }

    /// <summary>Check for a plausible x64 function prologue at a file offset.</summary>
    public static bool IsValidPrologue(PEAnalyzer pe, int off)
    {
        if (off < 0 || off + 3 > pe.DataLength) return false;
        byte b0 = pe.ReadByte(off), b1 = pe.ReadByte(off + 1), b2 = pe.ReadByte(off + 2);
        // push rxx with REX: 40 53/55/56/57
        if (b0 == 0x40 && (b1 >= 0x53 && b1 <= 0x57)) return true;
        // mov [rsp+xx], reg forms: 48 89 5C/6C/74/7C, 48 89 44/4C/54
        if (b0 == 0x48 && b1 == 0x89 && (b2 & 0xC7) == 0x44) return true;
        // sub rsp, imm8: 48 83 EC
        if (b0 == 0x48 && b1 == 0x83 && b2 == 0xEC) return true;
        // sub rsp, imm32: 48 81 EC
        if (b0 == 0x48 && b1 == 0x81 && b2 == 0xEC) return true;
        // mov r11, rsp: 4C 8B DC
        if (b0 == 0x4C && b1 == 0x8B && b2 == 0xDC) return true;
        // mov rbp, rsp: 48 8B EC
        if (b0 == 0x48 && b1 == 0x8B && b2 == 0xEC) return true;
        return false;
    }

    /// <summary>
    /// Verify the SLInit hook offset and data block.
    /// hookRva must point at a function prologue; all 8 data RVAs must be 4-aligned,
    /// inside .data, and spaced exactly per the known layout.
    /// </summary>
    public static bool VerifySLInit(PEAnalyzer pe, uint hookRva, IReadOnlyDictionary<string, uint> data, out string detail)
        => VerifySLInit(pe, hookRva, data, SlInitLayoutA, SlInitBlockSizeA, out detail);

    public static bool VerifySLInit(PEAnalyzer pe, uint hookRva, IReadOnlyDictionary<string, uint> data,
        (string name, int delta)[] layout, int blockSize, out string detail)
    {
        detail = "";
        int off = CheckedOffset(pe, rva: hookRva, 3, out detail);
        if (off < 0) { detail = "hook: " + detail; return false; }
        if (!IsValidPrologue(pe, off))
        {
            detail = $"hook prologue invalid at 0x{hookRva:X} ({Hex(pe, off, 4)})";
            return false;
        }

        if (pe.DataSection == null)
        {
            detail = "no .data section";
            return false;
        }

        foreach (var kv in layout)
        {
            if (!data.TryGetValue(kv.name, out uint r) || r == 0)
            {
                detail = $"missing SLInit data: {kv.name}";
                return false;
            }
            if ((r & 3) != 0)
            {
                detail = $"{kv.name} RVA 0x{r:X} not 4-aligned";
                return false;
            }
            if (!pe.DataSection.ContainsRVA(r))
            {
                detail = $"{kv.name} RVA 0x{r:X} outside .data";
                return false;
            }
        }

        // Exact layout spacing check
        uint baseRva = data["bInitialized"];
        foreach (var kv in layout)
        {
            if (data[kv.name] - baseRva != (uint)kv.delta)
            {
                detail = $"layout mismatch: {kv.name} at +{data[kv.name] - baseRva}, expect +{kv.delta}";
                return false;
            }
        }
        if (!pe.DataSection.ContainsRVA(baseRva + (uint)blockSize - 4))
        {
            detail = "SLInit block end outside .data";
            return false;
        }

        detail = $"hook prologue OK ({Hex(pe, off, 4)}), data block 0x{baseRva:X}..0x{baseRva + (uint)blockSize - 4:X} in .data";
        return true;
    }

    private static int CheckedOffset(PEAnalyzer pe, uint rva, int need, out string detail)
    {
        detail = "";
        if (rva == 0) { detail = "RVA is 0"; return -1; }
        uint off = pe.RVAToOffset(rva);
        if (off == 0 || off + need > pe.DataLength)
        {
            detail = $"RVA 0x{rva:X} not mappable or truncated";
            return -1;
        }
        return (int)off;
    }
}
