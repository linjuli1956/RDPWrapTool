# RDPWrapTool

RDPWrapTool 是一个面向 Windows 10/11 x64 的 RDP Wrapper 图形化管理与诊断工具。本仓库包含 C# 管理程序、C++ wrapper 源码，以及 Windows 11 25H2 的排查记录。

> 本项目是非官方衍生项目，与 Microsoft、Stas'M Corp. 及其他上游项目没有隶属或背书关系。请只在你拥有或获授权管理的计算机上使用，并自行确认 Windows 版本、授权条款和组织安全策略。

## 下载和使用

普通用户请在仓库右侧的 **Releases** 下载 `RDPWrapTool-v1.0.0-win-x64-portable.zip`，不要下载 GitHub 自动生成的“Source code”压缩包。

1. 解压完整压缩包，不要只复制单个 EXE。
2. 在目标电脑的本地控制台操作，避免重启远程桌面服务时中断唯一连接。
3. 双击 `app\启动_RDPWrapTool.cmd`，接受管理员权限提示。
4. 先运行检测/分析，核对系统版本、SHA256 和补丁前字节，再执行安装。
5. 保存分析报告和 `C:\Windows\Temp\rdpwrap.txt`，便于复核。

便携包自带 .NET 8 Windows Desktop Runtime，目标电脑无需另行安装 .NET。包内不包含任何机器的 `termsrv.dll`；分析时只读取目标电脑自己的系统文件。

## 源码目录

- `src/RDPWrapTool`：C# / Windows Forms 管理程序。
- `src/RDPWrapDLL`：C++ wrapper 源码，衍生自 RDP Wrapper / SuperRDP。
- `docs`：Windows 11 25H2 修复与验证记录。

完整便携运行环境体积较大，因此不提交到 Git 历史，只作为 Release 附件发布。

## 已验证版本

- 工具版本：`1.0.0`
- 平台：Windows 10/11 x64
- `rdpwrap.dll` SHA256：`654743426627198E959CEF9998E1FC9D2BF6F0973E44EFD4F975F43820E455DF`

自动分析证据不足时应停止安装，不要猜测或强行套用邻近版本偏移。详细验证过程见 [Windows 25H2 修复全过程](docs/RDPWrap_25H2_修复全过程.md)。

## 上游与许可证

本项目包含或衍生自以下开源工作：

- [stascorp/rdpwrap](https://github.com/stascorp/rdpwrap) — Copyright 2014 Stas'M Corp.
- [anhkgg/SuperRDP](https://github.com/anhkgg/SuperRDP) — 部分 wrapper 修改，Copyright 2021 anhkgg.
- 社区维护的 `rdpwrap.ini` 配置；具体来源列在程序的在线更新代码和文档中。

RDP Wrapper 相关源码使用 **Apache License 2.0**。该许可证允许复制、修改和再发布，通常不需要另行向原作者申请许可，但必须保留版权声明、许可证文本，并说明修改。本仓库已在 [LICENSE](LICENSE) 和 [NOTICE](NOTICE) 中保留这些信息。

便携包内的 Microsoft .NET Runtime 适用其随附的 `app/dotnet/LICENSE.txt` 和 `app/dotnet/ThirdPartyNotices.txt`，不由本仓库的 Apache License 重新许可。

## 免责声明

本软件按“原样”提供，不附带任何明示或默示担保。修改远程桌面服务可能造成连接中断、系统更新后失效或与本机安全策略冲突；使用前请准备本地登录和恢复手段。
