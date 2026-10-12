# 合同评审管理系统

[![License](https://img.shields.io/badge/license-AGPL--3.0%20%7C%20Commercial-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)
![React](https://img.shields.io/badge/React-18-61DAFB.svg)
![SQLite](https://img.shields.io/badge/SQLite-3-003B57.svg)

面向企业内部合同评审场景的**单体 Web 应用**：从合同录入、分阶段评审、审批签署到履行归档，覆盖组织架构、角色权限、评审规则、签约主体、关键节点、附件版本管理、通知中心、审计日志与数据库备份。

后端是 .NET 8 Minimal API，前端是 React 18 + Ant Design 5，数据全部落在单个 SQLite 文件里——**克隆下来 `dotnet run` 就能跑，不需要额外安装数据库或中间件。**

## 功能特性

| 模块 | 能力 |
| --- | --- |
| 认证与账户 | 图片验证码登录、Cookie 会话、登录失效自动登出、账户中心查看资料与修改密码 |
| 首页看板 | 合同总量与状态分布、近期合同列表 |
| 合同管理 | 录入与编辑、甲乙方及多方签约主体关联、币种金额、签署/生效/到期日期、付款条款、标签；草稿可编辑、删除 |
| 评审工作台 | 按阶段筛选合同、只读详情页、依据启用中的评审规则做人工检查 |
| 流程流转 | 12 个流程动作、11 种状态，驳回与签署失败强制填写意见，流程历史与评审意见留痕 |
| 组织架构 | 部门树、成员岗位与角色归属调整 |
| 角色权限 | 角色维护与权限点分配，写操作在服务端按权限点强校验 |
| 评审规则 | 规则增删改查、启用/停用 |
| 签约主体 | 企业/个人/分支机构、联系人、统一代码；存在关联合同时禁止删除 |
| 关键节点 | 合同的计划节点、负责人与备注 |
| 附件管理 | 上传、下载、软删除、版本号自增，文件落盘到 `data/attachments/` |
| 通知中心 | 站内提醒、标记已读 |
| 审计日志 | 关键操作留痕，一键下载 SQLite 备份 |
| 报表中心 | 状态、类型、部门统计与月度新增合同趋势图 |

## 技术栈

| 层 | 选型 |
| --- | --- |
| 后端 | .NET 8（`net8.0`）ASP.NET Core **Minimal API**，无控制器 |
| 数据库 | SQLite，唯一 NuGet 依赖 `Microsoft.Data.Sqlite` 8.0.10；首次启动自动建表并写入种子数据 |
| 前端 | React 18 + TypeScript（严格模式）+ Ant Design 5 + @ant-design/plots + TanStack Query 5 + react-router-dom 6（HashRouter） |
| 构建 | Vite 5，产物直接输出到 `wwwroot/`，由 ASP.NET Core 托管 |

## 快速开始

### 环境要求

- .NET SDK 8.0 或更高
- Node.js 18 或更高（仅前端开发与构建需要）

### 运行

```bash
git clone https://github.com/bestphper/contract-review-system.git
cd contract-review-system
dotnet restore
dotnet run --urls http://localhost:5080
```

浏览器打开 <http://localhost:5080>，默认账号 `admin` / `admin`（首次登录需输入页面生成的图片验证码）。

### 前端开发

前端源码位于 `frontend/`。开发时分别启动 API 和 Vite：

```bash
cd frontend
npm install
npm run dev
```

Vite 开发服务器监听 `http://localhost:5177`，并把 `/api` 请求代理到 `http://localhost:5080`。客户端路由使用 hash 形式，适配静态文件部署。

### 发布构建

```bash
cd frontend && npm run build
```

该脚本执行 `tsc -b && vite build`，会清空并写入上级项目的 `wwwroot/`，随后由 ASP.NET Core 一并托管。仅做类型检查可用 `npm run typecheck`。

> 后端代码修改后需要重启 ASP.NET Core 进程，单独刷新浏览器不会更新运行中的 API。

## 默认账号与演示数据

- 默认账号 `admin` / `admin`，**每次启动都会被重置为这个密码**（演示用途，见下方安全说明）。
- 演示业务账号密码统一为 `123456`，可按角色分工试用：`zhangwei`（业务负责人）、`chenjing`（法务评审人）、`huangmin`（财务审批人）、`wuqian`（只读查看者）。
- 首次启动会自动写入 36 份演示合同，覆盖全部 11 种状态，并配套流程历史、评审意见、关键节点、通知与审计日志；另有 12 个业务用户、13 个签约主体、8 条评审规则和 18 个带真实文件的附件。
- 演示数据写入是幂等的：检测到合同《数据中心服务器采购合同》即跳过。删除 `data/contract_review.db` 后重启即可重新生成。
- 管理员可调用 `POST /api/demo/seed` 幂等补充演示合同，不提供清空重置。

## 目录结构

```
Program.cs                                    # 全部 HTTP 路由（Minimal API），项目入口
ContractReviewSystem.csproj                   # 后端项目文件（net8.0）
src/ContractReviewSystem/
  Shared/AppModels.cs                         # 全部请求/响应 record、Security(SHA256)、Captcha(SVG)
  Infrastructure/SqliteStore.cs               # 全部 SQL：建表、种子数据、数据访问方法
  Attachments/ Auditing/ Authentication/
  Contracts/ Rules/                           # 预留分层目录，目前为空
frontend/
  src/main.tsx                                # 入口：ConfigProvider / QueryClient / HashRouter
  src/App.tsx                                 # 登录页 + 侧边栏布局 + 路由表
  src/api.ts                                  # 全部 API 类型与 fetch 封装
  src/pages.tsx                               # 页面组件
  vite.config.ts                              # 开发端口与 /api 代理，构建输出到 ../wwwroot
wwwroot/                                      # 前端构建产物（由 Vite 输出，勿手改）
data/contract_review.db  data/attachments/    # 运行时数据（SQLite 库 + 附件文件）
```

架构要点：

- `Program.cs` 只负责路由映射与会话校验（`Current` / `Require` / `RequirePermission` 本地函数）：读取 `cr_session` Cookie → 查内存 `sessions` 字典 → 未登录返回 401，权限不足返回 403。
- 会话与验证码都是**进程内内存**（`ConcurrentDictionary`），重启即失效，**不支持多实例部署**。
- 合同状态流转由 `SqliteStore` 中的动作白名单统一校验，未知动作直接返回 400。

## 合同状态机

```
草稿 ──提交评审──▶ 评审中 ──评审通过──▶ 评审通过 ──发起审批──▶ 审批中 ──审批通过──▶ 审批通过
 │                    │                                          │
 │                    └──评审驳回──▶ 已驳回                     └──审批驳回──▶ 评审通过
 └──作废──▶ 已作废

审批通过 ──发起签署──▶ 签署中 ──签署完成──▶ 已签署 ──开始履行──▶ 履行中 ──归档──▶ 已归档
                          └──签署失败──▶ 审批通过
```

| 动作 | 流转 | 所需权限 |
| --- | --- | --- |
| `submit` | 草稿 → 评审中 | `contract.edit` |
| `review-pass` | 评审中 → 评审通过 | `contract.review` |
| `reject` | 评审中 → 已驳回 | `contract.review` |
| `start-approval` | 评审通过 → 审批中 | `contract.approve` |
| `approve` | 审批中 → 审批通过 | `contract.approve` |
| `reject-approval` | 审批中 → 评审通过 | `contract.approve` |
| `start-signing` | 审批通过 → 签署中 | `contract.sign` |
| `signed` | 签署中 → 已签署 | `contract.sign` |
| `sign-failed` | 签署中 → 审批通过 | `contract.sign` |
| `activate` | 已签署 → 履行中 | `contract.edit` |
| `archive` | 履行中 → 已归档 | `contract.archive` |
| `void` | 草稿 → 已作废 | `contract.edit` |

系统管理员角色（`系统管理员`）可执行全部流程动作；其他用户按角色权限执行。`GET /api/contracts/{id}/actions` 返回当前用户在当前状态下可执行的动作，前端据此渲染按钮。

## 行为约定

- **分页**：合同、评审任务（按状态筛选）、通知、审计、签约主体、组织成员、角色、规则和合同附件列表支持分页。列表接口传入 `page` 和 `pageSize`，返回 `{ items, total, page, pageSize }`；页码最小为 1，每页最多 100 条。不传分页参数时保留数组响应，供主体、角色、人员选择器使用。合同和主体分页仍支持原有搜索、状态筛选。页面默认每页 10 条，可调整每页条数，搜索或筛选变化会回到第一页。
- **合同字段**：新建合同与 `/contracts/{id}` 编辑页共用合同字段：标题、类型、甲乙方主体、负责人、部门、币种、金额、签署/生效/到期日期、付款条款、说明和标签。主体从已有签约主体中选择，保存时同步甲乙方关联。
- **空值约定**：新增部门、角色、主体、合同、节点及用户归属时，省略的可选文本字段保存为空字符串，金额和上级部门等可空字段仍保存为 NULL。名称重复或关联记录不存在时返回可读的 400 错误；无操作权限时返回 403。
- **编辑窗口**：合同仅在草稿状态允许编辑、删除及变更附件和主体关联，提交评审后（包括已驳回）只读，管理员也不能修改内容。`DELETE /api/contracts/{id}` 删除草稿及其业务关联，保留审计记录；`DELETE /api/parties/{id}` 仅允许删除无关联合同的主体。
- **评审工作台**：详情路径 `/review/contracts/{id}` 始终只读，并展示启用的评审规则作为人工检查参考。评审通过属于 `contract.review`，提交属于 `contract.edit`，审批属于 `contract.approve`。流程历史、评审意见和审计日志记录实际登录人员，驳回及签署失败必须填写意见。

## API 一览

所有接口除登录、验证码、健康检查外均需有效会话；写操作还会按权限点校验。

**认证与账户**

- `GET /api/auth/captcha`、`POST /api/auth/login`、`POST /api/auth/logout`、`GET /api/auth/me`
- `POST /api/auth/password`

**合同与流程**

- `GET/POST /api/contracts`、`GET/PUT/DELETE /api/contracts/{id}`
- `GET /api/contracts/{id}/actions`
- `POST /api/contracts/{id}/submit|approve|reject`（简化流转）
- `POST /api/contracts/{id}/workflow/{action}`（完整流转动作，见状态机表）
- `GET /api/contracts/{id}/workflow`（流程历史）
- `GET/POST /api/contracts/{id}/parties`、`DELETE /api/contracts/{id}/parties/{partyId}`
- `GET/POST /api/contracts/{id}/milestones`（关键节点）
- `GET/POST /api/contracts/{id}/attachments`、`GET /api/attachments/{id}/download`、`DELETE /api/attachments/{id}`

**组织、权限与规则**

- `GET/POST /api/organization/departments`
- `GET /api/organization/users`、`POST /api/organization/users/{userId}/assignment`
- `GET/POST /api/organization/roles`
- `GET/POST /api/rules`、`PUT/DELETE /api/rules/{id}`

**签约主体、通知、审计与报表**

- `GET/POST /api/parties`、`DELETE /api/parties/{id}`
- `GET /api/notifications`、`POST /api/notifications/{id}/read`
- `GET /api/audit-logs`、`GET /api/backup`
- `GET /api/dashboard`、`GET /api/reports`
- `POST /api/demo/seed`（幂等补充演示合同）
- `GET /api/health`

## 数据存储与备份

- 数据库：`data/contract_review.db`（SQLite 单文件，首次启动自动建表并写入种子数据，重启不丢数据）。
- 附件：`data/attachments/{contractId}/{guid}{ext}`，采用软删除（`deleted_at`），版本号自增。
- 备份：登录后可在「审计日志」页点击「下载数据库备份」，或直接下载 `data/` 目录；迁移时拷贝整个 `data/` 目录即可。

## 安全说明（重要）

本项目是**演示性质**的实现，请勿直接用于生产环境。已知限制：

- 密码使用**未加盐 SHA256** 存储，未使用 bcrypt/Argon2 等慢哈希。
- 默认凭据 `admin` / `admin`，且**每次启动都会被重置**。
- 会话与验证码保存在进程内存中，重启即失效，无法横向扩展。
- 部分写接口（如新增关键节点、标记通知已读）只校验登录，未细分权限点。
- 附件上传只校验非空，**无类型与大小限制**。
- 未启用 HTTPS/HSTS，未实现请求限流与 CSRF 防护。
- 未做多租户隔离与敏感字段脱敏。

用于生产前，请至少替换密码哈希方案、移除默认账号重置逻辑、引入分布式会话或 JWT、补齐权限点校验与上传限制。欢迎提交改进。

## 许可与商业授权

本项目采用 **AGPL-3.0 与商业授权双许可**模式，你可以按需二选一：

### 免费使用（AGPL-3.0）

在遵守 [GNU Affero General Public License v3.0](LICENSE) 的前提下，可**免费**用于个人学习、研究、测试、内部自用，以及任何你愿意将衍生作品同样以 AGPL 开源的使用方式。

AGPL-3.0 的核心义务：如果你修改了本程序并通过网络向他人提供服务，必须向这些用户提供对应的完整源代码。

### 商业授权

以下场景需要购买商业授权，以获得免于 AGPL 开源义务的权利：

- 将本项目或其衍生作品集成进**闭源**产品或对外销售的软件；
- 以 SaaS / 网络服务形式对外提供，且不愿公开源代码；
- 企业内部商业用途中无法接受 AGPL 条款的情形。

商业授权联系：**bestphper@gmail.com**

### 贡献

除非你另有说明，你提交的 Pull Request 将按 AGPL-3.0 授权，并允许本项目以商业授权方式再许可——这是双许可模式得以运作的前提。

Copyright (C) 2026 bestphper

## 相关文档

- `合同评审管理系统.pdf`：2021 年的需求文档。旧版 .NET Framework 4.7 源码已不存在，本项目不承诺旧版二进制或界面兼容。
- `AGENTS.md`：面向 AI 编码代理的架构与开发约定说明。
