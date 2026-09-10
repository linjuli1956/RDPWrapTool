# RDPWrapTool

RDPWrapTool 是一个面向 Windows 10/11 x64 的 RDP Wrapper 图形化管理、自动分析、部署和用户管理工具。

> 本项目是非官方衍生项目，与 Microsoft、Stas'M Corp. 及其他上游项目没有隶属或背书关系。请只在你拥有或获授权管理的计算机上使用，并自行确认 Windows 版本、授权条款和组织安全策略。

## 当前版本特点

- 单 exe 发布：`rdpwrap.dll` 和 `rdpwrap.ini` 已内置到 `RDPWrapTool.exe`。
- 自动分析当前系统 `termsrv.dll`，key patch 点与 SLInit 数据块全部**从二进制推导**并通过字节级验证后生成 INI 配置。
- 添加/部署 INI 后自动重启 `TermService`，并**校验 3389 是否真的在监听**；失败自动回退或回滚。
- 安装 RDPWrap 时自动启用 Windows 远程桌面和防火墙规则。
- 创建本地用户或加入远程桌面用户组后，自动启用 Windows 远程桌面。
- 支持手动编辑、导入和在线更新 `rdpwrap.ini`。
- 总览/安装页显示监听状态、侦听事件与 rdpwrap 日志路径，并提供「恢复到可用状态」。

## 下载和使用

普通用户请在 GitHub Releases 下载 `RDPWrapTool.exe`。

使用方式：

1. 将 `RDPWrapTool.exe` 拷贝到目标电脑。
2. 双击运行，接受管理员权限提示。
3. 如果 INI 已支持当前系统版本，可直接安装。
4. 如果 INI 不支持当前系统版本，到“自动分析”页执行分析。
5. 分析通过后点击“添加到 INI 并部署到系统（自动校验+回滚）”，按弹窗提示确认部署结果。
6. 如果远程桌面连不上（3389 未监听），用「安装部署」页的“恢复到可用状态”即可回到可连接状态。

当前版本只需要拷贝一个文件：

```text
RDPWrapTool.exe
```

不需要额外携带：

```text
rdpwrap.dll
rdpwrap.ini
RDPWrapTool.exe.config
```

程序首次运行时会自动释放内置的 `rdpwrap.dll` 和 `rdpwrap.ini`（仅在文件不存在时释放，
不会覆盖已被自动分析修改过的工作副本）。

## 源码目录

完整项目结构和构建说明见：

```text
PROJECT_STRUCTURE.md
```

核心目录：

- `bin/`：最终发布 exe。
- `src/RDPWrapTool/`：C# WinForms 主程序。
- `src/RDPWrapTool/Core/`：安装、分析、INI、服务、用户等核心逻辑。
- `src/RDPWrapTool/Forms/MainForm.cs`：主界面和按钮事件。
- `src/RDPWrapDLL/`：C++ RDPWrap DLL 源码。

## 重新生成 exe

在项目根目录执行：

```powershell
dotnet restore src\RDPWrapTool\RDPWrapTool.csproj
dotnet build src\RDPWrapTool\RDPWrapTool.csproj -c Release
Copy-Item src\RDPWrapTool\bin\Release\net48\RDPWrapTool.exe bin\RDPWrapTool.exe -Force
```

最终发布文件：

```text
bin\RDPWrapTool.exe
```

## 关于自动分析准确性

本项目使用“从二进制推导 + 严格验证，通过才写入”的策略：

- 自动分析会寻找多个关键 patch 点，每个候选 offset 都必须通过字节级验证。
- SLInit 数据块（`bInitialized / bServerSku / lMaxUserSessions / ...`）不再按 build 号从写死的表里猜，
  而是从 `termsrv.dll` 自身推导：用其内部 WPP 追踪字符串
  `CSLQuery::Initialize - SLGetWindowsInformationDWORD for <变量名>` 恢复每个变量的地址，
  用存储指令形态（布尔槽 vs 计数槽）交叉校验。证据不足或结果不唯一时**拒绝写入 INI**。
- 任一关键点验证失败时，不会写入 INI。

### 部署是“校验通过才算成功”，失败自动回滚

- 部署前备份 `System32\rdpwrap.ini`。
- 部署后校验：TermService 状态、**3389 是否真的在监听**、事件 258/17、rdpwrap 日志中的补丁回读。
- 校验失败时依次尝试：完整 section → 不带 SLInitHook 的安全变体 → 回滚到部署前配置，
  最终一定会回到“远程桌面可用”的状态，不会把机器留在“服务在跑但连不上”的状态。
- 部署时会把 `[Main] LogFile` 改成服务账户可写的绝对路径，rdpwrap.dll 的诊断日志从此可见。

如果 Microsoft 大幅修改 `termsrv.dll` 结构，正确表现应是分析失败并拒绝写入或部署后自动回滚，
而不是强行生成配置。

## 命令行自检

```text
RDPWrapTool.exe --analyze <termsrv.dll路径> [--out <日志>] [--no-wpp] [--emit-layout-evidence <json>]
RDPWrapTool.exe --verify      检查当前 3389 监听与补丁日志
RDPWrapTool.exe --deploy      分析 + 写入 INI + 部署 + 校验（失败自动回滚）
RDPWrapTool.exe --restore     删除本工具生成的 section + 重启 + 校验
RDPWrapTool.exe --selftest-bad-layout   故意写入旧错误布局，验证安全变体/回滚有效（自检）
```

界面等价入口：「自动分析」页的“添加到 INI 并部署到系统（自动校验+回滚）”，
以及「安装部署」页的“恢复到可用状态”。

## 排障：远程桌面连不上怎么办

唯一的判断标准是 **3389 是否在监听**（`TermService` 显示 Running 并不代表可用）：

```powershell
Get-NetTCPConnection -LocalPort 3389 -State Listen
qwinsta
```

| 症状 | 处理 |
| --- | --- |
| 3389 未监听，最近有事件 17（RDP 服务启动失败） | 刚部署过自动分析配置 → 点「恢复到可用状态」（或 `--restore`），然后把「日志」页证据发出来 |
| 3389 未监听，事件通道也没有 258/17 | 看 `C:\ProgramData\RDPWrapTool\rdpwrap.txt` 与日志页诊断输出；必要时「卸载 RDPWrap」回到系统原生 `termsrv.dll` |
| 3389 正常，但多用户 / 远程自己不生效 | 确认 `--verify` 输出里 `Patches=True SLInit=True`；确认 INI 含当前版本的 `[10.0.x.y]` 与 `[10.0.x.y-SLInit]`；确认 `RDP-Tcp\fSingleSessionPerUser=0` |
| 找不到 rdpwrap 日志 | 部署时会把 `[Main] LogFile` 改成可写路径并授权 `NETWORK SERVICE`；可手工核对 INI 里的该行 |

详细步骤、证据位置与算法说明见 `PROJECT_STRUCTURE.md` 的「第十二～十四节」。

## 上游与许可证

本项目包含或衍生自以下开源工作：

- [stascorp/rdpwrap](https://github.com/stascorp/rdpwrap)
- [anhkgg/SuperRDP](https://github.com/anhkgg/SuperRDP)
- 社区维护的 `rdpwrap.ini` 配置

RDP Wrapper 相关源码使用 Apache License 2.0。请保留 `LICENSE` 和 `NOTICE`。

## 免责声明

本软件按“原样”提供，不附带任何明示或默示担保。修改远程桌面服务可能造成连接中断、系统更新后失效或与本机安全策略冲突。使用前请准备本地登录和恢复手段。
