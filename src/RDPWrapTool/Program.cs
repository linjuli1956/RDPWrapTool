using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RDPWrapTool.Core;

namespace RDPWrapTool;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    static int Main(string[] args)
    {
        // CLI mode for headless testing / scripting:
        //   RDPWrapTool.exe --analyze <termsrv.dll> [--write <rdpwrap.ini>]
        if (args.Length >= 2 && args[0].Equals("--analyze", StringComparison.OrdinalIgnoreCase))
        {
            AttachConsole(-1); // WinExe has no console; attach to the caller's one
            return RunAnalyzeCli(args);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Forms.MainForm());
        return 0;
    }

    private static int RunAnalyzeCli(string[] args)
    {
        string dllPath = args[1];
        string? iniPath = null;
        string outPath = Path.Combine(AppContext.BaseDirectory, "analyze_output.txt");
        for (int i = 2; i + 1 < args.Length; i++)
        {
            if (args[i].Equals("--write", StringComparison.OrdinalIgnoreCase))
                iniPath = args[i + 1];
            if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase))
                outPath = args[i + 1];
        }

        // WinExe is detached from the caller's console; tee everything to a file.
        var log = new System.Text.StringBuilder();
        void Out(string msg) { Console.WriteLine(msg); log.AppendLine(msg); }

        var analyzer = new TermSrvAnalyzer();
        analyzer.OnLog += Out;
        var result = analyzer.AnalyzeEx(dllPath);

        Out("");
        Out("================ VERDICT ================");
        Out(result.Success ? "SUCCESS - all four patch points byte-verified." : "FAILED - strict verification did not pass.");

        int exitCode = result.Success ? 0 : 1;
        if (result.Success)
        {
            Out("");
            Out("--- Generated INI sections ---");
            Out(analyzer.GenerateIniSection(result));

            if (iniPath != null)
            {
                var ini = new IniManager();
                ini.SetIniPath(iniPath);
                ini.OnLog += Out;
                var (sectionContent, slInitContent) = analyzer.BuildSectionBodies(result);
                bool ok = ini.AddVersionSections(result.Aliases, sectionContent, slInitContent, analyzer.GetRequiredPatchCodes(result));
                Out(ok ? $"[+] written into {iniPath}" : "[-] write failed");
                if (!ok) exitCode = 2;
            }
        }

        try { File.WriteAllText(outPath, log.ToString()); } catch { }
        return exitCode;
    }
}
