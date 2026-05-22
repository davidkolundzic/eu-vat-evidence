using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace VatEvidence.Vies;

/// <summary>
/// HTTP client for the EU VIES REST API.
/// Register via: services.AddHttpClient&lt;IViesClient, ViesClient&gt;()
/// </summary>
public sealed class ViesClient : IViesClient
{
  private readonly HttpClient _http;

  // EU Commission VIES REST API (available since 2022)
  private const string BaseUrl =
      "https://ec.europa.eu/taxation_customs/vies/rest-api/ms/{0}/vat/{1}";

  public ViesClient(HttpClient http)
  {
    _http = http;
    _http.Timeout = TimeSpan.FromSeconds(10);
  }

  /// <inheritdoc />
  public async Task<ViesResult> CheckAsync(
      string countryCode,
      string vatNumber,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
    ArgumentException.ThrowIfNullOrWhiteSpace(vatNumber);

    var url = string.Format(BaseUrl, countryCode.ToUpperInvariant(), vatNumber);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    try
    {
      var response = await _http.GetAsync(url, cancellationToken);

      if (!response.IsSuccessStatusCode)
        return ViesResult.Error(countryCode, vatNumber,
            $"VIES returned HTTP {(int)response.StatusCode}.");

      var dto = await response.Content.ReadFromJsonAsync<ViesResponseDto>(
          cancellationToken: cancellationToken);

      if (dto is null)
        return ViesResult.Error(countryCode, vatNumber, "VIES returned an empty response.");

      if (!dto.IsValid)
        return ViesResult.Inactive(countryCode, vatNumber, today);

      return ViesResult.Active(
          countryCode,
          vatNumber,
          dto.Name,
          dto.Address,
          today);
    }
    catch (TaskCanceledException)
    {
      return ViesResult.Error(countryCode, vatNumber, "VIES request timed out.");
    }
    catch (HttpRequestException ex)
    {
      return ViesResult.Error(countryCode, vatNumber, $"VIES network error: {ex.Message}");
    }
  }

  // -------------------------------------------------------------------------
  // Internal DTO — matches VIES REST API JSON response
  // -------------------------------------------------------------------------

  private sealed class ViesResponseDto
  {
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("requestDate")]
    public string? RequestDate { get; init; }

    [JsonPropertyName("userError")]
    public string? UserError { get; init; }
  }
}