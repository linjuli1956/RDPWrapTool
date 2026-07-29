using System;
using System.Drawing;
using System.Windows.Forms;
using RDPWrapTool.Core;

namespace RDPWrapTool.Forms;

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

    // Main layout
    private TabControl _tabControl = null!;

    // ===== Home Tab =====
    private TextBox _statusBox = null!;
    private Button _installBtn = null!;
    private Button _uninstallBtn = null!;
    private Button _restartSvcBtn = null!;
    private RichTextBox _homeLog = null!;

    // ===== Users Tab =====
    private TextBox _usernameTxt = null!;
    private TextBox _passwordTxt = null!;
    private TextBox _confirmPwdTxt = null!;
    private TextBox _fullnameTxt = null!;
    private Button _createUserBtn = null!;
    private ListBox _allUsersList = null!;
    private ListBox _rdpUsersList = null!;
    private Button _refreshUsersBtn = null!;
    private Button _addToRdpBtn = null!;
    private Button _removeFromRdpBtn = null!;
    private Button _deleteUserBtn = null!;
    private Button _changePwdBtn = null!;

    // ===== INI Tab =====
    private RichTextBox _iniEditor = null!;
    private Button _loadIniBtn = null!;
    private Button _saveIniBtn = null!;
    private Button _importIniBtn = null!;
    private Button _onlineUpdateBtn = null!;
    private TextBox _urlTxt = null!;
    private Button _downloadUrlBtn = null!;
    private Label _iniPathLabel = null!;

    // ===== Analyze Tab =====
    private Label _termsrvVerLabel = null!;
    private Button _analyzeBtn = null!;
    private RichTextBox _analysisReport = null!;
    private RichTextBox _generatedIni = null!;
    private Button _addToIniBtn = null!;

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

        _tabControl.SelectedIndexChanged += (s, e) => OnTabChanged();
        Load += MainForm_Load;
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

    #region Form Initialization

    private void InitializeComponent()
    {
        Text = $"RDPWrap Tool v{AppVersion} - 多用户远程桌面工具";
        Size = new Size(900, 650);
        MinimumSize = new Size(800, 550);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        _tabControl = new TabControl { Dock = DockStyle.Fill };

        CreateHomeTab();
        CreateUsersTab();
        CreateIniTab();
        CreateAnalyzeTab();

        Controls.Add(_tabControl);
    }

    private void CreateHomeTab()
    {
        var tab = new TabPage("主页");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Status group
        var statusGroup = new GroupBox { Text = "系统状态", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _statusBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F)
        };
        statusGroup.Controls.Add(_statusBox);
        panel.Controls.Add(statusGroup, 0, 0);

        // Actions group
        var actionGroup = new GroupBox { Text = "操作", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var actionLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));

        _installBtn = new Button { Text = "安装 RDPWrap", Dock = DockStyle.Fill, Margin = new Padding(3) };
        _installBtn.Click += (s, e) => InstallRdpWrap();

        _uninstallBtn = new Button { Text = "卸载 RDPWrap", Dock = DockStyle.Fill, Margin = new Padding(3) };
        _uninstallBtn.Click += (s, e) => UninstallRdpWrap();

        _restartSvcBtn = new Button { Text = "重启远程服务", Dock = DockStyle.Fill, Margin = new Padding(3) };
        _restartSvcBtn.Click += (s, e) => RestartService();

        actionLayout.Controls.Add(_installBtn, 0, 0);
        actionLayout.Controls.Add(_uninstallBtn, 1, 0);
        actionLayout.Controls.Add(_restartSvcBtn, 2, 0);
        actionGroup.Controls.Add(actionLayout);
        panel.Controls.Add(actionGroup, 0, 1);

        // Log
        var logGroup = new GroupBox { Text = "操作日志", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _homeLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220)
        };
        logGroup.Controls.Add(_homeLog);
        panel.Controls.Add(logGroup, 0, 2);
        panel.SetColumnSpan(logGroup, 2);
        panel.SetColumnSpan(actionGroup, 2);

        tab.Controls.Add(panel);
        _tabControl.TabPages.Add(tab);
    }

    private void CreateUsersTab()
    {
        var tab = new TabPage("用户管理");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Left: Create user
        var createGroup = new GroupBox { Text = "新建用户", Dock = DockStyle.Fill, Padding = new Padding(8) };
        var createLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6 };
        createLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        createLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++)
            createLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        createLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        createLayout.Controls.Add(new Label { Text = "用户名:", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _usernameTxt = new TextBox { Dock = DockStyle.Fill };
        createLayout.Controls.Add(_usernameTxt, 1, 0);

        createLayout.Controls.Add(new Label { Text = "密码:", TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        _passwordTxt = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        createLayout.Controls.Add(_passwordTxt, 1, 1);

        createLayout.Controls.Add(new Label { Text = "确认密码:", TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        _confirmPwdTxt = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        createLayout.Controls.Add(_confirmPwdTxt, 1, 2);

        createLayout.Controls.Add(new Label { Text = "全名(可选):", TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        _fullnameTxt = new TextBox { Dock = DockStyle.Fill };
        createLayout.Controls.Add(_fullnameTxt, 1, 3);

        _createUserBtn = new Button { Text = "创建用户并加入RDP组", Dock = DockStyle.Fill, Margin = new Padding(3) };
        _createUserBtn.Click += (s, e) => CreateUser();
        createLayout.Controls.Add(_createUserBtn, 0, 4);
        createLayout.SetColumnSpan(_createUserBtn, 2);

        _changePwdBtn = new Button { Text = "修改选中用户密码", Dock = DockStyle.Fill, Margin = new Padding(3) };
        _changePwdBtn.Click += (s, e) => ChangePassword();
        createLayout.Controls.Add(_changePwdBtn, 0, 5);
        createLayout.SetColumnSpan(_changePwdBtn, 2);

        createGroup.Controls.Add(createLayout);
        panel.Controls.Add(createGroup, 0, 0);

        // Right: User lists
        var listPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        listPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        listPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        listPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _refreshUsersBtn = new Button { Text = "刷新用户列表", Dock = DockStyle.Fill };
        _refreshUsersBtn.Click += (s, e) => RefreshUserLists();
        listPanel.Controls.Add(_refreshUsersBtn, 0, 0);
        listPanel.SetColumnSpan(_refreshUsersBtn, 2);

        var allUsersGroup = new GroupBox { Text = "所有本地用户", Dock = DockStyle.Fill };
        _allUsersList = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
        allUsersGroup.Controls.Add(_allUsersList);
        listPanel.Controls.Add(allUsersGroup, 0, 1);

        var rdpUsersGroup = new GroupBox { Text = "远程桌面用户组", Dock = DockStyle.Fill };
        _rdpUsersList = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
        rdpUsersGroup.Controls.Add(_rdpUsersList);
        listPanel.Controls.Add(rdpUsersGroup, 1, 1);

        // Action buttons
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _addToRdpBtn = new Button { Text = "加入RDP组", AutoSize = true, Margin = new Padding(3) };
        _addToRdpBtn.Click += (s, e) => AddToRdpGroup();
        _removeFromRdpBtn = new Button { Text = "移出RDP组", AutoSize = true, Margin = new Padding(3) };
        _removeFromRdpBtn.Click += (s, e) => RemoveFromRdpGroup();
        _deleteUserBtn = new Button { Text = "删除用户", AutoSize = true, Margin = new Padding(3), ForeColor = Color.Red };
        _deleteUserBtn.Click += (s, e) => DeleteUser();
        btnPanel.Controls.AddRange(new Control[] { _addToRdpBtn, _removeFromRdpBtn, _deleteUserBtn });
        listPanel.Controls.Add(btnPanel, 0, 2);
        listPanel.SetColumnSpan(btnPanel, 2);

        panel.Controls.Add(listPanel, 1, 0);
        tab.Controls.Add(panel);
        _tabControl.TabPages.Add(tab);
    }

    private void CreateIniTab()
    {
        var tab = new TabPage("INI 配置");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Toolbar
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(3) };
        _loadIniBtn = new Button { Text = "加载", AutoSize = true, Margin = new Padding(3) };
        _loadIniBtn.Click += (s, e) => LoadIni();
        _saveIniBtn = new Button { Text = "保存", AutoSize = true, Margin = new Padding(3) };
        _saveIniBtn.Click += (s, e) => SaveIni();
        _importIniBtn = new Button { Text = "从文件导入", AutoSize = true, Margin = new Padding(3) };
        _importIniBtn.Click += (s, e) => ImportIni();
        _onlineUpdateBtn = new Button { Text = "在线更新(默认源)", AutoSize = true, Margin = new Padding(3) };
        _onlineUpdateBtn.Click += async (s, e) => await OnlineUpdateDefault();

        _urlTxt = new TextBox { Width = 250, Margin = new Padding(3), PlaceholderText = "自定义INI下载URL" };
        _downloadUrlBtn = new Button { Text = "下载", AutoSize = true, Margin = new Padding(3) };
        _downloadUrlBtn.Click += async (s, e) => await OnlineUpdateCustom();

        toolbar.Controls.AddRange(new Control[] { _loadIniBtn, _saveIniBtn, _importIniBtn, _onlineUpdateBtn, _urlTxt, _downloadUrlBtn });
        panel.Controls.Add(toolbar, 0, 0);

        // Path label
        _iniPathLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 8F), ForeColor = Color.Gray };
        panel.Controls.Add(_iniPathLabel, 0, 1);

        // Editor
        _iniEditor = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(250, 250, 250),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        panel.Controls.Add(_iniEditor, 0, 2);

        tab.Controls.Add(panel);
        _tabControl.TabPages.Add(tab);
    }

    private void CreateAnalyzeTab()
    {
        var tab = new TabPage("自动分析");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 60));

        // Top: info + button
        var topPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        _termsrvVerLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "termsrv.dll 版本: 检测中...",
            Font = new Font("Microsoft YaHei UI", 10F)
        };
        _analyzeBtn = new Button { Text = "开始自动分析", Dock = DockStyle.Fill, Margin = new Padding(8) };
        _analyzeBtn.Click += async (s, e) => await RunAnalysis();

        topPanel.Controls.Add(_termsrvVerLabel, 0, 0);
        topPanel.Controls.Add(_analyzeBtn, 1, 0);
        panel.Controls.Add(topPanel, 0, 0);

        // Analysis report
        var reportGroup = new GroupBox { Text = "分析报告", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _analysisReport = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220)
        };
        reportGroup.Controls.Add(_analysisReport);
        panel.Controls.Add(reportGroup, 0, 1);

        // Add to INI button
        _addToIniBtn = new Button { Text = "将分析结果添加到INI文件", Dock = DockStyle.Fill, Margin = new Padding(3) };
        _addToIniBtn.Click += (s, e) => AddAnalysisToIni();
        panel.Controls.Add(_addToIniBtn, 0, 2);

        // Generated INI section
        var iniGroup = new GroupBox { Text = "生成的INI配置 (可直接编辑后添加)", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _generatedIni = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        iniGroup.Controls.Add(_generatedIni);
        panel.Controls.Add(iniGroup, 0, 3);

        tab.Controls.Add(panel);
        _tabControl.TabPages.Add(tab);
    }

    #endregion

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
