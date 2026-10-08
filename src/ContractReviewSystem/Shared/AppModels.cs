using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

record LoginRequest(string Username, string Password, string? CaptchaId, string? Captcha);
record PasswordRequest(string CurrentPassword, string NewPassword);
record UserDto(long Id, string Username, string DisplayName, string Role, string CreatedAt);
record CreateContractRequest(string Title, string? PartyA, string? PartyB, decimal? Amount, string? Description, string? ContractType = null, string? Department = null, long[]? PartyIds = null, string? Currency = null, string? SignedAt = null, string? EffectiveAt = null, string? ExpiresAt = null, string? Owner = null, string? PaymentTerms = null, string? Tags = null);
record UpdateContractRequest(string Title, string? ContractType, string? PartyA, string? PartyB, decimal? Amount, string? Currency, string? SignedAt, string? EffectiveAt, string? ExpiresAt, string? Owner, string? Department, string? PaymentTerms, string? Description, string? Tags);
record AttachmentDto(long Id, long ContractId, string FileName, string ContentType, long Size, int Version, string UploadedBy, string CreatedAt);
record ContractDto(long Id, string Number, string Title, string PartyA, string PartyB, decimal? Amount, string Status, string CreatedAt, string Description, int OpinionCount, int AttachmentCount = 0);
record ReviewRequest(string? Reviewer, string? Opinion);
record RuleRequest(string Name, string Content, string? CheckType = null, string? Config = null, bool Enabled = true);
record RuleDto(long Id, string Name, string Content, bool Enabled, string CheckType = "manual", string Config = "");
record ReviewCheckItemDto(long Id, long RoundId, long? RuleId, string RuleName, string RuleContent, string CheckType, string Config, string? AutoResult, string? ManualResult, string Reason, string? CheckedBy, string? CheckedAt);
record ReviewRoundDto(long Id, long ContractId, int RoundNo, string CreatedBy, string CreatedAt, IReadOnlyList<ReviewCheckItemDto> Items);
record CaptchaEntry(string Code, DateTimeOffset Expires);
record DepartmentDto(long Id, long? ParentId, string Name, string Code, string Manager, bool Enabled, int MemberCount);
record DepartmentRequest(string Name, string? Code, long? ParentId, string? Manager);
record RoleDto(long Id, string Name, string Description, bool Enabled, int UserCount, IReadOnlyList<string> Permissions);
record RoleRequest(string Name, string? Description, string[]? Permissions);
record AssignmentRequest(long[]? RoleIds, long? DepartmentId, string? Position);
record PartyLinkRequest(long PartyId, string Role);
record WorkflowActionRequest(string? Opinion, string? Note);
record OrgUserDto(long Id, string Username, string DisplayName, string Role, string Department, string Position, bool Enabled);
record PartyDto(long Id, string Name, string Type, string Code, string Contact, string Phone, string Address, bool Enabled, int ContractCount);
record PartyRequest(string Name, string Type, string? Code, string? Contact, string? Phone, string? Address);
record MilestoneDto(long Id, long ContractId, string Name, string PlannedAt, string? CompletedAt, string Status, string Owner, string? Note);
record MilestoneRequest(string Name, string PlannedAt, string? Owner, string? Note);
record NotificationDto(long Id, string Title, string Content, string Level, bool Read, string CreatedAt, long? ContractId);
record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
sealed class PageRequest(int page, int pageSize)
{
    public int Page { get; } = Math.Max(1, page);
    public int PageSize { get; } = Math.Clamp(pageSize, 1, 100);
    public int Total { get; set; }
}

static class Security
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

static class Captcha
{
    public static string RandomCode() => string.Concat(Enumerable.Range(0, 4).Select(_ => "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"[RandomNumberGenerator.GetInt32(0, 31)]));
    public static string Svg(string code) => $"<svg xmlns='http://www.w3.org/2000/svg' width='150' height='46' viewBox='0 0 150 46'><rect width='150' height='46' rx='8' fill='#eef4ff'/><path d='M0 10L150 36M0 38L150 8' stroke='#bdd0ee' stroke-width='2'/><text x='75' y='32' text-anchor='middle' font-family='Arial' font-size='25' font-weight='700' letter-spacing='7' fill='#1e4c86'>{code}</text></svg>";
}
