# VaultGuard · 备份还原与终端管控双系统

两套可独立部署的企业级应用：**虚拟化数据备份还原系统**（B/S）与**终端安全准入与行为审计系统**（C/S）。
均由 .NET 8 从零实现，含完整源码、公共库、编译脚本与设计文档。

| 项目 | 内容 |
|---|---|
| 技术栈 | .NET 8（C#）+ SQLite + Razor Pages + WPF-free 单文件发布 |
| 目标平台 | Windows 10 / 11 / Server 2016 及以上（x64） |
| 工程规模 | 4 个工程 / 79 个 C# 与 Razor 文件 / 约 7.5k 行（.cs 6082 行 + .cshtml 1444 行） |
| 编译验证 | `dotnet build -c Release` 通过，0 警告 0 错误（本机 SDK 8.0.200 实测） |

> **关于本仓库**
> 这是真实商业交付项目的**脱敏版本**：客户名称、产品商标、业务数据与交付过程资料已全部移除，
> 保留完整技术实现。工程代号 `VaultGuard` 与占位公司名均为中性替换名。
> 原作者保留本仓库展示权；本仓库不含任何客户保密信息。

---

## 一、两套软件

| 编号 | 产品名称 | 版本 | 架构 | 默认端口 | 默认账号 |
|---|---|---|---|---|---|
| 系统一 | 虚拟化数据备份还原系统 | V1.0 | B/S | http://localhost:5081 | admin / VaultGuard@2026 |
| 系统二 | 终端安全准入与行为审计系统 | V1.0 | C/S | http://localhost:5082 | admin / VaultGuard@2026 |

> 首次启动会自动创建默认管理员账号。本版本**未提供在线改密界面**；如需更换口令，可直接更新数据库 `users` 表中的 `password_hash` 与 `salt` 两个字段（哈希算法见公共库 `VaultGuard.Common.Security.PasswordHasher`）。
>
> 两套系统默认均绑定 `0.0.0.0`（所有网卡）：本机用 `localhost` 访问，局域网内其他电脑用 `http://服务器IP:端口` 访问。终端代理安装在别的电脑时，需把 `appsettings.json` 的 `ServerUrl` 改成控制端的局域网地址，并确保防火墙放行 `TCP 5082`。

---

## 二、目录结构

```
dotnet-backup-endpoint-suite/
├─ build.bat                      一键编译脚本（双击即可）
├─ VaultGuard.sln                 解决方案文件
├─ README.md                      本文件
├─ THIRD-PARTY-NOTICES.md         开源组件与许可证清单
├─ docs/                          设计与说明文档
│   ├─ 01-开发方案与工程结构设计.md
│   ├─ 02-编译与部署说明.md
│   ├─ 03-功能演示与验证步骤.md
│   └─ 04-虚拟机无代理备份接口说明.md
└─ src/
    ├─ VaultGuard.Common/           公共库：数据访问 / 口令哈希 / 凭据加密 / 文件摘要
    ├─ VaultGuard.Backup.Web/       系统一：备份还原控制台（B/S）
    ├─ VaultGuard.Endpoint.Server/  系统二：终端管控控制端（C/S 服务端）
    └─ VaultGuard.Endpoint.Agent/   系统二：终端代理（Windows 服务）
```

公共库被三个应用工程共同引用，编译顺序由解决方案自动处理。

---

## 三、快速开始

### 方式 A：一键编译（推荐）

双击 `build.bat`，或在命令行执行：

```bat
build.bat
```

脚本会检查环境、还原依赖、编译并发布出免安装单文件程序到 `dist\` 目录：

```
dist\BackupWeb\VaultGuard.Backup.Web.exe        系统一控制台
dist\EndpointServer\VaultGuard.Endpoint.Server.exe   系统二控制端
dist\EndpointAgent\VaultGuard.Endpoint.Agent.exe     系统二终端代理
```

### 方式 B：源码直接运行（开发调试）

```bat
dotnet run --project src\VaultGuard.Backup.Web     --urls http://localhost:5081
dotnet run --project src\VaultGuard.Endpoint.Server --urls http://localhost:5082
```

终端代理：

```bat
dotnet run --project src\VaultGuard.Endpoint.Agent
```

### 环境要求

- **.NET SDK 8.0 及以上**（下载地址 https://dotnet.microsoft.com/download/dotnet/8.0）
  - 只需装 SDK，**不需要安装 Visual Studio**（约 200 MB vs 10 GB）
  - 装完在命令行执行 `dotnet --version`，能显示 `8.0.xxx` 即可
- Windows 10 / 11 / Server 2016 及以上（64 位）
- 终端代理如需禁用 USB 存储设备，需以管理员身份运行

### 关于 NuGet 还原与自包含发布

本仓库**不携带离线包**，依赖在首次编译时从 nuget.org 还原（仅 2 个：`Microsoft.Data.Sqlite`、`ZstdSharp.Port`），需要本机能访问外网。

首次编译约 1~3 分钟，主要耗时在三套自包含程序的发布（每套约 70~90 MB）。

#### ★ 运行时包版本必须与 SDK 对应

自包含发布（`--self-contained`）需要的运行时包版本，由**本机 .NET SDK 版本**决定，
与项目源码无关。版本不对时，第 1 步编译会成功，第 2 步发布会报 `NU1102 找不到包`：

| 本机 SDK 版本 | 需要的运行时包版本 |
|---|---|
| SDK 8.0.1xx / 8.0.2xx（如 8.0.200） | 8.0.2 |
| SDK 8.0.3xx / 8.0.4xx（如 8.0.427） | 8.0.3x |

**先用 `dotnet --version` 确认版本**。联网环境下 SDK 会自动从官方源补齐所需版本，无需额外操作；
若需完全离线编译，可自建本地 NuGet 源并把对应 `runtimepack.win-x64.microsoft.netcore.app` 放进去。

> 只想跑起来看效果的话，也可以跳过自包含发布，直接用 `dotnet run` 启动（见方式 B）。


---

## 四、功能范围

逐项功能的可复现验证步骤见 `docs/03-功能演示与验证步骤.md`。

### 系统一 · 虚拟化数据备份还原系统

- 登录界面展示品牌名称与版权（`Branding` 单一来源，两套系统共用）
- 备份 Windows 文件夹（本地目录、盘符、UNC 共享）
- 文件还原（还原到原始位置或指定目录）
- 全量 / 差异 / 增量三种备份模式
- 使用账号密码添加 Windows 主机对象，凭据加密存储
- 虚拟机无代理备份：预留接口，需具备有效许可的 vSphere/vCenter 环境

### 系统二 · 终端安全准入与行为审计系统

- 登录窗口展示品牌名称与版权
- 禁用控制面板（策略下发即生效）
- 禁用 USB 存储设备（需管理员权限）
- 记录文件新建、修改、删除、重命名操作
- 按计算机对象、时间查询审计日志

---

## 五、数据存放位置

程序运行后在自身目录下自动创建 `data` 子目录：

| 文件 | 说明 |
|---|---|
| `data\vaultguard-backup.db` | 系统一数据库 |
| `data\vaultguard-endpoint.db` | 系统二数据库 |
| `data\secret.key` | 主机凭据加密密钥（**请妥善保管，丢失后已保存的密码无法解密**） |
| `data\logs\` | 运行日志 |

---

## 六、说明

本仓库为作者个人技术实现展示，客户名称、商标与业务数据已全部脱敏。
文中出现的 `示例科技有限公司` / `Example Technology Co., Ltd.` 均为占位名，
`Branding.cs` 中的公司名与版权行可由使用方自行替换。

本工程集成的开源组件及其许可证见 `THIRD-PARTY-NOTICES.md`。
