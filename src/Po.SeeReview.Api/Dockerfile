# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for better layer caching
COPY ["Directory.Packages.props", "./"]
COPY ["global.json", "./"]
COPY ["src/Po.SeeReview.Api/Po.SeeReview.Api.csproj", "src/Po.SeeReview.Api/"]
COPY ["src/Po.SeeReview.Core/Po.SeeReview.Core.csproj", "src/Po.SeeReview.Core/"]
COPY ["src/Po.SeeReview.Infrastructure/Po.SeeReview.Infrastructure.csproj", "src/Po.SeeReview.Infrastructure/"]
COPY ["src/Po.SeeReview.Client/Po.SeeReview.Client.csproj", "src/Po.SeeReview.Client/"]
COPY ["src/Po.SeeReview.Shared/Po.SeeReview.Shared.csproj", "src/Po.SeeReview.Shared/"]
COPY ["src/PoSeeReview.ServiceDefaults/PoSeeReview.ServiceDefaults.csproj", "src/PoSeeReview.ServiceDefaults/"]

# Restore dependencies
RUN dotnet restore "src/Po.SeeReview.Api/Po.SeeReview.Api.csproj"

# Copy source code
COPY ["src/", "src/"]

# Build and publish
WORKDIR "/src/src/Po.SeeReview.Api"
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Security: Run as non-root user (use existing app user in .NET 10 images)
USER app

# Copy published app
COPY --from=build /app/publish .

# Expose port (Azure Container Apps will use this)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check for Azure Container Apps
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Po.SeeReview.Api.dll"]
