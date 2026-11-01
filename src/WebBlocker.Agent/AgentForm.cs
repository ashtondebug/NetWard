using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebBlocker.Filtering;
using WebBlocker.Persistence;
using WebBlocker.Shared;

namespace WebBlocker.Agent
{
    internal sealed class AgentForm : Form
    {
        private readonly string _databasePath;
        private readonly Timer _refreshTimer;
        private readonly Timer _sessionTimer;

        private Label _statusLabel;
        private Label _statusDetailLabel;
        private TextBox _databaseBox;
        private Label _policySummaryLabel;
        private Label _categorySummaryLabel;
        private Label _browserLabel;
        private Button _startButton;
        private Button _stopButton;
        private Button _browseButton;
        private Button _browseAgainButton;
        private Button _stopBrowserButton;
        private DataGridView _eventGrid;
        private Label _eventCountLabel;

        private DomainFilteringProxy _proxy;
        private Process _browserProcess;
        private ManagedBrowserTarget _browserTarget;
        private string _sessionProfileDirectory;
        private long _policyVersion;
        private AccessPolicy _policy;
        private bool _refreshing;

        public AgentForm(string databasePath)
        {
            _databasePath = databasePath;
            BuildLayout();
            _refreshTimer = new Timer { Interval = 2000 };
            _refreshTimer.Tick += (sender, args) => RefreshEvents();
            _sessionTimer = new Timer { Interval = 5000 };
            _sessionTimer.Tick += (sender, args) => UpdateSessionState();
            Shown += (sender, args) => OnFormShown();
            FormClosing += OnFormClosing;
        }

        private void BuildLayout()
        {
            Text = "NetWard 网界卫士 - 被控端 (Alpha)";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(880, 600);
            Size = new Size(980, 680);
            BackColor = Color.FromArgb(246, 248, 251);
            Font = new Font("Microsoft YaHei UI", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(16)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // Header
            var header = new Panel { Dock = DockStyle.Fill, Height = 56 };
            var title = new Label
            {
                Text = "NetWard 网界卫士",
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(28, 40, 56),
                AutoSize = true,
                Location = new Point(0, 0)
            };
            _statusLabel = new Label
            {
                Text = "未启动",
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 60, 40),
                AutoSize = true,
                Location = new Point(0, 30)
            };
            header.Controls.Add(title);
            header.Controls.Add(_statusLabel);
            root.Controls.Add(header, 0, 0);

            // Status card
            var statusCard = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                AutoSize = true,
                Padding = new Padding(12),
                BackColor = Color.White
            };
            statusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            statusCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (var i = 0; i < 5; i++)
            {
                statusCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            _statusDetailLabel = AddStatusRow(statusCard, 0, "状态", "未启动。点击“启动被控端”开始接管本机显式代理流量。");
            _databaseBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = _databasePath };
            AddControlRow(statusCard, 1, "数据库", _databaseBox);
            _policySummaryLabel = AddStatusRow(statusCard, 2, "策略", "尚未加载策略。请先在管理端保存策略。");
            _categorySummaryLabel = AddStatusRow(statusCard, 3, "分类", "尚未导入分类名单。");
            _browserLabel = new Label { Text = "-", AutoSize = true, ForeColor = Color.FromArgb(70, 80, 95) };
            AddControlRow(statusCard, 4, "浏览器", _browserLabel);
            root.Controls.Add(statusCard, 0, 1);

            // Actions
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 44, WrapContents = false };
            _startButton = CreateButton("启动被控端", true);
            _startButton.Click += (sender, args) => StartAgent();
            _stopButton = CreateButton("停止被控端", false);
            _stopButton.Enabled = false;
            _stopButton.Click += (sender, args) => StopAgent();
            _browseButton = CreateButton("启动受管浏览", true);
            _browseButton.Click += (sender, args) => StartManagedBrowsing();
            _browseAgainButton = CreateButton("打开新页面", false);
            _browseAgainButton.Enabled = false;
            _browseAgainButton.Click += (sender, args) => OpenAnotherPage();
            _stopBrowserButton = CreateButton("结束浏览会话", false);
            _stopBrowserButton.Enabled = false;
            _stopBrowserButton.Click += (sender, args) => StopManagedBrowsing();
            var adminButton = CreateButton("打开管理端", false);
            adminButton.Click += (sender, args) => OpenAdministrator();
            var refreshButton = CreateButton("刷新", false);
            refreshButton.Click += (sender, args) => { LoadPolicy(); RefreshEvents(); };

            actions.Controls.Add(_startButton);
            actions.Controls.Add(_stopButton);
            actions.Controls.Add(_browseButton);
            actions.Controls.Add(_browseAgainButton);
            actions.Controls.Add(_stopBrowserButton);
            actions.Controls.Add(adminButton);
            actions.Controls.Add(refreshButton);
            root.Controls.Add(actions, 0, 2);

            // Events
            var eventsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.White,
                Padding = new Padding(12)
            };
            eventsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            eventsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var eventsHeader = new Panel { Dock = DockStyle.Fill, Height = 26 };
            var eventsTitle = new Label
            {
                Text = "最近的访问事件",
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, 0)
            };
            _eventCountLabel = new Label
            {
                Text = "",
                AutoSize = true,
                ForeColor = Color.FromArgb(110, 120, 135),
                Location = new Point(140, 2)
            };
            eventsHeader.Controls.Add(eventsTitle);
            eventsHeader.Controls.Add(_eventCountLabel);
            eventsPanel.Controls.Add(eventsHeader, 0, 0);

            _eventGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            _eventGrid.Columns.Add("time", "时间");
            _eventGrid.Columns.Add("domain", "目标主机");
            _eventGrid.Columns.Add("method", "方法");
            _eventGrid.Columns.Add("result", "结果");
            _eventGrid.Columns[0].Width = 130;
            _eventGrid.Columns[2].Width = 70;
            _eventGrid.Columns[3].Width = 110;
            _eventGrid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            _eventGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            _eventGrid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            eventsPanel.Controls.Add(_eventGrid, 0, 1);
            root.Controls.Add(eventsPanel, 0, 3);

            var footer = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = Color.FromArgb(110, 120, 135),
                Text = "Alpha 版仅过滤受管浏览会话内、显式指向本机代理的流量；不修改系统代理，不拦截直接连接与 VPN。"
            };
            root.Controls.Add(footer, 0, 4);
        }

        private static Button CreateButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                Padding = new Padding(12, 6, 12, 6),
                Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat
            };
            if (primary)
            {
                button.BackColor = Color.FromArgb(37, 99, 235);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderSize = 0;
            }

            return button;
        }

        private static Label AddStatusRow(TableLayoutPanel panel, int row, string caption, string value)
        {
            var valueLabel = new Label { Text = value, AutoSize = true, ForeColor = Color.FromArgb(70, 80, 95) };
            AddControlRow(panel, row, caption, valueLabel);
            return valueLabel;
        }

        private static void AddControlRow(TableLayoutPanel panel, int row, string caption, Control control)
        {
            var label = new Label
            {
                Text = caption,
                AutoSize = true,
                ForeColor = Color.FromArgb(110, 120, 135),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(control, 1, row);
        }

        private void OnFormShown()
        {
            LoadPolicy();
            RefreshEvents();
            _refreshTimer.Start();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs eventArgs)
        {
            _refreshTimer.Stop();
            _sessionTimer.Stop();
            StopManagedBrowsing();
            StopAgent();
            ManagedBrowserLauncher.RestoreInternetExplorerProxy();
        }

        private void LoadPolicy()
        {
            try
            {
                var store = new AccessEventStore(_databasePath);
                PolicySnapshot snapshot;
                if (!store.TryLoadPolicy(out snapshot))
                {
                    _policy = null;
                    _policyVersion = 1;
                    _statusDetailLabel.Text = "数据库中没有已保存的策略。请先在管理端保存策略。";
                    _policySummaryLabel.Text = "尚无策略";
                    _categorySummaryLabel.Text = "尚无分类名单";
                    _browseButton.Enabled = false;
                    return;
                }

                _policy = snapshot.Policy;
                _policyVersion = snapshot.Version;
                var enabledRules = _policy.Rules.Count(rule => rule.IsEnabled);
                var modeText = _policy.Mode == AccessMode.AllowList ? "白名单" : "黑名单";
                _policySummaryLabel.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "版本 {0} · {1} · 启用规则 {2} / 共 {3}",
                    _policyVersion,
                    modeText,
                    enabledRules,
                    _policy.Rules.Count);
                var enabledCategories = _policy.Categories.Count(category => category.IsEnabled);
                _categorySummaryLabel.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "已导入 {0} 个类别（启用 {1}）· 分类规则 {2} 条",
                    _policy.Categories.Count,
                    enabledCategories,
                    _policy.CategoryRules.Count);
                _browseButton.Enabled = _proxy != null;
                UpdateStatus();
            }
            catch (Exception exception)
            {
                _statusDetailLabel.Text = "读取策略失败：" + exception.Message;
                _policySummaryLabel.Text = "策略不可用";
                _categorySummaryLabel.Text = "策略不可用";
                _browseButton.Enabled = false;
            }
        }

        private void StartAgent()
        {
            if (_proxy != null)
            {
                return;
            }

            try
            {
                var store = new AccessEventStore(_databasePath);
                PolicySnapshot snapshot;
                if (!store.TryLoadPolicy(out snapshot))
                {
                    MessageBox.Show(
                        "数据库中没有已保存的策略。请先在管理端保存策略，再启动被控端。",
                        "NetWard 网界卫士 - 被控端",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var proxy = new DomainFilteringProxy(
                    snapshot.Policy,
                    0,
                    eventStore: store,
                    policyVersion: snapshot.Version);
                proxy.ClientError += (sender, args) =>
                    BeginInvoke(new Action(() =>
                        _statusDetailLabel.Text = "代理报告错误：" + args.Exception.Message));
                proxy.ValidateBlockPages();
                proxy.Start();

                _proxy = proxy;
                _policy = snapshot.Policy;
                _policyVersion = snapshot.Version;
                LoadPolicy();

                var browser = ManagedBrowserLauncher.DetectPreferred();
                _browserLabel.Text = browser == null
                    ? "未找到 Edge / Chrome / IE，无法启动受管浏览（代理仍可用）。"
                    : browser.DisplayName + "（受管会话将强制使用本机代理）";

                _startButton.Enabled = false;
                _stopButton.Enabled = true;
                _browseButton.Enabled = true;
                UpdateStatus();
            }
            catch (Exception exception)
            {
                _statusDetailLabel.Text = "启动失败：" + exception.Message;
                MessageBox.Show(
                    "无法启动被控端：\r\n" + exception.Message,
                    "NetWard 网界卫士 - 被控端",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void StopAgent()
        {
            if (_proxy == null)
            {
                return;
            }

            StopManagedBrowsing();
            try
            {
                _proxy.StopAsync().GetAwaiter().GetResult();
                _proxy.Dispose();
            }
            catch (Exception exception)
            {
                _statusDetailLabel.Text = "停止时出现错误：" + exception.Message;
            }
            finally
            {
                _proxy = null;
                _startButton.Enabled = true;
                _stopButton.Enabled = false;
                _browseButton.Enabled = false;
                UpdateStatus();
            }
        }

        private void StartManagedBrowsing()
        {
            if (_proxy == null)
            {
                MessageBox.Show(
                    "请先启动被控端。",
                    "NetWard 网界卫士 - 被控端",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                var target = ManagedBrowserLauncher.DetectPreferred();
                if (target == null)
                {
                    MessageBox.Show(
                        "未找到可用于受管浏览的浏览器（需要 Microsoft Edge、Google Chrome 或 Internet Explorer）。\r\n代理仍在运行，你也可以在任意应用中手动把代理设置为 127.0.0.1:" + _proxy.Port + "。",
                        "NetWard 网界卫士 - 被控端",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _sessionProfileDirectory = Path.Combine(
                    AgentPaths.GetManagedProfileRoot(),
                    "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                _browserTarget = target;
                _browserProcess = ManagedBrowserLauncher.Launch(
                    target,
                    _proxy.Port,
                    "https://www.bing.com/",
                    _sessionProfileDirectory);
                _sessionTimer.Start();
                _browseAgainButton.Enabled = true;
                _stopBrowserButton.Enabled = true;
                _browseButton.Enabled = false;
                UpdateStatus();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "无法启动受管浏览会话：\r\n" + exception.Message,
                    "NetWard 网界卫士 - 被控端",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OpenAnotherPage()
        {
            if (_proxy == null || _browserTarget == null)
            {
                return;
            }

            try
            {
                _browserProcess = ManagedBrowserLauncher.Launch(
                    _browserTarget,
                    _proxy.Port,
                    "https://www.bing.com/",
                    _sessionProfileDirectory);
                _sessionTimer.Start();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "无法打开新页面：\r\n" + exception.Message,
                    "NetWard 网界卫士 - 被控端",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void StopManagedBrowsing()
        {
            _sessionTimer.Stop();
            var usedInternetExplorer = _browserTarget != null &&
                                       _browserTarget.Kind == ManagedBrowserKind.InternetExplorer;
            try
            {
                if (_browserProcess != null && !_browserProcess.HasExited)
                {
                    _browserProcess.Kill();
                }
            }
            catch (Exception)
            {
            }

            _browserProcess = null;
            _browserTarget = null;
            if (usedInternetExplorer)
            {
                ManagedBrowserLauncher.RestoreInternetExplorerProxy();
            }

            _browseAgainButton.Enabled = false;
            _stopBrowserButton.Enabled = false;
            _browseButton.Enabled = _proxy != null;
            UpdateStatus();
        }

        private void UpdateSessionState()
        {
            if (_browserProcess != null && _browserProcess.HasExited)
            {
                _browserProcess = null;
            }

            _stopBrowserButton.Enabled = _browserProcess != null;
            _browseAgainButton.Enabled = _browserProcess != null;
            _browseButton.Enabled = _proxy != null && _browserProcess == null;
        }

        private void UpdateStatus()
        {
            if (_proxy == null)
            {
                _statusLabel.Text = "未启动";
                _statusLabel.ForeColor = Color.FromArgb(180, 60, 40);
                _statusDetailLabel.Text = "未启动。点击“启动被控端”开始接管本机显式代理流量。";
                return;
            }

            _statusLabel.Text = "运行中 · 127.0.0.1:" + _proxy.Port;
            _statusLabel.ForeColor = Color.FromArgb(22, 130, 80);
            _statusDetailLabel.Text = _browserProcess == null
                ? "代理正在运行。点击“启动受管浏览”打开一个被强制走代理的浏览器会话。"
                : "受管浏览会话进行中。关闭浏览器或点击“结束浏览会话”可结束。";
        }

        private void RefreshEvents()
        {
            if (_refreshing)
            {
                return;
            }

            _refreshing = true;
            try
            {
                var store = new AccessEventStore(_databasePath);
                var page = store.QueryAccessEvents(false, null, 1, 100);
                _eventGrid.Rows.Clear();
                foreach (var accessEvent in page.Events)
                {
                    var index = _eventGrid.Rows.Add(
                        accessEvent.OccurredAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        accessEvent.Domain,
                        accessEvent.Method,
                        DescribeResult(accessEvent.Result));
                    var row = _eventGrid.Rows[index];
                    if (accessEvent.Result == ProxyAccessResult.BlockedByCategory)
                    {
                        row.DefaultCellStyle.ForeColor = Color.FromArgb(180, 30, 30);
                        row.DefaultCellStyle.Font = new Font(_eventGrid.Font, FontStyle.Bold);
                    }
                    else if (accessEvent.IsBlocked)
                    {
                        row.DefaultCellStyle.ForeColor = Color.FromArgb(190, 110, 20);
                    }
                }

                _eventCountLabel.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "共 {0} 条（显示最近 {1} 条）",
                    page.TotalCount,
                    page.Events.Count);
            }
            catch (Exception)
            {
                _eventCountLabel.Text = "读取事件失败";
            }
            finally
            {
                _refreshing = false;
            }
        }

        private static string DescribeResult(ProxyAccessResult result)
        {
            switch (result)
            {
                case ProxyAccessResult.AllowedByPolicy:
                    return "允许";
                case ProxyAccessResult.BlockedByPolicy:
                    return "黑/白名单拦截";
                case ProxyAccessResult.BlockedByTimeLimit:
                    return "限时已用完";
                case ProxyAccessResult.BlockedByCategory:
                    return "违规内容拦截";
                case ProxyAccessResult.BlockPageFailure:
                    return "拦截页异常";
                default:
                    return result.ToString();
            }
        }

        private void OpenAdministrator()
        {
            var directory = Path.GetDirectoryName(Application.ExecutablePath);
            if (directory == null)
            {
                return;
            }

            var administratorPath = Path.Combine(directory, "NetWard.Administrator.exe");
            if (!File.Exists(administratorPath))
            {
                MessageBox.Show(
                    "未找到管理端程序：\r\n" + administratorPath,
                    "NetWard 网界卫士 - 被控端",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Process.Start(administratorPath, "--database \"" + _databasePath + "\"");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer.Dispose();
                _sessionTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}