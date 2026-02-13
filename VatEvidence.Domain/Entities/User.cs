namespace VatEvidence.Domain
{
  public sealed class User
  {
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<WorkspaceUser> Workspaces { get; set; } = new List<WorkspaceUser>();
  }
}