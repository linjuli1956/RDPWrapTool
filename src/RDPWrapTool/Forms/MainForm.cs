using System;
using System.Windows.Forms;
using RDPWrapTool.Core;

namespace RDPWrapTool.Forms;

/// <summary>
/// RDPWrap Tool main window. This partial holds all behavior; the visual
/// layout lives in MainForm.Designer.cs. Logic is unchanged from the original
/// release, only the presentation was refactored into the Designer file.
/// </summary>
public partial class MainForm : Form
{
    private static readonly string AppVersion =
        typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    // Core modules
    private readonly RDPWrapInstaller _installer = new();
    private readonly UserManager _userManager = new();
    private readonly IniManager _iniManager = new();
    private OnlineUpdater? _onlineUpdater;
    private readonly TermSrvAnalyzer _analyzer = new();

    public MainForm()
    {
        InitializeComponent();
        _onlineUpdater = new OnlineUpdater(_iniManager.IniPath);

        // Wire up events
        _installer.OnLog += msg => AppendLog(_homeLog, msg);
        _userManager.OnLog += msg => AppendLog(_homeLog, msg);
        _iniManager.OnLog += msg => AppendLog(_homeLog, msg);
        _analyzer.OnLog += msg => AppendLog(_analysisReport, msg);
        ServiceManager.OnLog += msg => AppendLog(_homeLog, msg);
        _onlineUpdater!.OnLog += msg => AppendLog(_homeLog, msg);
    }

    private void OnTabChanged()
    {
        switch (_tabControl.SelectedIndex)
        {
            case 0: // Home
                RefreshStatus();
                break;
            case 1: // Users
                if (_allUsersList.Items.Count == 0)
                    RefreshUserLists();
                break;
            case 2: // INI
                if (string.IsNullOrEmpty(_iniEditor.Text))
                    LoadIni();
                break;
            case 3: // Analyze
                var ver = RDPWrapInstaller.GetTermsrvVersion();
                var verStr = ver?.ToString() ?? "(未知)";
                _termsrvVerLabel.Text = $"termsrv.dll 版本: {verStr}";
                break;
        }
    }

    private void MainForm_Load(object? sender, EventArgs e)
    {
        RefreshStatus();
    }

    #region Home Tab Logic

    private void RefreshStatus()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"工具版本: v{AppVersion}");
        sb.AppendLine($"操作系统: {Environment.OSVersion.VersionString}");
        sb.AppendLine($"架构: {(Environment.Is64BitOperatingSystem ? "x64 (支持)" : "x86 (不支持)")}");

        var ver = RDPWrapInstaller.GetTermsrvVersion();
        sb.AppendLine($"termsrv.dll 版本: {ver}");

        _installer.CheckInstall();
        sb.AppendLine($"RDPWrap 已安装: {(_installer.IsInstalled ? "是" : "否")}");
        if (!string.IsNullOrEmpty(_installer.InstalledDllPath))
            sb.AppendLine($"ServiceDll: {_installer.InstalledDllPath}");

        var svcStatus = ServiceManager.GetServiceStatus(ServiceManager.TermServiceName);
        sb.AppendLine($"TermService 状态: {svcStatus}");

        var (supported, verStr) = _iniManager.CheckCurrentVersionSupport();
        sb.AppendLine($"INI 支持当前版本: {(supported ? "是" : "否")}");
        if (!supported && verStr != null)
            sb.AppendLine($"  (版本 {verStr} 不在INI中, 请使用自动分析)");

        _statusBox.Text = sb.ToString();

        // Update analyze tab label
        if (_termsrvVerLabel != null)
            _termsrvVerLabel.Text = $"termsrv.dll 版本: {ver?.ToString() ?? "(未知)"}";

        // Compact header status so the current state is always visible.
        if (_statusHeaderLabel != null)
        {
            _statusHeaderLabel.Text =
                $"termsrv {ver?.ToString() ?? "?"}  ·  RDPWrap {(_installer.IsInstalled ? "已安装" : "未安装")}  ·  INI {(supported ? "支持" : "不支持")}";
        }
    }

    private void InstallRdpWrap()
    {
        _installBtn.Enabled = false;
        try
        {
            bool ok = _installer.Install();
            if (!ok)
            {
                MessageBox.Show("RDPWrap 安装可能失败。请查看日志中的诊断信息。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshStatus();
        }
        finally
        {
            _installBtn.Enabled = true;
        }
    }

    private void UninstallRdpWrap()
    {
        var result = MessageBox.Show("确定要卸载 RDPWrap 吗？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        _uninstallBtn.Enabled = false;
        try
        {
            _installer.Uninstall();
            RefreshStatus();
        }
        finally
        {
            _uninstallBtn.Enabled = true;
        }
    }

    private void RestartService()
    {
        _restartSvcBtn.Enabled = false;
        try
        {
            AppendLog(_homeLog, "[*] 正在重启 TermService...");
            ServiceManager.StopService(ServiceManager.TermServiceName);
            System.Threading.Thread.Sleep(1000);
            bool started = ServiceManager.StartService(ServiceManager.TermServiceName);
            if (started)
            {
                AppendLog(_homeLog, "[+] TermService 已重启.");
            }
            else
            {
                AppendLog(_homeLog, "[-] TermService 启动失败！正在诊断...");
                ServiceManager.DiagnoseTermServiceFailure();
                MessageBox.Show("TermService 启动失败！请查看日志中的诊断信息。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshStatus();
        }
        finally
        {
            _restartSvcBtn.Enabled = true;
        }
    }

    #endregion

    #region Users Tab Logic

    private void CreateUser()
    {
        string username = _usernameTxt.Text.Trim();
        string password = _passwordTxt.Text;
        string confirm = _confirmPwdTxt.Text;
        string fullname = _fullnameTxt.Text.Trim();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            MessageBox.Show("用户名和密码不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (password != confirm)
        {
            MessageBox.Show("两次输入的密码不一致", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_userManager.CreateUser(username, password, string.IsNullOrEmpty(fullname) ? null : fullname))
        {
            _usernameTxt.Clear();
            _passwordTxt.Clear();
            _confirmPwdTxt.Clear();
            _fullnameTxt.Clear();
            RefreshUserLists();
        }
    }

    private void ChangePassword()
    {
        string? username = _allUsersList.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(username))
        {
            MessageBox.Show("请先在用户列表中选择一个用户", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string password = _passwordTxt.Text;
        string confirm = _confirmPwdTxt.Text;
        if (string.IsNullOrEmpty(password) || password != confirm)
        {
            MessageBox.Show("请输入一致的密码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _userManager.ChangePassword(username, password);
        _passwordTxt.Clear();
        _confirmPwdTxt.Clear();
    }

    private void RefreshUserLists()
    {
        _allUsersList.Items.Clear();
        _rdpUsersList.Items.Clear();

        foreach (var u in _userManager.ListUsers())
            _allUsersList.Items.Add(u);

        foreach (var u in _userManager.ListRdpUsers())
            _rdpUsersList.Items.Add(u);
    }

    private void AddToRdpGroup()
    {
        string? username = _allUsersList.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(username))
        {
            MessageBox.Show("请先选择一个用户", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_userManager.AddToRdpUsersGroup(username))
            RefreshUserLists();
    }

    private void RemoveFromRdpGroup()
    {
        string? username = _rdpUsersList.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(username))
        {
            MessageBox.Show("请先选择一个用户", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_userManager.RemoveFromRdpUsersGroup(username))
            RefreshUserLists();
    }

    private void DeleteUser()
    {
        string? username = _allUsersList.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(username))
        {
            MessageBox.Show("请先选择一个用户", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var result = MessageBox.Show($"确定要删除用户 '{username}' 吗？此操作不可撤销！", "确认删除",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        if (_userManager.DeleteUser(username))
            RefreshUserLists();
    }

    #endregion

    #region INI Tab Logic

    private void LoadIni()
    {
        _iniPathLabel.Text = $"文件: {_iniManager.IniPath}";
        _iniEditor.Text = _iniManager.LoadText();
        AppendLog(_homeLog, $"[*] 已加载INI: {_iniManager.IniPath}");
    }

    private void SaveIni()
    {
        if (_iniManager.SaveText(_iniEditor.Text))
        {
            // Also copy to System32 if installed
            if (_installer.IsInstalled)
            {
                _iniManager.CopyToSystem32();
            }
            MessageBox.Show("INI 保存成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ImportIni()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择 INI 文件",
            Filter = "INI 文件|*.ini|所有文件|*.*"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            if (_iniManager.ImportFrom(dlg.FileName))
                LoadIni();
        }
    }

    private async Task OnlineUpdateDefault()
    {
        _onlineUpdateBtn.Enabled = false;
        try
        {
            if (await _onlineUpdater.DownloadFromDefaultUrlsAsync())
            {
                LoadIni();
                MessageBox.Show("INI 在线更新成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("所有更新源均失败，请检查网络或手动导入。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _onlineUpdateBtn.Enabled = true;
        }
    }

    private async Task OnlineUpdateCustom()
    {
        string url = _urlTxt.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("请输入URL", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _downloadUrlBtn.Enabled = false;
        try
        {
            if (await _onlineUpdater.DownloadFromCustomUrlAsync(url))
            {
                LoadIni();
                MessageBox.Show("INI 下载成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        finally
        {
            _downloadUrlBtn.Enabled = true;
        }
    }

    #endregion

    #region Analyze Tab Logic

    private async Task RunAnalysis()
    {
        _analyzeBtn.Enabled = false;
        _analysisReport.Clear();
        _generatedIni.Clear();

        try
        {
            await Task.Run(() =>
            {
                var (success, iniSection, report) = _analyzer.AnalyzeAndGenerate();

                this.Invoke(() =>
                {
                    _analysisReport.AppendText(report + "\n");
                    if (success)
                    {
                        _generatedIni.Text = iniSection;
                        AppendLog(_analysisReport, "\n[+] 自动分析完成！请检查生成的INI配置。");
                    }
                    else
                    {
                        AppendLog(_analysisReport, "\n[-] 自动分析未能找到所有补丁偏移。");
                    }
                });
            });
        }
        catch (Exception ex)
        {
            AppendLog(_analysisReport, $"[-] 分析错误: {ex.Message}");
        }
        finally
        {
            _analyzeBtn.Enabled = true;
        }
    }

    private void AddAnalysisToIni()
    {
        string section = _generatedIni.Text.Trim();
        if (string.IsNullOrEmpty(section))
        {
            MessageBox.Show("没有可添加的INI配置。请先运行自动分析。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Extract version from the section header
        var match = System.Text.RegularExpressions.Regex.Match(section, @"\[(\d+\.\d+\.\d+\.\d+)\]");
        if (!match.Success)
        {
            MessageBox.Show("无法解析版本号。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string version = match.Groups[1].Value;

        // Parse the section content
        string sectionContent = "";
        string slInitContent = "";
        var parts = section.Split(new[] { $"[{version}-SLInit]" }, StringSplitOptions.None);
        if (parts.Length > 0)
        {
            var mainPart = parts[0].Replace($"[{version}]", "").Trim();
            sectionContent = mainPart;
        }
        if (parts.Length > 1)
        {
            slInitContent = parts[1].Trim();
        }

        if (_iniManager.AddVersionSection(version, sectionContent,
            string.IsNullOrEmpty(slInitContent) ? null : slInitContent))
        {
            MessageBox.Show($"版本 {version} 的INI配置已添加！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            // Switch to INI tab and reload
            _tabControl.SelectedIndex = 2;
            LoadIni();
        }
    }

    #endregion

    #region Utilities

    private void AppendLog(RichTextBox box, string msg)
    {
        if (box.InvokeRequired)
        {
            box.Invoke(() => AppendLog(box, msg));
            return;
        }
        box.AppendText(msg + "\n");
        box.ScrollToCaret();
    }

    #endregion
}
