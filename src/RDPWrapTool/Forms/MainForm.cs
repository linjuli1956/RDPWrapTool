using System;
using System.Drawing;
using System.Windows.Forms;
using RDPWrapTool.Core;

namespace RDPWrapTool.Forms;

public partial class MainForm : Form
{
    // Core modules
    private readonly RDPWrapInstaller _installer = new();
    private readonly UserManager _userManager = new();
    private readonly IniManager _iniManager = new();
    private OnlineUpdater? _onlineUpdater;
    private readonly TermSrvAnalyzer _analyzer = new();

    // Navigation shell
    private Panel _navPanel = null!;
    private Panel _contentPanel = null!;
    private Button[] _navButtons = null!;
    private Panel[] _pages = null!;
    private int _currentPage = -1;

    private static readonly string[] NavTitles =
        { "总览", "安装部署", "自动分析", "INI 配置", "用户管理", "日志" };

    private static readonly Color NavBack = Color.FromArgb(37, 37, 38);
    private static readonly Color NavIdle = Color.FromArgb(37, 37, 38);
    private static readonly Color NavActive = Color.FromArgb(0, 122, 204);
    private static readonly Color LogBack = Color.FromArgb(30, 30, 30);
    private static readonly Color LogFore = Color.FromArgb(220, 220, 220);

    // ===== Overview page =====
    private RichTextBox _statusBox = null!;
    private Button _refreshBtn = null!;
    private Button _ovEnableRemoteBtn = null!;
    private Button _ovRestartSvcBtn = null!;

    // ===== Install page =====
    private Button _installBtn = null!;
    private Button _uninstallBtn = null!;
    private Button _enableRemoteBtn = null!;
    private Button _restartSvcBtn = null!;
    private Button _restoreBtn = null!;
    private RichTextBox _installStatusBox = null!;

    // ===== Analyze page =====
    private Label _termsrvVerLabel = null!;
    private Button _analyzeBtn = null!;
    private RichTextBox _analysisReport = null!;
    private RichTextBox _generatedIni = null!;
    private Button _addToIniBtn = null!;
    private TermSrvAnalyzer.AnalysisResult? _lastAnalysis;

    // ===== INI page =====
    private RichTextBox _iniEditor = null!;
    private Button _loadIniBtn = null!;
    private Button _saveIniBtn = null!;
    private Button _importIniBtn = null!;
    private Button _onlineUpdateBtn = null!;
    private TextBox _urlTxt = null!;
    private Button _downloadUrlBtn = null!;
    private Label _iniPathLabel = null!;

    // ===== Users page =====
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

    // ===== Log page =====
    private RichTextBox _globalLog = null!;
    private Button _clearLogBtn = null!;

    public MainForm()
    {
        InitializeComponent();
        _onlineUpdater = new OnlineUpdater(_iniManager.IniPath);

        // All operational logs flow to the global log page
        _installer.OnLog += msg => AppendLog(_globalLog, msg);
        _userManager.OnLog += msg => AppendLog(_globalLog, msg);
        _iniManager.OnLog += msg => AppendLog(_globalLog, msg);
        _analyzer.OnLog += msg => AppendLog(_analysisReport, msg);
        ServiceManager.OnLog += msg => AppendLog(_globalLog, msg);
        _onlineUpdater!.OnLog += msg => AppendLog(_globalLog, msg);

        Load += (s, e) => ShowPage(0);
    }

    #region Shell

    private void InitializeComponent()
    {
        Text = "RDPWrap Tool - 多用户远程桌面工具";
        Size = new Size(1024, 700);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        // Left navigation bar
        _navPanel = new Panel { Dock = DockStyle.Left, Width = 170, BackColor = NavBack };
        var titleLabel = new Label
        {
            Text = "RDPWrap",
            Dock = DockStyle.Top,
            Height = 56,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold)
        };
        _navPanel.Controls.Add(titleLabel);

        _navButtons = new Button[NavTitles.Length];
        // Buttons are Dock=Top; add in REVERSE order so they stack correctly under the title
        for (int i = NavTitles.Length - 1; i >= 0; i--)
        {
            int idx = i;
            var btn = new Button
            {
                Text = "   " + NavTitles[i],
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Top,
                Height = 46,
                FlatStyle = FlatStyle.Flat,
                BackColor = NavIdle,
                ForeColor = Color.Gainsboro,
                Font = new Font("Microsoft YaHei UI", 10F),
                Margin = new Padding(0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => ShowPage(idx);
            _navButtons[i] = btn;
            _navPanel.Controls.Add(btn);
            btn.BringToFront();
        }
        titleLabel.BringToFront();

        var verLabel = new Label
        {
            Text = "net48 · 自动分析版",
            Dock = DockStyle.Bottom,
            Height = 30,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei UI", 8F)
        };
        _navPanel.Controls.Add(verLabel);

        // Content area with six stacked pages
        _contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 245, 245) };

        _pages = new Panel[NavTitles.Length];
        CreateOverviewPage();
        CreateInstallPage();
        CreateAnalyzePage();
        CreateIniPage();
        CreateUsersPage();
        CreateLogPage();
        foreach (var p in _pages)
        {
            p.Visible = false;
            _contentPanel.Controls.Add(p);
        }

        Controls.Add(_contentPanel);
        Controls.Add(_navPanel);
    }

    private void ShowPage(int index)
    {
        if (index == _currentPage) return;
        _currentPage = index;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = (i == index);
            _navButtons[i].BackColor = (i == index) ? NavActive : NavIdle;
            _navButtons[i].ForeColor = (i == index) ? Color.White : Color.Gainsboro;
        }
        _pages[index].BringToFront();
        OnPageEnter(index);
    }

    private void OnPageEnter(int index)
    {
        switch (index)
        {
            case 0: RefreshStatus(); break;
            case 1: RefreshInstallStatus(); break;
            case 2:
                var ver = RDPWrapInstaller.GetTermsrvVersion();
                _termsrvVerLabel.Text = $"termsrv.dll 版本: {ver?.ToString() ?? "(未知)"}";
                break;
            case 3:
                if (string.IsNullOrEmpty(_iniEditor.Text)) LoadIni();
                break;
            case 4:
                if (_allUsersList.Items.Count == 0) RefreshUserLists();
                break;
        }
    }

    private static RichTextBox MakeLogBox(bool readOnly = true) => new RichTextBox
    {
        Dock = DockStyle.Fill,
        ReadOnly = readOnly,
        Font = new Font("Consolas", 9F),
        BackColor = LogBack,
        ForeColor = LogFore,
        WordWrap = false,
        ScrollBars = RichTextBoxScrollBars.Both
    };

    private Button MakeActionButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(6),
            Font = new Font("Microsoft YaHei UI", 10F),
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        b.Click += onClick;
        return b;
    }

    #endregion

    #region Page builders

    private Panel NewPage()
    {
        return new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
    }

    private void CreateOverviewPage()
    {
        var page = NewPage();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Quick actions row
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _refreshBtn = MakeActionButton("刷新状态", (s, e) => RefreshStatus());
        _ovEnableRemoteBtn = MakeActionButton("启用远程桌面", (s, e) => EnableRemoteDesktop());
        _ovRestartSvcBtn = MakeActionButton("重启远程服务", (s, e) => RestartService());
        actions.Controls.Add(_refreshBtn, 0, 0);
        actions.Controls.Add(_ovEnableRemoteBtn, 1, 0);
        actions.Controls.Add(_ovRestartSvcBtn, 2, 0);
        layout.Controls.Add(actions, 0, 0);

        // Status dashboard
        var statusGroup = new GroupBox { Text = "系统状态", Dock = DockStyle.Fill, Padding = new Padding(10) };
        _statusBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 10F),
            BackColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        statusGroup.Controls.Add(_statusBox);
        layout.Controls.Add(statusGroup, 0, 1);

        page.Controls.Add(layout);
        _pages[0] = page;
    }

    private void CreateInstallPage()
    {
        var page = NewPage();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Big action buttons
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
        _installBtn = MakeActionButton("安装 RDPWrap", (s, e) => InstallRdpWrap());
        _uninstallBtn = MakeActionButton("卸载 RDPWrap", (s, e) => UninstallRdpWrap());
        _enableRemoteBtn = MakeActionButton("启用远程桌面", (s, e) => EnableRemoteDesktop());
        _restartSvcBtn = MakeActionButton("重启远程服务", (s, e) => RestartService());
        _restoreBtn = MakeActionButton("恢复到可用状态", (s, e) => RestoreSafeState());
        actions.Controls.Add(_installBtn, 0, 0);
        actions.Controls.Add(_uninstallBtn, 1, 0);
        actions.Controls.Add(_enableRemoteBtn, 2, 0);
        actions.Controls.Add(_restartSvcBtn, 3, 0);
        actions.Controls.Add(_restoreBtn, 4, 0);
        layout.Controls.Add(actions, 0, 0);

        // Deployment status
        var statusGroup = new GroupBox { Text = "部署状态", Dock = DockStyle.Fill, Padding = new Padding(10) };
        _installStatusBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9.5F),
            BackColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        statusGroup.Controls.Add(_installStatusBox);
        layout.Controls.Add(statusGroup, 0, 1);

        // Hint
        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6),
            ForeColor = Color.DimGray,
            Text = "说明：安装会将 rdpwrap.dll 与 rdpwrap.ini 部署到系统目录并注册到 TermService。\r\n" +
                   "安装和新增远程用户会自动启用 Windows 远程桌面；也可在本页手动启用。\r\n" +
                   "若 INI 不支持当前系统版本，请到「自动分析」页：分析 → 添加到 INI（自动部署+校验+失败自动回滚）。\r\n" +
                   "「恢复到可用状态」会删除自动分析生成的版本 section 并重启服务，用于 RDP 连不上时快速恢复。\r\n" +
                   "所有安装/卸载过程的详细输出见「日志」页。"
        };
        layout.Controls.Add(hint, 0, 2);

        page.Controls.Add(layout);
        _pages[1] = page;
    }

    private void CreateAnalyzePage()
    {
        var page = NewPage();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));

        // Top bar: version + analyze button
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        _termsrvVerLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "termsrv.dll 版本: 检测中...",
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold)
        };
        _analyzeBtn = MakeActionButton("开始自动分析", async (s, e) => await RunAnalysis());
        _analyzeBtn.BackColor = NavActive;
        _analyzeBtn.ForeColor = Color.White;
        top.Controls.Add(_termsrvVerLabel, 0, 0);
        top.Controls.Add(_analyzeBtn, 1, 0);
        layout.Controls.Add(top, 0, 0);

        // Report
        var reportGroup = new GroupBox { Text = "分析报告（字节级验证）", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _analysisReport = MakeLogBox();
        reportGroup.Controls.Add(_analysisReport);
        layout.Controls.Add(reportGroup, 0, 1);

        // Add-to-INI row
        _addToIniBtn = MakeActionButton("添加到 INI 并部署到系统（自动重启服务）", (s, e) => AddAnalysisToIni());
        layout.Controls.Add(_addToIniBtn, 0, 2);

        // Generated INI
        var iniGroup = new GroupBox { Text = "生成的 INI 配置", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _generatedIni = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(250, 250, 250),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        iniGroup.Controls.Add(_generatedIni);
        layout.Controls.Add(iniGroup, 0, 3);

        page.Controls.Add(layout);
        _pages[2] = page;
    }

    private void CreateIniPage()
    {
        var page = NewPage();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2), WrapContents = false };
        _loadIniBtn = new Button { Text = "加载", AutoSize = true, Margin = new Padding(3) };
        _loadIniBtn.Click += (s, e) => LoadIni();
        _saveIniBtn = new Button { Text = "保存", AutoSize = true, Margin = new Padding(3) };
        _saveIniBtn.Click += (s, e) => SaveIni();
        _importIniBtn = new Button { Text = "从文件导入", AutoSize = true, Margin = new Padding(3) };
        _importIniBtn.Click += (s, e) => ImportIni();
        _onlineUpdateBtn = new Button { Text = "在线更新(默认源)", AutoSize = true, Margin = new Padding(3) };
        _onlineUpdateBtn.Click += async (s, e) => await OnlineUpdateDefault();
        _urlTxt = new TextBox { Width = 280, Margin = new Padding(6, 5, 3, 3), Text = "https://" };
        _downloadUrlBtn = new Button { Text = "下载", AutoSize = true, Margin = new Padding(3) };
        _downloadUrlBtn.Click += async (s, e) => await OnlineUpdateCustom();
        toolbar.Controls.AddRange(new Control[] { _loadIniBtn, _saveIniBtn, _importIniBtn, _onlineUpdateBtn, _urlTxt, _downloadUrlBtn });
        layout.Controls.Add(toolbar, 0, 0);

        _iniPathLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 8F), ForeColor = Color.Gray };
        layout.Controls.Add(_iniPathLabel, 0, 1);

        _iniEditor = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9.5F),
            BackColor = Color.FromArgb(250, 250, 250),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        layout.Controls.Add(_iniEditor, 0, 2);

        page.Controls.Add(layout);
        _pages[3] = page;
    }

    private void CreateUsersPage()
    {
        var page = NewPage();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Left: create user
        var createGroup = new GroupBox { Text = "新建用户 / 修改密码", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var cl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7 };
        cl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        cl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) cl.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        cl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        cl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        cl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        cl.Controls.Add(new Label { Text = "用户名:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        _usernameTxt = new TextBox { Dock = DockStyle.Fill };
        cl.Controls.Add(_usernameTxt, 1, 0);
        cl.Controls.Add(new Label { Text = "密码:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        _passwordTxt = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        cl.Controls.Add(_passwordTxt, 1, 1);
        cl.Controls.Add(new Label { Text = "确认密码:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        _confirmPwdTxt = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        cl.Controls.Add(_confirmPwdTxt, 1, 2);
        cl.Controls.Add(new Label { Text = "全名(可选):", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 3);
        _fullnameTxt = new TextBox { Dock = DockStyle.Fill };
        cl.Controls.Add(_fullnameTxt, 1, 3);

        _createUserBtn = MakeActionButton("创建用户并启用远程", (s, e) => CreateUser());
        cl.Controls.Add(_createUserBtn, 0, 4);
        cl.SetColumnSpan(_createUserBtn, 2);
        _changePwdBtn = MakeActionButton("修改选中用户密码", (s, e) => ChangePassword());
        cl.Controls.Add(_changePwdBtn, 0, 5);
        cl.SetColumnSpan(_changePwdBtn, 2);
        createGroup.Controls.Add(cl);
        layout.Controls.Add(createGroup, 0, 0);

        // Right: lists + actions
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 78));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 22));

        _refreshUsersBtn = MakeActionButton("刷新用户列表", (s, e) => RefreshUserLists());
        right.Controls.Add(_refreshUsersBtn, 0, 0);
        right.SetColumnSpan(_refreshUsersBtn, 2);

        var allGroup = new GroupBox { Text = "所有本地用户", Dock = DockStyle.Fill, Padding = new Padding(6) };
        _allUsersList = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F) };
        allGroup.Controls.Add(_allUsersList);
        right.Controls.Add(allGroup, 0, 1);

        var rdpGroup = new GroupBox { Text = "远程桌面用户组", Dock = DockStyle.Fill, Padding = new Padding(6) };
        _rdpUsersList = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F) };
        rdpGroup.Controls.Add(_rdpUsersList);
        right.Controls.Add(rdpGroup, 1, 1);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _addToRdpBtn = new Button { Text = "加入RDP组 ←", AutoSize = true, Margin = new Padding(4) };
        _addToRdpBtn.Click += (s, e) => AddToRdpGroup();
        _removeFromRdpBtn = new Button { Text = "移出RDP组 →", AutoSize = true, Margin = new Padding(4) };
        _removeFromRdpBtn.Click += (s, e) => RemoveFromRdpGroup();
        _deleteUserBtn = new Button { Text = "删除用户", AutoSize = true, Margin = new Padding(4), ForeColor = Color.Red };
        _deleteUserBtn.Click += (s, e) => DeleteUser();
        btnRow.Controls.AddRange(new Control[] { _addToRdpBtn, _removeFromRdpBtn, _deleteUserBtn });
        right.Controls.Add(btnRow, 0, 2);
        right.SetColumnSpan(btnRow, 2);

        layout.Controls.Add(right, 1, 0);
        page.Controls.Add(layout);
        _pages[4] = page;
    }

    private void CreateLogPage()
    {
        var page = NewPage();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        _clearLogBtn = new Button { Text = "清空日志", AutoSize = true, Margin = new Padding(3) };
        _clearLogBtn.Click += (s, e) => _globalLog.Clear();
        var logHint = new Label
        {
            Text = "安装、卸载、服务操作、INI 读写、用户管理的全部输出汇总于此。",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray,
            Margin = new Padding(10, 8, 3, 3)
        };
        bar.Controls.Add(_clearLogBtn);
        bar.Controls.Add(logHint);
        layout.Controls.Add(bar, 0, 0);

        _globalLog = MakeLogBox();
        layout.Controls.Add(_globalLog, 0, 1);

        page.Controls.Add(layout);
        _pages[5] = page;
    }

    #endregion

    #region Overview / Install logic

    private void RefreshStatus()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"操作系统:        {Environment.OSVersion.VersionString}");
        sb.AppendLine($"架构:            {(Environment.Is64BitOperatingSystem ? "x64 (支持)" : "x86 (不支持)")}");

        var ver = RDPWrapInstaller.GetTermsrvVersion();
        sb.AppendLine($"termsrv.dll 版本: {ver}");

        _installer.CheckInstall();
        sb.AppendLine($"RDPWrap 已安装:   {(_installer.IsInstalled ? "是" : "否")}");
        if (!string.IsNullOrEmpty(_installer.InstalledDllPath))
            sb.AppendLine($"ServiceDll:       {_installer.InstalledDllPath}");

        var svcStatus = ServiceManager.GetServiceStatus(ServiceManager.TermServiceName);
        sb.AppendLine($"TermService 状态: {svcStatus}");
        // The service can be "Running" while termsrv failed to create its listener, so the
        // listener state is what actually decides whether RDP works.
        bool listening = ServiceManager.IsRdpPortListening();
        sb.AppendLine($"3389 监听状态:    {(listening ? "正在侦听（远程桌面可用）" : "未侦听（远程桌面不可连接！）")}");
        var tsEv = ServiceManager.GetTsStartupEvidence(DateTime.Now.AddMinutes(-15));
        if (tsEv.StartupFailed)
            sb.AppendLine($"  最近事件:      启动失败(17) {tsEv.StartupFailedAt:HH:mm:ss} → 请看日志页诊断");
        else if (tsEv.ListenerStarted)
            sb.AppendLine($"  最近事件:      侦听启动(258) {tsEv.ListenerStartedAt:HH:mm:ss}");
        var rdpLog = DeployVerifier.FindRdpWrapLog();
        sb.AppendLine($"rdpwrap 日志:    {(rdpLog ?? "(未找到，将部署时自动改为可写路径)")}");
        sb.AppendLine($"Windows 远程桌面: {(RDPWrapInstaller.IsRemoteDesktopEnabled() ? "已启用" : "未启用")}");

        var (supported, verStr) = _iniManager.CheckCurrentVersionSupport();
        sb.AppendLine($"INI 支持当前版本: {(supported ? "是" : "否")}");
        if (!supported && verStr != null)
            sb.AppendLine($"  → 版本 {verStr} 不在 INI 中，请到「自动分析」页生成配置");

        _statusBox.Text = sb.ToString();
        _termsrvVerLabel.Text = $"termsrv.dll 版本: {ver?.ToString() ?? "(未知)"}";
    }

    private void RefreshInstallStatus()
    {
        var sb = new System.Text.StringBuilder();
        _installer.CheckInstall();
        sb.AppendLine($"RDPWrap 已安装:  {(_installer.IsInstalled ? "是" : "否")}");
        sb.AppendLine($"ServiceDll:      {(_installer.InstalledDllPath ?? "(未检测到)")}");
        sb.AppendLine($"本地 INI:        {_iniManager.IniPath}");
        sb.AppendLine($"TermService:     {ServiceManager.GetServiceStatus(ServiceManager.TermServiceName)}");
        sb.AppendLine($"3389 监听:       {(ServiceManager.IsRdpPortListening() ? "是" : "否")}");
        sb.AppendLine($"远程桌面开关:    {(RDPWrapInstaller.IsRemoteDesktopEnabled() ? "已启用" : "未启用")}");
        var (supported, verStr) = _iniManager.CheckCurrentVersionSupport();
        sb.AppendLine($"INI 支持 {verStr}: {(supported ? "是" : "否")}");
        _installStatusBox.Text = sb.ToString();
    }

    private void InstallRdpWrap()
    {
        _installBtn.Enabled = false;
        try
        {
            var installStart = DateTime.Now;
            bool ok = _installer.Install();
            bool ready = ok && ServiceManager.WaitServiceReady();
            if (!ready)
            {
                var ev = ServiceManager.GetTsStartupEvidence(installStart.AddSeconds(-2));
                AppendLog(_globalLog, $"[-] 安装后校验未通过: 3389 监听={ServiceManager.IsRdpPortListening()} " +
                                      $"事件17={(ev.StartupFailed ? "有" : "无")}");
                ServiceManager.DiagnoseTermServiceFailure();
                MessageBox.Show("RDPWrap 安装未通过监听校验（3389 未就绪）。请查看日志页的诊断信息。",
                    "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshStatus();
            RefreshInstallStatus();
        }
        finally { _installBtn.Enabled = true; }
    }

    private void EnableRemoteDesktop()
    {
        _enableRemoteBtn.Enabled = false;
        _ovEnableRemoteBtn.Enabled = false;
        try
        {
            bool ok = _installer.EnableRemoteDesktop();
            if (ok)
                MessageBox.Show("Windows 远程桌面已启用，防火墙规则已配置。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show("启用远程桌面失败，请查看日志页。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            RefreshStatus();
            RefreshInstallStatus();
        }
        finally
        {
            _enableRemoteBtn.Enabled = true;
            _ovEnableRemoteBtn.Enabled = true;
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
            RefreshInstallStatus();
        }
        finally { _uninstallBtn.Enabled = true; }
    }

    private void RestartService()
    {
        _restartSvcBtn.Enabled = false;
        _ovRestartSvcBtn.Enabled = false;
        _enableRemoteBtn.Enabled = false;
        _ovEnableRemoteBtn.Enabled = false;
        try
        {
            AppendLog(_globalLog, "[*] 正在重启 TermService...");
            var restartStart = DateTime.Now;
            bool started = ServiceManager.RestartService(ServiceManager.TermServiceName);
            bool ready = started && ServiceManager.WaitServiceReady();
            if (ready)
            {
                AppendLog(_globalLog, "[+] TermService 已重启，3389 正在侦听。");
            }
            else
            {
                AppendLog(_globalLog, "[-] TermService 未就绪（服务状态或 3389 监听异常）！正在诊断...");
                ServiceManager.DiagnoseTermServiceFailure();
                var ev = ServiceManager.GetTsStartupEvidence(restartStart.AddSeconds(-2));
                MessageBox.Show(
                    "TermService 重启后未就绪：3389 未侦听或服务异常。\r\n" +
                    (ev.StartupFailed ? $"检测到「远程桌面服务启动失败」(事件 17) {ev.StartupFailedAt:HH:mm:ss}。\r\n" : "") +
                    "请查看日志页诊断信息；若是刚部署了自动分析配置，可点「恢复到可用状态」。",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshStatus();
            RefreshInstallStatus();
        }
        finally
        {
            _restartSvcBtn.Enabled = true;
            _ovRestartSvcBtn.Enabled = true;
            _enableRemoteBtn.Enabled = true;
            _ovEnableRemoteBtn.Enabled = true;
        }
    }

    #endregion

    #region Users logic

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
            _installer.EnableRemoteDesktop();
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
        {
            _installer.EnableRemoteDesktop();
            RefreshUserLists();
        }
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

    #region INI logic

    private void LoadIni()
    {
        _iniPathLabel.Text = $"文件: {_iniManager.IniPath}";
        _iniEditor.Text = _iniManager.LoadText();
        AppendLog(_globalLog, $"[*] 已加载INI: {_iniManager.IniPath}");
    }

    private void SaveIni()
    {
        if (_iniManager.SaveText(_iniEditor.Text))
        {
            _installer.CheckInstall();
            if (_installer.IsInstalled)
            {
                _iniManager.CopyToSystem32();
                AppendLog(_globalLog, "[*] 正在重启 TermService...");
                ServiceManager.RestartService(ServiceManager.TermServiceName);
                AppendLog(_globalLog, "[+] INI 已保存到工具目录和 System32，TermService 已重启。");
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
            if (await _onlineUpdater!.DownloadFromDefaultUrlsAsync())
            {
                LoadIni();
                MessageBox.Show("INI 在线更新成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("所有更新源均失败，请检查网络或手动导入。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally { _onlineUpdateBtn.Enabled = true; }
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
            if (await _onlineUpdater!.DownloadFromCustomUrlAsync(url))
            {
                LoadIni();
                MessageBox.Show("INI 下载成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        finally { _downloadUrlBtn.Enabled = true; }
    }

    #endregion

    #region Analyze logic

    private async Task RunAnalysis()
    {
        _analyzeBtn.Enabled = false;
        _analysisReport.Clear();
        _generatedIni.Clear();
        _lastAnalysis = null;

        try
        {
            await Task.Run(() =>
            {
                var result = _analyzer.AnalyzeEx();
                string iniSection = result.Success ? _analyzer.GenerateIniSection(result) : "";

                this.Invoke(() =>
                {
                    _analysisReport.AppendText(result.Report + "\n");
                    if (result.Success)
                    {
                        _lastAnalysis = result;
                        _generatedIni.Text = iniSection;
                        AppendLog(_analysisReport, "\n[+] 自动分析完成并通过字节级验证！点击下方按钮添加到 INI 并自动部署到系统。");
                    }
                    else
                    {
                        AppendLog(_analysisReport, "\n[-] 严格验证未通过，INI 不会被修改。");
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

    private async void AddAnalysisToIni()
    {
        if (_lastAnalysis == null || !_lastAnalysis.Success)
        {
            MessageBox.Show("没有已通过验证的分析结果。请先运行自动分析。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _addToIniBtn.Enabled = false;
        try
        {
            var (sectionContent, slInitContent) = _analyzer.BuildSectionBodies(_lastAnalysis);
            var aliases = _lastAnalysis.Aliases;
            var patchCodes = _analyzer.GetRequiredPatchCodes(_lastAnalysis);

            AppendLog(_globalLog, "\n[*] 正在写入 INI、部署到系统目录、重启服务并校验监听状态...");
            var result = await Task.Run(() =>
            {
                _installer.CheckInstall();
                if (_installer.IsInstalled)
                {
                    return DeployWorkflow.DeployAnalyzedSection(
                        _iniManager, _installer, aliases, sectionContent, slInitContent, patchCodes,
                        msg => AppendLog(_globalLog, msg));
                }
                // Not installed yet: full install, then verify.
                bool installed = _installer.Install();
                if (!installed) return new DeployResult { Message = "安装失败。", Stage = "failed" };
                return DeployVerifier.Verify(DateTime.Now.AddMinutes(-2), requirePatches: false,
                    msg => AppendLog(_globalLog, msg));
            });

            LoadIni();
            RefreshStatus();

            string summary =
                $"阶段: {result.Stage}\r\n" +
                $"监听 3389: {(result.PortListening ? "是" : "否")}\r\n" +
                $"事件 258(侦听启动): {(result.ListenerEvent ? "有" : "无")}\r\n" +
                $"事件 17(启动失败): {(result.StartupFailed ? "有" : "无")}\r\n" +
                $"补丁日志: patches={(result.PatchesApplied ? "OK" : "-")} slInit={(result.SlInitWritten ? "OK" : "-")}\r\n" +
                $"\r\n{result.Message}";

            if (result.Success)
            {
                AppendLog(_globalLog, "[+] 部署成功并校验通过。");
                MessageBox.Show($"版本 {string.Join(" / ", aliases)} 已部署并通过校验。\r\n\r\n{summary}",
                    "部署成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                AppendLog(_globalLog, $"[-] 部署未通过校验：{result.Describe()}");
                MessageBox.Show(
                    $"部署未通过校验（未达到「远程桌面可用」状态）。\r\n\r\n{summary}\r\n\r\n" +
                    "详细证据见「日志」页；完整分析报告见「自动分析」页。",
                    "部署结果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _addToIniBtn.Enabled = true;
        }
    }

    /// <summary>
    /// Emergency recovery: drop every auto-generated version section, redeploy and verify
    /// that the RDP listener comes back.
    /// </summary>
    private async void RestoreSafeState()
    {
        var aliases = _iniManager.GetGeneratedVersions();
        var self = RDPWrapInstaller.GetTermsrvVersion();
        if (self != null && !aliases.Contains(self.ToString())) aliases.Add(self.ToString());

        var confirm = MessageBox.Show(
            "将删除本工具自动分析生成的版本 section（含 -SLInit），重启 TermService 并校验 3389 监听是否恢复。\r\n" +
            $"当前 termsrv.dll 版本: {(self?.ToString() ?? "未知")}\r\n" +
            $"将移除: {(aliases.Count > 0 ? string.Join(" / ", aliases) : "(无)")}\r\n\r\n继续？",
            "恢复到可用状态", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _restoreBtn.Enabled = false;
        try
        {
            AppendLog(_globalLog, "\n[*] === 恢复到可用状态 ===");
            var result = await Task.Run(() =>
            {
                _iniManager.BackupSystem32Ini();
                if (aliases.Count > 0) _iniManager.RemoveVersionSections(aliases);
                _installer.CheckInstall();
                if (!_installer.IsInstalled)
                    return new DeployResult { Message = "RDPWrap 未安装，无需恢复。", Stage = "failed" };
                return _installer.DeployFilesOnly(verify: true, requirePatches: false);
            });

            LoadIni();
            RefreshStatus();

            if (result.PortListening)
            {
                MessageBox.Show(
                    $"已移除自动生成的 section 并重启服务。\r\n\r\n监听 3389: 是\r\n" +
                    "此时远程桌面应可正常连接（单会话）。若要再试多用户，请重新分析并部署（部署会自动校验与回滚）。",
                    "恢复完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    "恢复后 3389 仍未监听。\r\n请到「日志」页查看诊断信息，必要时点「卸载 RDPWrap」恢复到系统原生 termsrv.dll。",
                    "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _restoreBtn.Enabled = true;
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
