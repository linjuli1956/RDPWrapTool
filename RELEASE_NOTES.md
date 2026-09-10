# RDPWrapTool 当前版本

本版本修复了「自动分析通过但多用户/远程自己仍不可用」的根本原因，并让部署具备校验与自动回滚能力。

## 本次修复（重要）

### 1. SLInit 数据块不再靠 build 号猜测，改为从 termsrv.dll 推导

旧实现按 build 号在两张硬编码表里选布局（build ≥ 22000 用 LayoutA = `+0,+4,+8,+10,+18,+1C,+24,+28`），
再用同一张表去校验自己生成的地址（循环校验，永远通过）。

实测证明该布局对 Windows 11 24H2+/25H2/26H1 的多款 build 是错的：真实布局是
`+0,+4,+8,+10,+1C,+20,+28,+2C`（与社区 INI 的 28000.1516+ / 26100 后期一致）。
写错地址会把 `1` 写进 `bVailGuest`、把 `0` 写进 `bWVDEnabled`、把 `1` 写进 `ulMaxDebugSessions`，
并漏写真正的 `bFUSEnabled` —— 实测会导致 TermService 启动后 **3389 完全不监听**（事件日志：
本地会话管理器 id=17「远程桌面服务启动失败 0x800706BA」，且不再出现 id=258「侦听程序已开始侦听」）。

现在新增 `SlInitLayoutResolver`，用两份来自二进制自身的证据推导布局：

- termsrv.dll 内部 WPP 追踪字符串
  `CSLQuery::Initialize - SLGetWindowsInformationDWORD for <变量名>`：
  该字符串的 `lea` 之后就是该变量的存储指令，因此可以精确恢复
  `bRemoteConnAllowed / bMultimonAllowed / bAppServerAllowed / lMaxUserSessions / ulMaxDebugSessions / bFUSEnabled` 的地址。
- 存储指令形态：布尔槽是 `cmp [v],0` + 写 0/1 的形态，计数槽是直接写回查询结果；
  `lMaxUserSessions`、`ulMaxDebugSessions` 必须是计数槽，四个 Allowed/FUS 必须是布尔槽。

两条独立路径（WPP 命名 / 已知布局+类型签名）在本机上得到同一答案，且都不唯一时**拒绝写入 INI**。

### 2. 部署必须校验，失败自动回滚

- 部署前备份 `C:\Windows\System32\rdpwrap.ini`。
- 部署后校验：TermService 状态、**3389 是否真的在监听**、事件 258/17、rdpwrap.dll 日志里的补丁回读。
- 校验失败时按阶梯处理：完整 section → 去掉 SLInitHook 的安全变体 → 回滚到部署前的 INI，
  每一步都重新校验，保证机器不会被留在「服务在跑但 RDP 连不上」的状态。

### 3. rdpwrap.dll 诊断日志终于可用

在线 INI 的 `[Main] LogFile=\rdpwrap.txt` 指向服务账户写不了的位置，此前所有诊断（补丁原始字节/写入字节/回读）
都写不出来。现在部署时会自动改成可写绝对路径 `C:\ProgramData\RDPWrapTool\rdpwrap.txt` 并授权
`NETWORK SERVICE`，工具的诊断输出会直接读取它。

### 4. 其它

- 总览页/安装页显示 **3389 监听状态**、最近事件 258/17、rdpwrap 日志路径（此前只显示服务状态，会出现「服务 Running 但 RDP 完全不可用」却显示正常）。
- 安装部署页新增「恢复到可用状态」：删除本工具生成的版本 section 并重启校验。
- 新增 `rdpwrap.generated.txt` 清单，只删除本工具生成的 section（含别名 section），不会误删社区 section。
- 修复 `PEAnalyzer.RVAToOffset`：`.data` 的 VirtualSize(0x53C8) 远大于 RawSize(0x1000)，旧实现会把
  虚拟尾部 RVA 映射到 `.pdata` 的字节。
- 修正服务停止逻辑：不再无条件强杀 svchost，强杀前告警、强杀后等待旧 PID 退出再启动。

## 其它原有变化

- 单 exe 发布：`rdpwrap.dll` 和 `rdpwrap.ini` 已内置到 `RDPWrapTool.exe`。
- 安装 RDPWrap 时自动启用 Windows 远程桌面和防火墙规则。
- 创建用户、加入 RDP 用户组后自动启用 Windows 远程桌面。
- 新增 `PROJECT_STRUCTURE.md`，说明项目目录、文件用途、构建和打包命令。

## 验证记录（2026-09-10，Windows 10 Pro for Workstations 26H1 / build 28000.2952 / termsrv 10.0.28000.2952）

事故现场（部署前）：

- 3389 **无任何监听**，TCP 连 `127.0.0.2:3389` 与局域网地址均被 refused；
  `TermService` 仍显示 Running，`rdpwrap.dll`、`termsrv.dll` 均已加载。
- 事件日志：RCM id=258（侦听启动）最后一次在开机时（19:02:47，当时 INI 无本机版本 section、未打补丁）；
  19:10:12 写入并部署自动分析生成的 section → 19:10:14 LSM id=17「远程桌面服务启动失败 0x800706BA」，此后无 258。

修复后实测：

| 项目 | 结果 |
| --- | --- |
| 布局推导（WPP 名字路径） | `0,4,8,10,1C,20,28,2C`，与社区 `10.0.28000.1516+` 布局一致 |
| 布局推导（`--no-wpp` 退化路径） | 同一布局（两条独立路径一致） |
| 分析总判定 | `SUCCESS - all four patch points byte-verified` |
| 部署校验 | `Stage=full Success=True Listen=True Evt258=True Evt17=False Patches=True SLInit=True` |
| 3389 监听 | `0.0.0.0:3389` / `[::]:3389` Listen；`qwinsta` 显示 `rdp-tcp 65536 Listen` |
| RDP 协议探测 | `127.0.0.2:3389` 返回 X.224 Connection Confirm（selectedProtocol=2） |
| DLL 日志回读 | LocalOnly `EB`；SingleUser `B8 01 00 00 00 90 90`；DefPolicy `C7 87 38 06 00 00 00 01 00 00 EB`；SLInit 8 个变量回读一致 |
| 安全网自检 `--selftest-bad-layout` | 先用旧错误布局复现 3389 失去监听 → 自动回退为不带 SLInitHook → 3389 恢复（`stage=no-slinit-hook`） |
| 一键恢复 `--restore` | 精确删除本工具生成的两个别名 section 并恢复监听（不误删社区 section） |
| 状态校验 `--verify` | `Stage=verified Success=True Listen=True Evt258=True Evt17=False Patches=True SLInit=True` |

仍需人工确认的一项（需要交互式登录凭据，自动化无法覆盖）：

- console 已用某账号登录后，用**同一账号** `mstsc /v:127.0.0.2`，`qwinsta` 应同时显示两个 Active 会话
  （console 不被踢掉）。若不符合预期，请附 `C:\ProgramData\RDPWrapTool\rdpwrap.txt` 与当时的 `qwinsta` 输出。

## 新增命令行自检（排障用）

```text
RDPWrapTool.exe --analyze <termsrv.dll> [--out <log>] [--no-wpp] [--emit-layout-evidence <json>]
RDPWrapTool.exe --verify   [--out <log>]        当前 RDP 状态校验
RDPWrapTool.exe --deploy   [--out <log>]        分析 + 写入 + 部署 + 校验（失败自动回滚）
RDPWrapTool.exe --restore  [--out <log>]        移除自动生成的 section + 重启 + 校验
RDPWrapTool.exe --selftest-bad-layout           故意写入旧错误布局，验证回滚/安全变体有效
```

## 发布文件

普通用户只需要下载：

```text
RDPWrapTool.exe
```

仓库内对应路径：

```text
bin\RDPWrapTool.exe
```

## 构建方式

在项目根目录执行：

```powershell
dotnet restore src\RDPWrapTool\RDPWrapTool.csproj
dotnet build src\RDPWrapTool\RDPWrapTool.csproj -c Release
Copy-Item src\RDPWrapTool\bin\Release\net48\RDPWrapTool.exe bin\RDPWrapTool.exe -Force
```

更完整的构建和打包说明见：

```text
PROJECT_STRUCTURE.md
```
