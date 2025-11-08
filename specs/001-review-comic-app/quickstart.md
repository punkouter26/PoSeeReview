# Quickstart Guide

**Feature**: Restaurant Review Comic Generator  
**Branch**: 001-review-comic-app  
**Last Updated**: 2025-01-22

## Overview

This guide helps you set up the PoSeeReview development environment on your local machine. After completion, you'll have:
- ✅ .NET 9.0 SDK installed
- ✅ Azurite local storage emulator running
- ✅ Azure OpenAI and Google Maps API keys configured
- ✅ API running on http://localhost:5000 (HTTPS: 5001)
- ✅ Blazor WASM client accessible in browser
- ✅ Swagger UI available at http://localhost:5000/swagger

**Estimated Time**: 15 minutes

---

## Prerequisites

### Required Software
1. **Windows 10/11**, **macOS 11+**, or **Linux** (Ubuntu 20.04+)
2. **PowerShell 7+** (for script execution)
   - Windows: Installed by default
   - macOS/Linux: `brew install powershell` or download from [Microsoft](https://aka.ms/powershell)
3. **Git** 2.30+ (for cloning repository)
4. **.NET 9.0 SDK** (see installation below)
5. **Node.js 18+** (for Azurite)
   - Download from [nodejs.org](https://nodejs.org/)

### Required Services (Free Tier)
1. **Azure Account** (for Azure OpenAI and Storage)
   - Sign up at [azure.microsoft.com/free](https://azure.microsoft.com/free)
2. **Google Cloud Account** (for Maps API)
   - Sign up at [cloud.google.com](https://cloud.google.com)

---

## Step 1: Install .NET 9.0 SDK

### Verify Current Version
```powershell
dotnet --version
```

**Expected Output**: `9.0.xxx` (where xxx is the latest patch version)

### Install/Update .NET 9.0

**Windows (PowerShell)**:
```powershell
winget install Microsoft.DotNet.SDK.9
```

**macOS (Homebrew)**:
```bash
brew install dotnet@9
```

**Linux (Ubuntu/Debian)**:
```bash
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 9.0
```

**Manual Download**: Visit [dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0)

### Constitution Compliance Check
The constitution **REQUIRES** .NET 9.0 SDK (not .NET 8.0 or earlier). If `dotnet --version` shows anything other than `9.0.x`, the build will fail with:
```
error MSB4236: The SDK 'Microsoft.NET.Sdk.Web' specified could not be found.
```

---

## Step 2: Clone Repository & Navigate to Project

```powershell
git clone https://github.com/your-org/PoSeeReview.git
cd PoSeeReview
git checkout 001-review-comic-app
```

**Verify Branch**:
```powershell
git branch --show-current
```
Expected output: `001-review-comic-app`

---

## Step 3: Install Azurite (Azure Storage Emulator)

Azurite provides local emulation of Azure Table Storage and Blob Storage.

### Install Globally via npm
```powershell
npm install -g azurite
```

### Verify Installation
```powershell
azurite --version
```
Expected output: `3.x.x`

### Start Azurite
Run the provided setup script:
```powershell
.\scripts\setup-azurite.ps1
```

**Or manually**:
```powershell
azurite --location c:\azurite --silent
```

**Verify Azurite is Running**:
- Table Storage: `http://127.0.0.1:10002`
- Blob Storage: `http://127.0.0.1:10000`

**Troubleshooting**:
- Port conflict? Stop IIS or other services using port 10000/10002
- Permission error? Run PowerShell as Administrator

---

## Step 4: Configure API Keys

### 4.1 Azure OpenAI Setup

1. **Create Azure OpenAI Resource**:
   - Go to [Azure Portal](https://portal.azure.com)
   - Search "Azure OpenAI" → Create
   - Region: East US (has DALL-E 3 availability)
   - Pricing tier: Standard S0

2. **Deploy Models**:
   - In Azure OpenAI Studio, go to **Deployments**
   - Deploy **gpt-4o-mini** (name it `gpt-4o-mini`)
   - Deploy **dall-e-3** (name it `dall-e-3`)

3. **Get API Keys**:
   - Navigate to **Keys and Endpoint** in Azure Portal
   - Copy **Key 1** and **Endpoint** URL

### 4.2 Google Maps API Setup

1. **Enable APIs**:
   - Go to [Google Cloud Console](https://console.cloud.google.com)
   - Create new project (e.g., "PoSeeReview")
   - Enable **Places API (New)**
   - Enable **Geocoding API**

2. **Create API Key**:
   - Go to **Credentials** → Create Credentials → API Key
   - Copy the API key

3. **Restrict Key** (recommended):
   - Click "Restrict Key"
   - API restrictions: Select "Places API (New)" and "Geocoding API"
   - Application restrictions: HTTP referrers (add `http://localhost:5000/*`)

### 4.3 Update appsettings.Development.json

Edit `src/Po.SeeReview.Api/appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AzureStorage": {
    "ConnectionString": "UseDevelopmentStorage=true",
    "BlobContainerName": "comics",
    "RestaurantTableName": "PoSeeReviewRestaurants",
    "ComicTableName": "PoSeeReviewComics",
    "LeaderboardTableName": "PoSeeReviewLeaderboard"
  },
  "AzureOpenAI": {
    "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
    "ApiKey": "YOUR-AZURE-OPENAI-KEY",
    "GptDeploymentName": "gpt-4o-mini",
    "DalleDeploymentName": "dall-e-3"
  },
  "GoogleMaps": {
    "ApiKey": "YOUR-GOOGLE-MAPS-KEY"
  },
  "ApplicationInsights": {
    "ConnectionString": ""
  }
}
```

**⚠️ IMPORTANT**: Never commit API keys to Git. Add `appsettings.Development.json` to `.gitignore`.

---

## Step 5: Restore Dependencies & Build

```powershell
# From repository root
dotnet restore
dotnet build
```

**Expected Output**:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Common Errors**:
- `error NU1101: Unable to find package`: Run `dotnet restore` again
- `error MSB4236: SDK not found`: Install .NET 9.0 SDK (see Step 1)

---

## Step 6: Create Database Tables in Azurite

Run the database initialization script:

```powershell
dotnet run --project src/Po.SeeReview.Api -- --init-db
```

**Or manually** using Azure Storage Explorer:
1. Download [Azure Storage Explorer](https://azure.microsoft.com/features/storage-explorer/)
2. Connect to local storage (Azurite)
3. Create tables:
   - `PoSeeReviewRestaurants`
   - `PoSeeReviewComics`
   - `PoSeeReviewLeaderboard`
4. Create blob container: `comics` (public access: Blob)

---

## Step 7: Run the Application

### Option A: Run API + Blazor Together (Recommended)

```powershell
dotnet run --project src/Po.SeeReview.Api
```

**Expected Output**:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

**Open Browser**:
- **Blazor WASM App**: https://localhost:5001
- **Swagger UI**: https://localhost:5001/swagger
- **Health Check**: https://localhost:5001/api/health

### Option B: Run API and Client Separately (Advanced)

**Terminal 1 (API)**:
```powershell
dotnet run --project src/Po.SeeReview.Api --no-launch-profile
```

**Terminal 2 (Blazor Client)**:
```powershell
dotnet run --project src/Po.SeeReview.Client
```

---

## Step 8: Verify Installation

### 8.1 Health Check
```powershell
curl https://localhost:5001/api/health
```

**Expected Response**:
```json
{
  "status": "healthy",
  "version": "1.0.0",
  "dependencies": {
    "azureTableStorage": "ok",
    "azureBlobStorage": "ok",
    "azureOpenAI": "ok",
    "googleMapsAPI": "ok"
  }
}
```

### 8.2 Test Nearby Restaurants API
```powershell
curl "https://localhost:5001/api/restaurants/nearby?latitude=47.6062&longitude=-122.3321"
```

**Expected Response**: JSON array of restaurants in Seattle area

### 8.3 Test Blazor UI
1. Navigate to https://localhost:5001
2. Allow browser geolocation when prompted
3. Verify restaurant cards appear
4. Click "Generate Comic" on any restaurant
5. Wait 8-10 seconds for DALL-E generation
6. Verify comic displays in modal

---

## Step 9: Run Tests

### Unit Tests
```powershell
dotnet test tests/Po.SeeReview.UnitTests
```

### Integration Tests (requires Azurite running)
```powershell
dotnet test tests/Po.SeeReview.IntegrationTests
```

**Expected Output**:
```
Passed!  - Failed:     0, Passed:    42, Skipped:     0, Total:    42
```

### Manual E2E Tests (Playwright)
```powershell
# Install Playwright browsers (first time only)
npx playwright install

# Run tests manually (not in CI)
npx playwright test
```

---

## Troubleshooting

### Issue: "DALL-E API returned 401 Unauthorized"
**Solution**: Verify Azure OpenAI API key in `appsettings.Development.json`

### Issue: "Google Maps API returned 403 Forbidden"
**Solution**: Check API key restrictions - ensure `localhost:5000` is allowed

### Issue: "Table 'PoSeeReviewRestaurants' not found"
**Solution**: Run database initialization (Step 6)

### Issue: Blazor app shows blank page
**Solution**: Open browser console (F12) → check for CORS errors or missing API calls

### Issue: Port 5000/5001 already in use
**Solution**: 
```powershell
# Windows: Find process using port
netstat -ano | findstr :5000

# Kill process
taskkill /PID <PID> /F
```

### Issue: Azurite won't start
**Solution**:
```powershell
# Clear Azurite data and restart
Remove-Item -Recurse -Force c:\azurite
azurite --location c:\azurite --silent
```

---

## Next Steps

### Development Workflow
1. **Make code changes** in `src/` directory
2. **Hot reload**: Changes auto-reload in browser (Blazor WASM)
3. **Run tests**: `dotnet test` after each change (TDD)
4. **Commit**: `git add . && git commit -m "feat: your change"`

### Key Files to Edit
- **Blazor UI**: `src/Po.SeeReview.Client/Pages/Index.razor`
- **API Controllers**: `src/Po.SeeReview.Api/Controllers/`
- **Business Logic**: `src/Po.SeeReview.Core/Services/`
- **Infrastructure**: `src/Po.SeeReview.Infrastructure/Services/`

### Useful Commands
```powershell
# Watch tests (re-run on file changes)
dotnet watch test --project tests/Po.SeeReview.UnitTests

# Run API with hot reload
dotnet watch run --project src/Po.SeeReview.Api

# Format code
dotnet format

# View logs in real-time
tail -f logs/poseereview-*.log
```

### Documentation
- **Data Model**: See `specs/001-review-comic-app/data-model.md`
- **API Contracts**: See `specs/001-review-comic-app/contracts/openapi.yaml`
- **Research Decisions**: See `specs/001-review-comic-app/research.md`
- **Swagger UI**: https://localhost:5001/swagger

---

## Production Deployment (Future)

This quickstart is for **local development only**. For Azure production deployment:
1. Create Azure App Service (Windows, .NET 9.0 runtime)
2. Create Azure Storage Account (replace Azurite)
3. Update `appsettings.Production.json` with production connection strings
4. Deploy via Azure DevOps or GitHub Actions (CI/CD pipeline)

See `docs/deployment.md` (to be created in Phase 2) for details.

---

## Support

- **Issues**: [GitHub Issues](https://github.com/your-org/PoSeeReview/issues)
- **Constitution**: `.specify/memory/constitution.md`
- **Feature Spec**: `specs/001-review-comic-app/spec.md`

**Happy coding! 🚀**
