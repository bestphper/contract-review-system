using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.Security.Cryptography;
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    try { await next(ctx); }
    catch (SqliteException e) when (e.SqliteErrorCode == 19 && !ctx.Response.HasStarted)
    {
        app.Logger.LogWarning(e, "请求数据违反数据库约束");
        ctx.Response.Clear();
        await Results.BadRequest(new { message = e.SqliteExtendedErrorCode == 2067 ? "名称或编号已存在，请修改后重试" : "填写的数据不完整或关联记录不存在，请检查后重试" }).ExecuteAsync(ctx);
    }
});
app.UseDefaultFiles(); app.UseStaticFiles();
var db = new SqliteStore(Path.Combine(app.Environment.ContentRootPath, "data", "contract_review.db"), Path.Combine(app.Environment.ContentRootPath, "data", "attachments"));
await db.InitializeAsync();
var sessions = new ConcurrentDictionary<string, UserDto>();
var captchas = new ConcurrentDictionary<string, CaptchaEntry>();
UserDto? Current(HttpContext ctx) => ctx.Request.Cookies.TryGetValue("cr_session", out var token) && sessions.TryGetValue(token, out var user) ? user : null;
async Task Require(HttpContext ctx, Func<UserDto, Task<IResult>> action) { var result = Current(ctx) is { } user ? await action(user) : Results.Unauthorized(); await result.ExecuteAsync(ctx); }
async Task RequirePermission(HttpContext ctx, string permission, Func<UserDto, Task<IResult>> action) { var result = Current(ctx) is not { } user ? Results.Unauthorized() : await db.HasPermissionAsync(user.Id, permission) ? await action(user) : Results.Json(new { message = "没有执行此操作的权限" }, statusCode: 403); await result.ExecuteAsync(ctx); }
async Task DemoSeedHandler(HttpContext ctx) => await RequirePermission(ctx, "org.manage", user => db.SeedDemoAsync(user.Username));
async Task BackupHandler(HttpContext ctx) => await RequirePermission(ctx, "backup.download", _ => db.BackupAsync());

app.MapDelete("/api/contracts/{id:long}", async (HttpContext ctx,long id) => { await RequirePermission(ctx,"contract.edit", user=>db.DeleteContractAsync(id,user.Username)); });
app.MapDelete("/api/parties/{id:long}", async (HttpContext ctx,long id) => { await RequirePermission(ctx,"contract.edit", user=>db.DeletePartyAsync(id,user.Username)); });
app.MapGet("/api/contracts/{id:long}/actions", async (HttpContext ctx,long id) => { await Require(ctx, async user=>Results.Ok(await db.AvailableActionsAsync(id,user))); });
app.MapGet("/api/health", () => Results.Ok(new { name = "合同评审管理系统", status = "ok", database = "sqlite" }));
app.MapGet("/api/auth/captcha", () => { var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)); var code = Captcha.RandomCode(); captchas[id] = new CaptchaEntry(code, DateTimeOffset.UtcNow.AddMinutes(5)); return Results.Ok(new { id, image = Captcha.Svg(code) }); });
app.MapPost("/api/auth/login", async (HttpContext ctx, LoginRequest x) => { if (string.IsNullOrWhiteSpace(x.Username)) return Results.BadRequest(new { message = "请输入用户名" }); if (string.IsNullOrWhiteSpace(x.Password)) return Results.BadRequest(new { message = "请输入密码" }); if (string.IsNullOrWhiteSpace(x.Captcha)) return Results.BadRequest(new { message = "请输入验证码" }); if (!captchas.TryRemove(x.CaptchaId ?? "", out var c) || c.Expires < DateTimeOffset.UtcNow || !string.Equals(c.Code, x.Captcha.Trim(), StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { message = "验证码错误或已过期，请重新输入" }); var user = await db.LoginAsync(x.Username.Trim(), x.Password); if (user is null) return Results.Json(new { message = "用户名或密码错误，请检查后重试" }, statusCode: StatusCodes.Status401Unauthorized); var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); sessions[token] = user; ctx.Response.Cookies.Append("cr_session", token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromHours(8) }); return Results.Ok(new { user, message = "登录成功" }); });
app.MapPost("/api/auth/logout", (HttpContext ctx) => { if (ctx.Request.Cookies.TryGetValue("cr_session", out var token)) sessions.TryRemove(token, out _); ctx.Response.Cookies.Delete("cr_session"); return Results.Ok(); });
app.MapGet("/api/auth/me", (HttpContext ctx) => Current(ctx) is { } user ? Results.Ok(user) : Results.Unauthorized());
app.MapPost("/api/auth/password", async (HttpContext ctx, PasswordRequest x) => await Require(ctx, async user => await db.ChangePasswordAsync(user.Id, x.CurrentPassword, x.NewPassword) ? Results.Ok() : Results.BadRequest(new { message = "当前密码不正确或新密码过短" })));
app.MapGet("/api/dashboard", async (HttpContext ctx) => { await Require(ctx, async _ => Results.Ok(await db.DashboardAsync())); });
app.MapGet("/api/reports", async (HttpContext ctx) => { await Require(ctx, async _ => Results.Ok(await db.ReportsAsync())); });
app.MapGet("/api/contracts", async (HttpContext ctx, string? q, string? status, int? page, int? pageSize) => { await Require(ctx, async _ => Results.Ok(await db.PageAsync(p => db.ListContractsAsync(q, status, p), page, pageSize))); });
app.MapGet("/api/contracts/{id:long}", async (HttpContext ctx, long id) => await Require(ctx, async _ => await db.GetContractAsync(id) is { } c ? Results.Ok(c) : Results.NotFound()));
app.MapPut("/api/contracts/{id:long}", async (HttpContext ctx, long id, UpdateContractRequest x) => await RequirePermission(ctx, "contract.edit", user => string.IsNullOrWhiteSpace(x.Title) ? Task.FromResult<IResult>(Results.BadRequest(new { message = "合同标题不能为空" })) : db.UpdateContractAsync(id, x, user.Username)));
app.MapGet("/api/contracts/{id:long}/attachments", async (HttpContext ctx, long id, int? page, int? pageSize) => { await Require(ctx, async _ => Results.Ok(await db.PageAsync(p => db.ListAttachmentsAsync(id, p), page, pageSize))); });
app.MapGet("/api/contracts/{id:long}/parties", async (HttpContext ctx, long id) => await Require(ctx, async _ => Results.Ok(await db.ListContractPartiesAsync(id))));
app.MapPost("/api/contracts/{id:long}/parties", async (HttpContext ctx, long id, PartyLinkRequest x) => await RequirePermission(ctx, "contract.edit", user => db.LinkContractPartyAsync(id, x, user.Username)));
app.MapDelete("/api/contracts/{id:long}/parties/{partyId:long}", async (HttpContext ctx, long id, long partyId, string role) => await RequirePermission(ctx, "contract.edit", user => db.UnlinkContractPartyAsync(id, partyId, role, user.Username)));
app.MapPost("/api/contracts/{id:long}/attachments", async (HttpContext ctx, long id, IFormFile file) => await RequirePermission(ctx, "contract.edit", user => db.AddAttachmentAsync(id, file, user.Username))).DisableAntiforgery();
app.MapGet("/api/attachments/{id:long}/download", async (HttpContext ctx, long id) => await Require(ctx, async _ => await db.DownloadAttachmentAsync(id)));
app.MapDelete("/api/attachments/{id:long}", async (HttpContext ctx, long id) => await RequirePermission(ctx, "contract.edit", user => db.DeleteAttachmentAsync(id, user.Username)));
app.MapPost("/api/contracts", async (HttpContext ctx, CreateContractRequest x) => await RequirePermission(ctx, "contract.edit", async user => string.IsNullOrWhiteSpace(x.Title) ? Results.BadRequest(new { message = "合同标题不能为空" }) : Results.Created("/api/contracts", await db.AddContractAsync(x, user.Username))));
app.MapPost("/api/contracts/{id:long}/submit", async (HttpContext ctx, long id, WorkflowActionRequest x) => await RequirePermission(ctx, "contract.edit", user => db.ChangeAsync(id, "评审中", user, "提交评审", x.Opinion)));
app.MapPost("/api/contracts/{id:long}/approve", async (HttpContext ctx, long id, WorkflowActionRequest x) => await RequirePermission(ctx, "contract.review", user => db.ChangeAsync(id, "评审通过", user, "评审通过", x.Opinion)));
app.MapPost("/api/contracts/{id:long}/reject", async (HttpContext ctx, long id, ReviewRequest x) => await RequirePermission(ctx, "contract.review", user => db.RejectAsync(id, x, user)));
app.MapPost("/api/contracts/{id:long}/workflow/{action}", async (HttpContext ctx, long id, string action, WorkflowActionRequest x) => await Require(ctx, async user => await db.WorkflowActionAsync(id, action, x, user)));
app.MapPost("/api/demo/seed", (RequestDelegate)DemoSeedHandler);
app.MapGet("/api/contracts/{id:long}/workflow", async (HttpContext ctx, long id) => await Require(ctx, async _ => Results.Ok(await db.ListWorkflowAsync(id))));
app.MapGet("/api/rules", async (HttpContext ctx, int? page, int? pageSize) => { await Require(ctx, async _ => Results.Ok(await db.PageAsync(p => db.ListRulesAsync(p), page, pageSize))); });
app.MapPost("/api/rules", async (HttpContext ctx, RuleRequest x) => await RequirePermission(ctx, "rule.manage", async _ => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Content) ? Results.BadRequest() : Results.Ok(await db.AddRuleAsync(x))));
app.MapGet("/api/organization/departments", async (HttpContext ctx) => { await Require(ctx, async _ => Results.Ok(await db.ListDepartmentsAsync())); });
app.MapPost("/api/organization/departments", async (HttpContext ctx, DepartmentRequest x) => await RequirePermission(ctx, "org.manage", async _ => string.IsNullOrWhiteSpace(x.Name) ? Results.BadRequest(new { message = "部门名称不能为空" }) : Results.Ok(await db.AddDepartmentAsync(x))));
app.MapGet("/api/organization/users", async (HttpContext ctx, int? page, int? pageSize) => { await Require(ctx, async _ => Results.Ok(await db.PageAsync(p => db.ListOrgUsersAsync(p), page, pageSize))); });
app.MapGet("/api/organization/roles", async (HttpContext ctx, int? page, int? pageSize) => { await Require(ctx, async _ => Results.Ok(await db.PageAsync(p => db.ListRolesAsync(p), page, pageSize))); });
app.MapPost("/api/organization/roles", async (HttpContext ctx, RoleRequest x) => await RequirePermission(ctx, "role.manage", async _ => string.IsNullOrWhiteSpace(x.Name) ? Results.BadRequest(new { message = "角色名称不能为空" }) : Results.Ok(await db.AddRoleAsync(x))));
app.MapPost("/api/organization/users/{userId:long}/assignment", async (HttpContext ctx, long userId, AssignmentRequest x) => await RequirePermission(ctx, "org.manage", user => db.AssignUserAsync(userId, x, user.Username)));
app.MapGet("/api/parties", async (HttpContext ctx, string? q, int? page, int? pageSize) => { await Require(ctx, async _ => Results.Ok(await db.PageAsync(p => db.ListPartiesAsync(q, p), page, pageSize))); });
app.MapPost("/api/parties", async (HttpContext ctx, PartyRequest x) => await RequirePermission(ctx, "contract.edit", async user => string.IsNullOrWhiteSpace(x.Name) ? Results.BadRequest(new { message = "主体名称不能为空" }) : Results.Ok(await db.AddPartyAsync(x))));
app.MapGet("/api/contracts/{id:long}/milestones", async (HttpContext ctx, long id) => await Require(ctx, async _ => Results.Ok(await db.ListMilestonesAsync(id))));
app.MapPost("/api/contracts/{id:long}/milestones", async (HttpContext ctx, long id, MilestoneRequest x) => await Require(ctx, async _ => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.PlannedAt) ? Results.BadRequest(new { message = "节点名称和计划日期不能为空" }) : Results.Ok(await db.AddMilestoneAsync(id, x))));
app.MapGet("/api/notifications", async (HttpContext ctx, int? page, int? pageSize) => { await Require(ctx, async _ => Results.Ok(await db.PageAsync(p => db.ListNotificationsAsync(p), page, pageSize))); });
app.MapPost("/api/notifications/{id:long}/read", async (HttpContext ctx, long id) => await Require(ctx, _ => db.ReadNotificationAsync(id)));
app.MapGet("/api/audit-logs", async (HttpContext ctx, int? page, int? pageSize) => { await RequirePermission(ctx, "audit.view", async _ => Results.Ok(await db.PageAsync(p => db.ListAuditLogsAsync(p), page, pageSize))); });
app.MapGet("/api/backup", (RequestDelegate)BackupHandler);
app.MapFallback(async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html"));
});
app.Run();
