using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Domain;

namespace VatEvidence.Test.Integration.TestInfrastructure.Builders
{
  public sealed class WorkspaceBuilder
  {
    private Guid _id = TestGuids.WorkspaceId;
    private string _name = "Test Workspace";
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

    public static WorkspaceBuilder Default() => new();

    public WorkspaceBuilder WithId(Guid id) { _id = id; return this; }
    public WorkspaceBuilder WithName(string name) { _name = name; return this; }
    public WorkspaceBuilder WithCreatedAt(DateTimeOffset createdAt) { _createdAt = createdAt; return this; }

    public Workspace Build() => new Workspace
    {
      Id = _id,
      Name = _name,
      CreatedAt = _createdAt
    };

  }
}
