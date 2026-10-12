# AGENTS.md — 合同评审管理系统

本文件面向 AI 编码代理，介绍本仓库的架构、构建方式与开发约定。项目文档与代码注释均使用中文，本文件同样使用中文。

## 项目概述

合同评审管理系统：覆盖图片验证码登录、会话认证、首页统计、合同录入与状态流转、组织架构、角色权限、评审规则、签约主体、关键节点、附件管理、通知中心、审计日志和数据库备份的单体 Web 应用。

- 后端：.NET 8（`net8.0`）ASP.NET Core **Minimal API**，无控制器。唯一 NuGet 依赖是 `Microsoft.Data.Sqlite` 8.0.10，SQLite 既是数据库也是持久层（`data/contract_review.db`，首次启动自动建表并写入种子数据，重启不丢数据）。
- 前端：React 18 + TypeScript + Ant Design 5 + @ant-design/plots + TanStack Query + react-router-dom（**HashRouter**，适配纯静态文件部署）。Vite 5 构建，产物直接写入 `wwwroot/` 由 ASP.NET Core 托管。
- 根目录的 `合同评审管理系统.pdf` 是 2021 年的需求文档（旧版 .NET Framework 4.7 源码已不存在，不承诺旧版兼容）。

## 目录结构与代码组织

```
Program.cs                                    # 全部 HTTP 路由（Minimal API），项目入口
ContractReviewSystem.csproj                   # 后端项目文件（net8.0）
src/ContractReviewSystem/
  Shared/AppModels.cs                         # 全部请求/响应 record、Security(SHA256)、Captcha(SVG)
  Infrastructure/SqliteStore.cs               # 全部 SQL：建表、种子数据、所有数据访问方法
  Attachments/ Auditing/ Authentication/
  Contracts/ Rules/                           # 空目录（预留分层，目前无代码）
frontend/
  package.json vite.config.ts tsconfig.json
  src/main.tsx                                # 入口：ConfigProvider/QueryClient/HashRouter，auth-expired 处理
  src/App.tsx                                 # 登录页 + 侧边栏布局 Shell + 路由表
  src/api.ts                                  # 全部 API 类型与 fetch 封装
  src/pages.tsx                               # 全部页面组件（单文件）
  src/styles.css
wwwroot/                                      # 前端构建产物（由 Vite 输出，勿手改）
data/contract_review.db data/attachments/     # 运行时数据（SQLite 库 + 附件文件），不入版本管理
bin/ obj/                                     # .NET 构建产物
```

架构要点：

- `Program.cs` 只负责路由映射和会话校验（`Current`/`Require`/`RequirePermission` 本地函数）：读 `cr_session` Cookie → 查内存 `sessions` 字典 → 未登录返回 401；`Require*` 内部直接 `result.ExecuteAsync(ctx)` 写出响应（返回 `Task` 而非 `Task<IResult>`）。**注意**：单 `HttpContext` 参数的 async lambda 若返回 `Task<IResult>` 会被编译器推断为 `RequestDelegate`，返回值被静默丢弃（空 200，ASP0016 警告）——所以 `Require*` 自己执行结果，路由一律写成 `{ await Require(...); }` 语句体。
- 会话和验证码都是**进程内内存**（`ConcurrentDictionary`），重启即失效，不适用于多实例部署。
- 合同状态机：由 `SqliteStore.Actions` 字典（12 个动作）统一描述，`WorkflowActionAsync` 按 `From` 状态与所需权限校验，未知动作返回 400；流转历史与评审意见同事务写入 `contract_workflow_history`。简化接口 `ChangeAsync`/`RejectAsync` 只是转发到 `WorkflowActionAsync`（`submit`/`review-pass`/`reject`）。
- 附件采用软删除（`deleted_at`），文件落盘 `data/attachments/{contractId}/{guid}{ext}`，版本号自增。
- 启动时 `InitializeAsync` 会**重置 admin 密码为 `admin`**（演示性质），并 `INSERT OR IGNORE` 种子部门/角色/权限/主体/通知。
- 首次启动还会自动写入全套演示数据集（`SeedDemoDataAsync`，幂等：检测到合同《数据中心服务器采购合同》即跳过）：36 份合同覆盖状态机全部 11 种状态并配套流程历史/评审意见/节点/通知/审计日志，另有 12 个业务用户、10 个签约主体、8 条评审规则、带真实磁盘文件的附件。数据库文件被删除后重启即可重新生成。

## 构建与运行命令

后端（项目根目录）：

```bash
dotnet restore
dotnet run --urls http://localhost:5080
```

前端开发（另开终端；Vite 端口 5177，`/api` 代理到 5080）：

```bash
cd frontend
npm install
npm run dev
```

前端发布构建（`tsc -b && vite build`，清空并写入上级 `wwwroot/`）：

```bash
cd frontend && npm run build
```

类型检查：`npm run typecheck`。默认账号 `admin` / `admin`（每次启动会被重置）。

## 测试

**项目没有任何测试**：无 .NET 测试项目，package.json 也没有测试脚本。验证改动的方式是：

1. `cd frontend && npm run build`（含 `tsc` 类型检查）必须通过；
2. `dotnet build` 或 `dotnet run --urls http://localhost:5080` 能正常启动；
3. 手动走通相关 API / 页面（如登录 → 建合同 → 提交 → 通过/退回）。

添加测试框架前先与维护者确认。

## 代码风格约定

- **后端**：极简风格——顶级语句、单行 Lambda 路由、C# record 做 DTO、原始 SQL 字符串内联在 `SqliteStore` 中，SQL 参数一律用 `$name` 占位符 + `Add(x, "$name", value)`  helper（禁止字符串拼接防注入）。中文文案直接写在代码里（状态名、错误消息）。
- **前端**：TS 严格模式；单行压缩写法很常见（`App.tsx`、`pages.tsx` 基本是超长单行组件），新代码应与所在文件现有风格保持一致；API 调用全部走 `api.ts` 的封装，不要散落 `fetch`。
- 前端 401 统一约定：收到 401 时 `api.ts` 派发 `auth-expired` 事件，`main.tsx` 监听后将用户置为未登录。

## 安全注意事项（按现状如实描述，改动时需意识到）

- 密码使用**未加盐 SHA256** 存储（`Security.Hash`），属演示级实现，勿在生产使用。
- 权限校验已接入路由：写操作与敏感读取通过 `RequirePermission(ctx, permission, ...)` 校验权限点（`HasPermissionAsync`），角色名为 `系统管理员` 的用户直接放行，其余用户按 `role_permissions` 判定；只读接口仅用 `Require` 校验登录。部分写接口（如 `POST /api/contracts/{id}/milestones`、`POST /api/notifications/{id}/read`）目前只校验登录，未细分权限点。
- 默认凭据 `admin`/`admin` 且每次启动被重置。
- 会话与验证码都是**进程内内存**（`ConcurrentDictionary`），重启即失效，不支持多实例部署。
- 附件上传只校验非空，无类型/大小限制。
- `GET /api/backup` 需要 `backup.download` 权限，通过后会直接下载整库文件。

## 部署

单体自包含部署：发布前端构建到 `wwwroot/` 后，ASP.NET Core 同时托管 API 与静态文件，`MapFallback` 将所有非 `/api` 请求回退到 `index.html`（配合 hash 路由）。数据全部在 `data/` 目录（SQLite 单文件 + 附件目录），备份整目录即可迁移。
