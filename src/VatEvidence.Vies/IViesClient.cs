namespace VatEvidence.Vies;

/// <summary>
/// Abstraction for the EU VIES (VAT Information Exchange System) check.
/// Register the implementation via DI: services.AddViesClient()
/// </summary>
public interface IViesClient
{
  /// <summary>
  /// Checks whether a VAT number is currently active in the EU VIES system.
  /// </summary>
  /// <param name="countryCode">ISO 3166-1 alpha-2 prefix, e.g. "HR", "DE".</param>
  /// <param name="vatNumber">VAT number WITHOUT the country prefix, e.g. "12345678901".</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  Task<ViesResult> CheckAsync(
      string countryCode,
      string vatNumber,
      CancellationToken cancellationToken = default);
}