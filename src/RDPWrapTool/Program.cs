using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
        //   RDPWrapTool.exe --analyze <termsrv.dll> [--write <rdpwrap.ini>] [--out <log>]
        //                   [--no-wpp] [--emit-layout-evidence <json>]
        //   RDPWrapTool.exe --verify [--out <log>]
        //   RDPWrapTool.exe --deploy [--out <log>]      analyze + write + deploy + verify + rollback
        //   RDPWrapTool.exe --restore [--out <log>]     drop generated sections, redeploy, verify
        if (args.Length >= 1 && args[0].StartsWith("--", StringComparison.Ordinal))
        {
            AttachConsole(-1); // WinExe has no console; attach to the caller's one
            return RunCli(args);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Forms.MainForm());
        return 0;
    }

    private static string? ArgValue(string[] args, string name)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static int RunCli(string[] args)
    {
        string mode = args[0].ToLowerInvariant();
        string outPath = ArgValue(args, "--out") ?? Path.Combine(AppContext.BaseDirectory, "analyze_output.txt");

        var log = new StringBuilder();
        void Out(string msg) { Console.WriteLine(msg); log.AppendLine(msg); }

        var analyzer = new TermSrvAnalyzer { DisableWppNames = HasFlag(args, "--no-wpp") };
        analyzer.OnLog += Out;

        int exitCode;
        switch (mode)
        {
            case "--analyze":
                exitCode = RunAnalyze(args, analyzer, log, Out);
                break;
            case "--verify":
                exitCode = RunVerify(args, Out);
                break;
            case "--deploy":
                exitCode = RunDeploy(analyzer, Out);
                break;
            case "--restore":
                exitCode = RunRestore(Out);
                break;
            case "--selftest-bad-layout":
                exitCode = RunBadLayoutSelfTest(analyzer, Out);
                break;
            default:
                Out($"unknown mode {mode}");
                exitCode = 2;
                break;
        }

        try { File.WriteAllText(outPath, log.ToString()); } catch { }
        return exitCode;
    }

    private static int RunAnalyze(string[] args, TermSrvAnalyzer analyzer, StringBuilder log, Action<string> Out)
    {
        string? dllPath = args.Length >= 2 && !args[1].StartsWith("--") ? args[1] : null;
        string? iniPath = ArgValue(args, "--write");

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

            string? evidencePath = ArgValue(args, "--emit-layout-evidence");
            if (evidencePath != null)
            {
                File.WriteAllText(evidencePath, BuildEvidenceJson(result), new UTF8Encoding(false));
                Out($"[+] layout evidence written to {evidencePath}");
            }

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
        return exitCode;
    }

    private static int RunVerify(string[] args, Action<string> Out)
    {
        Out("[*] 当前 RDP 状态校验...");
        Out($"[*] TermService: {ServiceManager.GetServiceStatus(ServiceManager.TermServiceName)}");
        Out($"[*] 3389 监听: {(ServiceManager.IsRdpPortListening() ? "是" : "否")}");
        Out($"[*] TCP 127.0.0.2:3389 连接: {(ServiceManager.CanConnectRdp() ? "成功" : "失败")}");
        var logPath = DeployVerifier.FindRdpWrapLog();
        Out($"[*] rdpwrap 日志: {logPath ?? "(未找到)"}");

        var res = DeployVerifier.Verify(DateTime.Now.AddMinutes(-10), requirePatches: false, Out);
        Out("");
        Out("=========== VERIFY VERDICT ===========");
        Out(res.Describe());
        return res.PortListening ? 0 : 1;
    }

    private static int RunDeploy(TermSrvAnalyzer analyzer, Action<string> Out)
    {
        var result = analyzer.AnalyzeEx();
        if (!result.Success)
        {
            Out("[-] 分析未通过，拒绝部署。");
            return 1;
        }

        var ini = new IniManager();
        ini.OnLog += Out;
        var installer = new RDPWrapInstaller();
        installer.OnLog += Out;
        ServiceManager.OnLog += Out;

        installer.CheckInstall();
        if (!installer.IsInstalled)
        {
            Out("[*] RDPWrap 未安装，先执行安装...");
            installer.Install();
            installer.CheckInstall();
            if (!installer.IsInstalled)
            {
                Out("[-] 安装失败。");
                return 1;
            }
        }

        var (sectionContent, slInitContent) = analyzer.BuildSectionBodies(result);
        var deploy = DeployWorkflow.DeployAnalyzedSection(
            ini, installer, result.Aliases, sectionContent, slInitContent,
            analyzer.GetRequiredPatchCodes(result), Out);

        Out("");
        Out("=========== DEPLOY VERDICT ===========");
        Out(deploy.Describe());
        Out(deploy.Message);
        return deploy.Success ? 0 : 1;
    }

    private static int RunRestore(Action<string> Out)
    {
        var ini = new IniManager();
        ini.OnLog += Out;
        var installer = new RDPWrapInstaller();
        installer.OnLog += Out;
        ServiceManager.OnLog += Out;

        var ver = RDPWrapInstaller.GetTermsrvVersion();
        var aliases = ini.GetGeneratedVersions();
        if (ver != null && !aliases.Contains(ver.ToString())) aliases.Add(ver.ToString());

        ini.BackupSystem32Ini();
        Out($"[*] 移除 section: {string.Join(", ", aliases)}");
        ini.RemoveVersionSections(aliases);

        var res = installer.DeployFilesOnly(verify: true, requirePatches: false);
        Out("");
        Out("=========== RESTORE VERDICT ===========");
        Out(res.Describe());
        return res.PortListening ? 0 : 1;
    }

    /// <summary>
    /// Self test: deploy the analyzed section but with the HISTORICAL wrong SLInit data
    /// block (the pre-fix LayoutA deltas) to prove that the deploy workflow detects the
    /// broken state and recovers automatically instead of leaving RDP dead.
    /// </summary>
    private static int RunBadLayoutSelfTest(TermSrvAnalyzer analyzer, Action<string> Out)
    {
        var result = analyzer.AnalyzeEx();
        if (!result.Success)
        {
            Out("[-] 分析未通过，无法进行自检。");
            return 1;
        }

        var ini = new IniManager();
        ini.OnLog += Out;
        var installer = new RDPWrapInstaller();
        installer.OnLog += Out;
        ServiceManager.OnLog += Out;

        installer.CheckInstall();
        if (!installer.IsInstalled)
        {
            Out("[-] RDPWrap 未安装，自检需要已安装环境。");
            return 1;
        }

        var (sectionContent, slInitContent) = analyzer.BuildSectionBodies(result);
        uint baseRva = result.BInitialized;

        // Historical (broken) layout used before the fix: bRemoteConnAllowed +0x18,
        // bMultimonAllowed +0x1C, ulMaxDebugSessions +0x24, bFUSEnabled +0x28.
        var bad = new StringBuilder();
        bad.AppendLine($"bInitialized.x64      ={baseRva:X}");
        bad.AppendLine($"bServerSku.x64        ={baseRva + 0x4:X}");
        bad.AppendLine($"lMaxUserSessions.x64  ={baseRva + 0x8:X}");
        bad.AppendLine($"bAppServerAllowed.x64 ={baseRva + 0x10:X}");
        bad.AppendLine($"bRemoteConnAllowed.x64={baseRva + 0x18:X}");
        bad.AppendLine($"bMultimonAllowed.x64  ={baseRva + 0x1C:X}");
        bad.AppendLine($"ulMaxDebugSessions.x64={baseRva + 0x24:X}");
        bad.Append($"bFUSEnabled.x64       ={baseRva + 0x28:X}");

        Out("[*] 自检：故意写入历史错误的 SLInit 数据块（+18/+1C/+24/+28）");
        Out("[*] 期望：校验失败 → 回退为不带 SLInitHook 的配置或自动回滚，最终 3389 必须仍在监听。");

        var deploy = DeployWorkflow.DeployAnalyzedSection(
            ini, installer, result.Aliases, sectionContent, bad.ToString(),
            analyzer.GetRequiredPatchCodes(result), Out);

        Out("");
        Out("======== SELFTEST VERDICT (bad layout) ========");
        Out(deploy.Describe());
        Out($"final stage = {deploy.Stage}");
        Out($"3389 listening = {ServiceManager.IsRdpPortListening()}");
        return (deploy.Stage is "no-slinit-hook" or "rolled-back") && ServiceManager.IsRdpPortListening() ? 0 : 1;
    }

    private static string BuildEvidenceJson(TermSrvAnalyzer.AnalysisResult result)
    {
        var sb = new StringBuilder();
        var r = result.SlInitResolution;
        sb.AppendLine("{");
        sb.AppendLine($"  \"version\": \"{result.Version}\",");
        sb.AppendLine($"  \"slInitOffset\": \"{result.SLInitOffset:X}\",");
        sb.AppendLine($"  \"layoutDeltas\": \"{r?.LayoutString ?? ""}\",");
        sb.AppendLine($"  \"matchesKnownLayout\": {(r?.MatchesKnownLayout == true ? "true" : "false")},");
        sb.AppendLine($"  \"strategy\": \"{r?.Strategy ?? ""}\",");
        sb.AppendLine("  \"slots\": [");
        if (r != null)
        {
            var slots = r.Addresses.OrderBy(kv => kv.Value)
                .Select(kv =>
                {
                    var st = r.StoreFor(kv.Value);
                    return $"    {{ \"name\": \"{kv.Key}\", \"rva\": \"{kv.Value:X}\", " +
                           $"\"delta\": \"{kv.Value - r.Addresses["bInitialized"]:X}\", " +
                           $"\"kind\": \"{st?.Kind}\", \"storeIns\": \"0x{st?.StoreInsOffset ?? 0:X}\", " +
                           $"\"wppName\": {(st?.WppName != null ? "\"" + st.WppName + "\"" : "null")} }}";
                });
            sb.AppendLine(string.Join(",\r\n", slots));
        }
        sb.AppendLine("  ],");
        sb.AppendLine("  \"namedSlots\": [");
        if (r != null)
        {
            var named = r.NamedSlots.Select(s =>
                $"    {{ \"wppName\": \"{s.WppName}\", \"rva\": \"{s.Rva:X}\", \"kind\": \"{s.Kind}\", " +
                $"\"storeIns\": \"0x{s.StoreInsOffset:X}\", \"leaIns\": \"0x{s.WppLeaOffset:X}\" }}");
            sb.AppendLine(string.Join(",\r\n", named));
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
