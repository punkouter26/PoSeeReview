# Build stage
# Pinned to a specific minor.patch tag. Bare :10.0 drifts unpredictably
# between rebuilds (a new patch release would be silently picked up). Bump
# via Dependabot (.github/dependabot.yml → docker ecosystem).
FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble AS build
WORKDIR /src

# Copy solution and project files for better layer caching
COPY ["Directory.Packages.props", "./"]
COPY ["global.json", "./"]
COPY ["src/PoSeeReview.Api/PoSeeReview.Api.csproj", "src/PoSeeReview.Api/"]
COPY ["src/PoSeeReview.Core/PoSeeReview.Core.csproj", "src/PoSeeReview.Core/"]
COPY ["src/PoSeeReview.Infrastructure/PoSeeReview.Infrastructure.csproj", "src/PoSeeReview.Infrastructure/"]
COPY ["src/PoSeeReview.Client/PoSeeReview.Client.csproj", "src/PoSeeReview.Client/"]
COPY ["src/PoSeeReview.Shared/PoSeeReview.Shared.csproj", "src/PoSeeReview.Shared/"]

# Restore dependencies
RUN dotnet restore "src/PoSeeReview.Api/PoSeeReview.Api.csproj"

# Copy source code
COPY ["src/", "src/"]

# Build and publish
WORKDIR "/src/src/PoSeeReview.Api"
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0.6-noble-chiseled AS runtime
WORKDIR /app

# Install fonts and fontconfig for comic text overlay (run as root before switching to app user)
USER root
RUN apt-get update && apt-get install -y --no-install-recommends \
    fontconfig \
    fonts-liberation \
    fonts-dejavu-core \
    && fc-cache -f -v \
    && rm -rf /var/lib/apt/lists/*

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
    CMD curl --fail http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "PoSeeReview.Api.dll"]
