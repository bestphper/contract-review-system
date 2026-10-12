using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
sealed class SqliteStore
{
    readonly string cs; readonly string dbPath; readonly string attachmentRoot;
    public SqliteStore(string path, string attachmentRoot) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); Directory.CreateDirectory(attachmentRoot); this.dbPath = path; this.attachmentRoot = attachmentRoot; cs = $"Data Source={path};Foreign Keys=True"; }
    SqliteConnection Open() => new(cs);
    static async Task SyncMainPartiesAsync(SqliteConnection connection, SqliteTransaction transaction, long contractId, string? partyA, string? partyB)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM contract_parties WHERE contract_id=$id AND role IN ('甲方','乙方')";
        Add(command, "$id", contractId); await command.ExecuteNonQueryAsync();
        foreach (var (name, role) in new[] { (partyA, "甲方"), (partyB, "乙方") })
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO contract_parties(contract_id,party_id,role) SELECT $id,id,$role FROM parties WHERE id=(SELECT id FROM parties WHERE name=$name ORDER BY id LIMIT 1)";
            Add(command, "$id", contractId); Add(command, "$name", name); Add(command, "$role", role);
            await command.ExecuteNonQueryAsync();
        }
    }
    public async Task<object> PageAsync<T>(Func<PageRequest?, Task<IReadOnlyList<T>>> query, int? page, int? pageSize)
    {
        if (page is null && pageSize is null) return await query(null);
        var paging = new PageRequest(page ?? 1, pageSize ?? 10);
        var items = await query(paging);
        return new PagedResult<T>(items, paging.Total, paging.Page, paging.PageSize);
    }
    static async Task ApplyPageAsync(SqliteCommand command, PageRequest? paging)
    {
        if (paging is null) return;
        var sql = command.CommandText;
        command.CommandText = $"SELECT COUNT(*) FROM ({sql})";
        paging.Total = Convert.ToInt32(await command.ExecuteScalarAsync());
        command.CommandText = sql + " LIMIT $pageSize OFFSET $offset";
        Add(command, "$pageSize", paging.PageSize);
        Add(command, "$offset", ((long)paging.Page - 1) * paging.PageSize);
    }
    public async Task InitializeAsync()
    {
        await using var c = Open(); await c.OpenAsync();
        await using var x = c.CreateCommand();
        x.CommandText = @"
CREATE TABLE IF NOT EXISTS users(id INTEGER PRIMARY KEY AUTOINCREMENT,username TEXT UNIQUE NOT NULL,password_hash TEXT NOT NULL,display_name TEXT NOT NULL,role TEXT NOT NULL,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS contracts(id INTEGER PRIMARY KEY AUTOINCREMENT,number TEXT UNIQUE NOT NULL,title TEXT NOT NULL,contract_type TEXT NOT NULL DEFAULT '',party_a TEXT NOT NULL DEFAULT '',party_b TEXT NOT NULL DEFAULT '',amount REAL,currency TEXT NOT NULL DEFAULT 'CNY',signed_at TEXT,effective_at TEXT,expires_at TEXT,owner TEXT NOT NULL DEFAULT '',department TEXT NOT NULL DEFAULT '',payment_terms TEXT NOT NULL DEFAULT '',description TEXT NOT NULL DEFAULT '',tags TEXT NOT NULL DEFAULT '',status TEXT NOT NULL DEFAULT '草稿',created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS attachments(id INTEGER PRIMARY KEY AUTOINCREMENT,contract_id INTEGER NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,file_name TEXT NOT NULL,content_type TEXT NOT NULL,size INTEGER NOT NULL,version INTEGER NOT NULL,stored_name TEXT NOT NULL,uploaded_by TEXT NOT NULL,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,deleted_at TEXT);
CREATE TABLE IF NOT EXISTS reviews(id INTEGER PRIMARY KEY AUTOINCREMENT,contract_id INTEGER NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,reviewer TEXT NOT NULL,opinion TEXT NOT NULL DEFAULT '',status TEXT NOT NULL,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS rules(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL,content TEXT NOT NULL,enabled INTEGER NOT NULL DEFAULT 1,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS departments(id INTEGER PRIMARY KEY AUTOINCREMENT,parent_id INTEGER REFERENCES departments(id),name TEXT NOT NULL,code TEXT NOT NULL DEFAULT '',manager TEXT NOT NULL DEFAULT '',enabled INTEGER NOT NULL DEFAULT 1,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS positions(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL UNIQUE,description TEXT NOT NULL DEFAULT '');
CREATE TABLE IF NOT EXISTS roles(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL UNIQUE,description TEXT NOT NULL DEFAULT '',enabled INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS permissions(id INTEGER PRIMARY KEY AUTOINCREMENT,code TEXT NOT NULL UNIQUE,name TEXT NOT NULL,group_name TEXT NOT NULL DEFAULT '');
CREATE TABLE IF NOT EXISTS role_permissions(role_id INTEGER NOT NULL REFERENCES roles(id) ON DELETE CASCADE,permission_id INTEGER NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,PRIMARY KEY(role_id,permission_id));
CREATE TABLE IF NOT EXISTS user_roles(user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,role_id INTEGER NOT NULL REFERENCES roles(id) ON DELETE CASCADE,PRIMARY KEY(user_id,role_id));
CREATE TABLE IF NOT EXISTS user_departments(user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,department_id INTEGER NOT NULL REFERENCES departments(id) ON DELETE CASCADE,position TEXT NOT NULL DEFAULT '',is_primary INTEGER NOT NULL DEFAULT 1,PRIMARY KEY(user_id,department_id));
CREATE TABLE IF NOT EXISTS parties(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL,type TEXT NOT NULL,code TEXT NOT NULL DEFAULT '',contact TEXT NOT NULL DEFAULT '',phone TEXT NOT NULL DEFAULT '',address TEXT NOT NULL DEFAULT '',enabled INTEGER NOT NULL DEFAULT 1,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS contract_parties(contract_id INTEGER NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,party_id INTEGER NOT NULL REFERENCES parties(id) ON DELETE CASCADE,role TEXT NOT NULL DEFAULT '签约方',PRIMARY KEY(contract_id,party_id,role));
CREATE TABLE IF NOT EXISTS milestones(id INTEGER PRIMARY KEY AUTOINCREMENT,contract_id INTEGER NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,name TEXT NOT NULL,planned_at TEXT NOT NULL,completed_at TEXT,status TEXT NOT NULL DEFAULT '待完成',owner TEXT NOT NULL DEFAULT '',note TEXT NOT NULL DEFAULT '',created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS notifications(id INTEGER PRIMARY KEY AUTOINCREMENT,title TEXT NOT NULL,content TEXT NOT NULL,level TEXT NOT NULL DEFAULT '普通',read INTEGER NOT NULL DEFAULT 0,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,contract_id INTEGER REFERENCES contracts(id));
CREATE TABLE IF NOT EXISTS audit_logs(id INTEGER PRIMARY KEY AUTOINCREMENT,actor TEXT NOT NULL,action TEXT NOT NULL,entity_type TEXT NOT NULL,entity_id INTEGER,details TEXT NOT NULL DEFAULT '',created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS contract_workflow_history(id INTEGER PRIMARY KEY AUTOINCREMENT,contract_id INTEGER NOT NULL REFERENCES contracts(id) ON DELETE CASCADE,from_status TEXT NOT NULL,to_status TEXT NOT NULL,action TEXT NOT NULL,actor TEXT NOT NULL,opinion TEXT NOT NULL DEFAULT '',created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
INSERT OR IGNORE INTO users(username,password_hash,display_name,role) VALUES ('admin', $password, '系统管理员', '管理员');
";
        x.Parameters.AddWithValue("$password", Security.Hash("admin"));
        await x.ExecuteNonQueryAsync();
        await using var reset = c.CreateCommand();
        reset.CommandText = "UPDATE users SET password_hash=$password,display_name='系统管理员',role='管理员' WHERE username='admin'";
        reset.Parameters.AddWithValue("$password", Security.Hash("admin"));
        await reset.ExecuteNonQueryAsync();
        await SeedOrganizationAsync(c);
        await SeedDemoDataAsync(c);
        try { await using var migration = c.CreateCommand(); migration.CommandText = "ALTER TABLE attachments ADD COLUMN deleted_at TEXT"; await migration.ExecuteNonQueryAsync(); } catch (SqliteException) { }
    }

    async Task SeedOrganizationAsync(SqliteConnection c)
    {
        await using var seed = c.CreateCommand();
        seed.CommandText = @"INSERT OR IGNORE INTO departments(id,parent_id,name,code,manager) VALUES (1,NULL,'华夏合同科技有限公司','HQ','李明');
INSERT OR IGNORE INTO departments(id,parent_id,name,code,manager) VALUES (2,1,'业务部','BUS','王芳');
INSERT OR IGNORE INTO departments(id,parent_id,name,code,manager) VALUES (3,1,'法务部','LEG','陈律师');
INSERT OR IGNORE INTO departments(id,parent_id,name,code,manager) VALUES (4,1,'财务部','FIN','赵敏');
INSERT OR IGNORE INTO roles(id,name,description) VALUES (1,'系统管理员','维护组织、权限和所有业务数据'),(2,'业务负责人','创建和跟进本部门合同'),(3,'法务评审人','执行合同风险评审'),(4,'财务审批人','审核金额、付款和财务条款'),(5,'只读查看者','查看授权范围内数据');
INSERT OR IGNORE INTO permissions(code,name,group_name) VALUES ('dashboard.view','查看工作台','工作台'),('contract.view','查看合同','合同'),('contract.edit','编辑合同','合同'),('contract.review','执行评审','评审'),('contract.approve','执行审批','审批'),('contract.sign','执行签署','签署'),('contract.archive','归档合同','合同'),('org.manage','管理组织架构','系统'),('role.manage','管理角色权限','系统'),('rule.manage','管理评审规则','评审'),('audit.view','查看审计日志','系统'),('backup.download','下载数据库备份','系统');
INSERT OR IGNORE INTO role_permissions(role_id,permission_id) SELECT r.id,p.id FROM roles r CROSS JOIN permissions p WHERE r.name='系统管理员';
INSERT OR IGNORE INTO role_permissions(role_id,permission_id) SELECT r.id,p.id FROM roles r JOIN permissions p ON p.code IN ('dashboard.view','contract.view','contract.edit') WHERE r.name='业务负责人';
INSERT OR IGNORE INTO role_permissions(role_id,permission_id) SELECT r.id,p.id FROM roles r JOIN permissions p ON p.code IN ('dashboard.view','contract.view','contract.review','rule.manage') WHERE r.name='法务评审人';
INSERT OR IGNORE INTO role_permissions(role_id,permission_id) SELECT r.id,p.id FROM roles r JOIN permissions p ON p.code IN ('dashboard.view','contract.view','contract.approve') WHERE r.name='财务审批人';
INSERT OR IGNORE INTO role_permissions(role_id,permission_id) SELECT r.id,p.id FROM roles r JOIN permissions p ON p.code IN ('dashboard.view','contract.view') WHERE r.name='只读查看者';
INSERT OR IGNORE INTO user_roles(user_id,role_id) SELECT u.id,r.id FROM users u JOIN roles r ON r.name='系统管理员' WHERE u.username='admin';";
        await seed.ExecuteNonQueryAsync();
        await using var adminPermissions = c.CreateCommand(); adminPermissions.CommandText = "INSERT OR IGNORE INTO role_permissions(role_id,permission_id) SELECT r.id,p.id FROM roles r CROSS JOIN permissions p WHERE r.name='系统管理员'"; await adminPermissions.ExecuteNonQueryAsync();
        await using var parties = c.CreateCommand(); parties.CommandText = "INSERT OR IGNORE INTO parties(id,name,type,code,contact,phone,address) VALUES (1,'华夏合同科技有限公司','企业','91320000DEMO001','李明','13800000000','上海市浦东新区演示路1号'),(2,'上海未来供应链有限公司','企业','91310000DEMO002','周强','13900000000','上海市闵行区合同路2号'),(3,'王小明','个人','ID-DEMO-001','王小明','13700000000','上海市静安区');"; await parties.ExecuteNonQueryAsync();
        await using var notifications = c.CreateCommand(); notifications.CommandText = "INSERT OR IGNORE INTO notifications(id,title,content,level) VALUES (1,'合同评审待处理','有合同等待法务评审，请进入评审工作台处理。','重要'),(2,'节点提醒演示','系统会在合同关键节点临近时生成站内提醒。','普通');"; await notifications.ExecuteNonQueryAsync();
    }

    static object Value(object? value) => value ?? DBNull.Value;
    static string Text(SqliteDataReader r, string name) => r[name] is DBNull ? "" : Convert.ToString(r[name]) ?? "";
    static decimal? DecimalValue(SqliteDataReader r, string name) => r[name] is DBNull ? null : Convert.ToDecimal(r[name]);
    static ContractDto Contract(SqliteDataReader r) => new(Convert.ToInt64(r["id"]), Text(r, "number"), Text(r, "title"), Text(r, "party_a"), Text(r, "party_b"), DecimalValue(r, "amount"), Text(r, "status"), Text(r, "created_at"), Text(r, "description"), Convert.ToInt32(r["opinion_count"]), Convert.ToInt32(r["attachment_count"]));
    static void Add(SqliteCommand c, string name, object? value) => c.Parameters.AddWithValue(name, Value(value));

    public async Task<bool> HasPermissionAsync(long userId, string permission)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="SELECT EXISTS(SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$user AND r.name='系统管理员') OR EXISTS(SELECT 1 FROM user_roles ur JOIN role_permissions rp ON rp.role_id=ur.role_id JOIN permissions p ON p.id=rp.permission_id WHERE ur.user_id=$user AND p.code=$permission)"; Add(x,"$user",userId); Add(x,"$permission",permission); return Convert.ToInt32(await x.ExecuteScalarAsync()) == 1;
    }

    public async Task<UserDto?> LoginAsync(string username, string password)
    {
        await using var c = Open(); await c.OpenAsync(); await using var x = c.CreateCommand();
        x.CommandText = "SELECT id,username,display_name,role,created_at FROM users WHERE username=$u AND password_hash=$p"; Add(x, "$u", username); Add(x, "$p", Security.Hash(password));
        await using var r = await x.ExecuteReaderAsync(); return await r.ReadAsync() ? new UserDto(Convert.ToInt64(r["id"]), Text(r, "username"), Text(r, "display_name"), Text(r, "role"), Text(r, "created_at")) : null;
    }

    public async Task<bool> ChangePasswordAsync(long id, string current, string next)
    {
        if (string.IsNullOrWhiteSpace(next) || next.Length < 6) return false;
        await using var c = Open(); await c.OpenAsync(); await using var x = c.CreateCommand(); x.CommandText = "UPDATE users SET password_hash=$n WHERE id=$id AND password_hash=$o"; Add(x, "$n", Security.Hash(next)); Add(x, "$o", Security.Hash(current)); Add(x, "$id", id); return await x.ExecuteNonQueryAsync() == 1;
    }

    public async Task<object> DashboardAsync()
    {
        await using var c = Open(); await c.OpenAsync();
        async Task<long> Count(string? status = null) { await using var x = c.CreateCommand(); x.CommandText = status is null ? "SELECT COUNT(*) FROM contracts" : "SELECT COUNT(*) FROM contracts WHERE status=$s"; if (status is not null) Add(x, "$s", status); return Convert.ToInt64(await x.ExecuteScalarAsync()); }
        await using var summary = c.CreateCommand(); summary.CommandText = "SELECT COALESCE(SUM(amount), 0) FROM contracts"; var amount = Convert.ToDecimal(await summary.ExecuteScalarAsync() ?? 0);
        await using var recentCommand = c.CreateCommand(); recentCommand.CommandText = "SELECT c.*, (SELECT COUNT(*) FROM reviews r WHERE r.contract_id=c.id) opinion_count, (SELECT COUNT(*) FROM attachments a WHERE a.contract_id=c.id AND a.deleted_at IS NULL) attachment_count FROM contracts c ORDER BY c.id DESC LIMIT 8";
        var recent = new List<ContractDto>(); await using (var reader = await recentCommand.ExecuteReaderAsync()) while (await reader.ReadAsync()) recent.Add(Contract(reader));
        return new { total = await Count(), draft = await Count("草稿"), reviewing = await Count("评审中"), approved = await Count("评审通过") + await Count("审批通过") + await Count("已签署") + await Count("履行中") + await Count("已归档"), rejected = await Count("已驳回"), amount, recent };
    }

    public async Task<object> ReportsAsync()
    {
        await using var c=Open(); await c.OpenAsync();
        async Task<List<object>> Query(string sql) { await using var x=c.CreateCommand(); x.CommandText=sql; var list=new List<object>(); await using var r=await x.ExecuteReaderAsync(); while(await r.ReadAsync()) { var item=new Dictionary<string,object?>(); for(var i=0;i<r.FieldCount;i++) item[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i); list.Add(item); } return list; }
        return new { byStatus=await Query("SELECT status AS name,COUNT(*) AS value FROM contracts GROUP BY status ORDER BY value DESC"), byType=await Query("SELECT CASE WHEN contract_type='' THEN '未分类' ELSE contract_type END AS name,COUNT(*) AS value FROM contracts GROUP BY contract_type ORDER BY value DESC"), byDepartment=await Query("SELECT CASE WHEN department='' THEN '未分配' ELSE department END AS name,COUNT(*) AS value,COALESCE(SUM(amount),0) AS amount FROM contracts GROUP BY department ORDER BY value DESC"), monthly=await Query("SELECT substr(created_at,1,7) AS month,COUNT(*) AS count,COALESCE(SUM(amount),0) AS amount FROM contracts GROUP BY substr(created_at,1,7) ORDER BY month"), expiring=await Query("SELECT id,number,title,expires_at,status FROM contracts WHERE expires_at<>'' AND date(expires_at)<=date('now','+30 day') ORDER BY expires_at LIMIT 20") };
    }

    public async Task<IResult> SeedDemoAsync(string actor)
    {
        await using var c=Open(); await c.OpenAsync(); await using var exists=c.CreateCommand(); exists.CommandText="SELECT id FROM contracts WHERE title='年度供应链服务合同（演示）' LIMIT 1"; var existing=await exists.ExecuteScalarAsync(); if(existing is not null) return Results.Ok(new { created=false, id=Convert.ToInt64(existing) }); await using var x=c.CreateCommand(); x.CommandText="INSERT INTO contracts(number,title,contract_type,party_a,party_b,amount,currency,department,owner,description,status,expires_at) VALUES('HT-DEMO-001','年度供应链服务合同（演示）','采购合同','华夏合同科技有限公司','上海未来供应链有限公司',1280000,'CNY','业务部','王芳','用于演示合同评审、审批、节点和归档流程。','评审中',date('now','+120 day')); SELECT last_insert_rowid()"; var id=Convert.ToInt64(await x.ExecuteScalarAsync()); await using var link=c.CreateCommand(); link.CommandText="INSERT OR IGNORE INTO contract_parties(contract_id,party_id,role) VALUES($id,1,'甲方'),($id,2,'乙方')"; Add(link,"$id",id); await link.ExecuteNonQueryAsync(); await AuditAsync(actor,"生成演示数据","合同",id,"固定演示合同"); return Results.Ok(new { created=true,id });
    }

    public async Task<IReadOnlyList<ContractDto>> ListContractsAsync(string? q, string? status, PageRequest? paging = null)
    {
        await using var c = Open(); await c.OpenAsync(); await using var x = c.CreateCommand(); x.CommandText = "SELECT c.*, (SELECT COUNT(*) FROM reviews r WHERE r.contract_id=c.id) opinion_count, (SELECT COUNT(*) FROM attachments a WHERE a.contract_id=c.id AND a.deleted_at IS NULL) attachment_count FROM contracts c WHERE ($q='' OR c.title LIKE $like OR c.number LIKE $like OR c.party_a LIKE $like OR c.party_b LIKE $like) AND ($s='' OR c.status=$s) ORDER BY c.id DESC"; Add(x, "$q", q ?? ""); Add(x, "$like", $"%{q ?? ""}%"); Add(x, "$s", status ?? "");
        await ApplyPageAsync(x, paging); var result = new List<ContractDto>(); await using var r = await x.ExecuteReaderAsync(); while (await r.ReadAsync()) result.Add(Contract(r)); return result;
    }

    public async Task<object?> GetContractAsync(long id)
    {
        await using var c = Open(); await c.OpenAsync(); await using var x = c.CreateCommand(); x.CommandText = "SELECT c.*, (SELECT COUNT(*) FROM reviews r WHERE r.contract_id=c.id) opinion_count, (SELECT COUNT(*) FROM attachments a WHERE a.contract_id=c.id AND a.deleted_at IS NULL) attachment_count FROM contracts c WHERE c.id=$id"; Add(x, "$id", id); await using var r = await x.ExecuteReaderAsync(); if (!await r.ReadAsync()) return null;
        var dto = Contract(r); return new { dto.Id, dto.Number, dto.Title, dto.PartyA, dto.PartyB, dto.Amount, dto.Status, dto.CreatedAt, dto.Description, dto.OpinionCount, dto.AttachmentCount, ContractType = Text(r, "contract_type"), Currency = Text(r, "currency"), SignedAt = Text(r, "signed_at"), EffectiveAt = Text(r, "effective_at"), ExpiresAt = Text(r, "expires_at"), Owner = Text(r, "owner"), Department = Text(r, "department"), PaymentTerms = Text(r, "payment_terms"), Tags = Text(r, "tags") };
    }

    public async Task<IResult> UpdateContractAsync(long id, UpdateContractRequest v, string user)
    {
        await using var c = Open(); await c.OpenAsync(); await using var tx = await c.BeginTransactionAsync(); await using var x = c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText = "UPDATE contracts SET title=$title,contract_type=$type,party_a=$a,party_b=$b,amount=$amount,currency=$currency,signed_at=$signed,effective_at=$effective,expires_at=$expires,owner=$owner,department=$dept,payment_terms=$terms,description=$desc,tags=$tags,updated_at=CURRENT_TIMESTAMP WHERE id=$id AND status='草稿'";
        Add(x, "$id", id); Add(x, "$title", v.Title); Add(x, "$type", v.ContractType ?? ""); Add(x, "$a", v.PartyA ?? ""); Add(x, "$b", v.PartyB ?? ""); Add(x, "$amount", v.Amount); Add(x, "$currency", v.Currency ?? "CNY"); Add(x, "$signed", v.SignedAt); Add(x, "$effective", v.EffectiveAt); Add(x, "$expires", v.ExpiresAt); Add(x, "$owner", v.Owner ?? ""); Add(x, "$dept", v.Department ?? ""); Add(x, "$terms", v.PaymentTerms ?? ""); Add(x, "$desc", v.Description ?? ""); Add(x, "$tags", v.Tags ?? ""); if (await x.ExecuteNonQueryAsync() == 0) return Results.BadRequest(new { message="仅草稿合同可以编辑" }); await SyncMainPartiesAsync(c, (SqliteTransaction)tx, id, v.PartyA, v.PartyB); await WriteAuditAsync(c,(SqliteTransaction)tx,user,"编辑","合同",id,v.Title); await tx.CommitAsync(); return Results.Ok(await GetContractAsync(id));
    }

    public async Task<IReadOnlyList<AttachmentDto>> ListAttachmentsAsync(long contractId, PageRequest? paging = null)
    {
        await using var c = Open(); await c.OpenAsync(); await using var x = c.CreateCommand(); x.CommandText = "SELECT id,contract_id,file_name,content_type,size,version,uploaded_by,created_at FROM attachments WHERE contract_id=$id AND deleted_at IS NULL ORDER BY version DESC"; Add(x, "$id", contractId); await ApplyPageAsync(x, paging); var result = new List<AttachmentDto>(); await using var r = await x.ExecuteReaderAsync(); while (await r.ReadAsync()) result.Add(new AttachmentDto(Convert.ToInt64(r["id"]), Convert.ToInt64(r["contract_id"]), Text(r, "file_name"), Text(r, "content_type"), Convert.ToInt64(r["size"]), Convert.ToInt32(r["version"]), Text(r, "uploaded_by"), Text(r, "created_at"))); return result;
    }

    public async Task<IResult> AddAttachmentAsync(long contractId, IFormFile file, string user)
    {
        if(file is null || file.Length==0)return Results.BadRequest(new { message="文件不能为空" });
        await using var c=Open();await c.OpenAsync();using var tx=c.BeginTransaction();if(!await IsDraftAsync(c,tx,contractId))return Results.BadRequest(new { message="仅草稿合同可以上传附件" });
        var safeName=Path.GetFileName(file.FileName);var stored=$"{Guid.NewGuid():N}{Path.GetExtension(safeName)}";var dir=Path.Combine(attachmentRoot,contractId.ToString());Directory.CreateDirectory(dir);var path=Path.Combine(dir,stored);
        try { await using(var stream=File.Create(path))await file.CopyToAsync(stream);
            await using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="INSERT INTO attachments(contract_id,file_name,content_type,size,version,stored_name,uploaded_by) VALUES($id,$name,$type,$size,COALESCE((SELECT MAX(version)+1 FROM attachments WHERE contract_id=$id),1),$stored,$user); SELECT last_insert_rowid();";Add(x,"$id",contractId);Add(x,"$name",safeName);Add(x,"$type",file.ContentType??"application/octet-stream");Add(x,"$size",file.Length);Add(x,"$stored",stored);Add(x,"$user",user);var id=Convert.ToInt64(await x.ExecuteScalarAsync());await WriteAuditAsync(c,tx,user,"上传附件","合同",contractId,safeName);tx.Commit();return Results.Ok(new { id,fileName=safeName });
        } catch { if(File.Exists(path))File.Delete(path);throw; }
    }

    public async Task<IResult> DownloadAttachmentAsync(long id)
    {
        await using var c = Open(); await c.OpenAsync(); await using var x = c.CreateCommand(); x.CommandText = "SELECT contract_id,file_name,content_type,stored_name FROM attachments WHERE id=$id AND deleted_at IS NULL"; Add(x, "$id", id); await using var r = await x.ExecuteReaderAsync(); if (!await r.ReadAsync()) return Results.NotFound(); var path = Path.Combine(attachmentRoot, Text(r, "contract_id"), Text(r, "stored_name")); return File.Exists(path) ? Results.File(path, Text(r, "content_type"), Text(r, "file_name")) : Results.NotFound();
    }

    public async Task<IResult> DeleteAttachmentAsync(long id,string user)
    {
        await using var c=Open();await c.OpenAsync();using var tx=c.BeginTransaction();await using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="SELECT a.contract_id,a.file_name,c.status FROM attachments a JOIN contracts c ON c.id=a.contract_id WHERE a.id=$id AND a.deleted_at IS NULL";Add(x,"$id",id);long contractId;string name;
        await using(var r=await x.ExecuteReaderAsync()){if(!await r.ReadAsync())return Results.NotFound();if(Text(r,"status")!="草稿")return Results.BadRequest(new { message="仅草稿合同可以删除附件" });contractId=Convert.ToInt64(r["contract_id"]);name=Text(r,"file_name");}
        x.CommandText="UPDATE attachments SET deleted_at=CURRENT_TIMESTAMP WHERE id=$id";await x.ExecuteNonQueryAsync();await WriteAuditAsync(c,tx,user,"删除附件","合同",contractId,name);tx.Commit();return Results.Ok(new { deleted=true,contractId });
    }

    public async Task<long> AddContractAsync(CreateContractRequest v, string user)
    {
        await using var c = Open(); await c.OpenAsync(); await using var tx=await c.BeginTransactionAsync(); await using var x = c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText = "INSERT INTO contracts(number,title,contract_type,department,party_a,party_b,amount,description,currency,signed_at,effective_at,expires_at,owner,payment_terms,tags) VALUES($number,$title,$type,$dept,$a,$b,$amount,$description,$currency,$signed,$effective,$expires,$owner,$terms,$tags); SELECT last_insert_rowid();"; Add(x, "$number", $"HT-{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(100, 999)}"); Add(x, "$title", v.Title); Add(x, "$type", v.ContractType ?? ""); Add(x, "$dept", v.Department ?? ""); Add(x, "$a", v.PartyA ?? ""); Add(x, "$b", v.PartyB ?? ""); Add(x, "$amount", v.Amount); Add(x, "$description", v.Description ?? ""); Add(x,"$currency",v.Currency ?? "CNY"); Add(x,"$signed",v.SignedAt); Add(x,"$effective",v.EffectiveAt); Add(x,"$expires",v.ExpiresAt); Add(x,"$owner",v.Owner ?? ""); Add(x,"$terms",v.PaymentTerms ?? ""); Add(x,"$tags",v.Tags ?? ""); var id = Convert.ToInt64(await x.ExecuteScalarAsync()); if (v.PartyIds is { Length: > 0 }) { var partyIds=v.PartyIds.Distinct().ToArray(); for(var i=0;i<partyIds.Length;i++) { x.Parameters.Clear(); x.CommandText="INSERT OR IGNORE INTO contract_parties(contract_id,party_id,role) VALUES($contract,$party,$role)"; Add(x,"$contract",id); Add(x,"$party",partyIds[i]); Add(x,"$role",i==0?"甲方":i==1?"乙方":"签约方"); await x.ExecuteNonQueryAsync(); } } await SyncMainPartiesAsync(c, (SqliteTransaction)tx, id, v.PartyA, v.PartyB); await WriteAuditAsync(c,(SqliteTransaction)tx,user,"创建","合同",id,v.Title); await tx.CommitAsync(); return id;
    }

    static readonly Dictionary<string,(string From,string To,string Permission,string Label)> Actions = new(StringComparer.OrdinalIgnoreCase) {
        ["submit"]=("草稿","评审中","contract.edit","提交评审"), ["review-pass"]=("评审中","评审通过","contract.review","评审通过"), ["reject"]=("评审中","已驳回","contract.review","评审驳回"),
        ["start-approval"]=("评审通过","审批中","contract.approve","发起审批"), ["approve"]=("审批中","审批通过","contract.approve","审批通过"), ["reject-approval"]=("审批中","评审通过","contract.approve","审批驳回"),
        ["start-signing"]=("审批通过","签署中","contract.sign","发起签署"), ["signed"]=("签署中","已签署","contract.sign","签署完成"), ["sign-failed"]=("签署中","审批通过","contract.sign","签署失败"),
        ["activate"]=("已签署","履行中","contract.edit","开始履行"), ["archive"]=("履行中","已归档","contract.archive","归档"), ["void"]=("草稿","已作废","contract.edit","作废") };
    public async Task<object> AvailableActionsAsync(long id, UserDto user)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="SELECT status FROM contracts WHERE id=$id"; Add(x,"$id",id); var status=(string?)await x.ExecuteScalarAsync();
        var result=new List<object>(); foreach(var pair in Actions) if(pair.Value.From==status && await HasPermissionAsync(user.Id,pair.Value.Permission)) result.Add(new { action=pair.Key,label=pair.Value.Label }); return result;
    }
    public async Task<IResult> ChangeAsync(long id, string status, UserDto user, string action, string? opinion = null) => await WorkflowActionAsync(id,status=="评审中"?"submit":"review-pass",new WorkflowActionRequest(opinion,null),user);
    public async Task<IResult> WorkflowActionAsync(long id, string action, WorkflowActionRequest request, UserDto user)
    {
        if(!Actions.TryGetValue(action,out var t)) return Results.BadRequest(new { message="未知流程动作" });
        if(!await HasPermissionAsync(user.Id,t.Permission)) return Results.Json(new { message="没有执行此操作的权限" },statusCode:403);
        if(action is "reject" or "reject-approval" or "sign-failed" && string.IsNullOrWhiteSpace(request.Opinion)) return Results.BadRequest(new { message="请填写处理意见" });
        await using var c=Open(); await c.OpenAsync(); using var tx=c.BeginTransaction(); await using var x=c.CreateCommand(); x.Transaction=tx;
        x.CommandText="SELECT title,number,status FROM contracts WHERE id=$id"; Add(x,"$id",id);
        string title,number,status; await using(var r=await x.ExecuteReaderAsync()){if(!await r.ReadAsync())return Results.NotFound();title=Text(r,"title");number=Text(r,"number");status=Text(r,"status");}
        if(status!=t.From)return Results.BadRequest(new { message=$"当前状态为{status}，不能执行{t.Label}" });
        x.CommandText="UPDATE contracts SET status=$to,updated_at=CURRENT_TIMESTAMP WHERE id=$id AND status=$from";Add(x,"$to",t.To);Add(x,"$from",t.From);if(await x.ExecuteNonQueryAsync()!=1)return Results.BadRequest(new { message="合同状态已变化，请刷新" });
        x.CommandText="INSERT INTO contract_workflow_history(contract_id,from_status,to_status,action,actor,opinion) VALUES($id,$from,$to,$action,$actor,$opinion)";Add(x,"$action",t.Label);Add(x,"$actor",user.Username);Add(x,"$opinion",request.Opinion??"");await x.ExecuteNonQueryAsync();
        if(action is "review-pass" or "reject"){x.CommandText="INSERT INTO reviews(contract_id,reviewer,opinion,status) VALUES($id,$actor,$opinion,$to)";await x.ExecuteNonQueryAsync();}
        x.CommandText="INSERT INTO notifications(title,content,level,contract_id) VALUES($title,$content,'重要',$id)";Add(x,"$title",$"合同流程：{title}");Add(x,"$content",$"{t.Label}：{t.From} → {t.To}");await x.ExecuteNonQueryAsync();
        await WriteAuditAsync(c,tx,user.Username,t.Label,"合同",id,$"{number} {title}：{t.From} → {t.To}；{request.Opinion}");tx.Commit();return Results.Ok(new { status=t.To });
    }

    public async Task<IReadOnlyList<object>> ListWorkflowAsync(long id) { await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="SELECT id,from_status,to_status,action,actor,opinion,created_at FROM contract_workflow_history WHERE contract_id=$id ORDER BY id"; Add(x,"$id",id); var result=new List<object>(); await using var r=await x.ExecuteReaderAsync(); while(await r.ReadAsync()) result.Add(new { id=Convert.ToInt64(r["id"]), fromStatus=Text(r,"from_status"), toStatus=Text(r,"to_status"), action=Text(r,"action"), actor=Text(r,"actor"), opinion=Text(r,"opinion"), createdAt=Text(r,"created_at") }); return result; }

    public Task<IResult> RejectAsync(long id, ReviewRequest v, UserDto user) => WorkflowActionAsync(id,"reject",new WorkflowActionRequest(v.Opinion,null),user);

    public async Task<IReadOnlyList<RuleDto>> ListRulesAsync(PageRequest? paging = null)
    {
        await using var c = Open(); await c.OpenAsync(); await using var x = c.CreateCommand(); x.CommandText = "SELECT id,name,content,enabled FROM rules ORDER BY id DESC"; await ApplyPageAsync(x, paging); var result = new List<RuleDto>(); await using var r = await x.ExecuteReaderAsync(); while (await r.ReadAsync()) result.Add(new RuleDto(Convert.ToInt64(r["id"]), Text(r, "name"), Text(r, "content"), Convert.ToInt32(r["enabled"]) == 1)); return result;
    }

    public async Task<RuleDto> AddRuleAsync(RuleRequest v)
    {
        await using var c = Open(); await c.OpenAsync(); await using var x = c.CreateCommand(); x.CommandText = "INSERT INTO rules(name,content) VALUES($name,$content); SELECT last_insert_rowid();"; Add(x, "$name", v.Name); Add(x, "$content", v.Content); var id = Convert.ToInt64(await x.ExecuteScalarAsync()); return new RuleDto(id, v.Name, v.Content, true);
    }

    public async Task<IResult> UpdateRuleAsync(long id, RuleRequest v, string actor)
    {
        await using var c = Open(); await c.OpenAsync(); await using var tx = c.BeginTransaction(); await using var x = c.CreateCommand(); x.Transaction = tx;
        x.CommandText = "UPDATE rules SET name=$name,content=$content,enabled=$enabled WHERE id=$id";
        Add(x, "$id", id); Add(x, "$name", v.Name); Add(x, "$content", v.Content); Add(x, "$enabled", v.Enabled ? 1 : 0);
        if (await x.ExecuteNonQueryAsync() == 0) return Results.NotFound();
        await WriteAuditAsync(c, tx, actor, "编辑", "评审规则", id, v.Name); tx.Commit();
        return Results.Ok(new RuleDto(id, v.Name, v.Content, v.Enabled));
    }

    public async Task<IResult> DeleteRuleAsync(long id, string actor)
    {
        await using var c = Open(); await c.OpenAsync(); await using var tx = c.BeginTransaction(); await using var x = c.CreateCommand(); x.Transaction = tx;
        x.CommandText = "SELECT name FROM rules WHERE id=$id"; Add(x, "$id", id); var name = (string?)await x.ExecuteScalarAsync();
        if (name is null) return Results.NotFound();
        x.Parameters.Clear(); x.CommandText = "DELETE FROM rules WHERE id=$id"; Add(x, "$id", id); await x.ExecuteNonQueryAsync();
        await WriteAuditAsync(c, tx, actor, "删除", "评审规则", id, name); tx.Commit(); return Results.Ok(new { deleted = true });
    }

    public async Task<IReadOnlyList<DepartmentDto>> ListDepartmentsAsync()
    {
        await using var c = Open(); await c.OpenAsync(); await using var x = c.CreateCommand(); x.CommandText = "SELECT d.id,d.parent_id,d.name,d.code,d.manager,d.enabled,(SELECT COUNT(*) FROM user_departments ud WHERE ud.department_id=d.id) member_count FROM departments d ORDER BY d.id";
        var result = new List<DepartmentDto>(); await using var r = await x.ExecuteReaderAsync(); while (await r.ReadAsync()) result.Add(new DepartmentDto(Convert.ToInt64(r["id"]), r["parent_id"] is DBNull ? null : Convert.ToInt64(r["parent_id"]), Text(r,"name"), Text(r,"code"), Text(r,"manager"), Convert.ToInt32(r["enabled"]) == 1, Convert.ToInt32(r["member_count"]))); return result;
    }
    public async Task<DepartmentDto> AddDepartmentAsync(DepartmentRequest v)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="INSERT INTO departments(parent_id,name,code,manager) VALUES($parent,$name,$code,$manager); SELECT last_insert_rowid();"; Add(x,"$parent",v.ParentId); Add(x,"$name",v.Name); Add(x, "$code", v.Code ?? ""); Add(x, "$manager", v.Manager ?? ""); var id=Convert.ToInt64(await x.ExecuteScalarAsync()); return (await ListDepartmentsAsync()).First(d=>d.Id==id);
    }
    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(PageRequest? paging = null)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="SELECT r.id,r.name,r.description,r.enabled,(SELECT COUNT(*) FROM user_roles ur WHERE ur.role_id=r.id) user_count,COALESCE((SELECT group_concat(p.name,'、') FROM role_permissions rp JOIN permissions p ON p.id=rp.permission_id WHERE rp.role_id=r.id),'') permissions FROM roles r ORDER BY r.id"; await ApplyPageAsync(x, paging); var result=new List<RoleDto>(); await using var rd=await x.ExecuteReaderAsync(); while(await rd.ReadAsync()) result.Add(new RoleDto(Convert.ToInt64(rd["id"]),Text(rd,"name"),Text(rd,"description"),Convert.ToInt32(rd["enabled"])==1,Convert.ToInt32(rd["user_count"]),Text(rd,"permissions").Split('、',StringSplitOptions.RemoveEmptyEntries))); return result;
    }
    public async Task<RoleDto> AddRoleAsync(RoleRequest v)
    {
        await using var c=Open(); await c.OpenAsync(); await using var tx=await c.BeginTransactionAsync(); await using var x=c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText="INSERT INTO roles(name,description) VALUES($name,$description); SELECT last_insert_rowid()"; Add(x,"$name",v.Name); Add(x, "$description", v.Description ?? ""); var id=Convert.ToInt64(await x.ExecuteScalarAsync()); foreach(var code in v.Permissions ?? Array.Empty<string>()) { x.Parameters.Clear(); x.CommandText="INSERT OR IGNORE INTO role_permissions(role_id,permission_id) SELECT $role,id FROM permissions WHERE code=$code"; Add(x,"$role",id); Add(x,"$code",code); await x.ExecuteNonQueryAsync(); } await tx.CommitAsync(); return (await ListRolesAsync()).First(r=>r.Id==id);
    }
    public async Task<IReadOnlyList<OrgUserDto>> ListOrgUsersAsync(PageRequest? paging = null)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="SELECT u.id,u.username,u.display_name,COALESCE((SELECT group_concat(r.name,'、') FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=u.id),u.role) role,COALESCE(d.name,'未分配') department,COALESCE(ud.position,'') position FROM users u LEFT JOIN user_departments ud ON ud.user_id=u.id AND ud.is_primary=1 LEFT JOIN departments d ON d.id=ud.department_id ORDER BY u.id"; await ApplyPageAsync(x, paging); var result=new List<OrgUserDto>(); await using var r=await x.ExecuteReaderAsync(); while(await r.ReadAsync()) result.Add(new OrgUserDto(Convert.ToInt64(r["id"]),Text(r,"username"),Text(r,"display_name"),Text(r,"role"),Text(r,"department"),Text(r,"position"),true)); return result;
    }
    public async Task<IResult> AssignUserAsync(long userId, AssignmentRequest v, string actor)
    {
        await using var c=Open(); await c.OpenAsync(); await using var tx=await c.BeginTransactionAsync(); await using var x=c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText="DELETE FROM user_roles WHERE user_id=$user"; Add(x,"$user",userId); await x.ExecuteNonQueryAsync(); foreach(var roleId in v.RoleIds ?? Array.Empty<long>()) { x.Parameters.Clear(); x.CommandText="INSERT OR IGNORE INTO user_roles(user_id,role_id) VALUES($user,$role)"; Add(x,"$user",userId); Add(x,"$role",roleId); await x.ExecuteNonQueryAsync(); } x.Parameters.Clear(); x.CommandText="DELETE FROM user_departments WHERE user_id=$user"; Add(x,"$user",userId); await x.ExecuteNonQueryAsync(); if(v.DepartmentId is not null) { x.Parameters.Clear(); x.CommandText="INSERT INTO user_departments(user_id,department_id,position) VALUES($user,$department,$position)"; Add(x,"$user",userId); Add(x,"$department",v.DepartmentId); Add(x, "$position", v.Position ?? ""); await x.ExecuteNonQueryAsync(); } await tx.CommitAsync(); await AuditAsync(actor,"调整用户归属","用户",userId,$"部门:{v.DepartmentId};岗位:{v.Position}"); return Results.Ok();
    }
    public async Task<IReadOnlyList<PartyDto>> ListPartiesAsync(string? q, PageRequest? paging = null)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="SELECT p.*,(SELECT COUNT(*) FROM contracts c WHERE c.party_a=p.name OR c.party_b=p.name OR EXISTS(SELECT 1 FROM contract_parties cp WHERE cp.party_id=p.id AND cp.contract_id=c.id)) contract_count FROM parties p WHERE $q='' OR p.name LIKE $like OR p.code LIKE $like OR p.contact LIKE $like ORDER BY p.id DESC"; Add(x,"$q",q??""); Add(x,"$like",$"%{q??""}%"); await ApplyPageAsync(x, paging); var result=new List<PartyDto>(); await using var r=await x.ExecuteReaderAsync(); while(await r.ReadAsync()) result.Add(new PartyDto(Convert.ToInt64(r["id"]),Text(r,"name"),Text(r,"type"),Text(r,"code"),Text(r,"contact"),Text(r,"phone"),Text(r,"address"),Convert.ToInt32(r["enabled"])==1,Convert.ToInt32(r["contract_count"]))); return result;
    }
    public async Task<PartyDto> AddPartyAsync(PartyRequest v)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="INSERT INTO parties(name,type,code,contact,phone,address) VALUES($name,$type,$code,$contact,$phone,$address); SELECT last_insert_rowid();"; Add(x,"$name",v.Name); Add(x,"$type",v.Type); Add(x, "$code", v.Code ?? ""); Add(x, "$contact", v.Contact ?? ""); Add(x, "$phone", v.Phone ?? ""); Add(x, "$address", v.Address ?? ""); var id=Convert.ToInt64(await x.ExecuteScalarAsync()); return (await ListPartiesAsync("")).First(p=>p.Id==id);
    }
    public async Task<IReadOnlyList<object>> ListContractPartiesAsync(long contractId) { await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="SELECT p.id,p.name,p.type,p.code,cp.role FROM contract_parties cp JOIN parties p ON p.id=cp.party_id WHERE cp.contract_id=$id ORDER BY cp.role,p.name"; Add(x,"$id",contractId); var result=new List<object>(); await using var r=await x.ExecuteReaderAsync(); while(await r.ReadAsync()) result.Add(new { id=Convert.ToInt64(r["id"]), name=Text(r,"name"), type=Text(r,"type"), code=Text(r,"code"), role=Text(r,"role") }); return result; }
    public async Task<IResult> LinkContractPartyAsync(long contractId,PartyLinkRequest v,string user) {
        await using var c=Open();await c.OpenAsync();using var tx=c.BeginTransaction();if(!await IsDraftAsync(c,tx,contractId))return Results.BadRequest(new { message="仅草稿合同可以改变主体关联" });await using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="INSERT OR IGNORE INTO contract_parties(contract_id,party_id,role) VALUES($contract,$party,$role)";Add(x,"$contract",contractId);Add(x,"$party",v.PartyId);Add(x,"$role",v.Role);await x.ExecuteNonQueryAsync();await WriteAuditAsync(c,tx,user,"关联主体","合同",contractId,$"主体:{v.PartyId};角色:{v.Role}");tx.Commit();return Results.Ok(await ListContractPartiesAsync(contractId));
    }
    public async Task<IResult> UnlinkContractPartyAsync(long contractId,long partyId,string role,string user) {
        await using var c=Open();await c.OpenAsync();using var tx=c.BeginTransaction();if(!await IsDraftAsync(c,tx,contractId))return Results.BadRequest(new { message="仅草稿合同可以改变主体关联" });await using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="DELETE FROM contract_parties WHERE contract_id=$contract AND party_id=$party AND role=$role";Add(x,"$contract",contractId);Add(x,"$party",partyId);Add(x,"$role",role);if(await x.ExecuteNonQueryAsync()==0)return Results.NotFound();await WriteAuditAsync(c,tx,user,"解除主体关联","合同",contractId,$"主体:{partyId};角色:{role}");tx.Commit();return Results.Ok();
    }
    public async Task<IReadOnlyList<MilestoneDto>> ListMilestonesAsync(long contractId)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="SELECT id,contract_id,name,planned_at,completed_at,status,owner,note FROM milestones WHERE contract_id=$id ORDER BY planned_at"; Add(x,"$id",contractId); var result=new List<MilestoneDto>(); await using var r=await x.ExecuteReaderAsync(); while(await r.ReadAsync()) result.Add(new MilestoneDto(Convert.ToInt64(r["id"]),Convert.ToInt64(r["contract_id"]),Text(r,"name"),Text(r,"planned_at"),r["completed_at"] is DBNull?null:Text(r,"completed_at"),Text(r,"status"),Text(r,"owner"),Text(r,"note"))); return result;
    }
    public async Task<MilestoneDto> AddMilestoneAsync(long contractId, MilestoneRequest v)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="INSERT INTO milestones(contract_id,name,planned_at,owner,note) VALUES($id,$name,$planned,$owner,$note); SELECT last_insert_rowid();"; Add(x,"$id",contractId); Add(x,"$name",v.Name); Add(x,"$planned",v.PlannedAt); Add(x, "$owner", v.Owner ?? ""); Add(x, "$note", v.Note ?? ""); var id=Convert.ToInt64(await x.ExecuteScalarAsync()); return (await ListMilestonesAsync(contractId)).First(m=>m.Id==id);
    }
    public async Task<IReadOnlyList<NotificationDto>> ListNotificationsAsync(PageRequest? paging = null)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="SELECT id,title,content,level,read,created_at,contract_id FROM notifications ORDER BY id DESC"; await ApplyPageAsync(x, paging); var result=new List<NotificationDto>(); await using var r=await x.ExecuteReaderAsync(); while(await r.ReadAsync()) result.Add(new NotificationDto(Convert.ToInt64(r["id"]),Text(r,"title"),Text(r,"content"),Text(r,"level"),Convert.ToInt32(r["read"])==1,Text(r,"created_at"),r["contract_id"] is DBNull?null:Convert.ToInt64(r["contract_id"]))); return result;
    }
    public async Task<IResult> ReadNotificationAsync(long id)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="UPDATE notifications SET read=1 WHERE id=$id"; Add(x,"$id",id); return await x.ExecuteNonQueryAsync()==0?Results.NotFound():Results.Ok();
    }

    public async Task<IReadOnlyList<object>> ListAuditLogsAsync(PageRequest? paging = null)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="SELECT id,actor,action,entity_type,entity_id,details,created_at FROM audit_logs ORDER BY id DESC";
        await ApplyPageAsync(x, paging); var result=new List<object>(); await using var r=await x.ExecuteReaderAsync(); while(await r.ReadAsync()) result.Add(new { id=Convert.ToInt64(r["id"]), actor=Text(r,"actor"), action=Text(r,"action"), entityType=Text(r,"entity_type"), entityId=r["entity_id"] is DBNull?(long?)null:Convert.ToInt64(r["entity_id"]), details=Text(r,"details"), createdAt=Text(r,"created_at") }); return result;
    }

    public Task<IResult> BackupAsync()
    {
        var copy = Path.Combine(Path.GetTempPath(), $"contract-review-{DateTime.UtcNow:yyyyMMddHHmmss}.db");
        File.Copy(dbPath, copy, true);
        return Task.FromResult<IResult>(Results.File(copy, "application/octet-stream", Path.GetFileName(copy)));
    }

    static async Task<bool> IsDraftAsync(SqliteConnection c,SqliteTransaction tx,long id) { await using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="SELECT status FROM contracts WHERE id=$id";Add(x,"$id",id);return (string?)await x.ExecuteScalarAsync()=="草稿"; }
    static async Task WriteAuditAsync(SqliteConnection c, SqliteTransaction tx, string actor,string action,string type,long id,string details) { await using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="INSERT INTO audit_logs(actor,action,entity_type,entity_id,details) VALUES($actor,$action,$type,$id,$details)";Add(x,"$actor",actor);Add(x,"$action",action);Add(x,"$type",type);Add(x,"$id",id);Add(x,"$details",details);await x.ExecuteNonQueryAsync(); }
    public async Task<IResult> DeleteContractAsync(long id,string actor) {
        await using var c=Open();await c.OpenAsync();using var tx=c.BeginTransaction();await using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="SELECT number,title,status FROM contracts WHERE id=$id";Add(x,"$id",id);string details;
        await using(var r=await x.ExecuteReaderAsync()){if(!await r.ReadAsync())return Results.NotFound();if(Text(r,"status")!="草稿")return Results.BadRequest(new { message="仅草稿合同可以删除" });details=$"{Text(r,"number")} {Text(r,"title")}";}
        x.CommandText="DELETE FROM notifications WHERE contract_id=$id; DELETE FROM reviews WHERE contract_id=$id; DELETE FROM milestones WHERE contract_id=$id; DELETE FROM contract_workflow_history WHERE contract_id=$id; DELETE FROM contract_parties WHERE contract_id=$id; DELETE FROM attachments WHERE contract_id=$id; DELETE FROM contracts WHERE id=$id AND status='草稿'";await x.ExecuteNonQueryAsync();await WriteAuditAsync(c,tx,actor,"删除合同","合同",id,details);tx.Commit();var dir=Path.Combine(attachmentRoot,id.ToString());if(Directory.Exists(dir)){try{Directory.Delete(dir,true);}catch(IOException e){System.Diagnostics.Trace.TraceWarning($"合同{id}已删除，附件清理失败：{e.Message}");}catch(UnauthorizedAccessException e){System.Diagnostics.Trace.TraceWarning($"合同{id}已删除，附件清理失败：{e.Message}");}}return Results.Ok();
    }
    public async Task<IResult> DeletePartyAsync(long id,string actor) {
        await using var c=Open();await c.OpenAsync();using var tx=c.BeginTransaction();await using var x=c.CreateCommand();x.Transaction=tx;x.CommandText="SELECT name FROM parties WHERE id=$id";Add(x,"$id",id);var name=(string?)await x.ExecuteScalarAsync();if(name is null)return Results.NotFound();
        x.CommandText="SELECT EXISTS(SELECT 1 FROM contracts WHERE party_a=$name OR party_b=$name) OR EXISTS(SELECT 1 FROM contract_parties WHERE party_id=$id)";Add(x,"$name",name);if(Convert.ToInt32(await x.ExecuteScalarAsync())!=0)return Results.BadRequest(new { message="主体名下存在合同，不能删除" });x.CommandText="DELETE FROM parties WHERE id=$id";await x.ExecuteNonQueryAsync();await WriteAuditAsync(c,tx,actor,"删除主体","签约主体",id,name);tx.Commit();return Results.Ok();
    }

    async Task AuditAsync(string actor, string action, string entityType, long? entityId, string details)
    {
        await using var c=Open(); await c.OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="INSERT INTO audit_logs(actor,action,entity_type,entity_id,details) VALUES($actor,$action,$type,$id,$details)"; Add(x,"$actor",actor); Add(x,"$action",action); Add(x,"$type",entityType); Add(x,"$id",entityId); Add(x,"$details",details); await x.ExecuteNonQueryAsync();
    }

    async Task SeedDemoDataAsync(SqliteConnection c)
    {
        await using (var check = c.CreateCommand()) { check.CommandText = "SELECT id FROM contracts WHERE title='数据中心服务器采购合同' LIMIT 1"; if (await check.ExecuteScalarAsync() is not null) return; }
        var rng = new Random(20261005);
        var now = DateTime.UtcNow;
        string Ts(DateTime d) => d.ToString("yyyy-MM-dd HH:mm:ss");
        string D(DateTime d) => d.ToString("yyyy-MM-dd");
        async Task Exec(string sql, params (string Name, object? Value)[] ps) { await using var x = c.CreateCommand(); x.CommandText = sql; foreach (var (n, v) in ps) Add(x, n, v); await x.ExecuteNonQueryAsync(); }
        async Task<long> InsertId(string sql, params (string Name, object? Value)[] ps) { await using var x = c.CreateCommand(); x.CommandText = sql + "; SELECT last_insert_rowid()"; foreach (var (n, v) in ps) Add(x, n, v); return Convert.ToInt64(await x.ExecuteScalarAsync() ?? 0); }

        await Exec("INSERT OR IGNORE INTO departments(id,parent_id,name,code,manager) VALUES (5,2,'业务一部','BUS1','徐斌'),(6,2,'业务二部','BUS2','李娜')");
        var deptId = new Dictionary<string, long> { ["业务部"] = 2, ["法务部"] = 3, ["财务部"] = 4, ["业务一部"] = 5, ["业务二部"] = 6 };
        var users = new (string User, string Name, string Role, string Dept, string Pos, long RoleId)[] {
            ("zhangwei","张伟","业务负责人","业务部","业务经理",2),("xubin","徐斌","业务负责人","业务一部","部门经理",2),("lina","李娜","业务负责人","业务一部","业务专员",2),("wangqiang","王强","业务负责人","业务二部","业务专员",2),("liuyang","刘洋","业务负责人","业务部","业务专员",2),
            ("chenjing","陈静","法务评审人","法务部","法务主管",3),("yangfan","杨帆","法务评审人","法务部","法务专员",3),("zhaolei","赵磊","法务评审人","法务部","法务专员",3),
            ("huangmin","黄敏","财务审批人","财务部","财务主管",4),("zhoutao","周涛","财务审批人","财务部","财务专员",4),
            ("wuqian","吴倩","只读查看者","业务部","商务助理",5),("sunli","孙丽","只读查看者","财务部","出纳",5) };
        foreach (var u in users)
        {
            var created = now.AddDays(-rng.Next(60, 420));
            var id = await InsertId("INSERT INTO users(username,password_hash,display_name,role,created_at) VALUES($u,$p,$n,$r,$c)", ("$u", u.User), ("$p", Security.Hash("123456")), ("$n", u.Name), ("$r", u.Role), ("$c", Ts(created)));
            await Exec("INSERT OR IGNORE INTO user_roles(user_id,role_id) VALUES($u,$r)", ("$u", id), ("$r", u.RoleId));
            await Exec("INSERT INTO user_departments(user_id,department_id,position,is_primary) VALUES($u,$d,$p,1)", ("$u", id), ("$d", deptId[u.Dept]), ("$p", u.Pos));
            await Exec("INSERT INTO audit_logs(actor,action,entity_type,entity_id,details,created_at) VALUES('admin','创建','用户',$id,$n,$c)", ("$id", id), ("$n", u.Name), ("$c", Ts(created)));
        }

        var partyRows = new (string Name, string Type, string Code, string Contact, string Phone, string Addr)[] {
            ("杭州云启信息技术有限公司","企业","91330106MA8KXY2B7Q","郑凯","13757102346","杭州市西湖区文三路259号"),
            ("深圳市恒达物流有限公司","企业","91440300MA5FTY2R9X","林晓东","13602541278","深圳市南山区科技园南区R2-B栋"),
            ("北京中恒建设工程有限公司","企业","91110108MA01CY8W3J","高建军","13911234567","北京市海淀区学院路30号"),
            ("广州蓝海广告传媒有限公司","企业","91440101MA9W2C4T5K","梁嘉欣","13802234567","广州市天河区体育西路103号"),
            ("成都锐进医疗器械有限公司","企业","91510100MA6BKF3Y8W","唐国华","13908071234","成都市高新区天府大道北段966号"),
            ("南京瑞泽环保科技有限公司","企业","91320100MA1MKT7P2D","钱志强","13776651234","南京市建邺区江东中路359号"),
            ("武汉长江航运集团有限公司","企业","91420100MA4KXH9Q6F","冯永刚","13907123456","武汉市江岸区沿江大道145号"),
            ("陈立新","个人","110101197812034512","陈立新","13601012345","北京市东城区东直门南大街9号"),
            ("刘桂芳","个人","310104198504152026","刘桂芳","13701812345","上海市徐汇区漕溪北路88号"),
            ("马俊杰","个人","440305199002286614","马俊杰","13802281234","深圳市福田区深南大道2008号") };
        var allParties = new List<(long Id, string Name)> { (1, "华夏合同科技有限公司"), (2, "上海未来供应链有限公司"), (3, "王小明") };
        foreach (var p in partyRows)
        {
            var id = await InsertId("INSERT INTO parties(name,type,code,contact,phone,address,created_at) VALUES($n,$t,$c,$ct,$ph,$ad,$cr)", ("$n", p.Name), ("$t", p.Type), ("$c", p.Code), ("$ct", p.Contact), ("$ph", p.Phone), ("$ad", p.Addr), ("$cr", Ts(now.AddDays(-rng.Next(30, 400)))));
            allParties.Add((id, p.Name));
        }

        var ruleRows = new (string Name, string Content)[] {
            ("付款条款风险","付款比例与账期需与合同约定一致，预付款比例超过30%需财务总监复核。"),
            ("违约金上限","违约金总额原则上不超过合同金额的20%，超出部分需重新谈判。"),
            ("知识产权归属","技术开发类合同必须明确成果知识产权归属及后续改进权利。"),
            ("验收标准明确性","验收标准、验收期限与异议期必须量化可执行，禁止“双方协商确定”等模糊表述。"),
            ("保密期限","保密义务期限一般不超过合同终止后三年，超出需法务总监审批。"),
            ("争议解决条款","原则上约定己方所在地法院或仲裁委员会管辖，避免异地诉讼。"),
            ("阴阳合同风险","合同金额与结算单据必须一致，禁止签署与真实交易不符的版本。"),
            ("发票与税率条款","明确发票类型、税率及开票时点，价税分离表述需与报价一致。") };
        foreach (var r in ruleRows) await Exec("INSERT INTO rules(name,content) VALUES($n,$c)", ("$n", r.Name), ("$c", r.Content));

        var legals = new[] { "陈静", "杨帆", "赵磊" };
        var finances = new[] { "黄敏", "周涛" };
        var owners = new (string Name, string Dept)[] { ("张伟", "业务部"), ("徐斌", "业务一部"), ("李娜", "业务一部"), ("王强", "业务二部"), ("刘洋", "业务部") };
        var passOpinions = new[] { "已复核合同主体资格、付款条款与违约责任，整体风险可控，同意通过评审。", "知识产权归属及保密条款已按标准模板修订，同意通过。", "已补充验收标准与异议期约定，评审通过。", "违约责任上限已调整至合同金额的20%，无其他重大风险，通过。", "合同金额、税率与报价单一致，付款账期符合公司政策，同意通过。" };
        var rejectOpinions = new[] { "付款条款中预付款比例达50%，超出公司政策上限，请与供应商重新谈判后重新提交。", "违约责任不对等，仅约定我方义务，请补充对等条款后重新提交。", "验收标准未量化，缺少明确的验收期限与异议期，需修订后再报。", "合同期限超过三年但缺少价格调整机制，请补充约定或缩短合同期限。" };
        var finOpinions = new[] { "付款计划与年度预算匹配，同意审批。", "已核对开票信息与税率，同意。", "预算科目已落实，同意审批通过。" };
        var payments = new[] { "预付30%，验收合格后60日内支付尾款", "货到付款，月结30天", "按季度分期支付，每季度末结算", "签订后5个工作日内一次性支付", "预付10%，交付后支付80%，质保期满支付10%" };
        var tagPool = new[] { "年度框架", "常规采购", "紧急", "大客户", "需法务复核", "含保密条款", "预算内" };
        var typeSummary = new Dictionary<string, string> { ["采购合同"] = "约定采购物资的规格、数量、交付周期及验收标准", ["销售合同"] = "约定产品功能范围、交付里程碑、验收及售后支持义务", ["服务合同"] = "约定服务范围、服务等级（SLA）、考核与结算方式", ["租赁合同"] = "约定租赁标的、租金支付方式、维修保养责任及续租条件", ["技术开发合同"] = "约定开发范围、里程碑交付物、知识产权归属及保密义务", ["劳务合同"] = "约定派遣或外包人员数量、费用结算及安全责任", ["框架合作协议"] = "约定年度合作范围、单价清单及单项订单的下单机制", ["委托加工合同"] = "约定加工数量、质量标准、原材料供应及结算方式" };
        var titles = new (string Type, string Title, bool WeSell)[] {
            ("采购合同","数据中心服务器采购合同",false),("服务合同","华东区仓储物流服务合同",false),("销售合同","智能仓储管理系统销售合同",true),("租赁合同","浦东办公场地租赁合同",false),("采购合同","办公用品年度集中采购合同",false),
            ("技术开发合同","供应链数据中台技术开发合同",false),("销售合同","WMS软件产品销售合同",true),("委托加工合同","电子元器件委托加工合同",false),("服务合同","常年法律咨询服务合同",false),("销售合同","自动化分拣设备销售合同",true),
            ("框架合作协议","与长江航运集团战略合作框架协议",true),("服务合同","年度市场调研服务合同",false),("采购合同","实验室耗材采购合同",false),("租赁合同","昆山保税仓租赁合同",false),("服务合同","网络安全运维服务合同",false),
            ("销售合同","年度软件授权销售合同",true),("劳务合同","生产线劳务派遣协议",false),("服务合同","总部办公楼保洁服务合同",false),("技术开发合同","客户小程序定制开发合同",false),("租赁合同","商务用车租赁合同",false),
            ("采购合同","办公家具采购合同",false),("服务合同","行业展会布展服务合同",false),("销售合同","数字营销平台销售合同",true),("采购合同","生产辅料采购合同",false),("技术开发合同","AI视觉质检算法开发合同",false),
            ("服务合同","员工体检服务合同",false),("框架合作协议","渠道分销框架合作协议",true),("劳务合同","仓储环节劳务外包合同",false),("服务合同","法律咨询服务合同（专项）",false),("租赁合同","精密检测设备租赁合同",false),
            ("采购合同","办公电脑批量采购合同",false),("服务合同","企业宣传片制作服务合同",false),("服务合同","冷链运输服务合同",false),("委托加工合同","包装制品委托加工合同",false),("框架合作协议","云资源年度框架采购协议",false),("服务合同","SaaS订阅服务合同",false) };
        var statuses = new[] { "已归档","履行中","履行中","已签署","履行中","已归档","已签署","签署中","审批通过","履行中","已归档","审批中","签署中","评审通过","审批中","已驳回","评审通过","审批中","已驳回","评审通过","已驳回","评审通过","评审中","评审中","评审中","评审中","评审中","评审中","已作废","草稿","草稿","草稿","履行中","已签署","审批通过","草稿" };

        List<(string Action, string From, string To, string Actor, string Opinion)> Steps(string status, string owner, string legal, string finance)
        {
            var l = new List<(string Action, string From, string To, string Actor, string Opinion)>();
            void Add(string a, string f, string t, string actor, string o = "") => l.Add((a, f, t, actor, o));
            switch (status)
            {
                case "评审中": Add("提交评审", "草稿", "评审中", owner, "请法务评审合同主要条款与风险点。"); break;
                case "已驳回": Add("提交评审", "草稿", "评审中", owner, "请法务评审合同主要条款与风险点。"); Add("退回", "评审中", "已驳回", legal, rejectOpinions[rng.Next(rejectOpinions.Length)]); break;
                case "评审通过": Add("提交评审", "草稿", "评审中", owner, "请法务评审合同主要条款与风险点。"); Add("评审通过", "评审中", "评审通过", legal, passOpinions[rng.Next(passOpinions.Length)]); break;
                case "审批中": Add("提交评审", "草稿", "评审中", owner, "请法务评审合同主要条款与风险点。"); Add("评审通过", "评审中", "评审通过", legal, passOpinions[rng.Next(passOpinions.Length)]); Add("发起审批", "评审通过", "审批中", finance); break;
                case "审批通过": Add("提交评审", "草稿", "评审中", owner, "请法务评审合同主要条款与风险点。"); Add("评审通过", "评审中", "评审通过", legal, passOpinions[rng.Next(passOpinions.Length)]); Add("发起审批", "评审通过", "审批中", finance); Add("审批通过", "审批中", "审批通过", finance, finOpinions[rng.Next(finOpinions.Length)]); break;
                case "签署中": Add("提交评审", "草稿", "评审中", owner, "请法务评审合同主要条款与风险点。"); Add("评审通过", "评审中", "评审通过", legal, passOpinions[rng.Next(passOpinions.Length)]); Add("发起审批", "评审通过", "审批中", finance); Add("审批通过", "审批中", "审批通过", finance, finOpinions[rng.Next(finOpinions.Length)]); Add("发起签署", "审批通过", "签署中", owner); break;
                case "已签署": Add("提交评审", "草稿", "评审中", owner, "请法务评审合同主要条款与风险点。"); Add("评审通过", "评审中", "评审通过", legal, passOpinions[rng.Next(passOpinions.Length)]); Add("发起审批", "评审通过", "审批中", finance); Add("审批通过", "审批中", "审批通过", finance, finOpinions[rng.Next(finOpinions.Length)]); Add("发起签署", "审批通过", "签署中", owner); Add("完成签署", "签署中", "已签署", owner, "双方已完成用印，合同正式生效。"); break;
                case "履行中": Add("提交评审", "草稿", "评审中", owner, "请法务评审合同主要条款与风险点。"); Add("评审通过", "评审中", "评审通过", legal, passOpinions[rng.Next(passOpinions.Length)]); Add("发起审批", "评审通过", "审批中", finance); Add("审批通过", "审批中", "审批通过", finance, finOpinions[rng.Next(finOpinions.Length)]); Add("发起签署", "审批通过", "签署中", owner); Add("完成签署", "签署中", "已签署", owner, "双方已完成用印，合同正式生效。"); Add("生效履行", "已签署", "履行中", owner); break;
                case "已归档": Add("提交评审", "草稿", "评审中", owner, "请法务评审合同主要条款与风险点。"); Add("评审通过", "评审中", "评审通过", legal, passOpinions[rng.Next(passOpinions.Length)]); Add("发起审批", "评审通过", "审批中", finance); Add("审批通过", "审批中", "审批通过", finance, finOpinions[rng.Next(finOpinions.Length)]); Add("发起签署", "审批通过", "签署中", owner); Add("完成签署", "签署中", "已签署", owner, "双方已完成用印，合同正式生效。"); Add("生效履行", "已签署", "履行中", owner); Add("归档", "履行中", "已归档", "admin", "履约完毕，资料齐全，予以归档。"); break;
                case "已作废": Add("作废", "草稿", "已作废", owner, "业务需求取消，该合同不再执行。"); break;
            }
            return l;
        }

        var cursor = new DateTime(2025, 9, 2, 2, 0, 0, DateTimeKind.Utc);
        var attachmentTargets = new List<(long Id, string Title, string Owner)>();
        for (var i = 0; i < titles.Length; i++)
        {
            var (type, title, weSell) = titles[i];
            var status = statuses[i];
            cursor = cursor.AddDays(rng.Next(5, 16)); if (cursor > now.AddDays(-1)) cursor = now.AddDays(-rng.Next(1, 40));
            var created = cursor;
            var owner = owners[rng.Next(owners.Length)];
            var legal = legals[rng.Next(legals.Length)];
            var finance = finances[rng.Next(finances.Length)];
            var steps = Steps(status, owner.Name, legal, finance);
            var sim = created;
            var stepTimes = new DateTime[steps.Count];
            string? signedAt = null, effectiveAt = null;
            for (var s = 0; s < steps.Count; s++) { sim = sim.AddDays(rng.Next(1, 5)).AddHours(rng.Next(1, 9)); stepTimes[s] = sim; if (steps[s].To == "已签署") signedAt = D(sim); if (steps[s].To == "履行中") effectiveAt = D(sim); }
            string? expiresAt = null;
            if (effectiveAt is not null || signedAt is not null) { var exp = DateTime.Parse(effectiveAt ?? signedAt!, System.Globalization.CultureInfo.InvariantCulture).AddDays(365); if (i == 1) exp = now.AddDays(12); if (i == 33) exp = now.AddDays(25); expiresAt = D(exp); }
            var cpIdx = title.Contains("长江航运") ? allParties.FindIndex(p => p.Name.Contains("长江航运")) : rng.Next(2, allParties.Count);
            var counter = allParties[cpIdx];
            var partyA = weSell ? counter.Name : "华夏合同科技有限公司";
            var partyB = weSell ? "华夏合同科技有限公司" : counter.Name;
            var amount = Math.Truncate((decimal)(5 + rng.NextDouble() * 495) * 10000 / 1000) * 1000;
            var currency = i is 20 or 35 ? "USD" : "CNY";
            var tag = i % 4 == 0 ? "重点项目" : tagPool[rng.Next(tagPool.Length)];
            var desc = $"{title}，{typeSummary[type]}。由{owner.Dept}负责业务跟进。";
            var updated = steps.Count > 0 ? stepTimes[^1] : created;
            var id = await InsertId("INSERT INTO contracts(number,title,contract_type,party_a,party_b,amount,currency,signed_at,effective_at,expires_at,owner,department,payment_terms,description,tags,status,created_at,updated_at) VALUES($number,$title,$type,$a,$b,$amount,$currency,$signed,$effective,$expires,$owner,$dept,$pay,$desc,$tags,$status,$created,$updated)",
                ("$number", $"HT-{created.Year}-{i + 1:0000}"), ("$title", title), ("$type", type), ("$a", partyA), ("$b", partyB), ("$amount", amount), ("$currency", currency), ("$signed", signedAt), ("$effective", effectiveAt), ("$expires", expiresAt), ("$owner", owner.Name), ("$dept", owner.Dept), ("$pay", payments[rng.Next(payments.Length)]), ("$desc", desc), ("$tags", tag), ("$status", status), ("$created", Ts(created)), ("$updated", Ts(updated)));
            await Exec("INSERT INTO contract_parties(contract_id,party_id,role) VALUES($id,1,$r1),($id,$p,$r2)", ("$id", id), ("$r1", weSell ? "乙方" : "甲方"), ("$p", counter.Id), ("$r2", weSell ? "甲方" : "乙方"));
            await Exec("INSERT INTO audit_logs(actor,action,entity_type,entity_id,details,created_at) VALUES($a,'创建','合同',$id,$d,$c)", ("$a", owner.Name), ("$id", id), ("$d", title), ("$c", Ts(created)));
            for (var s = 0; s < steps.Count; s++)
            {
                var (action, from, to, actor, opinion) = steps[s];
                var t = stepTimes[s];
                await Exec("INSERT INTO contract_workflow_history(contract_id,from_status,to_status,action,actor,opinion,created_at) VALUES($id,$f,$to,$a,$ac,$o,$c)", ("$id", id), ("$f", from), ("$to", to), ("$a", action), ("$ac", actor), ("$o", opinion), ("$c", Ts(t)));
                await Exec("INSERT INTO notifications(title,content,level,read,created_at,contract_id) VALUES($t,$c,'重要',$r,$ct,$id)", ("$t", $"合同流程：{title}"), ("$c", $"合同状态已从{from}变更为{to}"), ("$r", t < now.AddDays(-5) ? 1 : 0), ("$ct", Ts(t)), ("$id", id));
                await Exec("INSERT INTO audit_logs(actor,action,entity_type,entity_id,details,created_at) VALUES($a,$ac,'合同',$id,$d,$c)", ("$a", actor), ("$ac", action), ("$id", id), ("$d", $"{from} -> {to}"), ("$c", Ts(t)));
            }
            if (expiresAt is not null && (i == 1 || i == 33)) await Exec("INSERT INTO notifications(title,content,level,read,created_at,contract_id) VALUES($t,$c,'重要',0,$n,$id)", ("$t", $"合同到期提醒：{title}"), ("$c", $"合同将于{expiresAt}到期，请提前跟进续签或终止事宜。"), ("$n", Ts(now.AddDays(-2))), ("$id", id));
            var reviewBase = steps.Count > 0 ? stepTimes[0] : created;
            if (status is "评审通过" or "审批中" or "审批通过" or "签署中" or "已签署" or "履行中" or "已归档")
            {
                var n = rng.Next(1, 3);
                for (var k = 0; k < n; k++) await Exec("INSERT INTO reviews(contract_id,reviewer,opinion,status,created_at) VALUES($id,$r,$o,'评审通过',$c)", ("$id", id), ("$r", legal), ("$o", passOpinions[rng.Next(passOpinions.Length)]), ("$c", Ts(reviewBase.AddDays(k + 1))));
            }
            else if (status == "已驳回") await Exec("INSERT INTO reviews(contract_id,reviewer,opinion,status,created_at) VALUES($id,$r,$o,'已驳回',$c)", ("$id", id), ("$r", legal), ("$o", steps[^1].Opinion), ("$c", Ts(reviewBase.AddDays(2))));
            else if (status == "评审中" && i % 2 == 0) await Exec("INSERT INTO reviews(contract_id,reviewer,opinion,status,created_at) VALUES($id,$r,$o,'评审中',$c)", ("$id", id), ("$r", legal), ("$o", "正在复核付款条款与违约责任，评审意见将在完成后提交。"), ("$c", Ts(reviewBase.AddDays(1))));
            if (status is "履行中" or "已归档" or "已签署")
            {
                var anchor = DateTime.Parse(effectiveAt ?? signedAt!, System.Globalization.CultureInfo.InvariantCulture);
                var names = new[] { "首付款支付", "交付验收", "中期款支付", "项目终验", "质保金到期" };
                var count = rng.Next(3, names.Length + 1);
                for (var m = 0; m < count; m++)
                {
                    var planned = anchor.AddDays(30 * (m + 1) + rng.Next(-5, 6));
                    var done = planned < now.Date;
                    await Exec("INSERT INTO milestones(contract_id,name,planned_at,completed_at,status,owner,note,created_at) VALUES($id,$n,$p,$cc,$s,$o,$note,$c)", ("$id", id), ("$n", names[m]), ("$p", D(planned)), ("$cc", done ? D(planned.AddDays(rng.Next(0, 3))) : null), ("$s", done ? "已完成" : "待完成"), ("$o", owner.Name), ("$note", done ? "已按计划完成" : "按计划推进中"), ("$c", Ts(anchor)));
                }
            }
            if (attachmentTargets.Count < 10 && status is "评审中" or "评审通过" or "审批中" or "已签署" or "履行中" or "已归档") attachmentTargets.Add((id, title, owner.Name));
        }

        foreach (var (cid, title, owner) in attachmentTargets)
        {
            var versions = rng.Next(1, 4);
            for (var v = 1; v <= versions; v++)
            {
                var stored = $"{Guid.NewGuid():N}.docx";
                var dir = Path.Combine(attachmentRoot, cid.ToString());
                Directory.CreateDirectory(dir);
                var body = new System.Text.StringBuilder();
                body.AppendLine($"《{title}》");
                body.AppendLine($"合同文本（第{v}版）");
                for (var line = 1; line <= 80; line++) body.AppendLine($"第{line}条 本条款为合同第{v}版演示文本，用于评审、审批与归档流程演示，具体权利义务以双方最终用印版本为准。");
                var content = body.ToString();
                await File.WriteAllTextAsync(Path.Combine(dir, stored), content);
                var fname = v == versions ? $"《{title}》用印版.docx" : $"《{title}》评审稿v{v}.docx";
                var at = now.AddDays(-rng.Next(0, 60));
                await Exec("INSERT INTO attachments(contract_id,file_name,content_type,size,version,stored_name,uploaded_by,created_at) VALUES($id,$n,$t,$s,$v,$st,$u,$c)", ("$id", cid), ("$n", fname), ("$t", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"), ("$s", System.Text.Encoding.UTF8.GetByteCount(content)), ("$v", v), ("$st", stored), ("$u", owner), ("$c", Ts(at)));
                await Exec("INSERT INTO audit_logs(actor,action,entity_type,entity_id,details,created_at) VALUES($a,'上传附件','合同',$id,$d,$c)", ("$a", owner), ("$id", cid), ("$d", fname), ("$c", Ts(at)));
            }
        }
    }
}
