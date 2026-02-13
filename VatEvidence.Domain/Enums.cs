using System;
using System.Collections.Generic;
using System.Text;

namespace VatEvidence.Domain
{
  public class CurrencyCodes
  {
    public const string EUR = "EUR";
    public const string USD = "USD";
    public const string GBP = "GBP";
    // Dodaj ostale valute po potrebi

  }
  public class CountryCodes
  {
    public const string HR = "HR";
    public const string DE = "DE";
    public const string FR = "FR";
   public const string US = "AT"; // austrija
    //  belgija
    public const string BE = "BE";
    // Dodaj ostale zemlje po potrebi
  }

  public static class ProviderNames
  {
    public const string Stripe = "stripe";
  }

  public enum ProviderKind { 
    Stripe = 1
  }
  public enum ProviderMode { 
    Test = 1, 
    Live = 2
  }
  public enum WorkspaceRole { 
    Owner = 1, 
    Member = 2
  }
  public enum EventProcessingStatus { 
    Received = 1, // event zaprimljen i spremljen 
    Processed = 2, // obrada prošla bez exception-a
    Failed = 3 // obrada završila s exception-om
  }

  public enum EvidenceType
  {
    Ipcountry = 1,
    Billingcountry = 2,
    PaymentCountry = 3
  }

  public enum TransactionStatus
  {
    Ok = 1,
    Mismatch = 2,
    Insufficient = 3
  }
  
  public enum ExportType
  {
    Csv = 1,
    Pdf = 2
  }

}
