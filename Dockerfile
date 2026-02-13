# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["VatEvidence.Web/VatEvidence.Web.csproj", "VatEvidence.Web/"]
COPY ["VatEvidence.Application/VatEvidence.Application.csproj", "VatEvidence.Application/"]
COPY ["VatEvidence.Domain/VatEvidence.Domain.csproj", "VatEvidence.Domain/"]
COPY ["VatEvidence.Infrastructure/VatEvidence.Infrastructure.csproj", "VatEvidence.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "VatEvidence.Web/VatEvidence.Web.csproj"

# Copy everything else
COPY . .

# Build
WORKDIR "/src/VatEvidence.Web"
RUN dotnet build "VatEvidence.Web.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "VatEvidence.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published app
COPY --from=publish /app/publish .

# Change ownership
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose port (Render uses PORT env variable)
EXPOSE 8080

# Set ASP.NET Core to listen on port from environment variable
ENV ASPNETCORE_URLS=http://+:8080

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "VatEvidence.Web.dll"]
