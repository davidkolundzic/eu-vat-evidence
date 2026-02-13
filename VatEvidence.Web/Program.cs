using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VatEvidence.Application.Evidence;
using VatEvidence.Application.Interfaces;
using VatEvidence.Application.Webhooks;
using VatEvidence.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
           .UseSnakeCaseNamingConvention();  // All tables/columns will be snake_case
});

builder.Services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<IEvidenceHashService, EvidenceHashService>();
builder.Services.AddScoped<IEvidenceChainVerifier, EvidenceChainVerifier>();
builder.Services.AddScoped<IEvidenceAppendService, EvidenceAppendService>();

builder.Services.AddScoped<IStripeSignatureValidator, StripeSignatureValidator>();
builder.Services.AddScoped<IWebhookProcessor, StripeWebhookProcessor>();

// Razor + Auth (pretpostavka da vec dodajes)


// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers(); // Enable API controllers for webhooks

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("webhook", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 100; // 100 webhook-ova po minuti
        opt.QueueLimit = 0;
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRouting();

app.UseRateLimiter();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapControllers(); // Map API controllers for webhooks

app.Run();
