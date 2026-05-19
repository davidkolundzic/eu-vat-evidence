using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using VatEvidence.Application.Evidence;
using VatEvidence.Application.Interfaces;
using VatEvidence.Infrastructure.Persistence;

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

app.Run();