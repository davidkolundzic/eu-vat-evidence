using System;
using System.Collections.Generic;
using System.Text;

namespace VatEvidence.Test.Integration.TestInfrastructure
{
  // Test-only deterministic GUIDs.
  // Do NOT use in production code.
  public static class TestGuids
  {
    public static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TransactionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid ProviderEventId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Evidence1Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Evidence2Id = Guid.Parse("55555555-5555-5555-5555-555555555555");

  }
}
