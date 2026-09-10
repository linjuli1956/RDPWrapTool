using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace RDPWrapTool.Core;

/// <summary>
/// Parses PE file structure and provides binary search capabilities.
/// Used by TermSrvAnalyzer to find patch offsets in termsrv.dll.
/// </summary>
public class PEAnalyzer : IDisposable
{
    private readonly byte[] _data;
    private bool _is64Bit;

    // PE sections
    public List<PESection> Sections { get; } = new();
    public PESection? TextSection { get; private set; }
    public PESection? DataSection { get; private set; }
    public uint ImageBase { get; private set; }
    public uint EntryPoint { get; private set; }

    /// <summary>
    /// Total length of the raw file data.
    /// </summary>
    public int DataLength => _data.Length;

    // Export directory
    public List<ExportEntry> Exports { get; } = new();

    public class PESection
    {
        public string Name { get; set; } = "";
        public uint VirtualAddress { get; set; }
        public uint VirtualSize { get; set; }
        public uint RawDataOffset { get; set; }
        public uint RawDataSize { get; set; }
        public uint Characteristics { get; set; }

        public bool ContainsRVA(uint rva)
        {
            return rva >= VirtualAddress && rva < VirtualAddress + VirtualSize;
        }

        /// <summary>
        /// True when the RVA is both inside the section and backed by real bytes in the
        /// file. Sections like termsrv.dll's .data have VirtualSize &gt;&gt; RawSize, so the
        /// virtual tail exists in memory but has no file content.
        /// </summary>
        public bool ContainsRVAInRawData(uint rva)
        {
            return ContainsRVA(rva) && (rva - VirtualAddress) < RawDataSize;
        }

        public uint RVAToOffset(uint rva)
        {
            return rva - VirtualAddress + RawDataOffset;
        }
    }

    public class ExportEntry
    {
        public string Name { get; set; } = "";
        public uint RVA { get; set; }
    }

    public PEAnalyzer(string filePath)
    {
        _data = File.ReadAllBytes(filePath);
        ParsePE();
    }

    private void ParsePE()
    {
        // DOS Header
        if (_data.Length < 0x40 || _data[0] != 'M' || _data[1] != 'Z')
            throw new Exception("Invalid DOS header");

        uint peOffset = BitConverter.ToUInt32(_data, 0x3C);
        if (peOffset + 4 > _data.Length)
            throw new Exception("Invalid PE offset");

        // PE Signature
        if (_data[(int)peOffset] != 'P' || _data[(int)peOffset + 1] != 'E' || _data[(int)peOffset + 2] != 0 || _data[(int)peOffset + 3] != 0)
            throw new Exception("Invalid PE signature");

        // COFF Header
        int coffOffset = (int)(peOffset + 4);
        ushort machine = BitConverter.ToUInt16(_data, coffOffset);
        ushort numberOfSections = BitConverter.ToUInt16(_data, coffOffset + 2);
        ushort sizeOfOptionalHeader = BitConverter.ToUInt16(_data, coffOffset + 16);

        _is64Bit = machine == 0x8664; // AMD64

        // Optional Header
        int optOffset = coffOffset + 20;
        if (optOffset + sizeOfOptionalHeader > _data.Length)
            throw new Exception("Invalid optional header");

        ushort magic = BitConverter.ToUInt16(_data, optOffset);
        _is64Bit = magic == 0x20B; // PE32+

        if (_is64Bit)
        {
            ImageBase = (uint)BitConverter.ToInt64(_data, optOffset + 24);
            EntryPoint = BitConverter.ToUInt32(_data, optOffset + 16);
        }
        else
        {
            ImageBase = BitConverter.ToUInt32(_data, optOffset + 28);
            EntryPoint = BitConverter.ToUInt32(_data, optOffset + 16);
        }

        // Section Headers
        int sectionOffset = optOffset + sizeOfOptionalHeader;
        for (int i = 0; i < numberOfSections; i++)
        {
            int so = sectionOffset + i * 40;
            if (so + 40 > _data.Length) break;

            var section = new PESection
            {
                Name = System.Text.Encoding.ASCII.GetString(_data, so, 8).TrimEnd('\0'),
                VirtualSize = BitConverter.ToUInt32(_data, so + 8),
                VirtualAddress = BitConverter.ToUInt32(_data, so + 12),
                RawDataSize = BitConverter.ToUInt32(_data, so + 16),
                RawDataOffset = BitConverter.ToUInt32(_data, so + 20),
                Characteristics = BitConverter.ToUInt32(_data, so + 36)
            };

            Sections.Add(section);

            if (section.Name == ".text")
                TextSection = section;
            else if (section.Name == ".data")
                DataSection = section;
        }

        // Parse Export Directory
        ParseExports(optOffset);
    }

    private void ParseExports(int optOffset)
    {
        // Export Directory RVA is at offset 96 (PE32) or 112 (PE32+) in Optional Header
        int exportDirOffset = _is64Bit ? optOffset + 112 : optOffset + 96;
        if (exportDirOffset + 8 > _data.Length) return;

        uint exportRVA = BitConverter.ToUInt32(_data, exportDirOffset);
        uint exportSize = BitConverter.ToUInt32(_data, exportDirOffset + 4);

        if (exportRVA == 0 || exportSize == 0) return;

        uint exportOffset = RVAToOffset(exportRVA);
        if (exportOffset == 0 || exportOffset + 40 > _data.Length) return;

        uint nameRVA = BitConverter.ToUInt32(_data, (int)exportOffset + 12);
        uint ordinalBase = BitConverter.ToUInt32(_data, (int)exportOffset + 16);
        uint numberOfFunctions = BitConverter.ToUInt32(_data, (int)exportOffset + 20);
        uint numberOfNames = BitConverter.ToUInt32(_data, (int)exportOffset + 24);
        uint addrOfFunctions = BitConverter.ToUInt32(_data, (int)exportOffset + 28);
        uint addrOfNames = BitConverter.ToUInt32(_data, (int)exportOffset + 32);
        uint addrOfNameOrdinals = BitConverter.ToUInt32(_data, (int)exportOffset + 36);

        uint funcOffset = RVAToOffset(addrOfFunctions);
        uint nameOffset = RVAToOffset(addrOfNames);
        uint ordOffset = RVAToOffset(addrOfNameOrdinals);

        for (int i = 0; i < numberOfNames; i++)
        {
            if (nameOffset + (uint)(i * 4) + 4 > _data.Length) break;
            uint nameRVA2 = BitConverter.ToUInt32(_data, (int)(nameOffset + i * 4));
            uint nameOff = RVAToOffset(nameRVA2);
            if (nameOff == 0 || nameOff >= _data.Length) continue;

            // Read null-terminated string
            int end = (int)nameOff;
            while (end < _data.Length && _data[end] != 0) end++;
            string name = System.Text.Encoding.ASCII.GetString(_data, (int)nameOff, end - (int)nameOff);

            if (ordOffset + (uint)(i * 2) + 2 > _data.Length) break;
            ushort ordinal = BitConverter.ToUInt16(_data, (int)(ordOffset + i * 2));

            if (funcOffset + (uint)(ordinal * 4) + 4 > _data.Length) break;
            uint funcRVA = BitConverter.ToUInt32(_data, (int)(funcOffset + ordinal * 4));

            Exports.Add(new ExportEntry { Name = name, RVA = funcRVA });
        }
    }

    /// <summary>
    /// Convert an RVA to a raw file offset. Returns 0 when the RVA is not backed by
    /// file data (unmapped, or inside the virtual-only tail of a section such as
    /// .data, whose RawSize is far smaller than its VirtualSize).
    /// </summary>
    public uint RVAToOffset(uint rva)
    {
        foreach (var sec in Sections)
        {
            if (sec.ContainsRVAInRawData(rva))
                return sec.RVAToOffset(rva);
        }
        return 0;
    }

    /// <summary>True when 'rva' is inside a section and backed by file bytes.</summary>
    public bool IsMappedInFile(uint rva) => RVAToOffset(rva) != 0;

    /// <summary>
    /// Read a little-endian uint32 at an RVA. Returns 0 when the RVA has no file content.
    /// </summary>
    public uint ReadUInt32AtRva(uint rva)
    {
        uint off = RVAToOffset(rva);
        if (off == 0 || off + 4 > _data.Length) return 0;
        return BitConverter.ToUInt32(_data, (int)off);
    }

    /// <summary>
    /// Find every NUL-terminated ANSI string that starts with 'prefix' and return its
    /// RVA plus the full text. Used to read the WPP trace strings embedded in
    /// termsrv.dll, which literally name the globals initialized by CSLQuery::Initialize
    /// (e.g. "CSLQuery::Initialize - SLGetWindowsInformationDWORD for bRemoteConnAllowed").
    /// </summary>
    public List<(uint rva, string text)> FindAnsiStrings(string prefix, int maxLength = 200)
    {
        var results = new List<(uint, string)>();
        if (string.IsNullOrEmpty(prefix)) return results;

        byte[] pat = System.Text.Encoding.ASCII.GetBytes(prefix);
        for (int i = 0; i + pat.Length < _data.Length; i++)
        {
            if (_data[i] != pat[0]) continue;
            bool ok = true;
            for (int j = 1; j < pat.Length; j++)
            {
                if (_data[i + j] != pat[j]) { ok = false; break; }
            }
            if (!ok) continue;

            var sb = new System.Text.StringBuilder();
            for (int k = i; k < _data.Length && k < i + maxLength; k++)
            {
                byte c = _data[k];
                if (c == 0) break;
                if (c < 0x20 || c > 0x7E) { sb.Clear(); break; }
                sb.Append((char)c);
            }
            if (sb.Length == 0) continue;

            uint rva = OffsetToRVA(i);
            if (rva != 0) results.Add((rva, sb.ToString()));
            i += pat.Length - 1;
        }
        return results;
    }

    /// <summary>
    /// Search for a byte pattern in the raw data starting from a given offset.
    /// Pattern can contain -1 (0xFF) as wildcard bytes.
    /// </summary>
    public int FindPattern(byte[] pattern, int startIndex = 0)
    {
        return FindPattern(pattern, startIndex, _data.Length - startIndex);
    }

    /// <summary>
    /// Search for a byte pattern within a specific range.
    /// </summary>
    public int FindPattern(byte[] pattern, int startIndex, int searchLength)
    {
        int endIndex = Math.Min(startIndex + searchLength, _data.Length - pattern.Length);

        for (int i = startIndex; i <= endIndex; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (pattern[j] != 0xFF && _data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    /// <summary>
    /// Find all occurrences of a pattern.
    /// </summary>
    public List<int> FindAllPatterns(byte[] pattern, int startIndex = 0, int searchLength = -1)
    {
        var results = new List<int>();
        if (searchLength < 0) searchLength = _data.Length - startIndex;
        int pos = startIndex;

        while (pos < _data.Length)
        {
            int found = FindPattern(pattern, pos, searchLength - (pos - startIndex));
            if (found < 0) break;
            results.Add(found);
            pos = found + 1;
        }
        return results;
    }

    /// <summary>
    /// Find all occurrences of a pattern where null bytes are wildcards.
    /// Unlike the 0xFF-wildcard overload, 0xFF here is a literal byte.
    /// </summary>
    public List<int> FindAllPatterns(byte?[] pattern, int startIndex, int searchLength)
    {
        var results = new List<int>();
        int endIndex = Math.Min(startIndex + searchLength, _data.Length) - pattern.Length;
        for (int i = startIndex; i <= endIndex; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (pattern[j].HasValue && _data[i + j] != pattern[j].Value)
                {
                    match = false;
                    break;
                }
            }
            if (match) results.Add(i);
        }
        return results;
    }

    /// <summary>
    /// Get the raw data for a section.
    /// </summary>
    public byte[] GetSectionData(PESection section)
    {
        int offset = (int)section.RawDataOffset;
        int size = (int)Math.Min(section.RawDataSize, _data.Length - offset);
        var result = new byte[size];
        Array.Copy(_data, offset, result, 0, size);
        return result;
    }

    /// <summary>
    /// Read a byte at a raw file offset.
    /// </summary>
    public byte ReadByte(int offset)
    {
        return _data[offset];
    }

    /// <summary>
    /// Read a uint32 at a raw file offset.
    /// </summary>
    public uint ReadUInt32(int offset)
    {
        return BitConverter.ToUInt32(_data, offset);
    }

    /// <summary>
    /// Read bytes from a raw file offset.
    /// </summary>
    public byte[] ReadBytes(int offset, int length)
    {
        var result = new byte[length];
        Array.Copy(_data, offset, result, 0, Math.Min(length, _data.Length - offset));
        return result;
    }

    /// <summary>
    /// Convert a raw file offset to RVA (Relative Virtual Address).
    /// </summary>
    public uint OffsetToRVA(int offset)
    {
        foreach (var sec in Sections)
        {
            if (offset >= sec.RawDataOffset && offset < sec.RawDataOffset + sec.RawDataSize)
            {
                return (uint)(offset - sec.RawDataOffset + sec.VirtualAddress);
            }
        }
        return 0;
    }

    /// <summary>
    /// Get the code section (.text) raw data offset and size.
    /// </summary>
    public (int offset, int size) GetCodeSectionInfo()
    {
        if (TextSection == null) return (0, 0);
        return ((int)TextSection.RawDataOffset, (int)TextSection.RawDataSize);
    }

    public void Dispose()
    {
        // Nothing to dispose - _data is managed
    }
}
