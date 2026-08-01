using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RDPWrapTool.Core;

/// <summary>
/// Handles installation and uninstallation of RDPWrap.
/// </summary>
public class RDPWrapInstaller
{
    private const string TermServiceRegKey = @"SYSTEM\CurrentControlSet\Services\TermService";
    private const string TermServiceParamsRegKey = @"SYSTEM\CurrentControlSet\Services\TermService\Parameters";
    private const string TerminalServerRegKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
    private const string LicensingCoreRegKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server\Licensing Core";
    private const string WinlogonRegKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string TerminalServerAddInsRegKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server\AddIns";

    private const string DefaultTermsrvDll = @"%SystemRoot%\System32\termsrv.dll";
    private readonly string _system32Dir;
    private readonly string _toolDir;

    public bool Is64Bit { get; private set; }
    public bool IsInstalled { get; private set; }
    public string? InstalledDllPath { get; private set; }
    /// <summary>True if a third-party wrapper (not rdpwrap.dll, not termsrv.dll) is detected.</summary>
    public bool ThirdPartyWrapperDetected { get; private set; }

    public event Action<string>? OnLog;
    private void Log(string msg) => OnLog?.Invoke(msg);

    public RDPWrapInstaller()
    {
        AssetManager.EnsureAssets();
        _system32Dir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        _toolDir = AssetManager.ToolDataDir;
        Is64Bit = Environment.Is64BitOperatingSystem;
    }

    /// <summary>
    /// Check if the system architecture is supported (x64 only).
    /// </summary>
    public bool CheckArchitecture()
    {
        Log($"[*] System architecture: {(Is64Bit ? "x64" : "x86")} - {(Is64Bit ? "Supported" : "NOT supported")}");
        return Is64Bit;
    }

    /// <summary>
    /// Check if RDPWrap is currently installed.
    /// Also detects third-party wrappers like SuperRDP.
    /// </summary>
    public bool CheckInstall()
    {
        ThirdPartyWrapperDetected = false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(TermServiceParamsRegKey, false);
            if (key == null)
            {
                Log("[-] Cannot open TermService Parameters registry key.");
                IsInstalled = false;
                return false;
            }

            var serviceDll = key.GetValue("ServiceDll") as string;
            if (string.IsNullOrEmpty(serviceDll))
            {
                Log("[-] ServiceDll registry value not found.");
                IsInstalled = false;
                return false;
            }

            InstalledDllPath = serviceDll;
            string lower = serviceDll.ToLowerInvariant();
            IsInstalled = lower.Contains("rdpwrap.dll");

            // Detect third-party wrappers (not termsrv.dll and not rdpwrap.dll)
            bool isOriginal = lower.Contains("termsrv.dll");
            ThirdPartyWrapperDetected = !IsInstalled && !isOriginal;

            Log($"[*] ServiceDll: {serviceDll}");
            Log($"[*] RDPWrap installed: {(IsInstalled ? "Yes" : "No")}");
            if (ThirdPartyWrapperDetected)
            {
                Log($"[!] 检测到第三方 RDP 包装器: {serviceDll}");
                Log("[!] 安装时将自动清理第三方包装器。");
            }
            return true;
        }
        catch (Exception ex)
        {
            Log($"[-] CheckInstall error: {ex.Message}");
            IsInstalled = false;
            return false;
        }
    }

    /// <summary>
    /// Get the version of termsrv.dll.
    /// </summary>
    public static Version? GetTermsrvVersion()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "termsrv.dll");
        return GetFileVersion(path);
    }

    /// <summary>
    /// Get file version of a DLL/EXE.
    /// </summary>
    public static Version? GetFileVersion(string filePath)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(filePath);
            return new Version(vi.FileMajorPart, vi.FileMinorPart, vi.FileBuildPart, vi.FilePrivatePart);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Install RDPWrap.
    /// </summary>
    public bool Install()
    {
        if (!CheckArchitecture())
        {
            Log("[-] Unsupported architecture. Only x64 is supported.");
            return false;
        }

        CheckInstall();
        if (IsInstalled)
        {
            Log("[*] RDPWrap is already installed. Please uninstall first.");
            return false;
        }

        // If a third-party wrapper is detected, clean it up first
        if (ThirdPartyWrapperDetected && !string.IsNullOrEmpty(InstalledDllPath))
        {
            Log($"[*] 清理第三方包装器: {InstalledDllPath}");
            // Stop service first to release the DLL
            Log("[*] Stopping TermService (清理第三方包装器)...");
            ServiceManager.StopService(ServiceManager.TermServiceName);
            System.Threading.Thread.Sleep(2000);

            // Reset ServiceDll to original termsrv.dll first
            Log("[*] Resetting ServiceDll to termsrv.dll...");
            ResetWrapperDll();

            // Try to delete the third-party DLL
            try
            {
                if (File.Exists(InstalledDllPath))
                {
                    File.Delete(InstalledDllPath);
                    Log($"[+] 已删除第三方包装器: {InstalledDllPath}");
                }
            }
            catch (Exception ex)
            {
                Log($"[!] 无法删除第三方包装器: {ex.Message} (将继续安装)");
            }

            // Also clean up superrdp.ini if it exists
            string superrdpIni = Path.Combine(_system32Dir, "superrdp.ini");
            if (File.Exists(superrdpIni))
            {
                try { File.Delete(superrdpIni); Log("[+] 已删除 superrdp.ini"); } catch { }
            }
        }

        string srcDll = Path.Combine(_toolDir, "rdpwrap.dll");
        string srcIni = Path.Combine(_toolDir, "rdpwrap.ini");
        string dstDll = Path.Combine(_system32Dir, "rdpwrap.dll");
        string dstIni = Path.Combine(_system32Dir, "rdpwrap.ini");

        if (!File.Exists(srcDll))
        {
            Log($"[-] rdpwrap.dll not found in tool directory: {srcDll}");
            return false;
        }

        if (!File.Exists(srcIni))
        {
            Log($"[-] rdpwrap.ini not found in tool directory: {srcIni}");
            return false;
        }

        // Check termsrv version support
        var ver = GetTermsrvVersion();
        if (ver != null)
        {
            Log($"[+] termsrv.dll version: {ver}");
            if (!IniManager.CheckVersionSupported(srcIni, ver.ToString()))
            {
                Log("[!] Current termsrv.dll version may not be in INI. Auto-analysis recommended.");
            }
        }

        try
        {
            // Stop TermService BEFORE copying files (to release any locked DLLs)
            Log("[*] Stopping TermService...");
            ServiceManager.StopService(ServiceManager.TermServiceName);
            System.Threading.Thread.Sleep(1000);

            // Copy DLL and INI to System32
            Log("[*] Copying rdpwrap.dll to System32...");
            File.Copy(srcDll, dstDll, true);
            Log("[*] Copying rdpwrap.ini to System32...");
            File.Copy(srcIni, dstIni, true);

            // Set ServiceDll in registry
            Log("[*] Setting ServiceDll registry...");
            SetWrapperDll(dstDll);

            // Ensure dependencies
            Log("[*] Checking service dependencies...");
            ServiceManager.EnsureDependencies();

            // Start TermService
            Log("[*] Starting TermService...");
            bool started = ServiceManager.StartService(ServiceManager.TermServiceName);
            if (!started)
            {
                Log("[-] TermService 启动失败！");
                ServiceManager.DiagnoseTermServiceFailure();
            }

            EnableRemoteDesktop();

            if (started)
            {
                Log("[+] RDPWrap installed successfully!");
            }
            else
            {
                Log("[!] RDPWrap 文件已安装，但 TermService 未能启动。请查看诊断信息。");
            }
            CheckInstall();
            return started;
        }
        catch (Exception ex)
        {
            Log($"[-] Install error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deploy/update rdpwrap.dll and rdpwrap.ini to System32 without full install.
    /// Use when RDPWrap is already installed and you just need to push updated files
    /// (e.g. after auto-analysis added a new version section to the local INI).
    /// Stops TermService, copies both files, ensures ServiceDll registry, restarts.
    /// </summary>
    public bool DeployFilesOnly()
    {
        string srcDll = Path.Combine(_toolDir, "rdpwrap.dll");
        string srcIni = Path.Combine(_toolDir, "rdpwrap.ini");
        string dstDll = Path.Combine(_system32Dir, "rdpwrap.dll");
        string dstIni = Path.Combine(_system32Dir, "rdpwrap.ini");

        if (!File.Exists(srcDll))
        {
            Log($"[-] rdpwrap.dll not found in tool directory: {srcDll}");
            return false;
        }
        if (!File.Exists(srcIni))
        {
            Log($"[-] rdpwrap.ini not found in tool directory: {srcIni}");
            return false;
        }

        try
        {
            Log("[*] 正在停止 TermService...");
            ServiceManager.StopService(ServiceManager.TermServiceName);
            System.Threading.Thread.Sleep(1000);

            Log("[*] 正在部署 rdpwrap.dll 到 System32...");
            File.Copy(srcDll, dstDll, true);
            Log("[*] 正在部署 rdpwrap.ini 到 System32...");
            File.Copy(srcIni, dstIni, true);

            // Ensure ServiceDll registry points to rdpwrap.dll
            Log("[*] 确认 ServiceDll 注册表...");
            SetWrapperDll(dstDll);

            Log("[*] 正在启动 TermService...");
            bool started = ServiceManager.StartService(ServiceManager.TermServiceName);
            if (!started)
            {
                Log("[-] TermService 启动失败！");
                ServiceManager.DiagnoseTermServiceFailure();
            }
            else
            {
                Log("[+] 部署完成，TermService 已重启。");
            }
            return started;
        }
        catch (Exception ex)
        {
            Log($"[-] Deploy error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enable Windows Remote Desktop at the OS level.
    /// This is separate from RDPWrap: Windows itself must allow RDP connections,
    /// the firewall must allow TCP 3389, and TermService must be startable.
    /// </summary>
    public bool EnableRemoteDesktop()
    {
        try
        {
            Log("[*] 正在启用 Windows 远程桌面...");
            TSConfigRegistry(true);
            TSConfigFirewall(true);
            ServiceManager.SetServiceStartType(ServiceManager.TermServiceName, System.ServiceProcess.ServiceStartMode.Manual);
            ServiceManager.EnsureDependencies();

            bool started = true;
            if (ServiceManager.GetServiceStatus(ServiceManager.TermServiceName) != System.ServiceProcess.ServiceControllerStatus.Running)
                started = ServiceManager.StartService(ServiceManager.TermServiceName);

            if (started)
            {
                Log("[+] Windows 远程桌面已启用。");
                return true;
            }

            Log("[!] Windows 远程桌面开关已启用，但 TermService 未能启动。");
            ServiceManager.DiagnoseTermServiceFailure();
            return false;
        }
        catch (Exception ex)
        {
            Log($"[-] 启用 Windows 远程桌面失败: {ex.Message}");
            return false;
        }
    }

    public static bool IsRemoteDesktopEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(TerminalServerRegKey, false);
            var value = key?.GetValue("fDenyTSConnections");
            return value is int deny && deny == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Uninstall RDPWrap.
    /// </summary>
    public bool Uninstall()
    {
        CheckInstall();
        if (!IsInstalled && !ThirdPartyWrapperDetected)
        {
            Log("[*] RDPWrap is not installed.");
            return false;
        }

        // If third-party wrapper detected, warn but proceed with cleanup
        if (ThirdPartyWrapperDetected && !IsInstalled)
        {
            Log($"[!] 检测到第三方包装器: {InstalledDllPath}");
            Log("[*] 将清理第三方包装器并恢复原始 termsrv.dll");
        }

        try
        {
            // Reset ServiceDll to original termsrv.dll
            Log("[*] Resetting ServiceDll registry...");
            ResetWrapperDll();

            // Kill TermService process
            Log("[*] Stopping TermService...");
            ServiceManager.StopService(ServiceManager.TermServiceName);
            System.Threading.Thread.Sleep(1000);

            // Delete files
            string dstDll = Path.Combine(_system32Dir, "rdpwrap.dll");
            string dstIni = Path.Combine(_system32Dir, "rdpwrap.ini");
            Log("[*] Deleting rdpwrap.dll...");
            DeleteFileWithRetry(dstDll);
            Log("[*] Deleting rdpwrap.ini...");
            DeleteFileWithRetry(dstIni);

            // Also delete third-party wrapper DLL if detected
            if (ThirdPartyWrapperDetected && !string.IsNullOrEmpty(InstalledDllPath))
            {
                Log($"[*] Deleting third-party wrapper: {InstalledDllPath}...");
                DeleteFileWithRetry(InstalledDllPath);
                string superrdpIni = Path.Combine(_system32Dir, "superrdp.ini");
                if (File.Exists(superrdpIni)) DeleteFileWithRetry(superrdpIni);
            }

            // Restart TermService
            Log("[*] Starting TermService...");
            bool uninstalledStarted = ServiceManager.StartService(ServiceManager.TermServiceName);
            if (!uninstalledStarted)
            {
                Log("[-] TermService 启动失败 (卸载后)！");
                ServiceManager.DiagnoseTermServiceFailure();
            }

            // Restore registry
            Log("[*] Restoring Terminal Server registry...");
            TSConfigRegistry(false);

            // Remove firewall rule
            Log("[*] Removing firewall rule...");
            TSConfigFirewall(false);

            Log("[+] RDPWrap uninstalled successfully!");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[-] Uninstall error: {ex.Message}");
            return false;
        }
    }

    private void SetWrapperDll(string dllPath)
    {
        using var key = Registry.LocalMachine.OpenSubKey(TermServiceParamsRegKey, true);
        if (key == null)
        {
            // Create the key
            using var newKey = Registry.LocalMachine.CreateSubKey(TermServiceParamsRegKey, true);
            newKey?.SetValue("ServiceDll", dllPath, RegistryValueKind.ExpandString);
        }
        else
        {
            key.SetValue("ServiceDll", dllPath, RegistryValueKind.ExpandString);
        }
    }

    private void ResetWrapperDll()
    {
        using var key = Registry.LocalMachine.OpenSubKey(TermServiceParamsRegKey, true);
        key?.SetValue("ServiceDll", DefaultTermsrvDll, RegistryValueKind.ExpandString);
    }

    /// <summary>
    /// Configure Terminal Server registry settings.
    /// </summary>
    public void TSConfigRegistry(bool enable)
    {
        // fDenyTSConnections
        using (var key = Registry.LocalMachine.OpenSubKey(TerminalServerRegKey, true))
        {
            key?.SetValue("fDenyTSConnections", enable ? 0 : 1, RegistryValueKind.DWord);
        }

        if (enable)
        {
            // EnableConcurrentSessions
            using var licKey = Registry.LocalMachine.CreateSubKey(LicensingCoreRegKey, true);
            licKey?.SetValue("EnableConcurrentSessions", 1, RegistryValueKind.DWord);

            // AllowMultipleTSSessions
            using var wlKey = Registry.LocalMachine.OpenSubKey(WinlogonRegKey, true);
            wlKey?.SetValue("AllowMultipleTSSessions", 1, RegistryValueKind.DWord);

            // AddIns for terminal services
            ConfigureAddIns(true);
        }
        else
        {
            using var licKey = Registry.LocalMachine.OpenSubKey(LicensingCoreRegKey, true);
            licKey?.SetValue("EnableConcurrentSessions", 0, RegistryValueKind.DWord);

            using var wlKey = Registry.LocalMachine.OpenSubKey(WinlogonRegKey, true);
            wlKey?.SetValue("AllowMultipleTSSessions", 0, RegistryValueKind.DWord);

            ConfigureAddIns(false);
        }
    }

    private void ConfigureAddIns(bool enable)
    {
        string[] addIns = { "Clip Redirector", "DND Redirector", "Dynamic VC" };
        foreach (var addIn in addIns)
        {
            try
            {
                string path = $@"{TerminalServerAddInsRegKey}\{addIn}";
                using var key = Registry.LocalMachine.CreateSubKey(path, true);
                if (key != null)
                {
                    key.SetValue("Name", addIn, RegistryValueKind.String);
                    key.SetValue("Enabled", enable ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                Log($"[!] ConfigureAddIns({addIn}) 跳过: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Configure firewall for Remote Desktop.
    /// </summary>
    public void TSConfigFirewall(bool enable)
    {
        try
        {
            if (enable)
            {
                RunNetsh("advfirewall firewall set rule group=\"remote desktop\" new enable=Yes");
                RunNetsh("advfirewall firewall add rule name=\"RDPWrapTool Remote Desktop 3389\" dir=in protocol=tcp localport=3389 profile=any action=allow");
            }
            else
            {
                RunNetsh("advfirewall firewall delete rule name=\"RDPWrapTool Remote Desktop 3389\"");
            }
        }
        catch (Exception ex)
        {
            Log($"[-] Firewall config error: {ex.Message}");
        }
    }

    private static void RunNetsh(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process.Start(psi)?.WaitForExit(10000);
    }

    private void DeleteFileWithRetry(string path)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            catch
            {
                System.Threading.Thread.Sleep(1000);
            }
        }
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
