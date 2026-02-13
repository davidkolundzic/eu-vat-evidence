using System;
using System.Collections.Generic;
using System.Text;

namespace VatEvidence.Domain
{
  public sealed class Workspace
  {
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<WorkspaceUser> Users { get; set; } = new List<WorkspaceUser>();
    public ICollection<ProviderConnection> ProviderConnections { get; set; } = new List<ProviderConnection>();
  }
}
