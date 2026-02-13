using System;
using System.Collections.Generic;
using System.Text;

namespace VatEvidence.Domain
{
  public sealed class Export
  {
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = default!;

    public ExportType Type { get; set; }
    public DateTimeOffset RangeFrom { get; set; }
    public DateTimeOffset RangeTo { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }

    public string FilePath { get; set; } = "";
    public string FileHash { get; set; } = "";
  }
}
