using System;
using System.Collections.Generic;
using System.Text;
using VatEvidence.Domain;

namespace VatEvidence.Test.Integration.TestInfrastructure.Builders
{
  public sealed class TransactionBuilder
  {
    private Guid _id = TestGuids.TransactionId;
    private Guid _workspaceId = TestGuids.WorkspaceId;

    private ProviderKind _provider = ProviderKind.Stripe;
    private ProviderMode _mode = ProviderMode.Test;

    private string _providerTransaction = "pi_test_123";
    private string? _providerChargeId = "ch_test_123";
    private long _amountMinor = 10050;
    private string _currency = CurrencyCodes.EUR;
    private string _customerEmail = "test@example.com";

    private DateTimeOffset _createdUtc = DateTimeOffset.UtcNow;
    private TransactionStatus _status = TransactionStatus.Ok;

    public static TransactionBuilder Default() => new();

    public TransactionBuilder WithId(Guid id) { _id = id; return this; }
    public TransactionBuilder ForWorkspaceId(Guid workspaceId) { _workspaceId = workspaceId; return this; }
    
    public TransactionBuilder WithProviderTransactionId(string providerTransactionId ) { _providerTransaction = providerTransactionId; return this; }
    public TransactionBuilder WithChargeId(string providerChargeId) { _providerChargeId = providerChargeId; return this; }
    
    public TransactionBuilder WithAmountMinor(long amountMinor) { _amountMinor = amountMinor; return this; }
    public TransactionBuilder WithCurrency(string currency) { _currency = currency; return this; }
    public TransactionBuilder WithCustomerEmail(string customerEmail) { _customerEmail = customerEmail; return this; }
    
    public TransactionBuilder CreatedUtc(DateTimeOffset createdUtc) { _createdUtc = createdUtc; return this; }
    public TransactionBuilder WithStatus(TransactionStatus status) { _status = status; return this; }

    public Transaction Build() => new()
    {
      Id = _id,
      WorkspaceId = _workspaceId,
      Provider = _provider,
      Mode = _mode,
      ProviderTransactionId = _providerTransaction,
      ProviderChargeId = _providerChargeId,
      AmountMinor = _amountMinor,
      Currency = _currency,
      CustomerEmail = _customerEmail,
      CreatedUtc = _createdUtc,
      Status = _status
    };

  }
}
