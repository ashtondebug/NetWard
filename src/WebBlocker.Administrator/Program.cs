using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WebBlocker.Persistence;
using WebBlocker.Shared;

namespace WebBlocker.Administrator
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                var databasePath = ParseDatabasePath(args);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PolicyManagerForm(databasePath));
            }
            catch (ArgumentException exception)
            {
                MessageBox.Show(exception.Message, "NetWard 网界卫士",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 2;
            }
            catch (SQLiteException exception)
            {
                MessageBox.Show("The local policy database operation failed:\r\n" + exception.Message,
                    "NetWard 网界卫士", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
            catch (IOException exception)
            {
                MessageBox.Show("The local policy database could not be read or written:\r\n" + exception.Message,
                    "NetWard 网界卫士", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
            catch (UnauthorizedAccessException exception)
            {
                MessageBox.Show("Access to the local policy database was denied:\r\n" + exception.Message,
                    "NetWard 网界卫士", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
            catch (InvalidOperationException exception)
            {
                MessageBox.Show("The local policy manager could not start:\r\n" + exception.Message,
                    "NetWard 网界卫士", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
        }

        private static string ParseDatabasePath(string[] args)
        {
            var applicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(applicationDataPath))
            {
                throw new InvalidOperationException("The current user's local application-data directory is unavailable.");
            }

            var databasePath = Path.Combine(applicationDataPath, "WebBlocker", "proxy.db");
            for (var index = 0; index < args.Length; index++)
            {
                if (!string.Equals(args[index], "--database", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Usage: NetWard.Administrator.exe [--database path]");
                }

                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("A path is required after --database.");
                }

                databasePath = args[++index];
            }

            return Path.GetFullPath(databasePath);
        }
    }

    internal sealed class PolicyManagerForm : Form
    {
        private readonly AccessEventStore _store;
        private readonly List<DomainRule> _rules = new List<DomainRule>();
        private readonly List<TimedAccessCategory> _timedCategories = new List<TimedAccessCategory>();
        private readonly List<TimedAccessRule> _timedRules = new List<TimedAccessRule>();
        private readonly ComboBox _mode = new ComboBox();
        private readonly TextBox _domain = new TextBox();
        private readonly CheckBox _includeSubdomains = new CheckBox();
        private readonly ListBox _ruleList = new ListBox();
        private readonly TextBox _timedCategoryName = new TextBox();
        private readonly NumericUpDown _timedCategoryMinutes = new NumericUpDown();
        private readonly ListBox _timedCategoryList = new ListBox();
        private readonly TextBox _timedDomain = new TextBox();
        private readonly ComboBox _timedCategory = new ComboBox();
        private readonly CheckBox _timedIncludeSubdomains = new CheckBox();
        private readonly ListBox _timedRuleList = new ListBox();
        private readonly ListBox _historyList = new ListBox();
        private readonly Label _status = new Label();
        private readonly List<PolicyHistoryEntry> _history = new List<PolicyHistoryEntry>();
        private readonly DataGridView _eventGrid = new DataGridView();
        private readonly TextBox _eventDomainSearch = new TextBox();
        private readonly CheckBox _blockedEventsOnly = new CheckBox();
        private readonly Button _previousEventPage = new Button();
        private readonly Button _nextEventPage = new Button();
        private readonly Label _eventPageStatus = new Label();
        private int _eventPageNumber = 1;
        private long _eventTotalCount;
        private const int EventPageSize = 100;

        public PolicyManagerForm(string databasePath)
        {
            _store = new AccessEventStore(databasePath);
            _store.PruneExpiredAccessEvents(DateTime.UtcNow);
            Text = "NetWard 网界卫士 - 管理端";
            MinimumSize = new System.Drawing.Size(850, 570);
            Size = new System.Drawing.Size(980, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new System.Drawing.Font("Segoe UI", 9F);

            BuildLayout(databasePath);
            LoadCurrentPolicy();
            RefreshHistory();
            RefreshEvents();
        }

        private void BuildLayout(string databasePath)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(18)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Controls.Add(root);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.Controls.Add(new Label
            {
                Text = "NetWard 网界卫士本地访问管理",
                Font = new System.Drawing.Font("Segoe UI Semibold", 16F),
                Dock = DockStyle.Fill
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Text = "管理本地策略并查看代理事件；不提供远程设备管理或系统级流量拦截。",
                ForeColor = System.Drawing.Color.DimGray,
                Dock = DockStyle.Fill
            }, 0, 1);
            root.Controls.Add(header, 0, 0);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var policyTab = new TabPage("访问策略");
            var timedAccessTab = new TabPage("限时访问");
            var categorizedProtectionTab = new TabPage("不良网站管控");
            var eventsTab = new TabPage("访问日志");
            tabs.TabPages.Add(policyTab);
            tabs.TabPages.Add(timedAccessTab);
            tabs.TabPages.Add(categorizedProtectionTab);
            tabs.TabPages.Add(eventsTab);
            root.Controls.Add(tabs, 0, 1);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            policyTab.Controls.Add(content);

            var policyPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(0, 0, 14, 0)
            };
            policyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            policyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            policyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            policyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            content.Controls.Add(policyPanel, 0, 0);

            var modeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
            modeRow.Controls.Add(new Label { Text = "策略模式", AutoSize = true, Padding = new Padding(0, 5, 8, 0) });
            _mode.DropDownStyle = ComboBoxStyle.DropDownList;
            _mode.Width = 190;
            _mode.Items.Add("白名单（未匹配默认拒绝）");
            _mode.Items.Add("黑名单（未匹配默认允许）");
            modeRow.Controls.Add(_mode);
            policyPanel.Controls.Add(modeRow, 0, 0);

            var addRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            addRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            addRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _domain.Dock = DockStyle.Fill;
            new ToolTip().SetToolTip(_domain, "example.com");
            addRow.Controls.Add(_domain, 0, 0);
            _includeSubdomains.Text = "包含子域名";
            _includeSubdomains.Checked = true;
            _includeSubdomains.AutoSize = true;
            addRow.Controls.Add(_includeSubdomains, 1, 0);
            var addButton = new Button { Text = "添加", Dock = DockStyle.Fill };
            addButton.Click += AddRule;
            addRow.Controls.Add(addButton, 2, 0);
            addRow.Controls.Add(new Label
            {
                Text = "输入主机名；支持国际化域名。请勿输入 URL、路径或端口。",
                ForeColor = System.Drawing.Color.DimGray,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 5, 0, 0)
            }, 0, 1);
            addRow.SetColumnSpan(addRow.GetControlFromPosition(0, 1), 3);
            policyPanel.Controls.Add(addRow, 0, 1);

            _ruleList.Dock = DockStyle.Fill;
            policyPanel.Controls.Add(_ruleList, 0, 2);
            var ruleActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            var updateButton = new Button { Text = "更新所选", AutoSize = true };
            updateButton.Click += UpdateSelectedRule;
            ruleActions.Controls.Add(updateButton);
            var toggleButton = new Button { Text = "启用/禁用", AutoSize = true };
            toggleButton.Click += ToggleSelectedRule;
            ruleActions.Controls.Add(toggleButton);
            var removeButton = new Button { Text = "删除所选规则", AutoSize = true };
            removeButton.Click += RemoveSelectedRule;
            ruleActions.Controls.Add(removeButton);
            policyPanel.Controls.Add(ruleActions, 0, 3);
            _ruleList.SelectedIndexChanged += SelectRuleForEditing;

            var historyPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            historyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            content.Controls.Add(historyPanel, 1, 0);
            historyPanel.Controls.Add(new Label
            {
                Text = "策略版本历史",
                Font = new System.Drawing.Font("Segoe UI Semibold", 10F),
                Dock = DockStyle.Fill
            }, 0, 0);
            _historyList.Dock = DockStyle.Fill;
            historyPanel.Controls.Add(_historyList, 0, 1);
            var historyActions = new FlowLayoutPanel { Dock = DockStyle.Fill };
            var restoreButton = new Button { Text = "恢复所选版本", AutoSize = true };
            restoreButton.Click += RestoreSelectedVersion;
            historyActions.Controls.Add(restoreButton);
            historyPanel.Controls.Add(historyActions, 0, 2);

            BuildEventLogTab(eventsTab);
            BuildTimedAccessTab(timedAccessTab);
            BuildCategorizedProtectionTab(categorizedProtectionTab);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            var saveButton = new Button { Text = "保存策略", Width = 120, Height = 32 };
            saveButton.Click += SavePolicy;
            actions.Controls.Add(saveButton);
            root.Controls.Add(actions, 0, 2);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.Controls.Add(_status, 0, 0);
            footer.Controls.Add(new Label
            {
                Text = "数据库：" + databasePath + "    |    保存策略后，已运行的代理需重启才会加载新版本。",
                ForeColor = System.Drawing.Color.DimGray,
                AutoEllipsis = true,
                Dock = DockStyle.Fill
            }, 0, 1);
            root.Controls.Add(footer, 0, 3);
        }

        private static void BuildCategorizedProtectionTab(TabPage categorizedProtectionTab)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(18),
                AutoSize = true
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            categorizedProtectionTab.Controls.Add(layout);

            layout.Controls.Add(new Label
            {
                Text = "按类别管理需要限制的网站",
                Font = new System.Drawing.Font("Segoe UI Semibold", 13F),
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = "分类名单尚未导入。当前仅展示类别名称，不会拦截或记录分类判定；收到并审核名单后才能启用防护。",
                ForeColor = System.Drawing.Color.DimGray,
                Dock = DockStyle.Fill,
                AutoSize = false
            }, 0, 1);

            var categories = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                RowCount = 3,
                AutoSize = true,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
            };
            categories.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            categories.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            categories.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            categories.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            categories.Controls.Add(new Label
            {
                Text = "网站类别",
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 0, 0);
            categories.Controls.Add(new Label
            {
                Text = "防护状态",
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 1, 0);
            categories.Controls.Add(new Label
            {
                Text = "名单状态",
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 2, 0);

            AddUnavailableCategoryRow(categories, 1, "色情网站");
            AddUnavailableCategoryRow(categories, 2, "赌博网站");
            layout.Controls.Add(categories, 0, 2);
        }

        private static void AddUnavailableCategoryRow(TableLayoutPanel categories, int row, string categoryName)
        {
            categories.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            categories.Controls.Add(new Label
            {
                Text = categoryName,
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 0, row);
            var protectionSwitch = new CheckBox
            {
                Text = "启用防护",
                AutoSize = true,
                Enabled = false,
                Anchor = AnchorStyles.Left
            };
            categories.Controls.Add(protectionSwitch, 1, row);
            categories.Controls.Add(new Label
            {
                Text = "等待管理员导入名单",
                ForeColor = System.Drawing.Color.DimGray,
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            }, 2, row);
        }

        private void BuildTimedAccessTab(TabPage timedAccessTab)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            timedAccessTab.Controls.Add(layout);

            layout.Controls.Add(new Label
            {
                Text = "限时规则叠加在白/黑名单之上：基础策略先决定是否允许，允许后再按域名类别启动每日连续倒计时。关闭浏览器不会暂停；耗尽后到本机次日零点重置。",
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.DimGray,
                AutoEllipsis = true
            }, 0, 0);

            var categoryEditor = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Padding = new Padding(0, 5, 0, 0)
            };
            _timedCategoryName.Width = 180;
            _timedCategoryMinutes.Minimum = 1;
            _timedCategoryMinutes.Maximum = 1440;
            _timedCategoryMinutes.Value = 30;
            _timedCategoryMinutes.Width = 76;
            categoryEditor.Controls.Add(new Label { Text = "类别", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
            categoryEditor.Controls.Add(_timedCategoryName);
            categoryEditor.Controls.Add(new Label { Text = "每日分钟", AutoSize = true, Padding = new Padding(4, 5, 0, 0) });
            categoryEditor.Controls.Add(_timedCategoryMinutes);
            var saveCategoryButton = new Button { Text = "添加/更新类别", AutoSize = true };
            saveCategoryButton.Click += AddOrUpdateTimedCategory;
            categoryEditor.Controls.Add(saveCategoryButton);
            var removeCategoryButton = new Button { Text = "删除类别", AutoSize = true };
            removeCategoryButton.Click += RemoveTimedCategory;
            categoryEditor.Controls.Add(removeCategoryButton);
            layout.Controls.Add(categoryEditor, 0, 1);

            _timedCategoryList.Dock = DockStyle.Fill;
            _timedCategoryList.SelectedIndexChanged += SelectTimedCategory;
            layout.Controls.Add(_timedCategoryList, 0, 2);

            var ruleEditor = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Padding = new Padding(0, 7, 0, 0)
            };
            _timedDomain.Width = 200;
            new ToolTip().SetToolTip(_timedDomain, "example.com");
            _timedCategory.DropDownStyle = ComboBoxStyle.DropDownList;
            _timedCategory.Width = 160;
            _timedIncludeSubdomains.Text = "包含子域名";
            _timedIncludeSubdomains.Checked = true;
            _timedIncludeSubdomains.AutoSize = true;
            ruleEditor.Controls.Add(_timedDomain);
            ruleEditor.Controls.Add(_timedCategory);
            ruleEditor.Controls.Add(_timedIncludeSubdomains);
            var addTimedRuleButton = new Button { Text = "添加限时网站", AutoSize = true };
            addTimedRuleButton.Click += AddTimedRule;
            ruleEditor.Controls.Add(addTimedRuleButton);
            layout.Controls.Add(ruleEditor, 0, 3);

            _timedRuleList.Dock = DockStyle.Fill;
            _timedRuleList.SelectedIndexChanged += SelectTimedRule;
            layout.Controls.Add(_timedRuleList, 0, 4);

            var ruleActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false
            };
            var updateTimedRuleButton = new Button { Text = "更新所选", AutoSize = true };
            updateTimedRuleButton.Click += UpdateTimedRule;
            ruleActions.Controls.Add(updateTimedRuleButton);
            var toggleTimedRuleButton = new Button { Text = "启用/禁用", AutoSize = true };
            toggleTimedRuleButton.Click += ToggleTimedRule;
            ruleActions.Controls.Add(toggleTimedRuleButton);
            var removeTimedRuleButton = new Button { Text = "删除所选", AutoSize = true };
            removeTimedRuleButton.Click += RemoveTimedRule;
            ruleActions.Controls.Add(removeTimedRuleButton);
            layout.Controls.Add(ruleActions, 0, 5);
        }

        private void LoadCurrentPolicy()
        {
            PolicySnapshot snapshot;
            if (!_store.TryLoadPolicy(out snapshot))
            {
                _mode.SelectedIndex = 0;
                _rules.Clear();
                _timedCategories.Clear();
                _timedRules.Clear();
                RenderRules();
                RenderTimedCategories();
                RenderTimedRules();
                _status.Text = "尚无已保存策略；选择模式并保存后才会写入数据库。";
                return;
            }

            _mode.SelectedIndex = snapshot.Policy.Mode == AccessMode.AllowList ? 0 : 1;
            _rules.Clear();
            _rules.AddRange(snapshot.Policy.Rules);
            _timedCategories.Clear();
            _timedCategories.AddRange(snapshot.Policy.TimedAccessCategories);
            _timedRules.Clear();
            _timedRules.AddRange(snapshot.Policy.TimedAccessRules);
            RenderRules();
            RenderTimedCategories();
            RenderTimedRules();
            _status.Text = string.Format(CultureInfo.CurrentCulture, "当前策略版本：{0}", snapshot.Version);
        }

        private void AddRule(object sender, EventArgs eventArgs)
        {
            try
            {
                var rule = new DomainRule(Guid.NewGuid(), _domain.Text, _includeSubdomains.Checked, true);
                foreach (var existingRule in _rules)
                {
                    if (string.Equals(existingRule.Domain, rule.Domain, StringComparison.Ordinal) &&
                        existingRule.IncludeSubdomains == rule.IncludeSubdomains)
                    {
                        throw new ArgumentException("该域名和匹配范围的规则已存在。");
                    }
                }

                _rules.Add(rule);
                _domain.Clear();
                RenderRules();
                _status.Text = "规则已添加到编辑列表；保存后才会写入策略。";
            }
            catch (ArgumentException exception)
            {
                MessageBox.Show(exception.Message, "无法添加规则",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SelectRuleForEditing(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _ruleList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _rules.Count)
            {
                return;
            }

            var rule = _rules[selectedIndex];
            _domain.Text = rule.Domain;
            _includeSubdomains.Checked = rule.IncludeSubdomains;
        }

        private void UpdateSelectedRule(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _ruleList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _rules.Count)
            {
                MessageBox.Show("请先选择要更新的规则。", "无法更新规则",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var existingRule = _rules[selectedIndex];
                var updatedRule = new DomainRule(
                    existingRule.Id,
                    _domain.Text,
                    _includeSubdomains.Checked,
                    existingRule.IsEnabled);
                for (var index = 0; index < _rules.Count; index++)
                {
                    if (index != selectedIndex &&
                        string.Equals(_rules[index].Domain, updatedRule.Domain, StringComparison.Ordinal) &&
                        _rules[index].IncludeSubdomains == updatedRule.IncludeSubdomains)
                    {
                        throw new ArgumentException("该域名和匹配范围的规则已存在。");
                    }
                }

                _rules[selectedIndex] = updatedRule;
                RenderRules();
                _ruleList.SelectedIndex = selectedIndex;
                _status.Text = "所选规则已更新到编辑列表；保存后才会写入策略。";
            }
            catch (ArgumentException exception)
            {
                MessageBox.Show(exception.Message, "无法更新规则",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ToggleSelectedRule(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _ruleList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _rules.Count)
            {
                MessageBox.Show("请先选择要启用或禁用的规则。", "无法更新规则",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var rule = _rules[selectedIndex];
            _rules[selectedIndex] = new DomainRule(
                rule.Id,
                rule.Domain,
                rule.IncludeSubdomains,
                !rule.IsEnabled);
            RenderRules();
            _ruleList.SelectedIndex = selectedIndex;
            _status.Text = "规则状态已更改；保存后才会写入策略。";
        }

        private void RemoveSelectedRule(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _ruleList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _rules.Count)
            {
                return;
            }

            _rules.RemoveAt(selectedIndex);
            RenderRules();
            _status.Text = "规则已从编辑列表移除；保存后才会更新策略。";
        }

        private void SavePolicy(object sender, EventArgs eventArgs)
        {
            if (_mode.SelectedIndex < 0)
            {
                MessageBox.Show("请选择访问策略模式。", "无法保存策略",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_rules.Count == 0)
            {
                var effect = _mode.SelectedIndex == 0
                    ? "白名单为空会拒绝所有域名。"
                    : "黑名单为空会允许所有域名。";
                if (MessageBox.Show(effect + "\r\n仍要保存吗？", "确认空规则策略",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
            }

            try
            {
                var accessMode = _mode.SelectedIndex == 0 ? AccessMode.AllowList : AccessMode.BlockList;
                var version = _store.SavePolicy(
                    new AccessPolicy(accessMode, _rules, _timedCategories, _timedRules));
                RefreshHistory();
                _status.Text = string.Format(CultureInfo.CurrentCulture,
                    "策略已保存为版本 {0}。如代理已运行，请重启代理以加载新策略。", version);
            }
            catch (ArgumentException exception)
            {
                MessageBox.Show(exception.Message, "无法保存策略",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (SQLiteException exception)
            {
                ShowStoreError(exception);
            }
            catch (IOException exception)
            {
                ShowStoreError(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                ShowStoreError(exception);
            }
        }

        private void AddOrUpdateTimedCategory(object sender, EventArgs eventArgs)
        {
            try
            {
                var category = new TimedAccessCategory(
                    _timedCategoryName.Text,
                    decimal.ToInt32(_timedCategoryMinutes.Value));
                var selectedIndex = _timedCategoryList.SelectedIndex;
                var targetIndex = _timedCategories.FindIndex(
                    item => string.Equals(item.Name, category.Name, StringComparison.OrdinalIgnoreCase));
                if (selectedIndex >= 0 &&
                    selectedIndex < _timedCategories.Count &&
                    targetIndex >= 0 &&
                    targetIndex != selectedIndex)
                {
                    throw new ArgumentException("同名类别已存在，不能将类别合并到该名称。");
                }

                if (selectedIndex >= 0 && selectedIndex < _timedCategories.Count)
                {
                    var previousName = _timedCategories[selectedIndex].Name;
                    if (!string.Equals(previousName, category.Name, StringComparison.Ordinal))
                    {
                        for (var index = 0; index < _timedRules.Count; index++)
                        {
                            var rule = _timedRules[index];
                            if (string.Equals(rule.Category, previousName, StringComparison.OrdinalIgnoreCase))
                            {
                                _timedRules[index] = new TimedAccessRule(
                                    rule.Id,
                                    rule.Domain,
                                    rule.IncludeSubdomains,
                                    category.Name,
                                    rule.IsEnabled);
                            }
                        }
                    }

                    _timedCategories[selectedIndex] = category;
                }
                else if (targetIndex < 0)
                {
                    _timedCategories.Add(category);
                }
                else
                {
                    _timedCategories[targetIndex] = category;
                }

                RenderTimedCategories();
                _timedCategoryList.SelectedIndex = _timedCategories.FindIndex(
                    item => string.Equals(item.Name, category.Name, StringComparison.OrdinalIgnoreCase));
                RenderTimedRules();
                _status.Text = "限时类别已更新到编辑列表；保存策略后才会写入数据库。";
            }
            catch (ArgumentException exception)
            {
                MessageBox.Show(exception.Message, "无法保存限时类别",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RemoveTimedCategory(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _timedCategoryList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _timedCategories.Count)
            {
                return;
            }

            var category = _timedCategories[selectedIndex];
            if (_timedRules.Exists(rule =>
                string.Equals(rule.Category, category.Name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("请先删除或重新分配引用此类别的限时网站。", "无法删除类别",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _timedCategories.RemoveAt(selectedIndex);
            RenderTimedCategories();
            _status.Text = "限时类别已从编辑列表移除；保存后才会写入策略。";
        }

        private void SelectTimedCategory(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _timedCategoryList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _timedCategories.Count)
            {
                return;
            }

            var category = _timedCategories[selectedIndex];
            _timedCategoryName.Text = category.Name;
            _timedCategoryMinutes.Value = category.TimeLimitMinutes;
        }

        private void AddTimedRule(object sender, EventArgs eventArgs)
        {
            if (_timedCategory.SelectedIndex < 0)
            {
                MessageBox.Show("请先创建并选择一个限时类别。", "无法添加限时网站",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var rule = new TimedAccessRule(
                    Guid.NewGuid(),
                    _timedDomain.Text,
                    _timedIncludeSubdomains.Checked,
                    _timedCategory.SelectedItem.ToString(),
                    true);
                EnsureUniqueTimedRule(rule, -1);
                _timedRules.Add(rule);
                _timedDomain.Clear();
                RenderTimedRules();
                _status.Text = "限时网站已添加到编辑列表；保存策略后才会写入数据库。";
            }
            catch (ArgumentException exception)
            {
                MessageBox.Show(exception.Message, "无法添加限时网站",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SelectTimedRule(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _timedRuleList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _timedRules.Count)
            {
                return;
            }

            var rule = _timedRules[selectedIndex];
            _timedDomain.Text = rule.Domain;
            _timedIncludeSubdomains.Checked = rule.IncludeSubdomains;
            _timedCategory.SelectedItem = rule.Category;
        }

        private void UpdateTimedRule(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _timedRuleList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _timedRules.Count)
            {
                MessageBox.Show("请先选择要更新的限时网站。", "无法更新限时网站",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_timedCategory.SelectedIndex < 0)
            {
                MessageBox.Show("请先选择一个限时类别。", "无法更新限时网站",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var existingRule = _timedRules[selectedIndex];
                var updatedRule = new TimedAccessRule(
                    existingRule.Id,
                    _timedDomain.Text,
                    _timedIncludeSubdomains.Checked,
                    _timedCategory.SelectedItem.ToString(),
                    existingRule.IsEnabled);
                EnsureUniqueTimedRule(updatedRule, selectedIndex);
                _timedRules[selectedIndex] = updatedRule;
                RenderTimedRules();
                _timedRuleList.SelectedIndex = selectedIndex;
                _status.Text = "限时网站已更新到编辑列表；保存策略后才会写入数据库。";
            }
            catch (ArgumentException exception)
            {
                MessageBox.Show(exception.Message, "无法更新限时网站",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ToggleTimedRule(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _timedRuleList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _timedRules.Count)
            {
                MessageBox.Show("请先选择要启用或禁用的限时网站。", "无法更新限时网站",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var rule = _timedRules[selectedIndex];
            _timedRules[selectedIndex] = new TimedAccessRule(
                rule.Id,
                rule.Domain,
                rule.IncludeSubdomains,
                rule.Category,
                !rule.IsEnabled);
            RenderTimedRules();
            _timedRuleList.SelectedIndex = selectedIndex;
            _status.Text = "限时网站状态已更改；保存策略后才会写入数据库。";
        }

        private void RemoveTimedRule(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _timedRuleList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _timedRules.Count)
            {
                return;
            }

            _timedRules.RemoveAt(selectedIndex);
            RenderTimedRules();
            _status.Text = "限时网站已从编辑列表移除；保存后才会写入策略。";
        }

        private void EnsureUniqueTimedRule(TimedAccessRule rule, int excludedIndex)
        {
            for (var index = 0; index < _timedRules.Count; index++)
            {
                if (index != excludedIndex &&
                    string.Equals(_timedRules[index].Domain, rule.Domain, StringComparison.Ordinal) &&
                    _timedRules[index].IncludeSubdomains == rule.IncludeSubdomains)
                {
                    throw new ArgumentException("该域名和匹配范围的限时规则已存在。");
                }
            }
        }

        private void RenderTimedCategories()
        {
            var selectedCategory = _timedCategory.SelectedItem == null
                ? null
                : _timedCategory.SelectedItem.ToString();

            _timedCategoryList.BeginUpdate();
            _timedCategory.BeginUpdate();
            try
            {
                _timedCategoryList.Items.Clear();
                _timedCategory.Items.Clear();
                foreach (var category in _timedCategories)
                {
                    _timedCategoryList.Items.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        "{0} — 每日连续 {1} 分钟",
                        category.Name,
                        category.TimeLimitMinutes));
                    _timedCategory.Items.Add(category.Name);
                }

                if (!string.IsNullOrEmpty(selectedCategory))
                {
                    _timedCategory.SelectedItem = _timedCategory.Items
                        .Cast<object>()
                        .FirstOrDefault(item => string.Equals(
                            item.ToString(), selectedCategory, StringComparison.OrdinalIgnoreCase));
                }
                if (_timedCategory.SelectedIndex < 0 && _timedCategory.Items.Count > 0)
                {
                    _timedCategory.SelectedIndex = 0;
                }
            }
            finally
            {
                _timedCategory.EndUpdate();
                _timedCategoryList.EndUpdate();
            }
        }

        private void RenderTimedRules()
        {
            _timedRuleList.BeginUpdate();
            try
            {
                _timedRuleList.Items.Clear();
                foreach (var rule in _timedRules)
                {
                    var category = _timedCategories.Find(item =>
                        string.Equals(item.Name, rule.Category, StringComparison.OrdinalIgnoreCase));
                    var duration = category == null
                        ? "类别缺失"
                        : string.Format(CultureInfo.CurrentCulture, "{0} 分钟/日", category.TimeLimitMinutes);
                    var scope = rule.IncludeSubdomains ? "含子域名" : "仅精确域名";
                    var state = rule.IsEnabled ? string.Empty : "（已禁用）";
                    _timedRuleList.Items.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        "{0}  —  {1}  —  {2}  —  {3}{4}",
                        rule.Domain,
                        rule.Category,
                        duration,
                        scope,
                        state));
                }
            }
            finally
            {
                _timedRuleList.EndUpdate();
            }
        }

        private void RestoreSelectedVersion(object sender, EventArgs eventArgs)
        {
            var selectedIndex = _historyList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _history.Count)
            {
                MessageBox.Show("请先选择一个历史版本。", "无法恢复策略",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedVersion = _history[selectedIndex].Version;
            if (MessageBox.Show(
                string.Format(CultureInfo.CurrentCulture,
                    "将版本 {0} 恢复为当前策略，并创建一个新的版本。是否继续？", selectedVersion),
                "确认恢复策略",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                var restoredVersion = _store.RestorePolicy(selectedVersion);
                LoadCurrentPolicy();
                RefreshHistory();
                _status.Text = string.Format(CultureInfo.CurrentCulture,
                    "版本 {0} 已恢复为新版本 {1}。", selectedVersion, restoredVersion);
            }
            catch (System.Collections.Generic.KeyNotFoundException exception)
            {
                MessageBox.Show(exception.Message, "无法恢复策略",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshHistory();
            }
            catch (SQLiteException exception)
            {
                ShowStoreError(exception);
            }
            catch (IOException exception)
            {
                ShowStoreError(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                ShowStoreError(exception);
            }
        }

        private void RefreshHistory()
        {
            _history.Clear();
            _history.AddRange(_store.QueryPolicyHistory(1, 100));
            _historyList.BeginUpdate();
            try
            {
                _historyList.Items.Clear();
                foreach (var entry in _history)
                {
                    _historyList.Items.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        "v{0}  {1}  {2} 条规则\r\n{3:yyyy-MM-dd HH:mm:ss} UTC",
                        entry.Version,
                        entry.Mode == AccessMode.AllowList ? "白名单" : "黑名单",
                        entry.RuleCount,
                        entry.UpdatedAtUtc));
                }
            }
            finally
            {
                _historyList.EndUpdate();
            }
        }

        private void RenderRules()
        {
            _ruleList.BeginUpdate();
            try
            {
                _ruleList.Items.Clear();
                foreach (var rule in _rules)
                {
                    var scope = rule.IncludeSubdomains ? "含子域名" : "仅精确域名";
                    var state = rule.IsEnabled ? string.Empty : "（已禁用）";
                    _ruleList.Items.Add(rule.Domain + "  —  " + scope + state);
                }
            }
            finally
            {
                _ruleList.EndUpdate();
            }
        }

        private void BuildEventLogTab(TabPage eventsTab)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            eventsTab.Controls.Add(layout);

            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Padding = new Padding(0, 7, 0, 0)
            };
            _eventDomainSearch.Width = 220;
            _eventDomainSearch.KeyDown += EventSearchKeyDown;
            filters.Controls.Add(_eventDomainSearch);
            _blockedEventsOnly.Text = "仅查看拒绝/拦截异常";
            _blockedEventsOnly.AutoSize = true;
            filters.Controls.Add(_blockedEventsOnly);
            var searchButton = new Button { Text = "筛选", AutoSize = true };
            searchButton.Click += ApplyEventFilters;
            filters.Controls.Add(searchButton);
            var refreshButton = new Button { Text = "刷新", AutoSize = true };
            refreshButton.Click += ApplyEventFilters;
            filters.Controls.Add(refreshButton);
            layout.Controls.Add(filters, 0, 0);

            _eventGrid.Dock = DockStyle.Fill;
            _eventGrid.ReadOnly = true;
            _eventGrid.AllowUserToAddRows = false;
            _eventGrid.AllowUserToDeleteRows = false;
            _eventGrid.AllowUserToResizeRows = false;
            _eventGrid.MultiSelect = false;
            _eventGrid.RowHeadersVisible = false;
            _eventGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _eventGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _eventGrid.Columns.Add("OccurredAtUtc", "时间（UTC）");
            _eventGrid.Columns.Add("RequestedUrl", "请求网址/隧道目标");
            _eventGrid.Columns.Add("Domain", "域名");
            _eventGrid.Columns.Add("Method", "方法");
            _eventGrid.Columns.Add("Result", "结果");
            _eventGrid.Columns.Add("MatchedRuleId", "命中规则 ID");
            _eventGrid.Columns.Add("PolicyVersion", "策略版本");
            _eventGrid.Columns["RequestedUrl"].ToolTipText =
                "HTTP 请求会记录完整网址；HTTPS CONNECT 仅能看到目标主机，无法读取加密路径。";
            _eventGrid.Columns["OccurredAtUtc"].FillWeight = 145;
            _eventGrid.Columns["RequestedUrl"].FillWeight = 260;
            _eventGrid.Columns["Domain"].FillWeight = 100;
            _eventGrid.Columns["Method"].FillWeight = 50;
            _eventGrid.Columns["Result"].FillWeight = 95;
            _eventGrid.Columns["MatchedRuleId"].FillWeight = 110;
            _eventGrid.Columns["PolicyVersion"].FillWeight = 55;
            layout.Controls.Add(_eventGrid, 0, 1);

            var paging = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 7, 0, 0)
            };
            _previousEventPage.Text = "上一页";
            _previousEventPage.AutoSize = true;
            _previousEventPage.Click += PreviousEventPage;
            paging.Controls.Add(_previousEventPage);
            _nextEventPage.Text = "下一页";
            _nextEventPage.AutoSize = true;
            _nextEventPage.Click += NextEventPage;
            paging.Controls.Add(_nextEventPage);
            _eventPageStatus.AutoSize = true;
            _eventPageStatus.Padding = new Padding(8, 4, 0, 0);
            paging.Controls.Add(_eventPageStatus);
            layout.Controls.Add(paging, 0, 2);
        }

        private void EventSearchKeyDown(object sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                ApplyEventFilters(sender, EventArgs.Empty);
            }
        }

        private void ApplyEventFilters(object sender, EventArgs eventArgs)
        {
            _eventPageNumber = 1;
            RefreshEvents();
        }

        private void PreviousEventPage(object sender, EventArgs eventArgs)
        {
            if (_eventPageNumber > 1)
            {
                _eventPageNumber--;
                RefreshEvents();
            }
        }

        private void NextEventPage(object sender, EventArgs eventArgs)
        {
            var pageCount = Math.Max(1L, (_eventTotalCount + EventPageSize - 1) / EventPageSize);
            if (_eventPageNumber < pageCount && _eventPageNumber < int.MaxValue)
            {
                _eventPageNumber++;
                RefreshEvents();
            }
        }

        private void RefreshEvents()
        {
            try
            {
                var page = _store.QueryAccessEvents(
                    _blockedEventsOnly.Checked,
                    _eventDomainSearch.Text,
                    _eventPageNumber,
                    EventPageSize);
                _eventTotalCount = page.TotalCount;
                if (_eventTotalCount > 0 &&
                    (long)(_eventPageNumber - 1) * EventPageSize >= _eventTotalCount)
                {
                    _eventPageNumber = (int)Math.Min(
                        int.MaxValue,
                        (_eventTotalCount + EventPageSize - 1) / EventPageSize);
                    page = _store.QueryAccessEvents(
                        _blockedEventsOnly.Checked,
                        _eventDomainSearch.Text,
                        _eventPageNumber,
                        EventPageSize);
                }

                _eventGrid.Rows.Clear();
                foreach (var accessEvent in page.Events)
                {
                    var resultText = accessEvent.Result == ProxyAccessResult.AllowedByPolicy
                        ? "允许"
                        : accessEvent.Result == ProxyAccessResult.BlockedByPolicy
                            ? "拒绝"
                            : accessEvent.Result == ProxyAccessResult.BlockedByTimeLimit
                                ? "限时已耗尽"
                                : "拦截页故障";
                    var rowIndex = _eventGrid.Rows.Add(
                        accessEvent.OccurredAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                                accessEvent.RequestedUrl ??
                                    (accessEvent.Method == "CONNECT" && accessEvent.DestinationPort.HasValue
                                        ? string.Format(
                                            CultureInfo.CurrentCulture,
                                            "HTTPS 隧道（目标端口 {0}，加密路径不可见）",
                                            accessEvent.DestinationPort.Value)
                                        : string.Empty),
                        accessEvent.Domain,
                        accessEvent.Method,
                        resultText,
                        accessEvent.MatchedRuleId.HasValue
                            ? accessEvent.MatchedRuleId.Value.ToString("D")
                            : string.Empty,
                        accessEvent.PolicyVersion);
                    _eventGrid.Rows[rowIndex].Cells["RequestedUrl"].ToolTipText =
                        accessEvent.RequestedUrl ??
                        (accessEvent.Method == "CONNECT"
                            ? string.Format(
                                CultureInfo.CurrentCulture,
                                "代理仅能看到 CONNECT 目标主机和端口 {0}；TLS 加密的完整网址不可见。",
                                accessEvent.DestinationPort)
                            : string.Empty);
                    if (accessEvent.IsBlocked)
                    {
                        _eventGrid.Rows[rowIndex].DefaultCellStyle.ForeColor = System.Drawing.Color.DarkRed;
                    }
                }

                var pageCount = Math.Max(1L, (_eventTotalCount + EventPageSize - 1) / EventPageSize);
                _previousEventPage.Enabled = _eventPageNumber > 1;
                _nextEventPage.Enabled = _eventPageNumber < pageCount;
                _eventPageStatus.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "第 {0}/{1} 页，共 {2} 条记录",
                    _eventPageNumber,
                    pageCount,
                    _eventTotalCount);
            }
            catch (SQLiteException exception)
            {
                ShowStoreError(exception);
            }
            catch (IOException exception)
            {
                ShowStoreError(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                ShowStoreError(exception);
            }
        }

        private static void ShowStoreError(Exception exception)
        {
            MessageBox.Show("本地策略数据库操作失败：\r\n" + exception.Message,
                "NetWard 网界卫士", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
