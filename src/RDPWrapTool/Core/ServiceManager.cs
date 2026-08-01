using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;

namespace RDPWrapTool.Core;

/// <summary>
/// Manages Windows services, specifically TermService and its dependencies.
/// </summary>
public class ServiceManager
{
    public const string TermServiceName = "TermService";
    public const string CertPropSvcName = "CertPropSvc";
    public const string SessionEnvName = "SessionEnv";

    #region P/Invoke for SCM API

    [StructLayout(LayoutKind.Sequential)]
    internal struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr OpenSCManagerW(string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryServiceStatusEx(IntPtr hService, int infoLevel,
        ref SERVICE_STATUS_PROCESS lpBuffer, int cbBufSize, out uint pcbBytesNeeded);

    internal const int SC_STATUS_PROCESS_INFO = 0;
    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SERVICE_QUERY_STATUS = 0x0004;
    internal const uint SERVICE_START = 0x0010;
    internal const uint SERVICE_STOP = 0x0020;
    internal const uint SERVICE_ENUMERATE_DEPENDENTS = 0x0008;
    internal const uint SERVICE_ALL_ACCESS = 0xF01FF;

    #endregion

    /// <summary>
    /// Get the current status of a service.
    /// </summary>
    public static ServiceControllerStatus? GetServiceStatus(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            sc.Refresh();
            return sc.Status;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get the process ID of a service.
    /// </summary>
    public static uint? GetServiceProcessId(string serviceName)
    {
        IntPtr hSCM = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (hSCM == IntPtr.Zero) return null;

        try
        {
            IntPtr hSvc = OpenServiceW(hSCM, serviceName, SERVICE_QUERY_STATUS);
            if (hSvc == IntPtr.Zero) return null;

            try
            {
                var status = new SERVICE_STATUS_PROCESS();
                if (QueryServiceStatusEx(hSvc, SC_STATUS_PROCESS_INFO, ref status,
                    Marshal.SizeOf<SERVICE_STATUS_PROCESS>(), out _))
                {
                    return status.dwProcessId;
                }
                return null;
            }
            finally
            {
                CloseServiceHandle(hSvc);
            }
        }
        finally
        {
            CloseServiceHandle(hSCM);
        }
    }

    /// <summary>
    /// Start a service. Tries ServiceController first, then sc.exe as fallback.
    /// </summary>
    public static bool StartService(string serviceName, int timeoutMs = 30000)
    {
        // Strategy 1: Use ServiceController
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Running)
                return true;
            if (sc.Status == ServiceControllerStatus.StartPending)
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMilliseconds(timeoutMs));
                return sc.Status == ServiceControllerStatus.Running;
            }
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMilliseconds(timeoutMs));
            if (sc.Status == ServiceControllerStatus.Running)
                return true;
        }
        catch (Exception ex)
        {
            LogMessage($"StartService({serviceName}) ServiceController error: {ex.Message}");
            // If it's a Win32Exception, log the native error code
            if (ex is Win32Exception winEx)
                LogMessage($"  Win32 Error Code: {winEx.NativeErrorCode} (0x{winEx.NativeErrorCode:X})");
        }

        // Strategy 2: Use sc.exe as fallback
        try
        {
            LogMessage($"[*] 尝试使用 sc.exe 启动 {serviceName}...");
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"start {serviceName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var p = Process.Start(psi);
            if (p != null)
            {
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                if (!string.IsNullOrEmpty(output))
                    LogMessage($"  sc.exe output: {output.Trim()}");
                if (!string.IsNullOrEmpty(error))
                    LogMessage($"  sc.exe error: {error.Trim()}");
                if (p.ExitCode == 0)
                {
                    System.Threading.Thread.Sleep(3000);
                    return GetServiceStatus(serviceName) == ServiceControllerStatus.Running;
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage($"sc.exe start error: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Stop a service. Tries ServiceController first, then kills the process.
    /// </summary>
    public static bool StopService(string serviceName, int timeoutMs = 15000)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status == ServiceControllerStatus.Stopped)
                return true;
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMilliseconds(timeoutMs));
            if (sc.Status == ServiceControllerStatus.Stopped)
                return true;
        }
        catch (Exception ex)
        {
            LogMessage($"StopService({serviceName}) error: {ex.Message}");
        }

        // Fallback: kill the process
        LogMessage($"[*] 尝试强制终止 {serviceName} 进程...");
        KillTermService();
        System.Threading.Thread.Sleep(2000);
        return GetServiceStatus(serviceName) == ServiceControllerStatus.Stopped;
    }

    /// <summary>
    /// Restart a service. For TermService, stops the dependent UmRdpService first:
    /// TermService's stop wait-hint can be up to 60s while UmRdpService holds it.
    /// </summary>
    public static bool RestartService(string serviceName, int timeoutMs = 20000)
    {
        if (serviceName.Equals(TermServiceName, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var um = new ServiceController("UmRdpService");
                if (um.Status == ServiceControllerStatus.Running)
                {
                    LogMessage("[*] 先停止依赖服务 UmRdpService...");
                    um.Stop();
                    um.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMilliseconds(8000));
                }
            }
            catch { /* best effort */ }
        }
        StopService(serviceName, timeoutMs);
        System.Threading.Thread.Sleep(1000);
        return StartService(serviceName, timeoutMs);
    }

    /// <summary>
    /// Kill a process by PID.
    /// </summary>
    public static bool KillProcess(uint pid)
    {
        try
        {
            var proc = Process.GetProcessById((int)pid);
            proc.Kill();
            proc.WaitForExit(5000);
            return true;
        }
        catch (Exception ex)
        {
            LogMessage($"KillProcess({pid}) error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Kill the TermService process.
    /// </summary>
    public static bool KillTermService()
    {
        var pid = GetServiceProcessId(TermServiceName);
        if (pid == null || pid == 0)
        {
            LogMessage("TermService PID not found or is 0.");
            return false;
        }
        return KillProcess(pid.Value);
    }

    /// <summary>
    /// Check and fix service dependencies for TermService.
    /// </summary>
    public static void EnsureDependencies()
    {
        EnsureServiceStartType(CertPropSvcName);
        EnsureServiceStartType(SessionEnvName);
    }

    private static void EnsureServiceStartType(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            // Just check it's not disabled
            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                try { sc.Start(); } catch { }
            }
        }
        catch (Exception ex)
        {
            LogMessage($"EnsureServiceStartType({serviceName}): {ex.Message}");
        }
    }

    /// <summary>
    /// Set service start type (automatic, manual, disabled).
    /// </summary>
    public static bool SetServiceStartType(string serviceName, ServiceStartMode startType)
    {
        try
        {
            // Use sc.exe for reliability
            string mode = startType switch
            {
                ServiceStartMode.Automatic => "auto",
                ServiceStartMode.Manual => "demand",
                ServiceStartMode.Disabled => "disabled",
                _ => "demand"
            };
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config {serviceName} start= {mode}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var p = Process.Start(psi);
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            LogMessage($"SetServiceStartType({serviceName}) error: {ex.Message}");
            return false;
        }
    }

    public static event Action<string>? OnLog;
    private static void LogMessage(string msg) => OnLog?.Invoke(msg);

    /// <summary>
    /// Diagnose why TermService failed to start. Checks rdpwrap.txt log, event log, DLL architecture.
    /// </summary>
    public static void DiagnoseTermServiceFailure()
    {
        LogMessage("\n========== 诊断信息 ==========");

        // 1. Check service status
        try
        {
            using var sc = new ServiceController(TermServiceName);
            LogMessage($"[*] 服务状态: {sc.Status}");
            LogMessage($"[*] 服务启动类型: {sc.StartType}");
        }
        catch (Exception ex)
        {
            LogMessage($"[-] 无法获取服务状态: {ex.Message}");
        }

        // 2. Check rdpwrap.txt log (created by rdpwrap.dll)
        string[] logPaths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rdpwrap.txt"),
            Path.Combine(AppContext.BaseDirectory, "rdpwrap.txt")
        };
        foreach (var logPath in logPaths)
        {
            if (File.Exists(logPath))
            {
                LogMessage($"[*] 找到 RDPWrap 日志: {logPath}");
                try
                {
                    var lines = File.ReadAllLines(logPath);
                    var lastLines = lines.Length > 30 ? lines.Skip(lines.Length - 30).ToArray() : lines;
                    LogMessage("--- rdpwrap.txt (最后30行) ---");
                    foreach (var line in lastLines)
                        LogMessage($"  {line}");
                }
                catch { }
            }
        }
        if (!logPaths.Any(File.Exists))
            LogMessage("[-] 未找到 rdpwrap.txt 日志文件 (DLL可能未被加载)");

        // 3. Check rdpwrap.dll exists and architecture
        string dllPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rdpwrap.dll");
        if (File.Exists(dllPath))
        {
            LogMessage($"[*] rdpwrap.dll 存在: {dllPath}");
            try
            {
                var bytes = File.ReadAllBytes(dllPath);
                int peOff = BitConverter.ToInt32(bytes, 0x3C);
                ushort machine = BitConverter.ToUInt16(bytes, peOff + 4);
                string arch = machine switch
                {
                    0x8664 => "x64 (AMD64)",
                    0x14C => "x86 (i386) - 错误! 需要x64",
                    _ => $"未知 (0x{machine:X4})"
                };
                LogMessage($"[*] rdpwrap.dll 架构: {arch}");
            }
            catch (Exception ex)
            {
                LogMessage($"[-] 无法读取DLL架构: {ex.Message}");
            }
        }
        else
        {
            LogMessage("[-] rdpwrap.dll 不存在于 System32 中!");
        }

        // 4. Check rdpwrap.ini exists
        string iniPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rdpwrap.ini");
        if (File.Exists(iniPath))
            LogMessage($"[*] rdpwrap.ini 存在: {iniPath}");
        else
            LogMessage("[-] rdpwrap.ini 不存在于 System32 中!");

        // 5. Check event log for TermService errors
        try
        {
            LogMessage("[*] 检查系统事件日志...");
            var events = new EventLog("System");
            var entries = events.Entries.Cast<EventLogEntry>()
                .Where(e => (e.Source == "Service Control Manager" || e.Source == "TermService")
                           && e.EntryType == EventLogEntryType.Error
                           && e.TimeGenerated > DateTime.Now.AddMinutes(-10))
                .Take(5);
            foreach (var entry in entries)
            {
                LogMessage($"  [{entry.TimeGenerated:HH:mm:ss}] {entry.Source}: {entry.Message.Substring(0, Math.Min(200, entry.Message.Length))}");
            }
        }
        catch (Exception ex)
        {
            LogMessage($"[-] 无法读取事件日志: {ex.Message}");
        }

        // 6. Check service dependencies
        try
        {
            using var sc = new ServiceController(TermServiceName);
            var deps = sc.ServicesDependedOn;
            foreach (var dep in deps)
            {
                LogMessage($"[*] 依赖服务 {dep.ServiceName}: {dep.Status}");
            }
        }
        catch { }

        // 7. Use sc.exe to get detailed service info
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "query TermService",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            var p = Process.Start(psi);
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                LogMessage($"[*] sc query TermService:\n{output.Trim()}");
            }
        }
        catch { }

        LogMessage("========== 诊断结束 ==========\n");
    }
}
