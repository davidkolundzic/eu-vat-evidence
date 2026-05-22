using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using VatEvidence.Application.Evidence;
using VatEvidence.Application.Interfaces;
using VatEvidence.Core.Validation;
using VatEvidence.Infrastructure.Persistence;
using VatEvidence.Vies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
  options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
         .UseSnakeCaseNamingConvention();
});

builder.Services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<IEvidenceHashService, EvidenceHashService>();
builder.Services.AddScoped<IEvidenceChainVerifier, EvidenceChainVerifier>();
builder.Services.AddScoped<IEvidenceAppendService, EvidenceAppendService>();

builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddHttpClient<IViesClient, ViesClient>();
//builder.Services.AddEndpointsApiExplorer(); // For Swagger/OpenAPI documentation (optional)
//builder.Services.AddSwaggerGen(); // For Swagger/OpenAPI documentation (optional)

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
  using (var scope = app.Services.CreateScope())
  {
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
      dbContext.Database.Migrate();
      app.Logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
      app.Logger.LogError(ex, "Error applying database migrations.");
      throw;
    }
  }
}

if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Error");
  app.UseHsts();
}

app.UseHttpsRedirection();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
  ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapControllers();

//  --- Step 1: Format-only validation ---
app.MapGet("/api/vat/validate/{vatNumber}", (string vatNumber) =>
{

  var result = VatNumberValidator.Validate(vatNumber);

  return result.IsValid ? Results.Ok(result) : Results.UnprocessableEntity(result);
})
  .WithName("ValidateVatNumber")
  .WithSummary("No‑network validation");

// ── Step 2: format + VIES check ──────────────────────────────────────────
app.MapGet("/api/vat/check/{vatNumber}", async (
    string vatNumber,
    IViesClient viesClient,
    CancellationToken ct) =>
{
  var fmt = VatNumberValidator.Validate(vatNumber);
  if (!fmt.IsValid)
    return Results.UnprocessableEntity(fmt);

  var vies = await viesClient.CheckAsync(
      fmt.CountryCode!,
      fmt.NormalizedVat![2..],
      ct);

  return Results.Ok(new
  {
    Format = fmt,
    Vies = vies
  });
})
.WithName("CheckVat")
.WithSummary("Format validacija + VIES aktivan status");

app.Run();