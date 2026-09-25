# PMate

项目经理的本地助手：项目、里程碑、任务看板、任务时间轴、承诺跟踪、工时表、工作笔记、RAID 登记表、提醒通知。

- 数据存在本机的 SQLite 数据库里，不上传到任何地方。
- 代码通过 GitHub 私有仓库同步。使用的电脑每次启动时自动 `git pull` 更新。
- 只需要 .NET 10 SDK 和 Git。不需要 Node.js，也不需要管理员权限。

## 架构

```
浏览器（Edge 应用窗口）
   │  http://localhost:5178   只监听本机
   ▼
ASP.NET Core (.NET 10)  ── wwwroot/  前端：纯 JavaScript 模块，无需构建
   │  Api/Endpoints.cs      REST API
   │  Services/             业务规则（状态变更、级联删除、导入导出）
   │  Data/                 EF Core + 迁移
   ▼
SQLite   %LOCALAPPDATA%\PMate\pmate.db
         %LOCALAPPDATA%\PMate\backups\   每天自动备份，保留 14 份
```

| 目录 | 内容 |
|---|---|
| `src/PMate/Domain` | 数据实体 |
| `src/PMate/Auth` | 用户、登录、密码哈希、权限 |
| `src/PMate/Services` | 业务逻辑。所有规则都在服务端，以后多人使用时不用搬 |
| `src/PMate/Infrastructure` | 数据路径、自动备份、更新检查、提醒 |
| `src/PMate/Desktop` | Windows 托盘图标和通知。只在 Windows 上编译 |
| `src/PMate/wwwroot/js` | 前端：`api.js` 调接口，`store.js` 管数据，其余是界面 |
| `tests/PMate.Tests` | 单元测试 |
| `scripts/` | 启动、停止、开机自启 |
| `legacy/` | 第一版单文件工具 |

## 开发电脑

```bash
dotnet tool restore
dotnet run --project src/PMate --launch-profile dev
```

开发时打开 http://localhost:5179 。数据存在仓库里的 `.devdata/`，已被 git 忽略。

```bash
dotnet test
```

修改数据库结构（例如新增一张表）：

```bash
dotnet ef migrations add 名称 --project src/PMate --output-dir Data/Migrations
```

迁移文件要一起提交。使用的电脑启动时会自动升级数据库。

## 使用的电脑（第一次安装）

1. 安装 .NET 10 SDK 和 Git。
2. 克隆仓库到一个固定位置，例如：
   ```bash
   git clone https://github.com/<你的账号>/pmate.git D:\Tools\pmate
   ```
3. 创建桌面快捷方式和开机自启：
   ```bash
   powershell -ExecutionPolicy Bypass -File D:\Tools\pmate\scripts\install-startup.ps1
   ```
4. 双击桌面上的 **PMate**。第一次启动需要下载依赖包，大约 1–2 分钟。

## 更新是怎么工作的

1. 在开发电脑上提交并推送到 GitHub。
2. 使用的电脑有两种方式拿到新版本：
   - 开机或点击桌面快捷方式时，`start.ps1` 自动执行 `git pull`。
   - 程序每小时检查一次 GitHub。有新版本时，页面顶部出现 **Update and restart** 按钮。
3. 数据库在仓库外面，所以更新不会影响数据。

如果 `git pull` 失败（例如没有网络），会继续用当前版本启动。日志在 `%LOCALAPPDATA%\PMate\logs`。

## 登录和用户

- **第一次启动**：登录页会要求创建第一个账号。这个账号是管理员（Admin）。
- **角色**：Admin 管理用户、设置、看板列、备份和更新。Member 使用项目、任务、笔记和 RAID 登记表。
- **添加用户**：Admin 在 **Users** 页面添加用户，并给一个临时密码。用户第一次登录时必须改成自己的密码。
- **Remember me**：勾选后 30 天内不用重新登录。不勾选时，关闭浏览器就要重新登录。浏览器也可以用自己的密码管理器保存密码。
- **密码安全**：只保存加盐的 PBKDF2 哈希值，从不保存明文密码。连续输错 5 次，账号锁定 5 分钟。
- **退出其他登录**：修改密码、停用账号或修改角色后，这个用户在其他电脑上的登录会立即失效。
- **停用而不是删除**：用户只能停用，不能删除，这样记录上的“创建人”仍然正确。系统至少要有一个启用的 Admin。

## 功能说明

**提醒通知**：程序运行时，时钟旁边有一个 PMate 图标。工作日会发两次 Windows 通知：
- 早上（默认 09:00）：今天到期和已逾期的任务、承诺、RAID 项
- 傍晚（默认 17:30）：今天的工时不够时，提醒你补记

时间可以在 **Settings & Data** 里修改。电脑晚于提醒时间开机时，开机后会补发。

**撤销删除**：删除后，屏幕下方会出现 **Undo** 按钮。数据会在“最近删除”里保留 30 天，可以在 **Settings & Data** 里恢复。

**工作笔记**：按项目归类，边写边自动保存。支持中英文全文搜索（SQLite FTS5）。选中一行文字，可以直接转成任务、承诺或 RAID 项。

**RAID 登记表**：风险、假设、问题、依赖。每个项目单独编号（R-1、I-2 等）。风险的等级 = 影响 × 可能性。

## 从第一版迁移数据

在旧的单文件工具里点 **Export backup (JSON)**。然后在新版本的 **Settings & Data → Restore from backup** 里选择这个文件。

## 安全

- 只监听 `localhost`，局域网里的其他电脑访问不到。
- 只接受 Host 为 `localhost` 的请求，防止 DNS 重绑定攻击。
- 所有写操作都要求 `X-PM-Client` 请求头，其他网站无法在后台修改数据。
- 数据文件和备份永远不进 git（见 `.gitignore`）。

## 以后上云、多人使用

现在的结构已经为这一步做了准备：

| 需要做的事 | 在哪里改 |
|---|---|
| 换成云数据库（PostgreSQL 或 Azure SQL） | `Program.cs` 里换 EF Core 提供程序，重新生成迁移 |
| 公司账号登录（Microsoft 365，建议用 Entra ID 单点登录） | 现在已有用户和登录。在 `Auth/AuthSetup.cs` 里加一个 OpenID Connect 登录方式，按邮箱对应到 `Users` 表 |
| 任务分配给团队成员 | `TaskItem.Owner` 现在是名字，改成关联 `Users` 表的 `OwnerUserId` |
| 权限（谁能看哪个项目） | 增加项目成员表，在服务层过滤 |
| 部署 | Azure App Service 或容器，前端代码不用改 |

部署到 Google Cloud 免费服务器：见 [docs/deploy-gcp.md](docs/deploy-gcp.md)。

注意：公司数据放到云端之前，要先得到 IT 的批准。
