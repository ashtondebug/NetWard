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
| 白名单/黑名单及域名匹配 | 原型已实现 | 仅应用于显式代理收到的请求 |
| 限时访问 | 原型已实现 | 类别叠加于基础策略；首次访问启动每日连续墙钟倒计时，关闭浏览器不暂停 |
| 普通/超时拦截页 | 已实现 | HTTP 可显示本地页；HTTPS CONNECT 不解密，也不能替换加密页面 |
| 访问申请 | 仅界面 | HTTP 页面预填尝试网址并填写理由；提交尚未发送、保存或通知管理员 |
| 分类网站管控 | 占位界面 | 显示色情、赌博类别；名单未导入，开关禁用，不执行分类拦截 |
| 违规提示页 | 静态资源 | 尚未与分类策略连接；页面文字不代表额外取证功能 |
| HTTP 完整网址日志 | 已实现 | 包含路径和查询字符串；查询参数可能含敏感信息 |
| HTTPS 目标日志 | 部分可见 | 仅可见 CONNECT 主机名和端口；加密路径/查询不可见 |
| 30 天日志留存 | 本地已实现 | 代理启动及运行时每小时清理；管理端启动时也清理 |
| 设备 IP、交互式 Windows 用户名、浏览时长 | 未实现 | 需要经授权的端点身份与会话计时机制 |
| 远程配对、管理端通知、策略/日志同步 | 未实现 | 尚无认证设备通信协议或被控端程序 |
| Kiosk 全屏及管理员授权退出 | 未实现 | 需先启动受控浏览会话；普通网页不能加载后自行强制全屏 |
| 安装/卸载及系统级访问控制 | 未实现 | 不安装驱动、服务或系统网络配置 |

更完整的需求、技术边界和逐项状态见[产品需求与实现状态](docs/product-requirements.md)及[开发路线图](docs/roadmap.md)。

## 快速开始

### 环境要求

- Windows。
- .NET SDK 9.0（构建）。
- .NET Framework 4.8 或更高版本（运行目标应用）。
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

### 启动本地管理端

在另一个 PowerShell 窗口中运行：

```powershell
.\src\WebBlocker.Administrator\bin\Release\net48\NetWard.Administrator.exe `
  --database "$env:TEMP\NetWard\netward-dev.db"
```

管理端可编辑白/黑名单规则和限时访问类别、查看/恢复策略历史、分页查询访问日志。此处与上面的代理示例使用同一测试数据库。保存策略不会热更新已运行的代理；验证新策略时应重启代理。

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
tests/
  WebBlocker.Shared.Tests/
docs/
scripts/
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

## 协作与许可

欢迎通过 GitHub Issues 讨论需求或提交 Pull Request。开始前请阅读[贡献指南](CONTRIBUTING.md)，并以[产品需求文档](docs/product-requirements.md)和[路线图](docs/roadmap.md)为准。贡献需附测试结果，并如实注明尚未验证的平台和功能边界。

当前仓库没有声明开源许可证。GitHub 上可见不等于获得复制、分发或再许可的授权；提交代码或复用内容前，请先确认仓库后续发布的许可证及贡献条款。
