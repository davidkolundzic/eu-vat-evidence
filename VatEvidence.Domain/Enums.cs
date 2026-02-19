using System;
using System.Collections.Generic;
using System.Text;

namespace VatEvidence.Domain
{
  public static class CurrencyCodes
  {
    public const string EUR = "EUR";
    public const string USD = "USD";
    public const string GBP = "GBP";
    // Dodaj ostale valute po potrebi

  }

  /// <summary>
  /// Represents the context of a country, including its ISO code and membership status in the European Union (EU),
  /// European Economic Area (EEA), and non-EU classification.
  /// </summary>
  /// <remarks>Use this record to determine a country's status with respect to EU and EEA membership, which may
  /// affect regulatory, tax, or business logic in applications that operate across multiple countries.</remarks>
  /// <param name="Code">The normalized ISO 3166-1 alpha-2 country code, such as "HR" for Croatia. This value can be null if the country is
  /// unspecified.</param>
  /// <param name="IsValid">true if the country context contains valid and recognized information; otherwise, false.</param>
  /// <param name="IsEu">true if the country is a member of the European Union (EU); otherwise, false.</param>
  /// <param name="IsEea">true if the country is a member of the European Economic Area (EEA); otherwise, false.</param>
  /// <param name="IsNonEu">true if the country is classified as a non-EU country; otherwise, false.</param>
  public sealed record CountryContext(
      string? Code, // normalized ISO code (npr. "HR")
      bool IsValid,
      bool IsEu,
      bool IsEea,
      bool IsNonEu
    );
  public static class CountryClassification
  {
    // ISO 3166-1 alpha-2 (full list) - zalijepi svoju kompletnu listu ovdje
    private static readonly HashSet<string> _isoAlpha2 =
      new(StringComparer.OrdinalIgnoreCase)
      {
      "AF","AL","DZ","AS","AD","AO","AI","AQ","AG","AR","AM","AW","AU","AT","AZ",
      "BS","BH","BD","BB","BY","BE","BZ","BJ","BM","BT","BO","BQ","BA","BW","BV","BR","IO","BN","BG","BF","BI",
      "KH","CM","CA","CV","KY","CF","TD","CL","CN","CX","CC","CO","KM","CG","CD","CK","CR","CI","HR","CU","CW","CY","CZ",
      "DK","DJ","DM","DO",
      "EC","EG","SV","GQ","ER","EE","SZ","ET",
      "FK","FO","FJ","FI","FR","GF","PF","TF",
      "GA","GM","GE","DE","GH","GI","GR","GL","GD","GP","GU","GT","GG","GN","GW","GY",
      "HT","HM","VA","HN","HK","HU",
      "IS","IN","ID","IR","IQ","IE","IM","IL","IT",
      "JM","JP","JE","JO",
      "KZ","KE","KI","KP","KR","KW","KG",
      "LA","LV","LB","LS","LR","LY","LI","LT","LU",
      "MO","MG","MW","MY","MV","ML","MT","MH","MQ","MR","MU","YT","MX","FM","MD","MC","MN","ME","MS","MA","MZ","MM",
      "NA","NR","NP","NL","NC","NZ","NI","NE","NG","NU","NF","MK","MP","NO",
      "OM",
      "PK","PW","PS","PA","PG","PY","PE","PH","PN","PL","PT","PR",
      "QA",
      "RE","RO","RU","RW",
      "BL","SH","KN","LC","MF","PM","VC","WS","SM","ST","SA","SN","RS","SC","SL","SG","SX","SK","SI","SB","SO","ZA","GS","SS","ES","LK","SD","SR","SJ","SE","CH","SY",
      "TW","TJ","TZ","TH","TL","TG","TK","TO","TT","TN","TR","TM","TC","TV",
      "UG","UA","AE","GB","US","UM","UY","UZ",
      "VU","VE","VN","VG","VI",
      "WF","EH",
      "YE",
      "ZM","ZW"
      };

    // EU - 27
    private static readonly HashSet<string> _eu = new(StringComparer.OrdinalIgnoreCase)
    {
      "AT", // Austria
      "BE", // Belgium
      "BG", // Bulgaria
      "CY", // Cyprus
      "CZ", // Czech Republic
      "DE", // Germany
      "DK", // Denmark
      "EE", // Estonia
      "ES", // Spain
      "FI", // Finland
      "FR", // France
      "GR", // Greece
      "HR", // Croatia
      "HU", // Hungary
      "IE", // Ireland
      "IT", // Italy
      "LT", // Lithuania
      "LU", // Luxembourg
      "LV", // Latvia
      "MT", // Malta
      "NL", // Netherlands
      "PL", // Poland
      "PT", // Portugal
      "RO", // Romania
      "SE", // Sweden
      "SI", // Slovenia
      "SK"  // Slovakia
    };

    // EEA = EU + (NO, IS, LI)
    private static readonly HashSet<string> _eea =
      new(StringComparer.OrdinalIgnoreCase)
      {
      "AT","BE","BG","HR","CY","CZ","DK","EE","FI","FR","DE","GR","HU","IE","IT",
      "LV","LT","LU","MT","NL","PL","PT","RO","SK","SI","ES","SE",
      "NO","IS","LI"
      };


    public static CountryContext Classify(string? rawCode)
    {
      if (string.IsNullOrWhiteSpace(rawCode))
      {
        return new CountryContext(null, false, false, false, false);
      }

      var normalized = rawCode.Trim().ToUpperInvariant();

      /*
       * -  
       */
      if (normalized.Length != 2 ||
        !char.IsLetter(normalized[0]) ||
        !char.IsLetter(normalized[1]) ||
        !_isoAlpha2.Contains(normalized)) // - 
      {
        return new CountryContext(null, false, false, false, false);
      }

      
      var isEu = _eu.Contains(normalized);
      var isEea = _eea.Contains(normalized);
      var isNonEu = !isEu;

      return new CountryContext(
        normalized,
        true,
        isEu,
        isEea,
        isNonEu
      );
    }



  }

  public static class StripeEventTypes
  {
    public const string PaymentIntentSucceeded = "payment_intent.succeeded";
    public const string CheckoutSessionCompleted = "checkout.session.completed";
    public const string ChargeSucceeded = "charge.succeeded";
    public const string ChargeUpdated = "charge.updated";
  }

  public static class ProviderNames
  {
    public const string Stripe = "stripe";
  }

  public enum ProviderKind
  {
    Stripe = 1
  }
  public enum ProviderMode
  {
    Test = 1,
    Live = 2
  }
  public enum WorkspaceRole
  {
    Owner = 1,
    Member = 2
  }
  public enum EventProcessingStatus
  {
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
