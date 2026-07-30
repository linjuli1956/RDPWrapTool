using System;
using System.Drawing;
using System.Windows.Forms;
using RDPWrapTool.UI;

namespace RDPWrapTool.Forms;

/// <summary>
/// Visual layout for MainForm. All control fields, layout and event wiring
/// live here; behavior is implemented in MainForm.cs. The structure follows the
/// user workflow: a persistent status header on top, then four tabs ordered by
/// the natural operating sequence (Install -> Users -> INI -> Analyze).
/// </summary>
partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    // Root chrome
    private TableLayoutPanel _rootLayout = null!;
    private TableLayoutPanel _headerLayout = null!;
    private Label _appTitleLabel = null!;
    private Label _appSubtitleLabel = null!;
    private Label _statusHeaderLabel = null!;
    private ModernTabControl _tabControl = null!;

    // Home tab
    private TableLayoutPanel _homeLayout = null!;
    private ModernPanel _statusCard = null!;
    private TextBox _statusBox = null!;
    private ModernPanel _actionsCard = null!;
    private TableLayoutPanel _actionsLayout = null!;
    private ModernButton _installBtn = null!;
    private ModernButton _restartSvcBtn = null!;
    private ModernButton _uninstallBtn = null!;
    private ModernPanel _logCard = null!;
    private RichTextBox _homeLog = null!;

    // Users tab
    private TableLayoutPanel _usersLayout = null!;
    private ModernPanel _createCard = null!;
    private TableLayoutPanel _createForm = null!;
    private TextBox _usernameTxt = null!;
    private TextBox _passwordTxt = null!;
    private TextBox _confirmPwdTxt = null!;
    private TextBox _fullnameTxt = null!;
    private ModernButton _createUserBtn = null!;
    private ModernButton _changePwdBtn = null!;
    private TableLayoutPanel _usersRight = null!;
    private TableLayoutPanel _usersToolbar = null!;
    private ModernButton _refreshUsersBtn = null!;
    private ModernButton _addToRdpBtn = null!;
    private ModernButton _removeFromRdpBtn = null!;
    private ModernButton _deleteUserBtn = null!;
    private ModernPanel _allUsersCard = null!;
    private ListBox _allUsersList = null!;
    private ModernPanel _rdpUsersCard = null!;
    private ListBox _rdpUsersList = null!;

    // INI tab
    private TableLayoutPanel _iniLayout = null!;
    private TableLayoutPanel _iniToolbar = null!;
    private ModernButton _loadIniBtn = null!;
    private ModernButton _saveIniBtn = null!;
    private ModernButton _importIniBtn = null!;
    private ModernButton _onlineUpdateBtn = null!;
    private TextBox _urlTxt = null!;
    private ModernButton _downloadUrlBtn = null!;
    private Label _iniPathLabel = null!;
    private RichTextBox _iniEditor = null!;

    // Analyze tab
    private TableLayoutPanel _analyzeLayout = null!;
    private TableLayoutPanel _analyzeTop = null!;
    private Label _termsrvVerLabel = null!;
    private ModernButton _analyzeBtn = null!;
    private ModernPanel _reportCard = null!;
    private RichTextBox _analysisReport = null!;
    private ModernButton _addToIniBtn = null!;
    private ModernPanel _genCard = null!;
    private RichTextBox _generatedIni = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        // --- Window ---
        Text = $"RDPWrap Tool v{AppVersion} - 多用户远程桌面工具";
        Size = new Size(960, 680);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Theme.StyleForm(this);

        // --- Root: header strip on top, tabs below ---
        _rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Background,
            Margin = new Padding(0),
        };
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Background,
            Margin = new Padding(18, 12, 18, 6),
        };
        _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        _headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _tabControl = new ModernTabControl { Dock = DockStyle.Fill, Margin = new Padding(12, 0, 12, 12) };

        _rootLayout.Controls.Add(_headerLayout, 0, 0);
        _rootLayout.Controls.Add(_tabControl, 0, 1);

        BuildHeader();
        BuildHomeTab();
        BuildUsersTab();
        BuildIniTab();
        BuildAnalyzeTab();
        Controls.Add(_rootLayout);

        // --- Wiring (moved here from the inline layout) ---
        _tabControl.SelectedIndexChanged += (s, e) => OnTabChanged();
        Load += MainForm_Load;

        _installBtn.Click += (s, e) => InstallRdpWrap();
        _uninstallBtn.Click += (s, e) => UninstallRdpWrap();
        _restartSvcBtn.Click += (s, e) => RestartService();

        _createUserBtn.Click += (s, e) => CreateUser();
        _changePwdBtn.Click += (s, e) => ChangePassword();
        _refreshUsersBtn.Click += (s, e) => RefreshUserLists();
        _addToRdpBtn.Click += (s, e) => AddToRdpGroup();
        _removeFromRdpBtn.Click += (s, e) => RemoveFromRdpGroup();
        _deleteUserBtn.Click += (s, e) => DeleteUser();

        _loadIniBtn.Click += (s, e) => LoadIni();
        _saveIniBtn.Click += (s, e) => SaveIni();
        _importIniBtn.Click += (s, e) => ImportIni();
        _onlineUpdateBtn.Click += async (s, e) => await OnlineUpdateDefault();
        _downloadUrlBtn.Click += async (s, e) => await OnlineUpdateCustom();

        _analyzeBtn.Click += async (s, e) => await RunAnalysis();
        _addToIniBtn.Click += (s, e) => AddAnalysisToIni();
    }

    private void BuildHeader()
    {
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Background,
            Margin = new Padding(0),
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

        _appTitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "RDPWrap Tool",
            Font = Theme.TitleFont,
            ForeColor = Theme.Primary,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0),
        };
        _appSubtitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "多用户远程桌面工具",
            Font = Theme.SubFont,
            ForeColor = Theme.TextSecondary,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0),
        };
        left.Controls.Add(_appTitleLabel, 0, 0);
        left.Controls.Add(_appSubtitleLabel, 0, 1);

        _statusHeaderLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "状态检测中...",
            Font = Theme.UIFont,
            ForeColor = Theme.TextSecondary,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0),
        };

        _headerLayout.Controls.Add(left, 0, 0);
        _headerLayout.Controls.Add(_statusHeaderLabel, 1, 0);
    }

    // ----------------------------------------------------------------- Home
    private void BuildHomeTab()
    {
        var page = new TabPage("主页");
        page.BackColor = Theme.Background;
        page.UseVisualStyleBackColor = false;

        _homeLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.Background,
            Margin = new Padding(0),
        };
        _homeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        _homeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        _homeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 248));
        _homeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _statusCard = Card("系统状态");
        _statusBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Font = Theme.MonoSmall,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.TextPrimary,
            Margin = new Padding(0),
        };
        _statusCard.Controls.Add(_statusBox);

        _actionsCard = Card("操作");
        _actionsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface,
            Margin = new Padding(0),
        };
        _actionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _actionsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        _actionsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        _actionsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));

        _installBtn = Fill("安装 RDPWrap", ButtonRole.Primary);
        _restartSvcBtn = Fill("重启远程服务", ButtonRole.Accent);
        _uninstallBtn = Fill("卸载 RDPWrap", ButtonRole.Danger);
        _actionsLayout.Controls.Add(_installBtn, 0, 0);
        _actionsLayout.Controls.Add(_restartSvcBtn, 0, 1);
        _actionsLayout.Controls.Add(_uninstallBtn, 0, 2);
        _actionsCard.Controls.Add(_actionsLayout);

        _logCard = Card("操作日志");
        _homeLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = Theme.MonoSmall,
            BackColor = Theme.LogBackground,
            ForeColor = Theme.LogForeground,
            Margin = new Padding(0),
        };
        _logCard.Controls.Add(_homeLog);

        _homeLayout.Controls.Add(_statusCard, 0, 0);
        _homeLayout.Controls.Add(_actionsCard, 1, 0);
        _homeLayout.Controls.Add(_logCard, 0, 1);
        _homeLayout.SetColumnSpan(_logCard, 2);

        page.Controls.Add(_homeLayout);
        _tabControl.TabPages.Add(page);
    }

    // ----------------------------------------------------------------- Users
    private void BuildUsersTab()
    {
        var page = new TabPage("用户管理");
        page.BackColor = Theme.Background;
        page.UseVisualStyleBackColor = false;

        _usersLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Background,
            Margin = new Padding(0),
        };
        _usersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        _usersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _usersLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Left: create user form
        _createCard = Card("新建用户");
        _createForm = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            BackColor = Theme.Surface,
            Margin = new Padding(0),
        };
        _createForm.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        _createForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _createForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _createForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _createForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _createForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _createForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        _createForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _usernameTxt = MkInput();
        _passwordTxt = MkInput(usePassword: true);
        _confirmPwdTxt = MkInput(usePassword: true);
        _fullnameTxt = MkInput();
        _createForm.Controls.Add(MkFieldLabel("用户名:"), 0, 0);
        _createForm.Controls.Add(_usernameTxt, 1, 0);
        _createForm.Controls.Add(MkFieldLabel("密码:"), 0, 1);
        _createForm.Controls.Add(_passwordTxt, 1, 1);
        _createForm.Controls.Add(MkFieldLabel("确认密码:"), 0, 2);
        _createForm.Controls.Add(_confirmPwdTxt, 1, 2);
        _createForm.Controls.Add(MkFieldLabel("全名(可选):"), 0, 3);
        _createForm.Controls.Add(_fullnameTxt, 1, 3);

        _createUserBtn = Fill("创建用户并加入RDP组", ButtonRole.Primary);
        _createForm.Controls.Add(_createUserBtn, 0, 4);
        _createForm.SetColumnSpan(_createUserBtn, 2);

        _changePwdBtn = Fill("修改选中用户密码", ButtonRole.Subtle);
        _createForm.Controls.Add(_changePwdBtn, 0, 5);
        _createForm.SetColumnSpan(_changePwdBtn, 2);

        _createCard.Controls.Add(_createForm);

        // Right: toolbar + two list cards side by side
        _usersRight = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.Background,
            Margin = new Padding(0),
        };
        _usersRight.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _usersRight.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _usersRight.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _usersRight.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _usersToolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 8),
        };
        _usersToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        _usersToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _usersToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _usersToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _usersToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _usersToolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _refreshUsersBtn = Fill("刷新用户列表", ButtonRole.Subtle);
        _addToRdpBtn = Tool("加入RDP组", ButtonRole.Success);
        _removeFromRdpBtn = Tool("移出RDP组", ButtonRole.Warning);
        _deleteUserBtn = Tool("删除用户", ButtonRole.Danger);
        _usersToolbar.Controls.Add(_refreshUsersBtn, 0, 0);
        _usersToolbar.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background }, 1, 0);
        _usersToolbar.Controls.Add(_addToRdpBtn, 2, 0);
        _usersToolbar.Controls.Add(_removeFromRdpBtn, 3, 0);
        _usersToolbar.Controls.Add(_deleteUserBtn, 4, 0);

        _allUsersCard = Card("所有本地用户");
        _allUsersList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = Theme.MonoSmall,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.TextPrimary,
            Margin = new Padding(0),
            IntegralHeight = false,
        };
        _allUsersCard.Controls.Add(_allUsersList);

        _rdpUsersCard = Card("远程桌面用户组");
        _rdpUsersList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = Theme.MonoSmall,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.TextPrimary,
            Margin = new Padding(0),
            IntegralHeight = false,
        };
        _rdpUsersCard.Controls.Add(_rdpUsersList);

        _usersRight.Controls.Add(_usersToolbar, 0, 0);
        _usersRight.SetColumnSpan(_usersToolbar, 2);
        _usersRight.Controls.Add(_allUsersCard, 0, 1);
        _usersRight.Controls.Add(_rdpUsersCard, 1, 1);

        _usersLayout.Controls.Add(_createCard, 0, 0);
        _usersLayout.Controls.Add(_usersRight, 1, 0);

        page.Controls.Add(_usersLayout);
        _tabControl.TabPages.Add(page);
    }

    // ----------------------------------------------------------------- INI
    private void BuildIniTab()
    {
        var page = new TabPage("INI 配置");
        page.BackColor = Theme.Background;
        page.UseVisualStyleBackColor = false;

        _iniLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Background,
            Margin = new Padding(0),
        };
        _iniLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _iniLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        _iniLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        _iniLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _iniToolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 8),
        };
        _iniToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _iniToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _iniToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _iniToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _iniToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _iniToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _iniToolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _loadIniBtn = Tool("加载", ButtonRole.Subtle);
        _saveIniBtn = Tool("保存", ButtonRole.Primary);
        _importIniBtn = Tool("从文件导入", ButtonRole.Subtle);
        _onlineUpdateBtn = Tool("在线更新(默认源)", ButtonRole.Accent);
        _downloadUrlBtn = Tool("下载", ButtonRole.Accent);
        _urlTxt = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = Theme.UIFont,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            PlaceholderText = "自定义INI下载URL",
            Margin = new Padding(8, 10, 8, 10),
        };

        _iniToolbar.Controls.Add(_loadIniBtn, 0, 0);
        _iniToolbar.Controls.Add(_saveIniBtn, 1, 0);
        _iniToolbar.Controls.Add(_importIniBtn, 2, 0);
        _iniToolbar.Controls.Add(_onlineUpdateBtn, 3, 0);
        _iniToolbar.Controls.Add(_urlTxt, 4, 0);
        _iniToolbar.Controls.Add(_downloadUrlBtn, 5, 0);

        _iniPathLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.MonoSmall,
            ForeColor = Theme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(2, 0, 0, 4),
        };

        _iniEditor = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = Theme.MonoFont,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.TextPrimary,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            Margin = new Padding(0),
        };

        _iniLayout.Controls.Add(_iniToolbar, 0, 0);
        _iniLayout.Controls.Add(_iniPathLabel, 0, 1);
        _iniLayout.Controls.Add(_iniEditor, 0, 2);
        page.Controls.Add(_iniLayout);
        _tabControl.TabPages.Add(page);
    }

    // --------------------------------------------------------------- Analyze
    private void BuildAnalyzeTab()
    {
        var page = new TabPage("自动分析");
        page.BackColor = Theme.Background;
        page.UseVisualStyleBackColor = false;

        _analyzeLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.Background,
            Margin = new Padding(0),
        };
        _analyzeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _analyzeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        _analyzeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        _analyzeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        _analyzeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));

        _analyzeTop = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 8),
        };
        _analyzeTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _analyzeTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        _analyzeTop.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _termsrvVerLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "termsrv.dll 版本: 检测中...",
            Font = Theme.HeaderFont,
            ForeColor = Theme.Primary,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
        };
        _analyzeBtn = Fill("开始自动分析", ButtonRole.Primary);
        _analyzeTop.Controls.Add(_termsrvVerLabel, 0, 0);
        _analyzeTop.Controls.Add(_analyzeBtn, 1, 0);

        _reportCard = Card("分析报告");
        _analysisReport = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = Theme.MonoSmall,
            BackColor = Theme.LogBackground,
            ForeColor = Theme.LogForeground,
            Margin = new Padding(0),
        };
        _reportCard.Controls.Add(_analysisReport);

        _addToIniBtn = Fill("将分析结果添加到INI文件", ButtonRole.Accent);

        _genCard = Card("生成的INI配置 (可直接编辑后添加)");
        _generatedIni = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = Theme.MonoFont,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.TextPrimary,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            Margin = new Padding(0),
        };
        _genCard.Controls.Add(_generatedIni);

        _analyzeLayout.Controls.Add(_analyzeTop, 0, 0);
        _analyzeLayout.Controls.Add(_reportCard, 0, 1);
        _analyzeLayout.Controls.Add(_addToIniBtn, 0, 2);
        _analyzeLayout.Controls.Add(_genCard, 0, 3);
        page.Controls.Add(_analyzeLayout);
        _tabControl.TabPages.Add(page);
    }

    // ---------------------------------------------------------------- helpers
    private static ModernPanel Card(string title)
    {
        return new ModernPanel
        {
            Dock = DockStyle.Fill,
            Title = title,
            AccentColor = Theme.Accent,
            Margin = new Padding(6),
        };
    }

    private static ModernButton Fill(string text, ButtonRole role)
    {
        return new ModernButton
        {
            Text = text,
            Role = role,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(6, 6, 6, 6),
        };
    }

    private static ModernButton Tool(string text, ButtonRole role)
    {
        return new ModernButton
        {
            Text = text,
            Role = role,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4, 6, 4, 6),
        };
    }

    private static Label MkFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = Theme.UIFont,
            ForeColor = Theme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0),
        };
    }

    private static TextBox MkInput(bool usePassword = false)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Font = Theme.UIFont,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            UseSystemPasswordChar = usePassword,
            Margin = new Padding(0, 4, 0, 4),
        };
    }
}
