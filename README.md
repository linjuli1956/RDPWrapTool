# RDPWrapTool

RDPWrapTool 是一个面向 Windows 10/11 x64 的 RDP Wrapper 图形化管理、自动分析、部署和用户管理工具。

> 本项目是非官方衍生项目，与 Microsoft、Stas'M Corp. 及其他上游项目没有隶属或背书关系。请只在你拥有或获授权管理的计算机上使用，并自行确认 Windows 版本、授权条款和组织安全策略。

## 当前版本特点

- 单 exe 发布：`rdpwrap.dll` 和 `rdpwrap.ini` 已内置到 `RDPWrapTool.exe`。
- 自动分析当前系统 `termsrv.dll`，通过严格字节级验证后生成 INI 配置。
- 添加 INI 后可自动部署到 System32 并重启 `TermService`。
- 安装 RDPWrap 时自动启用 Windows 远程桌面和防火墙规则。
- 创建本地用户或加入远程桌面用户组后，自动启用 Windows 远程桌面。
- 支持手动编辑、导入和在线更新 `rdpwrap.ini`。

## 下载和使用

普通用户请在 GitHub Releases 下载 `RDPWrapTool.exe`。

使用方式：

1. 将 `RDPWrapTool.exe` 拷贝到目标电脑。
2. 双击运行，接受管理员权限提示。
3. 如果 INI 已支持当前系统版本，可直接安装。
4. 如果 INI 不支持当前系统版本，到“自动分析”页执行分析。
5. 分析通过后点击“添加到 INI 并部署到系统（自动重启服务）”。

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

程序首次运行时会自动释放内置的 `rdpwrap.dll` 和 `rdpwrap.ini`。

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

本项目使用“严格验证，通过才写入”的策略：

- 自动分析会寻找多个关键 patch 点。
- 每个候选 offset 都必须通过字节级验证。
- 任一关键点验证失败时，不会写入 INI。

因此，验证通过的结果可信度较高；但不能保证未来所有 Windows 新版本都能自动分析成功。如果 Microsoft 大幅修改 `termsrv.dll` 结构，正确表现应是分析失败并拒绝写入，而不是强行生成配置。

## 上游与许可证

本项目包含或衍生自以下开源工作：

- [stascorp/rdpwrap](https://github.com/stascorp/rdpwrap)
- [anhkgg/SuperRDP](https://github.com/anhkgg/SuperRDP)
- 社区维护的 `rdpwrap.ini` 配置

RDP Wrapper 相关源码使用 Apache License 2.0。请保留 `LICENSE` 和 `NOTICE`。

## 免责声明

本软件按“原样”提供，不附带任何明示或默示担保。修改远程桌面服务可能造成连接中断、系统更新后失效或与本机安全策略冲突。使用前请准备本地登录和恢复手段。
