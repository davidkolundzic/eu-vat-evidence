namespace VatEvidence.Domain
{
  public sealed class WorkspaceUser
  {
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = default!;
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public WorkspaceRole Role { get; set; }
  }
}