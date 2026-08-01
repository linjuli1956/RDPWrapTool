# RDPWrapTool 当前版本

本版本将项目整理为更轻量的单 exe 发布形态。

## 主要变化

- 清理临时脚本、测试 DLL、构建中间产物和旧渲染工程。
- 新增 `PROJECT_STRUCTURE.md`，说明项目目录、文件用途、构建和打包命令。
- `rdpwrap.dll` 和 `rdpwrap.ini` 已内置到 `RDPWrapTool.exe`。
- 用户分发时只需要拷贝 `bin\RDPWrapTool.exe`。
- 新增 `AssetManager`，首次运行自动释放内置 DLL/INI。
- 安装 RDPWrap 时自动启用 Windows 远程桌面和防火墙规则。
- 总览页和安装部署页新增“启用远程桌面”按钮。
- 创建用户、加入 RDP 用户组后自动启用 Windows 远程桌面。
- 自动分析仍采用严格验证策略：验证失败不写入 INI。

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
