using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RDPWrapTool.Core;

/// <summary>
/// Deploys an auto-analyzed INI section and guarantees the machine is never left worse
/// than before: the previous INI is backed up, the result is verified (listener + events +
/// rdpwrap log), and on failure the tool walks a diagnostic ladder and finally rolls back.
/// </summary>
public static class DeployWorkflow
{
    /// <summary>
    /// Write the analyzed section into the tool INI, deploy it, verify it, and - when
    /// verification fails - fall back to a section without SLInitHook and finally to the
    /// pre-deploy INI.
    /// </summary>
    public static DeployResult DeployAnalyzedSection(
        IniManager ini,
        RDPWrapInstaller installer,
        IReadOnlyList<string> aliases,
        string sectionContent,
        string? slInitContent,
        IEnumerable<string>? requiredPatchCodes,
        Action<string> log,
        bool enableDiagnosticLadder = true,
        bool requirePatches = true)
    {
        // ---- snapshot for rollback ----
        string? preText = ini.LoadText();
        if (string.IsNullOrEmpty(preText)) preText = null;
        ini.BackupSystem32Ini();

        // ---- make sure rdpwrap.dll diagnostics can actually be written ----
        if (preText != null)
        {
            string withLogPath = IniManager.EnsureWritableLogPath(preText);
            if (withLogPath != preText)
            {
                log($"[*] 将 [Main] LogFile 指向可写路径: {IniManager.DefaultLogPath}");
                ini.SaveText(withLogPath);
            }
        }
        IniManager.EnsureLogDirectory();

        // ---- stage 1: the full analyzed section ----
        log($"[*] 写入 section: {string.Join(" / ", aliases)}（含 SLInitHook）");
        if (!ini.AddVersionSections(aliases, sectionContent, slInitContent, requiredPatchCodes))
        {
            var fail = new DeployResult { Message = "写入 INI 失败。", Stage = "failed" };
            log("[-] " + fail.Message);
            return fail;
        }

        var r1 = installer.DeployFilesOnly(verify: true, requirePatches: requirePatches);
        r1.Stage = r1.Success ? "full" : "failed";
        if (r1.Success) return r1;

        // ---- stage 2: same code patches, but no SLInitHook ----
        if (enableDiagnosticLadder && slInitContent != null)
        {
            log("[!] 完整 section 校验失败，尝试不带 SLInitHook 的安全变体（只保留三个代码补丁）...");
            ini.RemoveVersionSections(aliases);
            string safeSection = StripSlInitHook(sectionContent);
            if (ini.AddVersionSections(aliases, safeSection, null, requiredPatchCodes))
            {
                var r2 = installer.DeployFilesOnly(verify: true, requirePatches: false);
                if (r2.Success)
                {
                    r2.Stage = "no-slinit-hook";
                    r2.Message = "完整 section 会让 RDP 监听失效，已回退为不带 SLInitHook 的配置（代码补丁保留）。" +
                                 "多会话可能仍受策略限制，但远程桌面可正常使用。";
                    log("[!] " + r2.Message);
                    return r2;
                }
                log("[-] 安全变体同样无法建立监听。");
            }
        }

        // ---- stage 3: roll back to the pre-deploy INI ----
        log("[!] 回滚到部署前的 INI...");
        if (preText != null) ini.SaveText(IniManager.EnsureWritableLogPath(preText));
        var r3 = installer.DeployFilesOnly(verify: true, requirePatches: false);
        if (r3.Success)
        {
            r3.Stage = "rolled-back";
            r3.Message = "部署后校验未通过，已自动回滚到部署前的 INI，远程桌面监听已恢复。";
            log("[+] " + r3.Message);
        }
        else
        {
            r3.Stage = "failed";
            r3.Message = "部署后校验未通过，且回滚后 RDP 监听仍未恢复，请立即人工处理（卸载 RDPWrap 或恢复系统备份）。";
            log("[-] " + r3.Message);
        }
        return r3;
    }

    /// <summary>
    /// Remove SLInitHook.x64 / SLInitOffset.x64 / SLInitFunc.x64 from a generated section
    /// body. Without SLInitHook rdpwrap.dll never replaces CSLQuery::Initialize, so a wrong
    /// data block cannot break termsrv startup.
    /// </summary>
    public static string StripSlInitHook(string sectionContent)
    {
        var sb = new StringBuilder();
        foreach (var line in sectionContent.Replace("\r\n", "\n").Split('\n'))
        {
            string t = line.TrimStart();
            if (t.StartsWith("SLInitHook.", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("SLInitOffset.", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("SLInitFunc.", StringComparison.OrdinalIgnoreCase))
                continue;
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }
}
