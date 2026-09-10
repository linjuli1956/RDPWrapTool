using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RDPWrapTool.Core;

/// <summary>
/// Outcome of a deploy attempt. Success means "deployed AND verified working", never
/// "the SCM reported Running" - that was the bug that left this machine without an RDP
/// listener while the UI said "部署成功，补丁已生效".
/// </summary>
public sealed class DeployResult
{
    /// <summary>Deployed and verified (listener up, no RDS startup failure, patches present in the DLL log).</summary>
    public bool Success;
    public bool FilesDeployed;

    /// <summary>3389 is in the TCP listen state.</summary>
    public bool PortListening;

    /// <summary>TerminalServices logged "listener RDP-Tcp started listening" (event 258) after the restart.</summary>
    public bool ListenerEvent;

    /// <summary>TerminalServices logged "Remote Desktop Services failed to start" (event 17) after the restart.</summary>
    public bool StartupFailed;

    /// <summary>rdpwrap.dll log shows the three code patches were written and read back.</summary>
    public bool PatchesApplied;

    /// <summary>rdpwrap.dll log shows the SLInit data writes happened without exception.</summary>
    public bool SlInitWritten;

    /// <summary>
    /// full            - analyzed section deployed and verified
    /// no-slinit-hook  - fell back to a section without SLInitHook (code patches only)
    /// rolled-back     - verification failed, previous INI restored
    /// failed          - verification failed and rollback also failed
    /// </summary>
    public string Stage = "failed";

    public string Message = "";
    public List<string> Evidence { get; } = new();

    public string Describe() =>
        $"Stage={Stage} Success={Success} Files={FilesDeployed} Listen={PortListening} " +
        $"Evt258={ListenerEvent} Evt17={StartupFailed} Patches={PatchesApplied} SLInit={SlInitWritten}";
}

/// <summary>Collects the independent signals that decide whether RDP actually works.</summary>
public static class DeployVerifier
{
    /// <summary>
    /// Verify the RDP state after a deploy/restart.
    /// </summary>
    /// <param name="since">Local time just before the service was restarted.</param>
    /// <param name="requirePatches">When true, the rdpwrap log must show the patches were applied.</param>
    public static DeployResult Verify(DateTime since, bool requirePatches = true, Action<string>? log = null)
    {
        var res = new DeployResult();

        bool running = ServiceManager.GetServiceStatus(ServiceManager.TermServiceName)
            == System.ServiceProcess.ServiceControllerStatus.Running;
        res.Evidence.Add($"TermService Running = {running}");

        if (running)
            ServiceManager.WaitServiceReady(20000);

        res.PortListening = ServiceManager.IsRdpPortListening();
        res.Evidence.Add($"3389 listening = {res.PortListening}");
        if (res.PortListening)
            res.Evidence.Add($"TCP 127.0.0.2:3389 connect = {ServiceManager.CanConnectRdp()}");

        var ev = ServiceManager.GetTsStartupEvidence(since);
        // The listener-start event (258) lands a moment after the port opens; poll briefly
        // so a healthy start is not reported as "no evidence".
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!ev.ListenerStarted && !ev.StartupFailed && sw.ElapsedMilliseconds < 6000)
        {
            System.Threading.Thread.Sleep(1000);
            ev = ServiceManager.GetTsStartupEvidence(since);
        }
        res.ListenerEvent = ev.ListenerStarted;
        res.StartupFailed = ev.StartupFailed;
        res.Evidence.AddRange(ev.Messages);
        res.Evidence.AddRange(ev.Notes.Select(n => "[!] " + n));
        if (!ev.ListenerStarted && !ev.StartupFailed)
            res.Evidence.Add("[i] 重启后既没有 258 也没有 17（通道读取可能不可用）");

        // rdpwrap.dll log: the DLL logs the original/patch/readback bytes of every patch site
        // and every SLInit direct write. Only usable when [Main] LogFile points somewhere
        // the service account can write - which the deploy step now guarantees.
        var logText = ReadRdpWrapLog();
        if (logText != null)
        {
            var lines = logText.Split('\n');
            res.PatchesApplied = new[]
            {
                "LocalOnlyPatch", "SingleUserPatch", "DefPolicyPatch"
            }.All(p => lines.Any(l => l.Contains($"PatchFunc: {p}") && l.Contains("SignPtr=")))
              && !lines.Any(l => l.Contains("SKIP (SignPtr<=Base") || l.Contains("patchCode name not found"));

            bool slInitHook = lines.Any(l => l.Contains("SLInitHook: Readback:"));
            bool slInitDirect = lines.Any(l => Regex.IsMatch(l, @"SLInitDirect: bInitialized offset=0x[1-9A-Fa-f]"));
            bool slInitException = lines.Any(l => l.Contains("SLInitDirect: EXCEPTION")) ||
                                   lines.Any(l => l.Contains("SLInitDirect: Section not found"));
            res.SlInitWritten = (slInitHook || slInitDirect) && !slInitException;

            var tail = lines.Where(l => l.Trim().Length > 0).Reverse().Take(25).Reverse().ToList();
            res.Evidence.Add($"rdpwrap 日志: patches={res.PatchesApplied} slInit={res.SlInitWritten}");
            res.Evidence.AddRange(tail.Select(t => "  | " + t.Trim()));
        }
        else
        {
            res.Evidence.Add("[!] 未找到 rdpwrap 日志（[Main] LogFile 指向的位置不可写）");
        }

        res.Success = running
                      && res.PortListening
                      && !res.StartupFailed
                      && (!requirePatches || (res.PatchesApplied && res.SlInitWritten));
        res.Stage = res.Success ? "verified" : "failed";
        if (res.Message.Length == 0)
            res.Message = res.Success ? "RDP 状态校验通过。" : "RDP 状态校验未通过。";

        if (log != null)
            foreach (var line in res.Evidence) log(line);
        return res;
    }

    /// <summary>Path of the rdpwrap.dl log, matching [Main] LogFile as deployed.</summary>
    public static string? FindRdpWrapLog()
    {
        foreach (var p in new[]
                 {
                     IniManager.DefaultLogPath,
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rdpwrap.txt"),
                     @"C:\rdpwrap.txt",
                     Path.Combine(AppContext.BaseDirectory, "rdpwrap.txt")
                 })
        {
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static string? ReadRdpWrapLog()
    {
        var path = FindRdpWrapLog();
        if (path == null) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }
}
