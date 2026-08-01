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
│  │  │  ├─ IniManager.cs                        -- rdpwrap.ini 加载、保存、导入、写入版本 section
│  │  │  ├─ OffsetVerifier.cs                    -- 自动分析结果的字节级验证逻辑
│  │  │  ├─ OnlineUpdater.cs                     -- 在线下载/更新 rdpwrap.ini
│  │  │  ├─ PEAnalyzer.cs                        -- PE 文件解析；RVA/文件偏移转换和字节搜索
│  │  │  ├─ RDPWrapInstaller.cs                  -- 安装、卸载、部署、启用远程桌面、防火墙配置
│  │  │  ├─ ServiceManager.cs                    -- TermService 启动、停止、重启和诊断
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

里面只保留最终给用户运行的 exe：

```text
bin/RDPWrapTool.exe
```

当前版本已经把 `rdpwrap.dll` 和 `rdpwrap.ini` 内置进 exe，所以用户拷贝到其他电脑时只需要拷贝这一个文件。

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
- 命令行分析模式：

```text
RDPWrapTool.exe --analyze <termsrv.dll路径> [--write <rdpwrap.ini路径>] [--out <输出日志路径>]
```

命令行模式主要用于测试自动分析逻辑。

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
- 程序运行时会自动释放出来。
- 安装时会复制到：

```text
C:\Windows\System32\rdpwrap.dll
```

如果以后重新编译了 `src/RDPWrapDLL/` 里的 C++ DLL，需要用新 DLL 替换这个文件，然后重新编译主程序 exe。

### `rdpwrap.ini`

RDP Wrapper 配置文件。

用途：

- 编译主程序时作为嵌入资源打进 exe。
- 程序运行时会自动释放出来。
- 自动分析成功后会写入新的版本 section。
- 部署时会复制到：

```text
C:\Windows\System32\rdpwrap.ini
```

如果更新了这个 INI，也需要重新编译主程序，新的 exe 才会内置新版 INI。

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

### `RDPWrapInstaller.cs`

RDPWrap 安装、卸载、部署逻辑。

负责：

- 检测 RDPWrap 是否已安装。
- 检测第三方 wrapper。
- 安装 `rdpwrap.dll` 和 `rdpwrap.ini` 到 System32。
- 设置 `TermService` 的 `ServiceDll` 注册表。
- 已安装时纯覆盖部署文件。
- 卸载 RDPWrap 并恢复原始 `termsrv.dll`。
- 启用 Windows 远程桌面。
- 配置防火墙 3389 入站规则。

重要方法：

- `Install()`：完整安装。
- `DeployFilesOnly()`：已安装时覆盖 DLL/INI 并重启服务。
- `Uninstall()`：卸载。
- `EnableRemoteDesktop()`：启用系统远程桌面和防火墙。
- `IsRemoteDesktopEnabled()`：检测系统远程桌面是否已打开。

### `ServiceManager.cs`

Windows 服务管理工具。

负责：

- 启动 `TermService`。
- 停止 `TermService`。
- 重启 `TermService`。
- 查询服务状态。
- 查询服务 PID。
- 必要时结束服务进程。
- 诊断 `TermService` 启动失败。

主要用于安装、部署、保存 INI 后自动重启远程桌面服务。

### `IniManager.cs`

`rdpwrap.ini` 管理器。

负责：

- 加载 INI。
- 保存 INI。
- 导入外部 INI。
- 检测当前系统版本是否已被 INI 支持。
- 添加或替换自动分析生成的版本 section。
- 自动补充缺失的 `[PatchCodes]`。
- 将 INI 复制到 System32。

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
- 自动寻找 RDPWrap 需要的 patch offset。
- 生成对应的 INI section。
- 调用 `OffsetVerifier` 做严格验证。

注意：

- 自动分析不是保证未来所有 Windows 版本 100% 成功。
- 当前策略是“验证通过才写入，验证失败就拒绝修改 INI”。
- 未来 Windows 如果大改 `termsrv.dll` 结构，正确表现应该是分析失败，而不是写入不可靠配置。

### `OffsetVerifier.cs`

offset 字节级验证器。

负责：

- 验证 `LocalOnly` patch 点。
- 验证 `SingleUser` patch 点。
- 验证 `DefPolicy` patch 点。
- 验证 `SLInit` hook 和数据区布局。
- 定义自动生成 INI 所需的 patch code 名称。

这是自动分析准确性的关键保护层。

### `PEAnalyzer.cs`

PE 文件解析工具。

负责：

- 读取 PE 文件结构。
- 解析 `.text`、`.data` 等 section。
- 在 RVA 和文件偏移之间转换。
- 搜索指定字节特征。

`TermSrvAnalyzer` 依赖它分析 `termsrv.dll`。

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
- 查看 `TermService` 状态。
- 查看 Windows 远程桌面是否启用。
- 安装 RDPWrap。
- 卸载 RDPWrap。
- 手动启用 Windows 远程桌面。
- 重启远程桌面服务。
- 自动分析当前系统 `termsrv.dll`。
- 将分析结果添加到 INI 并自动部署到 System32。
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
