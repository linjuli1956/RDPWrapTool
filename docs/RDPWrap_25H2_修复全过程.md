# Windows 25H2 RDPWrap "连接数量有限" 修复全过程

> 日期：2026-07-30
> 环境：Windows 11 Pro for Workstations 25H2（OS Build 26200.8875）
> termsrv.dll：显示版本 10.0.26100.8875 / 数值版本 10.0.26100.8737
> 结果：✅ 四补丁全部生效，SLInit hook 触发，多会话恢复

---

## 1. 问题现象

RDP 连接时弹出：

> 与此计算机的连接数量是有限的，现在已经使用所有连接。请尝试稍后连接或与系统管理员联系。

这是 RDPWrap 的经典症状：**SLInit 没生效**，`lMaxUserSessions` 停在客户端 SKU 默认值（1），控制台已占用 1 个会话，任何第二个连接都会被拒。

---

## 2. 诊断过程（每步都有证据）

诊断脚本：`diag_26h2.ps1` / `diag2_26h2.ps1` / `diag3_26h2.ps1`（项目根目录）

### 2.1 termsrv.dll 指纹比对 → 发现版本根本不是 26H2

| 文件 | MD5 | 版本 |
|---|---|---|
| `C:\Windows\System32\termsrv.dll` | `9FAD34A2...` | **10.0.26100.8875** |
| 项目里分析的副本 | `3EAD5DB3...` | 10.0.28000.2336（26H2，另一台机器的） |

**结论：本机是 25H2（26100 分支），之前修的 `[10.0.28000.2336]` 节对本机完全无效。**

### 2.2 System32 安装状态 → INI 是修复前的旧版

- `rdpwrap.dll` / `rdpwrap.ini` 都在，svchost（PID 11468）确实加载了 rdpwrap.dll
- 但 INI 是 7/29 22:37 部署的**修复前版本**：
  - `[10.0.28000.2336]` 还是旧的错误偏移（30389 / C0030 / 858E4），且缺 `LocalOnlyPatch`
  - 不含 `mov_eax_1_nop_2` 补丁码
  - **末尾无 CRLF**（会导致最后一行 hex 值被截断）
  - `LogFile=\rdpwrap.txt` → svchost 无权写 C:\ 根目录 → **没有任何日志**

### 2.3 决定性事实：两份 INI 里 26100 节的数量 = 0

项目 INI 里一个 26100 节都没有 → rdpwrap 加载后找不到本机版本节 → **完全空转**，系统维持默认单会话限制。

### 2.4 拉取社区最新 INI

- `raw.githubusercontent.com` 超时（网络问题），asmtron 镜像太旧
- 通过 **jsDelivr 镜像**成功下载 sebaxakerhtc 最新 INI（554KB，85 个 26100 节）：
  `https://fastly.jsdelivr.net/gh/sebaxakerhtc/rdpwrap.ini@master/rdpwrap.ini`
- 没有 8875，但有 **8737**（最近邻版本）

---

## 3. 偏移验证（不验证不上生产）

脚本：`find_26100_8875.py`（58 种配置自动打分）→ `verify_26100_8875.py`（定点复核）

对 `[10.0.26100.8737]` 配置逐字节比对本机 DLL：

| 补丁点 | 偏移 | 期望字节 | 结果 |
|---|---|---|---|
| LocalOnly（jmpshort） | `0x95301` | `74`（jz rel8） | ✅ MATCH |
| SingleUser（mov_eax_1_nop_2，7字节） | `0xA25AB` | `48 FF 15`（call [rip]，恰好7字节） | ✅ MATCH |
| DefPolicy（r9d_rdi_jmp） | `0x9F74D` | `44 8B 8F 38 06 00 00 45 3B C1 75`（mov r9d,[rdi+638h]） | ✅ MATCH |
| SLInit hook | `0xB6678` | `40 57 48 81 EC`（函数序言） | ✅ MATCH |

附加证据：hook 目标函数 `0xB6678` 内部有对 SLInit 数据区 `0x12913C`（bServerSku）的写入 → 确认是真实的 `CSLQuery::Initialize`。

**4/4 全部通过 → 8737 配置与 8875 二进制代码一致，可用。**

---

## 4. 最隐蔽的坑：版本资源"双标"

第一次部署 8875 节后重启，日志依然没有任何 Patch 输出。读源码定位：

- rdpwrap 的 `GetModuleVersion()`（[RDPWrap.cpp:341](my_rdpwrap_tool/src/RDPWrapDLL/RDPWrap.cpp)）用 `GetModuleHandle` + `FindResource` 读**已加载模块的 VS_FIXEDFILEINFO 数值字段**
- 日志显示 `Version: 10.0.26100.8737`，而文件属性/PowerShell 显示的是**字符串** 8875
- rdpwrap 按数值版本拼节名 `[10.0.26100.8737]` 去查 INI
- 关键：`IniFile->SectionExists(Sect)` 失败时**静默跳过全部补丁，零报错**（[RDPWrap.cpp:766](my_rdpwrap_tool/src/RDPWrapDLL/RDPWrap.cpp)）

**修复：8737 和 8875 两个节名都写进 INI，内容相同。**

---

## 5. 最终修复内容

两份 INI 同步追加（`my_rdpwrap_tool\bin\rdpwrap.ini` 和 `src\RDPWrapTool\rdpwrap.ini`），末尾保留 CRLF：

```ini
[10.0.26100.8737]
LocalOnlyPatch.x64=1
LocalOnlyOffset.x64=95301
LocalOnlyCode.x64=jmpshort
SingleUserPatch.x64=1
SingleUserOffset.x64=A25AB
SingleUserCode.x64=mov_eax_1_nop_2
DefPolicyPatch.x64=1
DefPolicyOffset.x64=9F74D
DefPolicyCode.x64=CDefPolicy_Query_r9d_rdi_jmp
SLInitHook.x64=1
SLInitOffset.x64=B6678
SLInitFunc.x64=New_CSLQuery_Initialize

[10.0.26100.8737-SLInit]
bInitialized.x64      =129138
bServerSku.x64        =12913C
lMaxUserSessions.x64  =129140
bAppServerAllowed.x64 =129148
bRemoteConnAllowed.x64=129154
bMultimonAllowed.x64  =129158
ulMaxDebugSessions.x64=129160
bFUSEnabled.x64       =129164
```

（`[10.0.26100.8875]` 及 `-SLInit` 节内容相同，一并写入）

部署脚本 `deploy_8875.ps1`：校验末尾 CRLF → 复制到 System32 → 重启 TermService → 读日志验证。

---

## 6. 成功验证日志（C:\Windows\Temp\rdpwrap.txt）

```
Version:    10.0.26100.8737
Freezing threads...
Patch CEnforcementCore::GetInstanceOfTSLicense               ← LocalOnly ✓
Patch CSessionArbitrationHelper::IsSingleSessionPerUserEnabled ← SingleUser ✓
Patch CDefPolicy::Query                                      ← DefPolicy ✓
Hook CSLQuery::Initialize                                    ← SLInit hook ✓
Resumimg threads...
>>> CSLQuery::Initialize                                     ← hook 已触发 ✓
SLInit [0x...913C] bServerSku = 1
SLInit [0x...9154] bRemoteConnAllowed = 1
SLInit [0x...9164] bFUSEnabled = 1
SLInit [0x...9148] bAppServerAllowed = 1
SLInit [0x...9158] bMultimonAllowed = 1
SLInit [0x...9140] lMaxUserSessions = 0    ← 0 = 不限制
SLInit [0x...9138] bInitialized = 1
<<< CSLQuery::Initialize
```

各变量地址低 24 位（0x129138~0x129164）与验证值完全吻合。

---

## 7. 经验总结（下次少踩坑）

1. **rdpwrap 认的是数值版本，不是文件属性里显示的字符串版本**——以日志里 `Version:` 行为准建节；两者不一致时（ servicing 分支常见）两种节名都写。
2. **节不存在 = 静默零补丁**，无任何报错日志。判断补丁是否生效只能看日志有没有 `Patch CDefPolicy::Query` / `>>> CSLQuery::Initialize`。
3. **任何偏移上 INI 前必须逐字节验证**：jz=0x74、call [rip]=48 FF 15、mov r9d,[rdi+638h]=44 8B 8F 38 06、函数序言、SLInit 数据区写入者，五类证据缺一不可。
4. **INI 末尾必须有 CRLF**，否则最后一行 hex 被解析截断。
5. **LogFile 路径必须对 svchost 可写**（用 `C:\Windows\Temp\rdpwrap.txt`，不要用 `\rdpwrap.txt`）。
6. GitHub raw 不通时走 jsDelivr 镜像拉社区 INI。
7. 同一版本号 ≠ 同一二进制：部署前先比 MD5/SHA256。

---

## 8. 涉及文件

| 文件 | 作用 |
|---|---|
| `my_rdpwrap_tool\bin\rdpwrap.ini` | 生产 INI（含 26H2 28000.2336 + 25H2 8737/8875 全部修正） |
| `my_rdpwrap_tool\src\RDPWrapTool\rdpwrap.ini` | 源码同步副本 |
| `community_latest.ini` | sebaxakerhtc 社区 INI 快照 |
| `find_26100_8875.py` | 58 种 26100 配置自动打分筛选 |
| `verify_26100_8875.py` | 定点逐字节复核 |
| `diag_26h2.ps1` ~ `diag3_26h2.ps1` | 本地诊断脚本 |
| `deploy_8875.ps1` | 部署 + 重启 + 验证一键脚本 |
