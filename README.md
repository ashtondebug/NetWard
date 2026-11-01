# NetWard 网界卫士

> 面向经授权的 Windows 共享计算机网页访问管理原型：本地策略管理、显式代理、审计记录与后续端点管理能力。

NetWard（网界卫士）旨在逐步发展为由**管理端**与**被控端**组成的 Windows 网页访问管控系统。当前仓库仍是早期原型：已经具备本地策略管理和回环显式代理，但尚未具备远程设备管理或系统级流量拦截。

## 这是什么

项目的长期目标包括：

- 管理端管理多个经过授权的 Windows 被控端，配置策略、查看审计记录、处理访问申请。
- 被控端执行白名单、黑名单、限时访问及经管理员启用的类别策略，并向管理端同步状态。
- 提供清晰的本地拦截提示、管理员审批流程、合法退出与故障恢复。

**当前可运行的范围**是本地 WinForms 管理界面、策略/持久化组件和仅监听 `127.0.0.1` 的显式 HTTP/HTTPS CONNECT 代理。代理只处理明确配置为使用它的应用请求；它不强制其他程序走代理，也不应被描述为系统级过滤器。

## 当前状态

| 能力 | 状态 | 范围与限制 |
| --- | --- | --- |
| 白名单/黑名单及域名匹配 | Alpha 已实现 | 仅应用于显式代理收到的请求；重叠规则取最具体命中 |
| 限时访问 | Alpha 已实现 | 类别叠加于基础策略；首次访问启动每日连续墙钟倒计时，关闭浏览器不暂停 |
| 三类拦截页 | Alpha 已实现 | `block.html` 普通拦截、`timeout.html` 限时到期、`violation.html` 分类违规；HTTP 可显示，HTTPS CONNECT 不解密 |
| 分类网站管控 | Alpha 已实现 | 管理端从 Markdown 导入、可启用/停用；命中显示违规页并记录为违规事件 |
| 被控端程序 `NetWard.Agent` | Alpha 新增 | 启动/停止代理、状态与实时事件、启动受管浏览会话 |
| 受管浏览会话 | Alpha 新增 | 独立浏览器 profile + `--proxy-server`，Edge/Chrome 生效；会话结束即回收 |
| 访问申请 | 仅界面 | HTTP 页面预填尝试网址并填写理由；提交尚未发送、保存或通知管理员 |
| HTTP 完整网址日志 | 已实现 | 包含路径和查询字符串；查询参数可能含敏感信息 |
| HTTPS 目标日志 | 部分可见 | 仅可见 CONNECT 主机名和端口；加密路径/查询不可见 |
| 30 天日志留存 | 本地已实现 | 代理启动及运行时每小时清理；管理端启动时也清理 |
| 设备 IP、交互式 Windows 用户名、浏览时长 | 未实现 | 需要经授权的端点身份与会话计时机制 |
| 老机型版 / 最新支持版双版本交付 | Alpha 双前端 | Legacy：WinForms 管理端 + `NetWard.Agent`；Modern：`NetWard.Modern`（WinUI 3，net9）通过引擎桥复用同一数据库与代理，真实读写策略/事件 |
| 远程配对、管理端通知、策略/日志同步 | 未实现 | 尚无认证设备通信协议，单机部署 |
| Kiosk 全屏及管理员授权退出 | 未实现 | 受管浏览是普通窗口，不是锁屏 kiosk |
| 安装/卸载及系统级访问控制 | 未实现 | 提供构建/启动脚本，不包含安装器；不安装驱动、服务或修改系统网络设置 |

更完整的需求、技术边界和逐项状态见[产品需求与实现状态](docs/product-requirements.md)及[开发路线图](docs/roadmap.md)。

## 快速开始

### 环境要求

- Windows。
- .NET SDK 9.0（构建）。
- .NET Framework 4.8（当前本地应用的目标；Legacy 版若支持 Windows 7 SP1，须保持 4.8 基线，不得将该兼容目标理解为 4.8.1）。
- 首次构建需能从 NuGet 还原 .NET Framework 参考程序集及 SQLite 依赖。

### 构建并测试

在仓库根目录运行：

```powershell
dotnet build .\NetWard.sln --configuration Release
.\scripts\test.ps1
```

测试脚本会构建解决方案、运行策略/SQLite/代理集成测试，并执行代理命令行冒烟测试。网络测试仅使用本机回环服务，不依赖外部网站。

### 启动本地代理

```powershell
.\src\WebBlocker.ProxyHost\bin\Release\net48\NetWard.ProxyHost.exe `
  --mode allowlist `
  --rule subdomains:example.com `
  --database "$env:TEMP\NetWard\netward-dev.db"
```

代理只监听 `127.0.0.1:18080`。需要在**测试应用中手动**将 HTTP/HTTPS 代理设置为该地址；项目不会自动修改 Windows 或浏览器网络设置。按 `Ctrl+C` 停止代理。

- 使用 `--mode blocklist` 启用黑名单模式。
- 使用 `--rule exact:example.com` 匹配精确主机；`subdomains:example.com` 同时匹配子域。
- 使用 `--port 0` 选择可用端口，使用 `--help` 查看完整参数。
- 首次运行需提供策略模式和规则；有意设置空规则时才使用 `--allow-empty-rules`。
- 默认数据库路径为 `%LOCALAPPDATA%\WebBlocker\proxy.db`；示例使用独立开发数据库。

HTTP 被拒请求会收到本地拦截页。HTTPS CONNECT 被拒时代理返回 403；代理不安装证书、不解密 TLS，也不能在加密连接内显示本地 HTML。直接连接、其他代理、VPN、DoH、QUIC/HTTP/3 等流量可能绕过或避开本原型。


### 运行 Alpha（推荐方式）

```powershell
.\scripts\build.ps1
.\scripts\start-admin.bat     # 配置策略、导入分类名单、查看日志
.\scripts\start-agent.bat     # 启动被控端，然后点“启动受管浏览”
```

Alpha 的完整链路是：管理端保存策略 → 被控端点“启动被控端”载入策略并监听随机回环端口 →
点“启动受管浏览”打开一个被强制走该代理的浏览器会话 → 访问被拦域名时看到对应拦截页，
同时被控端右侧实时列出访问事件。

分类名单从管理端「不良网站管控」页导入 Markdown 文件；示例格式见
[`samples/category-list.example.md`](samples/category-list.example.md)（只含保留测试域名，不含真实名单）。

**边界（务必阅读）**：受管浏览只保证该会话内的浏览器流量经过代理，不修改系统代理、
不拦截直接连接、其他浏览器、VPN 或 DoH。它是一份可验证的 Alpha，不是不可绕过的系统级管控。
### 启动本地管理端

在另一个 PowerShell 窗口中运行：

```powershell
.\src\WebBlocker.Administrator\bin\Release\net48\NetWard.Administrator.exe `
  --database "$env:TEMP\NetWard\netward-dev.db"
```

管理端可编辑白/黑名单规则和限时访问类别、查看/恢复策略历史、分页查询访问日志。此处与上面的代理示例使用同一测试数据库。保存策略不会热更新已运行的代理；验证新策略时应重启代理。

## Modern 版（WinUI 3，管理端 + 被控端）

两个角色都提供 WinUI 3 原生界面，使用官方组件：`NavigationView` 侧边栏、`InfoBar`、`ListView`、`AutoSuggestBox`、
`ComboBox`、`NumberBox`、`ContentDialog`、`DropDownButton` + `RadioMenuFlyoutItem` 主题菜单；被控端额外使用 `MicaBackdrop`。

| 应用 | 项目 | 职责 |
| --- | --- | --- |
| Modern 管理端 | `src/NetWard.Modern.Administrator` | 总览、网页访问规则、限时访问、分类名单导入与启用、访问日志、策略历史与恢复；**启用/暂停管理**；PerMonitorV2 高 DPI |
| Modern 被控端 | `src/NetWard.Modern` | 单页极简且**只读**：设备名称/IP、连接码、连接状态与运行时间、断线回退提示；启停管理只能由管理端操作 |

两者都通过 `src/NetWard.EngineBridge`（net48）复用已验证的引擎和同一个 `%LOCALAPPDATA%\\WebBlocker\\proxy.db`，
不引入第二套数据格式。截图：[管理端](docs/images/modern-admin.png)、[被控端](docs/images/modern-agent.png)。

```powershell
dotnet build .\\src\\NetWard.Modern.Administrator\\NetWard.Modern.Administrator.csproj -c Release
.\\src\\NetWard.Modern.Administrator\\bin\\Release\\net9.0-windows10.0.19041.0\\win-x64\\NetWard.Modern.Administrator.exe
.\\src\\NetWard.Modern\\bin\\Release\\net9.0-windows10.0.19041.0\\win-x64\\NetWard.Modern.exe
```

管理端的策略编辑会写入数据库；被控端重启「启动保护」后加载新策略。边界与 Legacy Alpha 相同：
不修改系统代理，不拦截直接连接、其他浏览器、VPN 或 DoH。

## 功能需求摘要

以下是项目所有者提出的目标，不代表目前已经完成：

1. **策略与限时访问**：白名单、黑名单及叠加的限时类别策略。额度按类别配置；首次访问启动每日连续墙钟计时，关闭浏览器不暂停，耗尽后拦截并显示独立提示页，次日本地零点重置。
2. **申请访问**：拦截页中提供“申请访问”；尝试访问的网址自动填入，用户补充理由并提交。管理端通过 Windows 通知收到申请，支持限时放行、拒绝、限时忽略；忽略期间同一被控端对同一目标的重复申请不再重复通知。
3. **不良网站分类**：管理端显示色情、赌博等类别；具体分类和域名只从所有者提供并审核的 MD 文件导入，不得贡献者自行编造。开启后命中类别应使用区别于普通黑/白名单及超时页的违规提示。
4. **违规提示与取证文案**：醒目显示“此计算机因访问违规内容已被阻断”和“请等待管理人员与你联系”。所有者已明确不使用摄像头；不得暗中采集摄像头/屏幕，也不得声称未实际采集的证据或日志字段已被固定。
5. **审计与留存**：目标字段包括设备 IP、完整网址、访问时间、网站/主机名、Windows 用户名、请求方式、策略结果及浏览时长，记录保留 30 天。HTTP 完整 URL 可见且可能包含敏感查询参数；当前 HTTPS 仅能看到主机名/端口。回环代理无法直接识别设备 IP 或交互式用户；请求耗时也不等于浏览时长。
6. **全屏体验**：违规提示需免用户确认全屏。普通 HTML 无权在当前浏览器加载后强制全屏；目标方案是浏览前启动可见的 kiosk 浏览会话。浏览器优先级为 Edge、Chrome、Windows 默认浏览器；最后一种回退不保证全屏。退出需管理员授权且必须有合法恢复/卸载途径。
7. **Windows UI 与兼容性**：管理端采用 Windows 原生设计语言，并分别面向 Windows 7、10、11验证。没有在真实设备测试的平台不得宣称兼容通过。
8. **安全与交付**：设备需明确授权后配对；通信需身份认证、加密、撤销、重放/重复处理和设备隔离。最终交付目标包括独立 EXE、安装/卸载、审计和恢复流程；当前均未完整实现。

逐项验收、约束和实现状态维护在[产品需求文档](docs/product-requirements.md)；按优先级排列的开发工作维护在[路线图](docs/roadmap.md)。

## 模块地图

| 路径 | 组件 | 职责与现状 |
| --- | --- | --- |
| `src/WebBlocker.Shared` | 策略核心 | 域名规范化、白/黑名单与限时规则评估 |
| `src/WebBlocker.Filtering` | 显式代理 | HTTP/CONNECT 请求处理、策略判定、拦截页 |
| `src/WebBlocker.Persistence` | 本地存储 | SQLite 策略、历史、限时窗口和访问事件 |
| `src/WebBlocker.Administrator` | 管理端 | 本地 WinForms 规则、类别占位 UI 和日志查看 |
| `src/WebBlocker.ProxyHost` | 代理宿主 | 命令行配置、运行代理及数据库维护 |
| `src/NetWard.EngineBridge` | 引擎桥（net48） | 为 WinUI 前端提供 JSON 查询与代理托管，复用同一 SQLite 策略/事件库 |
| `src/NetWard.Modern` | Modern 前端（WinUI 3） | 真实策略/分类/事件视图，启停保护、受管浏览、打开 Legacy 管理端 |
| `src/WebBlocker.Agent` | 被控端（Alpha） | 代理控制台、受管浏览会话、实时访问事件 |
| `tests/WebBlocker.Shared.Tests` | 自动化测试 | 策略、迁移、持久化、HTTP/CONNECT 集成测试 |
| `docs` | 项目文档 | 产品需求、安全设计、协议方向、兼容性、路线图 |
| `scripts` | 开发脚本 | 构建、测试与命令行冒烟验证 |

## 架构、数据与安全边界

- 当前是单机原型，代理只绑定 loopback；没有配对、远程同步、登录或管理员角色边界。
- SQLite 默认位于当前 Windows 用户的 `%LOCALAPPDATA%\WebBlocker\proxy.db`。访问事件在 30 天后清理；备份不会自动创建。
- HTTP 日志含完整请求 URL（可能含敏感查询参数）。HTTPS CONNECT 不解密，仅记录可见的目标主机和端口。
- 事件代表代理的策略判断，不证明目标网页成功加载；事件数据库目前也不是防篡改审计库。
- 本项目不采集摄像头/屏幕图像；设备 IP、交互式 Windows 用户名和浏览时长当前未实现。
- 不配置代理、不修改 DNS/hosts/防火墙、不安装 WFP 驱动或 Windows 服务，不阻止设备所有者合法恢复软件。
- 部署仅限获得设备所有者或授权管理员同意的测试环境。不要将当前原型用于系统级访问控制。

## 仓库结构

```text
NetWard.sln
src/
  WebBlocker.Shared/
  WebBlocker.Filtering/
  WebBlocker.Persistence/
  WebBlocker.Administrator/
  WebBlocker.ProxyHost/
  NetWard.EngineBridge/
  NetWard.Modern/
tests/
  WebBlocker.Shared.Tests/
docs/
scripts/
website/                 # Static product website and user guide
```

## 文档导航

[文档索引](docs/README.md)按主题列出所有文档。常用入口：

- [完整产品需求与当前实现状态](docs/product-requirements.md)
- [开发路线图与下一步 TODO](docs/roadmap.md)
- [架构与实现状态](docs/architecture.md)
- [安全设计与风险](docs/security.md)
- [Windows 兼容性说明](docs/compatibility.md)
- [WFP 可行性评估](docs/wfp-feasibility.md)
- [通信协议设计方向](docs/protocol.md)
- [安装与开发](docs/installation.md)
- [故障排查](docs/troubleshooting.md)
- [贡献指南](CONTRIBUTING.md)
- [版本变更记录](CHANGELOG.md)

## 产品官网

静态产品官网源码位于 [`website/`](website/)。该目录包含产品介绍、明确区分当前实现与规划能力的使用文档，以及主题切换和响应式布局。如何本地预览、部署及更新仓库和开发者链接，请见[官网开发说明](website/README.md)。

## 协作与许可

欢迎通过 GitHub Issues 讨论需求或提交 Pull Request。开始前请阅读[贡献指南](CONTRIBUTING.md)，并以[产品需求文档](docs/product-requirements.md)和[路线图](docs/roadmap.md)为准。贡献需附测试结果，并如实注明尚未验证的平台和功能边界。

当前仓库没有声明开源许可证。GitHub 上可见不等于获得复制、分发或再许可的授权；提交代码或复用内容前，请先确认仓库后续发布的许可证及贡献条款。
