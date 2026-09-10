# RDPWrapTool 项目说明

本文档用于给后续开发者快速了解项目结构、每个文件的用途，以及修改代码后如何重新生成最终 exe。

## 一、当前目录树

```text
RDPWrapTool/                                      -- 项目根目录
├─ bin/                                          -- 最终发布目录
│  └─ RDPWrapTool.exe                            -- 主程序；可直接拷贝到其他电脑运行
├─ src/                                          -- 源码目录
│  ├─ RDPWrapTool/                               -- C# WinForms 主程序工程
│  │  ├─ Core/                                   -- 主程序核心功能代码
│  │  │  ├─ AssetManager.cs                      -- 内置资源释放；支持只拷贝一个 exe
│  │  │  ├─ DeployVerifier.cs                    -- 部署结果模型 + 监听/事件/日志三重校验
│  │  │  ├─ DeployWorkflow.cs                    -- 部署编排：备份 → 部署 → 校验 → 安全变体 → 回滚
│  │  │  ├─ IniManager.cs                        -- rdpwrap.ini 加载、保存、导入、写入版本 section
│  │  │  ├─ OffsetVerifier.cs                    -- 自动分析结果的字节级验证逻辑
│  │  │  ├─ OnlineUpdater.cs                     -- 在线下载/更新 rdpwrap.ini
│  │  │  ├─ PEAnalyzer.cs                        -- PE 文件解析；RVA/文件偏移转换和字节搜索
│  │  │  ├─ RDPWrapInstaller.cs                  -- 安装、卸载、部署、启用远程桌面、防火墙配置
│  │  │  ├─ ServiceManager.cs                    -- TermService 启动、停止、重启和诊断
│  │  │  ├─ SlInitLayoutResolver.cs              -- 从 termsrv.dll 推导 SLInit 数据块布局（核心）
│  │  │  ├─ TermSrvAnalyzer.cs                   -- 自动分析 termsrv.dll 并生成 INI 配置
│  │  │  ├─ UserManager.cs                       -- 本地用户创建、删除、改密、加入远程桌面组
│  │  │  └─ VersionHelper.cs                     -- termsrv.dll 版本识别和版本别名生成
│  │  ├─ Forms/                                  -- 界面代码目录
│  │  │  └─ MainForm.cs                          -- 主窗口 UI；按钮、页面、事件逻辑都在这里
│  │  ├─ app.manifest                            -- 程序清单；申请管理员权限
│  │  ├─ Program.cs                              -- 程序入口；支持 GUI 模式和命令行分析模式
│  │  ├─ rdpwrap.dll                             -- 内置到 exe 的 RDPWrap 核心 DLL
│  │  ├─ rdpwrap.ini                             -- 内置到 exe 的初始 INI 配置
│  │  └─ RDPWrapTool.csproj                      -- C# 项目文件；控制编译和资源嵌入
│  └─ RDPWrapDLL/                                -- C++ RDPWrap DLL 源码工程
│     ├─ dllmain.cpp                             -- DLL 入口
│     ├─ Export.def                              -- DLL 导出定义
│     ├─ IniFile.cpp                             -- DLL 内部 INI 解析实现
│     ├─ IniFile.h                               -- DLL 内部 INI 解析头文件
│     ├─ RDPWrap.cpp                             -- DLL hook/wrapper 核心逻辑
│     ├─ RDPWrapDLL.vcxproj                      -- Visual Studio C++ DLL 工程文件
│     ├─ stdafx.cpp                              -- 预编译头实现
│     ├─ stdafx.h                                -- 预编译头
│     ├─ targetver.h                             -- Windows SDK 目标版本配置
│     └─ util.cpp                                -- DLL 辅助函数
└─ PROJECT_STRUCTURE.md                          -- 项目结构、构建、打包、发布说明文档
```

## 二、根目录说明

### `bin/`

最终发布目录。

用户只需要这一个文件：

```text
bin/RDPWrapTool.exe
```

当前版本已经把 `rdpwrap.dll` 和 `rdpwrap.ini` 内置进 exe，所以用户拷贝到其他电脑时只需要拷贝这一个文件。

在本机开发/排障时，这个目录同时充当「工具数据目录」（exe 所在目录可写时优先使用它），因此运行后还会出现：

```text
bin/rdpwrap.dll              -- 从 exe 释放出来的工作用 DLL（部署时复制到 System32）
bin/rdpwrap.ini              -- 工作用 INI（自动分析/在线更新会修改它）
bin/rdpwrap.ini.bak*         -- 覆盖前的自动备份 / 事故现场备份
bin/rdpwrap.generated.txt    -- 本工具生成过的版本 section 清单（用于安全回滚）
```

这些属于运行期文件，不必提交（见「第十节」）。

### `src/`

源码目录。

包含两个工程：

- `src/RDPWrapTool/`：C# WinForms 主程序。
- `src/RDPWrapDLL/`：C++ RDPWrap DLL 源码。

### `PROJECT_STRUCTURE.md`

本说明文档。

用于记录项目结构、文件用途、构建方法和发布注意事项。

## 三、主程序工程：`src/RDPWrapTool/`

这是用户双击运行的图形界面程序，使用 C# WinForms 编写，目标框架是 `.NET Framework 4.8`。

### `RDPWrapTool.csproj`

C# 主程序项目文件。

关键配置：

- `TargetFramework=net48`
- `OutputType=WinExe`
- `UseWindowsForms=true`
- 使用 `app.manifest` 申请管理员权限
- 将 `rdpwrap.dll` 和 `rdpwrap.ini` 作为 `EmbeddedResource` 打包进 exe

也就是说，最终 exe 里已经包含初始 DLL 和 INI。

### `Program.cs`

程序入口。

支持两种模式：

- 图形界面模式：双击 `RDPWrapTool.exe`。
- 命令行模式（排障/脚本/自检）：

```text
RDPWrapTool.exe --analyze <termsrv.dll路径> [--write <rdpwrap.ini路径>] [--out <输出日志路径>]
                [--no-wpp] [--emit-layout-evidence <json路径>]
RDPWrapTool.exe --verify   [--out <输出日志路径>]   检查当前 3389 监听、事件与补丁日志
RDPWrapTool.exe --deploy   [--out <输出日志路径>]   分析 + 写入 INI + 部署 + 校验（失败自动阶梯回退/回滚）
RDPWrapTool.exe --restore  [--out <输出日志路径>]   移除本工具生成的 section + 重启 + 校验
RDPWrapTool.exe --selftest-bad-layout               故意写入旧错误布局，验证安全变体/回滚是否生效
```

说明：

- `--no-wpp` 用于回归测试「不带 WPP 名字」的退化路径（应仍然推导出同一布局）。
- `--emit-layout-evidence` 输出 SLInit 证据表 JSON，便于和社区 INI 做差分比对。
- `--selftest-bad-layout` 是安全网自检：预期最终 `stage=no-slinit-hook` 或 `rolled-back`，且 3389 仍在监听。

命令行模式主要用于测试自动分析、校验和回滚逻辑。

### `app.manifest`

Windows 程序清单文件。

作用：

- 让程序启动时请求管理员权限。
- 声明 Windows 兼容性。

本项目需要写入 System32、修改注册表、控制 Windows 服务，所以必须以管理员权限运行。

### `rdpwrap.dll`

RDP Wrapper 核心 DLL 文件。

用途：

- 编译主程序时作为嵌入资源打进 exe。
- 程序运行时会自动释放出来（**仅当目标文件不存在时**）。
- 安装时会复制到：

```text
C:\Windows\System32\rdpwrap.dll
```

如果以后重新编译了 `src/RDPWrapDLL/` 里的 C++ DLL，需要用新 DLL 替换这个文件，然后重新编译主程序 exe。

DLL 侧日志：`WriteToLog` 会把补丁的原始字节/写入字节/回读结果、`SLInitDirect` 的每个变量写入结果写进
INI `[Main] LogFile` 指定的文件。该路径由主程序在部署时改成可写路径，工具的诊断功能会读取它。

### `rdpwrap.ini`

RDP Wrapper 配置文件。

用途：

- 编译主程序时作为嵌入资源打进 exe。
- 程序运行时会自动释放出来（**仅当目标文件不存在时**，避免覆盖已被自动分析修改过的工作 INI）。
- 自动分析成功后会写入新的版本 section（写入前会备份、写入后会校验）。
- 部署时会复制到：

```text
C:\Windows\System32\rdpwrap.ini
```

如果更新了这个 INI，也需要重新编译主程序，新的 exe 才会内置新版 INI。
如果机器上已存在旧的工作 INI（含自动分析生成的 section），内置 INI 不会自动覆盖它；
刷新方式见「第八节」。

## 四、核心代码：`src/RDPWrapTool/Core/`

### `AssetManager.cs`

内置资源释放管理器。

负责：

- 从 exe 内释放 `rdpwrap.dll`。
- 从 exe 内释放 `rdpwrap.ini`。
- 选择可写的数据目录。
- 支持“只拷贝一个 exe”运行。

释放位置优先级：

1. exe 所在目录。
2. 如果 exe 所在目录不可写，则使用：

```text
%ProgramData%\RDPWrapTool
```

重要行为：

- **只在目标文件不存在时释放**（`ExtractIfMissing`）。因为工作用的 `rdpwrap.ini` 会被自动分析和在线更新持续修改，
  每次启动都覆盖会把用户生成的 section 冲掉。
- `EmbeddedIniDiffersFromWorking()` 可以检测「exe 内置 INI」与「本地工作 INI」是否不一致；
  `ExtractOverwrite()` 用于手动用内置资源覆盖本地工作文件。
  （手工刷新步骤见「第八节」。）

### `RDPWrapInstaller.cs`

RDPWrap 安装、卸载、部署逻辑。

负责：

- 检测 RDPWrap 是否已安装。
- 检测第三方 wrapper。
- 安装 `rdpwrap.dll` 和 `rdpwrap.ini` 到 System32。
- 设置 `TermService` 的 `ServiceDll` 注册表。
- 已安装时纯覆盖部署文件，并在部署后**校验 RDP 是否真的可用**。
- 卸载 RDPWrap 并恢复原始 `termsrv.dll`。
- 启用 Windows 远程桌面。
- 配置防火墙 3389 入站规则。

重要方法：

- `Install()`：完整安装。
- `DeployFilesOnly(bool verify, bool requirePatches)`：已安装时覆盖 DLL/INI、重启服务、校验监听；
  返回 `DeployResult`（含 `Success / PortListening / PatchesApplied / SlInitWritten / Stage / Evidence`）。
- `Uninstall()`：卸载。
- `EnableRemoteDesktop()`：启用系统远程桌面和防火墙。
- `IsRemoteDesktopEnabled()`：检测系统远程桌面是否已打开。

注意：安装/部署期间会确保 `[Main] LogFile` 指向服务账户可写的路径，并清空旧日志，
以便用 rdpwrap.dll 自己的日志判断补丁是否真的写进去了（见「第十三节」）。

### `ServiceManager.cs`

Windows 服务管理工具。

负责：

- 启动 `TermService`。
- 停止 `TermService`（优先优雅停止；超时才强杀，强杀前告警、强杀后等旧 PID 退出）。
- 重启 `TermService`（先停 `UmRdpService` 依赖）。
- 查询服务状态、服务 PID。
- `WaitServiceReady()`：等到「服务 Running **且 3389 已监听**」才算就绪。
- `IsRdpPortListening()` / `CanConnectRdp()`：监听与连通性判定。
- `GetTsStartupEvidence()`：读取事件 258（侦听启动）/ 17（RDP 服务启动失败），事件 ID 与系统语言无关。
- 诊断 `TermService` 启动失败（服务状态、rdpwrap 日志、监听状态、事件、依赖、`sc query`）。

主要用于安装、部署、保存 INI 后自动重启远程桌面服务，并判断重启后远程桌面是否真的可用。

### `IniManager.cs`

`rdpwrap.ini` 管理器。

负责：

- 加载 INI。
- 保存 INI。
- 导入外部 INI。
- 检测当前系统版本是否已被 INI 支持。
- 添加或替换自动分析生成的版本 section（含 `-SLInit` 配对与同体别名 section）。
- 自动补充缺失的 `[PatchCodes]`。
- `BackupSystem32Ini()`：覆盖前备份系统目录里的 INI。
- `RemoveVersionSections()`：删除本工具生成的 section（含同体别名 section），不会误删社区 section。
- `GeneratedManifestPath`（`rdpwrap.generated.txt`）：记录本工具生成过的 section 名称，供「一键恢复」精确回滚。
- `EnsureWritableLogPath()` / `EnsureLogDirectory()`：把 `[Main] LogFile` 改成可写绝对路径并授权
  `NETWORK SERVICE`，否则 rdpwrap.dll 的诊断日志永远写不出来。
- 将 INI 复制到 System32。

常量与路径：

```text
DefaultLogPath       = C:\ProgramData\RDPWrapTool\rdpwrap.txt
System32IniPath      = C:\Windows\System32\rdpwrap.ini
GeneratedManifestPath= <工具数据目录>\rdpwrap.generated.txt
```

### `OnlineUpdater.cs`

在线更新 INI 的模块。

负责：

- 从默认社区源下载 `rdpwrap.ini`。
- 从用户输入的 URL 下载 INI。
- 下载后做基础格式检查。
- 覆盖前备份旧 INI。

### `TermSrvAnalyzer.cs`

自动分析 `termsrv.dll` 的核心模块。

负责：

- 读取当前系统 `termsrv.dll`。
- 自动寻找 RDPWrap 需要的 patch offset（LocalOnly / SingleUser / DefPolicy）。
- 定位 `CSLQuery::Initialize`（E8 函数图 + 引用锚点打分 + WPP 字符串确认）并调用
  `SlInitLayoutResolver` 推导 SLInit 数据块。
- 生成对应的 INI section，并在报告里输出「SLInit 证据表」（变量 / 地址 / delta / 存储形态 / 证据来源）。
- 调用 `OffsetVerifier` 做严格验证。

注意：

- 自动分析不是保证未来所有 Windows 版本 100% 成功。
- 当前策略是“验证通过才写入，验证失败就拒绝修改 INI”。
- 未来 Windows 如果大改 `termsrv.dll` 结构，正确表现应该是分析失败，而不是写入不可靠配置。
- SLInit 布局的完整推导算法与实测基准表见「第十二节」。

### `OffsetVerifier.cs`

offset 字节级验证器。

负责：

- 验证 `LocalOnly` patch 点。
- 验证 `SingleUser` patch 点。
- 验证 `DefPolicy` patch 点。
- 验证 `SLInit` hook 与数据块（校验的是 `SlInitLayoutResolver` 推导出的地址集合及其存储形态，
  不再用「同一张表生成再同一张表校验」的循环校验）。
- 定义自动生成 INI 所需的 patch code 名称。

这是自动分析准确性的关键保护层。

### `SlInitLayoutResolver.cs`

SLInit 数据块布局推导器（本项目最关键的分析逻辑）。

背景：`SlInitLayoutA/B` 这类按 build 号硬编码的 delta 表在真实 Windows build 上并不成立
（社区 INI 里至少存在 3 种真实布局），而旧实现还用同一张表反向校验自己生成的地址，因此错布局永远能通过。
实测：在 10.0.28000.2952 上使用旧的 LayoutA 会让 TermService 启动后 3389 完全不监听。

负责：

- 枚举被 hook 函数（`CSLQuery::Initialize`）内部所有对 `.data` 的 RIP 相对存储指令。
- 读取 termsrv.dll 内置的 WPP 追踪字符串
  `CSLQuery::Initialize - SLGetWindowsInformationDWORD for <变量名>`，
  通过「字符串 lea → 其后第一条存储指令」恢复每个变量的地址。
- 依据存储指令形态判定槽位类型：`ImmOne`（`mov dword [rip],1`）、`Bool`（写 0/1 的形态）、`Count`（原样写回）。
- 组装 8 个地址：`bInitialized`（函数体内最后一条 `imm=1` 存储）、`bServerSku = bInitialized+4`，
  其余 6 个取 WPP 名字（`lMaxUserSessions` / `ulMaxDebugSessions` 必须是计数槽，四个 Allowed/FUS 必须是布尔槽）。
- 交叉校验：地址唯一、4 字节对齐、均在 `.data`、都被 hook 函数写过、delta 组合命中已知真实布局白名单
  `{L1,L2,L3}` 或 6 个名字全部命中；任一不满足即拒绝（严格模式，不写 INI）。
- 证据不足时退化为「已知布局 + 类型签名」唯一匹配路径；仍不唯一则失败。

### `DeployVerifier.cs` / `DeployWorkflow.cs`

部署校验与安全编排。

- `DeployVerifier`：判定“RDP 到底能不能用”，而不是“服务是不是 Running”：
  TermService 状态、**3389 端口是否处于监听**、能否 TCP 连接、事件 258（侦听启动）/17（RDP 服务启动失败）、
  rdpwrap.dll 日志中三个代码补丁与 SLInit 写入的回读结果。
- `DeployWorkflow`：部署前备份系统 INI → 写入 section（并把 `[Main] LogFile` 改成可写路径）→ 部署 → 校验；
  失败则依次尝试「去掉 SLInitHook 的安全变体」→「回滚到部署前 INI」，每一步都重新校验，
  返回带阶段（`full` / `no-slinit-hook` / `rolled-back` / `failed`）的 `DeployResult`。

### `PEAnalyzer.cs`

PE 文件解析工具。

负责：

- 读取 PE 文件结构。
- 解析 `.text`、`.data` 等 section。
- 在 RVA 和文件偏移之间转换。
- 搜索指定字节特征。
- `FindAnsiStrings()`：枚举以指定 ASCII 前缀开头的字符串及其 RVA（用于读取 termsrv.dll 内置的 WPP 变量名）。
- `IsMappedInFile()` / `ReadUInt32AtRva()`：判断 RVA 是否有真实文件内容并读取。

注意：

- `RVAToOffset()` 只在 `rva` 落在 section 且**在 RawSize 之内**时才返回偏移。
  termsrv.dll 的 `.data` 是 `VirtualSize=0x53C8` 但 `RawSize=0x1000`，
  旧实现会把虚拟尾部的 RVA 映射到 `.pdata` 的真实字节，读到完全无关的数据。

`TermSrvAnalyzer` 与 `SlInitLayoutResolver` 依赖它分析 `termsrv.dll`。

### `VersionHelper.cs`

版本识别辅助类。

负责：

- 获取 `termsrv.dll` 文件版本。
- 生成候选版本名。
- 处理不同 Windows 版本资源字段差异。

生成多个版本别名可以提高 INI section 命中率。

### `UserManager.cs`

本地用户管理模块。

负责：

- 创建 Windows 本地用户。
- 删除 Windows 本地用户。
- 修改本地用户密码。
- 将用户加入 `Remote Desktop Users` 组。
- 将用户移出 `Remote Desktop Users` 组。
- 列出本地用户。
- 列出远程桌面用户组成员。

当前界面里创建用户或加入 RDP 组后，会自动调用 `EnableRemoteDesktop()` 打开系统远程桌面。

## 五、界面代码：`src/RDPWrapTool/Forms/`

### `MainForm.cs`

主窗口和全部页面交互逻辑。

包含页面：

- 总览
- 安装部署
- 自动分析
- INI 配置
- 用户管理
- 日志

主要功能：

- 查看系统架构、`termsrv.dll` 版本、RDPWrap 安装状态。
- 查看 `TermService` 状态、**3389 监听状态**、最近事件 258/17、rdpwrap 日志路径。
- 安装 RDPWrap。
- 卸载 RDPWrap。
- 手动启用 Windows 远程桌面。
- 重启远程桌面服务（带就绪与监听校验）。
- 自动分析当前系统 `termsrv.dll`（报告含 SLInit 证据表）。
- 将分析结果添加到 INI 并部署（自动校验，失败自动安全变体/回滚）。
- 「恢复到可用状态」：删除本工具生成的 section 并重启校验。
- 编辑、导入、在线更新 INI。
- 创建远程用户。
- 管理远程桌面用户组。
- 查看操作日志。

## 六、DLL 工程：`src/RDPWrapDLL/`

这是 RDPWrap DLL 的 C++ 源码工程。

一般情况下，日常修改 C# 主程序不需要动这里。

只有需要修改 RDPWrap DLL 本身的 hook 逻辑时，才需要重新编译这个工程。

### `RDPWrapDLL.vcxproj`

Visual Studio C++ DLL 项目文件。

用于编译生成 `rdpwrap.dll`。

### `RDPWrap.cpp`

RDPWrap DLL 的主要逻辑。

负责 wrapper/hook 相关核心行为。

### `IniFile.cpp`

INI 读取和解析逻辑实现。

### `IniFile.h`

INI 读取和解析逻辑头文件。

### `util.cpp`

辅助工具函数。

### `dllmain.cpp`

DLL 入口文件。

### `Export.def`

DLL 导出定义文件。

### `stdafx.cpp`

预编译头实现文件。

### `stdafx.h`

预编译头文件。

### `targetver.h`

Windows SDK 目标版本配置头文件。

## 七、修改代码后如何重新生成 exe / 打包发布

下面所有命令都在 PowerShell 中执行。

### 7.1 进入项目根目录

```powershell
cd C:\pro\my_rdpwrap_tool
```

### 7.2 首次构建或依赖丢失时，还原依赖

如果第一次在新电脑上编译，或者清理过 `obj` 后提示 NuGet/引用程序集缺失，先执行：

```powershell
dotnet restore src\RDPWrapTool\RDPWrapTool.csproj
```

说明：

- 项目目标框架是 `.NET Framework 4.8`。
- `RDPWrapTool.csproj` 中引用了 `Microsoft.NETFramework.ReferenceAssemblies`，用于在未安装完整 targeting pack 的机器上编译。
- 第一次 restore 需要联网访问 NuGet。

### 7.3 普通重新生成 exe

修改 C# 代码后，执行：

```powershell
dotnet build src\RDPWrapTool\RDPWrapTool.csproj -c Release
```

生成出来的新 exe 位于：

```text
src\RDPWrapTool\bin\Release\net48\RDPWrapTool.exe
```

然后复制到项目发布目录：

```powershell
Copy-Item src\RDPWrapTool\bin\Release\net48\RDPWrapTool.exe bin\RDPWrapTool.exe -Force
```

最终给用户使用的文件是：

```text
bin\RDPWrapTool.exe
```

### 7.4 一键重新生成并复制到发布目录

平时最常用这一组命令：

```powershell
cd C:\pro\my_rdpwrap_tool
dotnet build src\RDPWrapTool\RDPWrapTool.csproj -c Release
Copy-Item src\RDPWrapTool\bin\Release\net48\RDPWrapTool.exe bin\RDPWrapTool.exe -Force
```

### 7.5 清理后重新生成

如果怀疑旧构建产物影响结果，使用 clean build：

```powershell
cd C:\pro\my_rdpwrap_tool
dotnet clean src\RDPWrapTool\RDPWrapTool.csproj -c Release
dotnet build src\RDPWrapTool\RDPWrapTool.csproj -c Release
Copy-Item src\RDPWrapTool\bin\Release\net48\RDPWrapTool.exe bin\RDPWrapTool.exe -Force
```

### 7.6 生成后清理中间目录

如果希望项目目录继续保持干净，只保留源码和最终 exe，可以在复制 exe 后执行：

```powershell
Remove-Item src\RDPWrapTool\bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item src\RDPWrapTool\obj -Recurse -Force -ErrorAction SilentlyContinue
```

注意：

- 这不会删除根目录的 `bin\RDPWrapTool.exe`。
- 下次构建时可能需要重新 restore。

### 7.7 完整打包命令

如果想“一次性完成清理、构建、复制、清理中间产物”，执行：

```powershell
cd C:\pro\my_rdpwrap_tool
dotnet restore src\RDPWrapTool\RDPWrapTool.csproj
dotnet clean src\RDPWrapTool\RDPWrapTool.csproj -c Release
dotnet build src\RDPWrapTool\RDPWrapTool.csproj -c Release
New-Item -ItemType Directory -Force -Path bin | Out-Null
Copy-Item src\RDPWrapTool\bin\Release\net48\RDPWrapTool.exe bin\RDPWrapTool.exe -Force
Remove-Item src\RDPWrapTool\bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item src\RDPWrapTool\obj -Recurse -Force -ErrorAction SilentlyContinue
```

执行完成后，最终发布文件就是：

```text
C:\pro\my_rdpwrap_tool\bin\RDPWrapTool.exe
```

### 7.8 验证 exe 是否包含内置资源

当前 exe 应该内置两个资源：

- `RDPWrapTool.Assets.rdpwrap.dll`
- `RDPWrapTool.Assets.rdpwrap.ini`

可以用 PowerShell 检查：

```powershell
cd C:\pro\my_rdpwrap_tool
$asm = [System.Reflection.Assembly]::LoadFile((Resolve-Path .\bin\RDPWrapTool.exe))
$asm.GetManifestResourceNames()
```

正常输出应包含：

```text
RDPWrapTool.Assets.rdpwrap.dll
RDPWrapTool.Assets.rdpwrap.ini
```

### 7.9 用 Visual Studio 生成

也可以不用命令行，直接用 Visual Studio：

1. 打开：

```text
src\RDPWrapTool\RDPWrapTool.csproj
```

2. 选择配置：

```text
Release
```

3. 点击：

```text
生成 -> 生成解决方案
```

4. 生成后复制：

```text
src\RDPWrapTool\bin\Release\net48\RDPWrapTool.exe
```

到：

```text
bin\RDPWrapTool.exe
```

## 八、如果修改了 rdpwrap.ini 或 rdpwrap.dll

因为主程序会把这两个文件打进 exe，所以修改后必须重新生成 exe。

### 修改了 `src/RDPWrapTool/rdpwrap.ini`

操作：

1. 保存新版 `rdpwrap.ini`。
2. 执行第 7.4 或第 7.7 节的打包命令。
3. 确认新 exe 已复制到 `bin/RDPWrapTool.exe`。

### 修改了 `src/RDPWrapTool/rdpwrap.dll`

操作：

1. 替换 `src/RDPWrapTool/rdpwrap.dll`。
2. 执行第 7.4 或第 7.7 节的打包命令。
3. 确认新 exe 已复制到 `bin/RDPWrapTool.exe`。

### 修改了 `src/RDPWrapTool/rdpwrap.ini`（或想用内置 INI 刷新本地工作副本）

因为 `AssetManager` 只在文件不存在时释放内置资源，工作副本不会被自动覆盖。要手动刷新：

```powershell
Remove-Item bin\rdpwrap.ini -Force          # 删除旧工作副本
.\bin\RDPWrapTool.exe --verify              # 任意一次启动都会重新释放内置 INI
```

注意：这会丢掉工作副本里自动分析新增的 section。更稳妥的做法是保留工作副本，
只把内置 INI 里缺的版本 section 手工合并进来（用 `--analyze --write` 重新生成也可以）。

### 修改了 `src/RDPWrapDLL/` 的 C++ 源码

操作：

1. 用 Visual Studio 编译 `src/RDPWrapDLL/RDPWrapDLL.vcxproj`。
2. 得到新的 `rdpwrap.dll`。
3. 用新 DLL 替换：

```text
src/RDPWrapTool/rdpwrap.dll
```

4. 再执行第 7.4 或第 7.7 节的打包命令，重新生成 C# 主程序 exe。

## 九、发布打包说明

当前发布不需要压缩包也可以，只拷贝一个文件即可：

```text
bin/RDPWrapTool.exe
```

如果要上传 GitHub Releases，建议：

1. 先执行第 7.7 节的完整打包命令。
2. 确认 `bin/RDPWrapTool.exe` 是最新文件。
3. 在 GitHub 仓库创建 Release。
4. 上传 `bin/RDPWrapTool.exe` 作为附件。

## 十、不应上传到 GitHub 的内容

以下属于临时文件、构建产物、测试文件，不建议提交：

```text
src/**/bin/
src/**/obj/
bin/Debug/
bin/Release/
*.pdb
*.exp
*.lib
temp_analyze/
analyze_smoke.txt
temp_*.ps1
find_localonly.ps1
analyze_termsrv.cs
termsrv.dll
widows10/
```

运行期生成的本地文件（不必提交）：

```text
bin/rdpwrap.ini            -- 工具工作用 INI（会被自动分析/在线更新修改）
bin/rdpwrap.dll            -- 工具工作用 DLL
bin/rdpwrap.ini.bak*       -- 覆盖前自动备份
bin/rdpwrap.generated.txt  -- 本工具生成过的版本 section 清单（用于安全回滚）
analyze_output.txt         -- 命令行模式输出
```

当前仓库根目录的 `bin/RDPWrapTool.exe` 是最终发布 exe，可以保留。

如果以后不想把 exe 放进源码仓库，也可以不提交 `bin/`，改用 GitHub Releases 发布 exe。

## 十一、GitHub 维护建议

建议继续使用原仓库，不建议删除重建。

推荐流程：

1. 保留原 GitHub 仓库。
2. 提交本次清理后的源码和文档。
3. 给新版本打 tag。
4. 用 GitHub Releases 上传 `bin/RDPWrapTool.exe`。

除非旧仓库包含敏感信息，或者你想彻底换项目名，否则没有必要删除旧仓库。

## 十二、SLInit 布局推导算法与实测基准

### 12.1 为什么不能用固定布局表

rdpwrap 的 `[版本-SLInit]` 段给出的是 `CSLQuery::Initialize` 里 8 个全局变量的**绝对 RVA**。
写入错误地址不会报错，但会把值写进别的全局变量，后果可以是灾难性的（实测：3389 完全不监听）。

旧实现有两处根本问题：

1. 按 build 号在两张硬编码表里挑布局（`build >= 22000` → `SlInitLayoutA`）。
   实测社区 INI 里至少存在 3 种真实布局，其中 `0,4,8,10,1C,20,28,2C` 这种根本没有出现在旧表里：

   | 布局 | delta 组合 | 出现在 |
   | --- | --- | --- |
   | L1 | `0,4,8,10,18,1C,24,28` | 20348、26100 早期、28000.1/.7/.1199/.1340 |
   | L2 | `0,4,8,10,1C,20,28,2C` | 26100 后期、28000.1516+、**本机 28000.2952** |
   | L3 | `0,4,8,10,18,1C,20,24` | 19041 系列 |

2. 校验是循环的：`data[name] = base + delta`，再用同一张表的 `delta` 去比对 →
   任何布局都能"通过验证"。唯一真实的检查只是「这 8 个地址在 `.text` 里被引用过」，
   而错布局和正确布局的槽位**都被写过**（同一块里连续 13 个 dword 全有存储指令），所以必然通过。

### 12.2 现在的推导算法（`SlInitLayoutResolver.Resolve`）

1. 在 hook 函数体（`CSLQuery::Initialize`，本机为 `[0xB6A34, 0xB818C)`）内枚举所有写入 `.data`
   的 RIP 相对存储指令，得到候选槽位及其指令位置。
2. 在 `.rdata` 里找前缀为 `CSLQuery::Initialize - SLGetWindowsInformationDWORD for ` 的 ANSI 字符串
   （DLL 自带的 WPP 追踪串，非本地化文本），对每个串找函数体内的 `lea`，取其后的第一条存储指令
   → `变量名 → 地址`。同一名字出现多解或地址离群时丢弃并记录证据。
3. 依据存储形态标注类型：
   - `C7 05 .. imm=1` → `ImmOne`（`bInitialized`）
   - 存储前 64 字节内存在 `mov dword [rsp+X],1` 与 `mov dword [rsp+X],0` 成对形态 → `Bool`
   - 其余 → `Count`（直接写回查询结果）
4. 组装：`bInitialized` = 块内最后一条 `imm=1` 存储的目标；`bServerSku` = `bInitialized+4` 的布尔槽；
   其余 6 个取 WPP 名字，并强制类型（`lMaxUserSessions`、`ulMaxDebugSessions` 必须是计数槽；
   `bAppServerAllowed`、`bRemoteConnAllowed`、`bMultimonAllowed`、`bFUSEnabled` 必须是布尔槽）。
5. 校验（任一不满足即失败，不写 INI）：地址唯一 / 4 字节对齐 / 在 `.data` / 都被 hook 函数写过 /
   delta 组合命中已知真实布局白名单或 6 个名字全部命中。
6. 若 WPP 名字不可用（旧版或裁剪过的二进制），退化为「已知布局 + 类型签名唯一匹配」；
   仍不唯一则失败。

界面上「自动分析」报告会打印完整证据表；命令行可以导出 JSON：

```powershell
.\bin\RDPWrapTool.exe --analyze C:\Windows\System32\termsrv.dll `
    --emit-layout-evidence C:\ProgramData\RDPWrapTool\layout_evidence.json
.\bin\RDPWrapTool.exe --analyze C:\Windows\System32\termsrv.dll --no-wpp   # 强制走退化路径做回归
```

### 12.3 实测基准（`10.0.28000.2952`，2026-09-10）

| 变量 | 地址 | delta | 存储形态 | 证据（WPP lea / 存储指令） |
| --- | --- | --- | --- | --- |
| bInitialized | 0x133198 | +0x00 | ImmOne | store @0xB815C |
| bServerSku | 0x13319C | +0x04 | Bool | store @0xB6AB8 |
| lMaxUserSessions | 0x1331A0 | +0x08 | Count | WPP @0xB7906 / store @0xB79A0 |
| bAppServerAllowed | 0x1331A8 | +0x10 | Bool | WPP @0xB7116 / store @0xB71C9 |
| bRemoteConnAllowed | 0x1331B4 | +0x1C | Bool | WPP @0xB6CDF / store @0xB6D92 |
| bMultimonAllowed | 0x1331B8 | +0x20 | Bool | WPP @0xB750E / store @0xB75C1 |
| ulMaxDebugSessions | 0x1331C0 | +0x28 | Count | WPP @0xB7AE9 / store @0xB7B83 |
| bFUSEnabled | 0x1331C4 | +0x2C | Bool | WPP @0xB6EF6 / store @0xB6FA9 |

同一块里还写着 4 个不属于本配置的全局：`bAllowAADLicensing`(+0xFFFFFFFC)、`bAutomatedAppServerInstallation`(+0x0C)、
`ulMaxAgentSessions`(+0x14)、`bVailGuest`(+0x18)、`bWVDEnabled`(+0x24)。旧布局正是把值写到了它们身上：

| 旧代码写的位置 | 真实身份 | 后果 |
| --- | --- | --- |
| bRemoteConnAllowed → +0x18 | **bVailGuest** | 被置 1 |
| bMultimonAllowed → +0x1C | bRemoteConnAllowed | 恰好也是 1 |
| ulMaxDebugSessions → +0x24 | **bWVDEnabled** | 被置 0 |
| bFUSEnabled → +0x28 | **ulMaxDebugSessions** | 被置 1，真正的 bFUSEnabled 从未被写 |

三个代码补丁点（LocalOnly / SingleUser / DefPolicy）与社区 `[10.0.28000.2804]` 的四个偏移**统一相差 0x760**，
补丁码也完全一致，说明这些点位一直是找对的——出问题的只有 SLInit 数据块。

## 十三、部署校验与回滚机制

### 13.1 为什么需要

旧流程用 `StartService` 是否返回 `Running` 当成功判据。实际上 TermService 可以是 Running 而 termsrv
根本没有创建侦听器（`fDenyTSConnections=0`、`RDP-Tcp\fEnableWinStation=1` 都正常，但 3389 无人监听），
于是界面报"部署成功、补丁已生效"，而远程桌面已经完全连不上。2026-09-10 的事故就是这样发生的。

### 13.2 判定标准（`DeployVerifier.Verify`）

只有同时满足才算成功：

1. `TermService` 处于 Running，且 `WaitServiceReady()` 等到 **3389 进入监听**。
2. 端口处于 Listen（并尝试 `127.0.0.2:3389` TCP 连接）。
3. 重启后**没有**事件 17（RDP 服务启动失败）；有事件 258（侦听启动）作为正向证据
   （事件 ID 与系统语言无关；258 会晚一点写入，所以会轮询几秒）。
4. rdpwrap 日志里三个代码补丁都有 `PatchFunc ... Readback`、没有 `SKIP`；
   `SLInitDirect` 的写入没有异常（hook 回读或 direct 写入至少一种成功）。

### 13.3 阶梯与回滚（`DeployWorkflow.DeployAnalyzedSection`）

```text
备份系统 INI + [Main] LogFile 改成可写路径
  └─ 写入完整 section → 部署 → 校验
        ├─ 通过  → stage = full
        └─ 失败  → 写入"不带 SLInitHook"的 section（只保留三个代码补丁）→ 部署 → 校验
                     ├─ 通过 → stage = no-slinit-hook（远程桌面可用，多会话可能仍受限）
                     └─ 失败 → 恢复部署前的 INI → 部署 → 校验
                                 ├─ 恢复 → stage = rolled-back
                                 └─ 失败 → stage = failed（提示人工处理/卸载）
```

「不带 SLInitHook」这一层是特意设计的：没有 `SLInitHook.x64=1`，rdpwrap.dll 就不会替换
`CSLQuery::Initialize`，错误的数据块不可能再破坏 termsrv 启动过程，因此可以单独隔离问题。

### 13.4 自检

```powershell
# 故意写入旧错误布局（+18/+1C/+24/+28），验证安全网是否生效
.\bin\RDPWrapTool.exe --selftest-bad-layout
```

预期结果：最终 `stage = no-slinit-hook` 或 `rolled-back`，且 `3389 listening = True`。
本机 2026-09-10 实测为该预期（同时复现了旧布局会让 3389 失去监听）。

## 十四、排障与恢复手册

### 14.1 先看状态

```powershell
.\bin\RDPWrapTool.exe --verify
```

或看界面「总览」页：`3389 监听状态`、最近事件 258/17、rdpwrap 日志路径。

关键判据只有一条：**3389 是否在监听**。`TermService` 显示 Running 不代表远程桌面可用。

```powershell
Get-NetTCPConnection -LocalPort 3389 -State Listen
qwinsta                                # 应看到 rdp-tcp 处于 Listen
```

### 14.2 常见症状与处理

| 症状 | 处理 |
| --- | --- |
| 3389 未监听，最近有事件 17 | 刚部署了自动分析配置：点「恢复到可用状态」，或命令行 `--restore`；然后把日志页证据发出来 |
| 3389 未监听，且事件通道无 258/17 | 看 rdpwrap 日志（`C:\ProgramData\RDPWrapTool\rdpwrap.txt`）与 `ServiceManager.DiagnoseTermServiceFailure()` 输出；必要时「卸载 RDPWrap」回到系统原生 termsrv.dll |
| 3389 正常但多用户/远程自己不生效 | 检查 `--verify` 里的 `Patches=True SLInit=True`；确认 INI 里有当前版本的 section（`[10.0.x.y]` 与 `[10.0.x.y-SLInit]`）；确认 `RDP-Tcp\fSingleSessionPerUser=0` |
| 同一账号连 127.0.0.2 被踢掉 console | 需要 SingleUser 补丁生效（`PatchFunc: SingleUserPatch.x64 ... Readback: B8 01 00 00 00 90 90`），见日志 |
| 版本 section 明明写了却不生效 | rdpwrap 用**已加载模块版本资源**拼 section 名，可能与文件版本号不同（本机是 2952 与 2113 的差异），所以生成时会同时写多个别名 section |
| 日志文件找不到 | `[Main] LogFile` 必须指向服务账户可写路径；部署时会自动改成 `C:\ProgramData\RDPWrapTool\rdpwrap.txt` 并授权，也可手工核对 |

### 14.3 一键恢复

- 界面：「安装部署」页 →「恢复到可用状态」（删除本工具生成的 section → 重启 → 校验 3389）。
- 命令行：

```powershell
.\bin\RDPWrapTool.exe --restore
```

只会删除 `rdpwrap.generated.txt` 清单里记录的 section 以及与其同体的别名 section，不会动社区 section。

### 14.4 证据在哪

| 证据 | 位置 |
| --- | --- |
| 分析报告 + SLInit 证据表 | 界面「自动分析」页 / `--analyze --out <日志>` |
| 布局证据 JSON | `--emit-layout-evidence <json>` |
| rdpwrap.dll 补丁/回读日志 | `C:\ProgramData\RDPWrapTool\rdpwrap.txt` |
| 部署/回滚阶段与判定 | 界面「日志」页 / `--deploy --out <日志>`（`DeployResult.Describe()`） |
| 系统事件 | RCM 通道 id=258、LSM 通道 id=17 |
| INI 备份 | 工具数据目录 `rdpwrap.ini.bak.<时间戳>`、`rdpwrap.ini.pre-restore-<时间戳>` |
