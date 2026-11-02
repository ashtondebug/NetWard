# PROGRESS

> 本文件按 `AGENTS.md` 第十一阶段要求维护：记录每个阶段完成内容、当前状态与待办，便于隔天续作。
> 记录规则：只写**已验证**的事实；未验证的能力必须标注“未验证”。

---

## 当前阶段：Alpha 已交付（2026-11-01）

### 本轮结论

NetWard 现在是一个**可真正运行的单机 Alpha**：管理端配置策略与分类名单，被控端载入策略、
启动回环代理，并通过受管浏览会话真正执行白名单/黑名单、限时访问与分类违规拦截，
三类拦截页都可被真实触发，且访问事件可在被控端与管理端查看。

构建：`dotnet build NetWard.sln -c Release` → **0 警告 0 错误**，7 个项目。
测试：`scripts\test.ps1` → **30 项全通过**。

### 本轮完成的改动

**策略核心（WebBlocker.Shared）**

- 新增 `CategoryConfig`、`CategoryRule` 与 `AccessPolicy` 的分类集合参数（保持向后兼容的旧构造函数）。
- 新增 `PolicyEvaluator.EvaluateCategory`：分类命中优先于基础策略，返回携带类别的拒绝决策。
- 修复重叠规则匹配：改为「最具体优先」，不再依赖存储顺序，审计记录能指向真正生效的规则。
- `PolicyDecision` 增加 `MatchedCategory`。

**持久化（WebBlocker.Persistence）**

- schema v5：新增 `BlockedCategories`、`BlockedCategoryRules` 及两张 `PolicyVersionBlocked*` 历史表；
  重建 `AccessEvents` 以接受 `BlockedByCategory`（Result=4）；v4→v5 迁移。
- `SavePolicy` / `RestorePolicy` / `TryLoadPolicy` 全量携带分类；新增 `UpdateCategoryState` 用于启用/停用。
- `ProxyAccessEvent.IsKnownResult` 统一结果校验。

**过滤（WebBlocker.Filtering）**

- 新增 `BlockPageCatalog`：按 `ProxyAccessResult` 路由到 `block.html` / `timeout.html` / `violation.html`，
  启动时 `ValidateBlockPages()` 缺失页面即明确报错。
- 普通拦截页由 `index.html` 更名为 `block.html`。
- 事件写入失败不再改变已计算出的响应（记录错误但不误报成功/失败）。
- 拦截页读取失败路径不再二次抛异常，确保 503 一定发出。

**被控端（新增 WebBlocker.Agent）**

- `NetWard.Agent.exe`：状态卡片、启动/停止代理、策略与分类摘要、最近 100 条事件（违规红色高亮）、
  打开管理端、端到端受管浏览按钮。
- `ManagedBrowserLauncher`：Edge/Chrome 使用独立 profile + `--proxy-server`（不改系统设置）；
  IE 回退使用 per-user WinINET 代理并在会话结束时恢复。

**管理端（WebBlocker.Administrator）**

- 「不良网站管控」由占位改为真实功能：Markdown 导入、预览（类别/域名/重复/无法识别的行）、
  逐类别启用开关、保存后生效。
- 新增 `CategoryListParser`；`#` 视为文档标题，`##` 起为类别，支持 `include:subdomains` 与 `*.` 前缀。
- 状态栏改为 `AutoSize`，不再截断“策略已保存为版本 N”这类关键反馈。

**缺陷修复（本轮实测确认）**

- 黑名单模式判定方向错误，已修正并用 30 项测试锁定语义。
- schema 版本超前/库损坏时，代理与管理端不再以未处理异常崩溃，改为可读错误。
- 超长 HTTP 方法名不再走到 500+丢事件（在访问事件构造前由请求层拦截）。

**工程与交付**

- 新增 `scripts\build.ps1`、`scripts\start-agent.bat`、`scripts\start-admin.bat`。
- 新增 `samples\category-list.example.md`（仅含保留测试域名，不含真实名单）。
- 更新 README、CHANGELOG、architecture、installation、roadmap、troubleshooting 至 Alpha 实际状态。

### 端到端验证记录（本机实测）

- 用真实 `WebBlocker.Filtering` 程序集启动代理，allowlist 仅允许 `allowed.example.test`、
  分类规则命中 `adult.example.test`：
  - `adult.example.test` → 403，内容含「此计算机因访问违规内容已被阻断」
  - `blocked.example.test` → 403，内容含「此网页已被管理员设置为禁止访问」
  - `allowed.example.test` → 转发（该保留域名不存在，得到连接错误而非拦截页）
  - 分类命中页不含超时页文案，普通拦截页不含违规页文案
- Edge 156 实测遵守 `--proxy-server` 与独立 `--user-data-dir`，产生 HTTP 与 HTTPS CONNECT 请求。

- 管理端总览移除外层 ScrollViewer，改为「统计卡 Auto + 事件列表 *」的固定行布局；只保留 ListView 自身滚动条，消除双层滚动冲突。

### 访问申请落地与代理稳定性（2026-11-02）

- 拦截页表单改为 `fetch` 提交到本机 `http://127.0.0.1:18765/request`；`AccessRequestServer` 追加保存到 `%LOCALAPPDATA%\\WebBlocker\\access-requests.jsonl`。
- 管理端新增「访问申请」页：读取本机申请，选中后一键把域名加入规则并保存策略。
- 代理 accept 循环遇到客户端重置不再退出；此前监听仍在但不再接受连接，导致未命中网站访问超时/变慢。
- 被控端注册进程退出/未处理异常时恢复系统代理，避免残留失效代理。
- 实测：表单提交生成 JSONL；允许的 HTTPS 走代理约 0.09s；被拦截 HTTPS 返回 1.8MB 拦截页。

### 被拦截 HTTPS 显示拦截页（本地 CA，2026-11-02）

- `HttpsInterception` 仅对被策略拒绝的 443 CONNECT 终止 TLS：签发目标站点叶证书、读取请求头并返回 `block.html` / `timeout.html` / `violation.html`；允许的 HTTPS 仍原样隧道。
- 本地根 CA `CN=NetWard Local CA` 安装到当前用户 Root 存储，私钥与证书位于 `%LOCALAPPDATA%\\WebBlocker\\https-ca.pfx`（Alpha 未加密，安全边界已记录）。
- 修复 TLS 请求头跨包读取导致的浏览器“加载中”卡死。
- 实测普通 Edge（系统代理）访问 `https://www.baidu.com/` 显示 NetWard 拦截页且证书锁正常；HTTP 同样显示拦截页。
- 已知限制：Firefox 默认不信任 Windows 证书库；CA 私钥可签发任意证书，必须仅在授权测试设备使用。

### 默认接管系统代理（2026-11-02）

- 被控端启用管理时自动写入当前用户 WinINET 代理：`ProxyEnable=1`、`ProxyServer=127.0.0.1:<port>`，并临时清除 PAC、追加 `<local>` 绕过。
- 暂停管理或正常退出时从 `%LOCALAPPDATA%\\WebBlocker\\system-proxy.backup.json` 恢复原 `ProxyEnable/ProxyServer/ProxyOverride/AutoConfigURL`，删除备份。
- 启动时若发现遗留备份（上次异常退出），先恢复再按当前管理状态决定是否重新接管。
- 普通 Edge 不经 `--proxy-server` 参数即可命中拦截：实测 `http://www.baidu.com/` 显示拦截页；暂停管理后代理恢复为原 Clash 设置。
- HTTPS 仍只拒绝 CONNECT（403），浏览器显示连接失败，不显示拦截 HTML。

### 策略热更新：管理端保存后被控端立即生效（2026-11-02）

- `DomainFilteringProxy` 在请求路径按 1 秒节流查询数据库策略版本；版本变化时原地替换策略对象，不重启进程、不改变监听端口。
- 新增 `PolicyChanged` 事件；ProxyHost 输出 `POLICY version=N`，被控端解析后 1 秒内刷新界面版本文案。
- 实测同一代理端口：v7 允许 example.com（200）→ v8 加入规则（403）→ v9 移除规则（200），全程未重启被控端。
- 30 项测试与代理冒烟测试保持通过。

### 拦截页彩蛋：world.execute(me);（2026-11-02）

- `block.html` 与 `timeout.html` 的盾牌对号 logo 支持彩蛋：3 秒内快速点击 10 次弹出菜单。
- 菜单「运行」在当前拦截页内用 `iframe srcdoc` 打开终端风格体验，不跳转、不请求 kaixinol.github.io。
- 体验的 CSS、JS、字体、音频全部内联为 data URI；音频从 98kbps 重编码为 32kbps 单声道以控制体积。
- 每页增加约 1.78 MB（内联 base64），页面保持完全离线、自包含；violation.html 未加入。
- 修复重开问题：关闭后清除 iframe 与 data-loaded；兼容不支持 srcdoc 的旧浏览器（document.write 回退）。
- 验证：30 项测试全绿；Edge 无头截图确认菜单与终端页；解码载荷无 kaixinol/jsdelivr 请求。

### 管理控制权收归管理端（2026-11-01）

- 被控端移除全部启停按钮（启动/停止保护、启动受管浏览），不再提供任何自行启用或禁止管理的入口。
- 管理端新增原生 `ToggleSwitch`「启用管理」：开启写入 `%LOCALAPPDATA%\\WebBlocker\\management.state = enabled`，关闭写入 `disabled`；
  关闭前弹出 `ContentDialog` 二次确认。
- 被控端每 2 秒读取该状态：`enabled` 且代理未运行时自动启动，`disabled` 时停止代理；界面显示「管理状态：已启用/已暂停（由管理端控制）」。
- 被控端状态提示明确写出「被控端不允许自行启用管理，请在管理端启用」。
- 实测：状态 `disabled` → 被控端显示「管理已暂停」；状态 `enabled` → 被控端显示「已启用」且本地代理运行；管理端开关正确反映状态。
- 截图：`docs/images/modern-agent-paused.png`、`modern-agent-enabled.png`、`modern-admin-manage.png`。

### 管理端高 DPI、提示自动消失、被控端极简化（2026-11-01）

- 管理端 app.manifest 增加 `PerMonitorV2` + `longPathAware`，并在 170% 缩放下验证文字清晰、2×2 统计卡不挤压。
- 管理端状态 InfoBar 对 Warning/Error 启动 5 秒计时器自动关闭（`SetInfoBar` + `_infoBarTimer`），成功/就绪提示保持。
- 被控端移除 NavigationView 与统计/事件列表，改为单页：设备名称与 IP、连接码、连接状态与运行时间、断线回退 InfoBar、受管浏览按钮。
- 管理端每 2 秒写入 `%LOCALAPPDATA%\\WebBlocker\\admin.heartbeat`；被控端按 8 秒新鲜度判断连接：
  未连接显示连接码，连接后隐藏连接码并显示运行时间，心跳消失后显示「连接已断开」+「已切换到本地拦截策略」。
- 实测三态：未连接（连接码 690547）→ 已连接（运行 00:00:03）→ 断开（显示回退提示、上次连接 00:00:14）。
- 截图：`docs/images/modern-admin.png`、`modern-agent-unconnected.png`、`modern-agent-connected.png`、`modern-agent-disconnected.png`。

### Modern 管理端与被控端双 WinUI 3 应用（2026-11-01）

- `src/NetWard.Modern` 定位为 **Modern 被控端**：`NavigationView` 导航、状态 InfoBar、启动/停止保护、受管浏览、最近事件、只读策略/分类视图。
- 新增 `src/NetWard.Modern.Administrator` **Modern 管理端**：总览、网页访问规则（增删改启停 + 模式切换）、
  限时访问（类别/网站 CRUD）、分类管控（Markdown 浏览/预览/导入/启用）、访问日志（搜索/仅拦截）、策略历史与恢复。
- 两者均使用 WinUI 3 官方组件（NavigationView、InfoBar、ListView、AutoSuggestBox、ComboBox、NumberBox、ContentDialog、
  DropDownButton + RadioMenuFlyoutItem），不再使用自绘按钮或自绘配色。
- `NetWard.EngineBridge` 新增 `save-policy`、`history-json`、`restore-version`、`parse-categories` 与 `events-json` 过滤参数，
  管理端通过 JSON 写回同一 SQLite 策略库（已验证 `save-policy` 返回版本并回读正确）。
- 构建：解决方案 0 警告 0 错误；测试 30 项全绿。

### Modern UI 重构为原生 WinUI 3（2026-11-01 晚）

- 侧边栏由自绘 ToggleButton 改为原生 `NavigationView`（Left 模式、原生选中指示、PaneHeader/PaneFooter）。
- 窗口启用 `MicaBackdrop`；内容区使用系统主题资源（`CardBackgroundFillColorDefaultBrush`、`CardStrokeColorDefaultBrush`、
  `TextFillColorPrimaryBrush`、`TextFillColorSecondaryBrush`、`OverlayCornerRadius` 等），移除自绘色板。
- 状态与边界提示改用原生 `InfoBar`（Success/Informational/Warning）；主题切换改为原生 `ToggleSwitch`。
- 列表与搜索改为原生 `ListView` + `AutoSuggestBox`；主按钮使用 `AccentButtonStyle`。
- 修复 XAML 运行期资源查找失败：`Application.Resources` 需合并 `XamlControlsResources`。
- 重新截图：`docs/images/modern-alpha-overview.png`、`docs/images/modern-alpha-running.png`。
- 仍通过 `--autostart` 自检：代理随机端口 + 经代理请求 `adult.example.test` 返回 403。

### Modern Alpha（WinUI 3）接入（2026-11-01）

- 新增 `src/NetWard.EngineBridge`（net48）：以 `status-json` / `policy-json` / `events-json` 输出真实策略与事件；
  以 `run --parent-pid` 托管代理并输出 `READY port=`，父进程退出或主动停止即结束，不留后台监听。
- 新增 `src/NetWard.Modern`（WinUI 3 / net9.0-windows / Windows App SDK 2.6.4）：
  总览（策略版本、规则数、分类数、事件数）、访问策略、分类管控、事件记录四个页面；
  运行控制支持启动/停止保护、启动受管浏览、打开 Legacy 管理端；主题可切换。
- 视觉与布局沿用 `demos/modern` 的设计参考；`demos/modern` 仍保持静态演示，不接业务。
- 构建时自动把引擎与 Legacy 管理端复制到 Modern 输出目录（`engine\`、`tools\legacy-admin\`），目录可独立运行。

**验证记录**

- 构建：`dotnet build src\NetWard.Modern\NetWard.Modern.csproj -c Release` → 0 警告 0 错误。
- 启动：窗口稳定运行，标题 `NetWard 网界卫士 · Modern Alpha`；截图为真实数据库内容（策略版本 1、1 条基础规则、1 个分类、4 条事件）。
- 真实路径自检（`--autostart`）：应用内「启动保护」路径启动代理于随机端口（实测 9390），
  通过该端口请求 `adult.example.test` 与 `blocked.example.test` 均返回 403，状态栏显示「保护运行中」。
- 截图：`docs/images/modern-alpha-overview.png`、`docs/images/modern-alpha-running.png`。

**Modern 边界**

- Modern 端为只读策略视图 + 运行控制；策略/分类编辑仍由 Legacy 管理端完成。
- 未实现 Modern 被控端 Agent、配对/远程通信、kiosk；受管浏览仍是普通窗口 + 显式代理。
- 未在干净的 Windows 10/11 最低版本机器上做兼容性矩阵测试。

### 已知限制（Alpha 边界，不得夸大）

- 受管浏览只覆盖该会话内的浏览器流量；不修改系统代理、不拦截直接连接/其他浏览器/VPN/DoH。
- 无局域网配对、无设备身份、无加密通信、无远程策略下发与日志同步。
- 无 WFP 驱动、无系统级不可绕过拦截、无 kiosk 锁屏、无安装器与代码签名。
- 分类名单必须由管理员提供；仓库只含保留测试域名的示例。
- Modern (WinUI 3) 仍为静态设计参考，未接入业务逻辑。

### 下一步（待排期）

- [ ] 分类名单：接收并校验管理员提供的正式名单，记录来源/版本/日期。
- [ ] 访问申请：拦截页表单接入被控端本地队列，管理端可查看并审批。
- [ ] 被控端身份：设备 ID、交互式 Windows 用户名、客户端 IP（解决审计归因）。
- [ ] 局域网配对与加密通信（协议方向见 `docs/protocol.md`）。
- [ ] 安装器与卸载、独立版本号、CI。

---

## 已完成（历史）

### 2026-11-01 · 仓库推送与全项目摸查

- 以 `debug_life@qq.com` 身份把主仓库推送到 `github.com/ashtondebug/NetWard`，`website/` 未包含（它是独立仓库）。
- 全项目摸查：23 个 C# 文件 / 5189 行；构建 0 警告；24 项测试全绿；schema v4 实测确认。
- 确认缺陷：规则首匹配而非最具体、IDN（`faß.de → fass.de`）与浏览器不一致、
  schema 超前导致未处理异常崩溃、超长方法名 500 且丢事件、`PRAGMA user_version` 事务外读取。

### 2026-10-29 ~ 10-30 · 原型里程碑（既有）

- 策略引擎、回环显式代理、SQLite v1–v4、管理端 WinForms、限时访问、策略历史、30 天清理。
- 两个 Modern WinUI 3 静态 Demo（不接业务，仅设计参考）。