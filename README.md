# 合同评审管理系统

合同评审管理系统，覆盖图片验证码登录、会话认证、首页统计、合同录入、签约主体关联、组织架构、角色权限、完整生命周期流转、评审规则、附件管理、报表、通知、审计和账户安全设置。

## macOS 开发运行

```bash
dotnet restore
dotnet run --urls http://localhost:5080
```

浏览器打开 <http://localhost:5080>，默认账号 `admin` / `admin`。

## React 前端开发

前端源码位于 `frontend/`，使用 React、Ant Design、Ant Design Charts 和 TanStack Query。开发时分别启动 API 和 Vite：

```bash
cd frontend
npm install
npm run dev
```

Vite 会将 `/api` 请求代理到 `http://localhost:5080`。发布构建使用 `npm run build`，产物会直接写入上级项目的 `wwwroot/`，随后由 ASP.NET Core 托管。客户端路由使用 hash 形式，适配现有静态文件部署。

当前演示账号为 `admin` / `admin`。登录后可从左侧“组织架构”调整部门、岗位和角色归属，“角色权限”创建角色，“签约主体”维护企业与个人，“报表中心”查看状态、类型和部门统计，“通知中心”查看站内提醒，“审计日志”查看操作记录并下载 SQLite 备份；合同详情支持主体关联、完整流程历史、关键节点和附件管理。管理员可调用 `POST /api/demo/seed` 幂等补充演示合同，不提供清空重置。

## API

- `POST /api/auth/login`
- `GET/POST /api/contracts`
- `POST /api/contracts/{id}/submit|approve|reject`
- `POST /api/contracts/{id}/workflow/{action}`：审批、签署、履行和归档动作
- `GET /api/contracts/{id}/parties`、`POST/DELETE /api/contracts/{id}/parties`
- `GET /api/reports`、`GET /api/audit-logs`、`GET /api/backup`
- `POST /api/organization/roles`、`POST /api/organization/users/{id}/assignment`
- `GET/POST /api/rules`
- `GET /api/health`

合同、评审任务（按合同状态筛选）、通知、审计、签约主体、组织成员、角色、规则和合同附件列表支持分页。列表接口传入 `page` 和 `pageSize`，返回 `{ items, total, page, pageSize }`；页码最小为 1，每页最多 100 条。不传分页参数时保留数组响应，供主体、角色、人员选择器使用。合同和主体分页仍支持原有搜索、状态筛选。页面默认每页 10 条，可调整每页条数，搜索或筛选变化会回到第一页。

新建合同与 `/contracts/{id}` 编辑页面共用合同字段：标题、类型、甲乙方主体、负责人、部门、币种、金额、签署/生效/到期日期、付款条款、说明和标签。主体从已有签约主体中选择，保存时同步甲乙方关联。新增部门、角色、主体、合同、节点及用户归属时，省略的可选文本字段保存为空字符串，金额和上级部门等可空字段仍保存为 NULL。名称重复或关联记录不存在时返回可读的 400 错误；无操作权限时返回 403。

后端代码修改后需要重启 ASP.NET Core 进程，单独刷新浏览器不会更新运行中的 API。前端分页兼容旧数组响应，避免前后端版本不一致时列表被错误显示为空。

合同仅在草稿状态允许编辑、删除及变更附件和主体关联，提交评审后（包括已驳回）只读，管理员也不能修改内容。`DELETE /api/contracts/{id}` 删除草稿及其业务关联，保留审计记录；`DELETE /api/parties/{id}` 仅允许删除无关联合同的主体，兼容历史合同中以名称记录的甲乙方。

评审工作台按阶段筛选合同，详情路径 `/review/contracts/{id}` 始终只读，并展示启用的评审规则作为人工检查参考。`GET /api/contracts/{id}/actions` 返回当前用户在当前状态可执行的动作；系统管理员可执行全部流程角色动作，其他用户按角色权限操作。评审通过属于 `contract.review`，提交属于 `contract.edit`，审批属于 `contract.approve`。流程历史、评审意见和审计日志记录实际登录人员，驳回及签署失败必须填写意见。报表中心新增状态、类型、部门数量及月度新增合同统计图，保留明细表。

应用启动时会自动创建 `data/contract_review.db`、业务表、管理员和默认评审规则。合同、状态、评审意见、规则和审计记录均写入 SQLite，重启后不会丢失。默认数据库账号为 `admin` / `admin`，首次登录需要输入页面生成的图片验证码。登录后可从顶部“账户”查看资料并修改密码，右上角可退出登录。旧版 .NET Framework 4.7 源码未找到，因此本项目不承诺旧版二进制或界面兼容。
