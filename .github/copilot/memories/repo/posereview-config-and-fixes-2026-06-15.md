# PoSeeReview — Config / KV / Dev-loop Notes (2026-06-15)

## Google Maps key flow (fixed 2026-06-15)

### What was wrong
- `kv-poshared` has `GoogleMaps--ApiKey` (shared, unprefixed) — the
  `SharedKeyVaultSecretManager` first pass maps it to `GoogleMaps:ApiKey`.
- The local dev launch command was setting
  `$env:GoogleMaps__ApiKey = "dev-placeholder-google-maps"`. ASP.NET Core
  loads env vars *before* the Azure Key Vault configuration provider, so
  the env var shadowed the real key, Google returned REQUEST_DENIED, and
  `RestaurantsController.GetNearbyRestaurants` returned 503 →
  "Restaurant search is temporarily unavailable" on the Blazor page.
- `StartupSecretValidator` was logging a warning in Dev but never failing
  fast, so this was a silent failure.

### What changed
- Mirrored the key to `kv-poshared` → `PoSeeReview--GoogleMaps--ApiKey`
  (39 chars, starts `AIzaSy…`). The `PrefixKeyVaultSecretManager` second
  pass loads this and the app-prefixed value wins.
- Flipped `StartupSecretValidator` so **`GoogleMaps:ApiKey` is fail-fast
  in every environment** (Dev, Test, Prod). AI keys still follow the
  PoFunQuiz "warn-Dev, throw-Prod" pattern.
  - **Side effect:** local dev now refuses to boot if the key is missing
    or if the dev box can't reach `kv-poshared`. That's the point, but
    document it before someone is surprised on a plane.
- Bicep: added `infra/main.bicep` top-level param
  `poSeeReviewGoogleMapsApiKey` (secure, default `''`) and a conditional
  `Microsoft.KeyVault/vaults/secrets` resource in `secrets.bicep` for
  `PoSeeReview--GoogleMaps--ApiKey`. Set via
  `azd env set poSeeReviewGoogleMapsApiKey <key>` then `azd provision`.
  Compiles clean on Bicep CLI 0.43.8 (no `readEnvironmentVariable` —
  that was renamed `readCliEnv` and is version-fragile).

### Pattern for future Po-apps
1. **Always also write the app-prefixed mirror to KV** when a secret is
   shared. The two-pass load (shared → app-specific) means the app
   owns the value, and `PrefixKeyVaultSecretManager` is the explicit
   "this app's own secret" boundary.
2. **Fail-fast on keys that make the page unusable.** A "warn only" prod
   policy is fine for AI/Map features *as long as* the page renders
   meaningful copy when they're missing. For PoSeeReview, no Google key
   = page is broken, so the validator must throw.
3. **Never set `$env:GoogleMaps__ApiKey` in the dev launch command.**
   KV is the source of truth, and the env-var provider loads earlier.
   If you need a one-off override, use `dotnet user-secrets set` instead.

## Open follow-ups (out of scope 2026-06-15 minimum change)
- `ExpiredComicCleanupService` is *not* resilient to Azurite outages.
  On 2026-06-15 it took the whole host down with an unhandled exception
  inside `ExecuteAsync` when Azurite was unreachable on 10002. Wrap each
  tick in try/catch + log Error, never bubble. (User declined this in
  the 2026-06-15 "minimum scope" decision — flag for next pass.)
- `IsDevPlaceholderKey()` shortcut in `RestaurantsController` is dead
  code now that the validator refuses to start with a placeholder key.
  Could be removed in a separate cleanup PR.
- `curl` on Windows rejects `https://localhost:5001` as "Bad hostname"
  (URL-malware filter). Use `Invoke-WebRequest -SkipCertificateCheck`
  in PowerShell scripts instead. Document in README or SCRIPTS/.

## Deployment-name drift (fixed 2026-06-15)

### What was wrong
- Shared KV secret `AzureOpenAI--DeploymentName` = `gpt-5.4-nano`
  (the only real deployment in `po-aiservices-shared`, verified
  2026-06-14 against `az cognitiveservices account deployment list`).
- App-prefixed mirror `PoSeeReview--AzureOpenAI--DeploymentName` =
  `gpt-4o` (set 2026-05-02, single version, enabled). The `gpt-4o`
  model does NOT exist in the resource — never has.
- The `PrefixKeyVaultSecretManager` second pass loads the app-prefixed
  value and the **prefix pass wins**, so the live API resolved
  `AzureOpenAI:DeploymentName = gpt-4o` regardless of the shared value.
  `StartupSecretValidator` was happy because the drift guard only ran
  in non-Dev environments — and only checked for the SHARED secret.
- Symptom: every comic generation returned
  `HTTP 404 invalid_request_error: DeploymentNotFound`.

### What changed
- Updated `kv-poshared` → `PoSeeReview--AzureOpenAI--DeploymentName`
  to `gpt-5.4-nano` (mirroring the shared value).
- Validator strengthened: see `StartupSecretValidator.cs` — drift guard
  now logs the actual KV-resolved value (warn in Dev, throw in Prod)
  instead of only checking the constant `ExpectedDeploymentName`.
  Caught the regression on the next run because we now log the live
  value, not just compare to a hard-coded string.

### Pattern for future Po-apps
1. **When you rename / repoint an Azure OpenAI deployment in the
   shared resource (`po-aiservices-shared` or similar), you MUST
   update BOTH KV secrets** — the shared one (`AzureOpenAI--DeploymentName`)
   and the per-app one (`PoAppName--AzureOpenAI--DeploymentName`).
   The per-app one wins by design; if you only update the shared one,
   the prefix pass still serves the old value.
2. **The prefix-pass override is silent.** No warning, no log. The only
   way to detect drift is to compare the LIVE config (via `/api/diag`
   or a custom health probe) against the actual `az cognitiveservices
   account deployment list` output. Build this check into your startup
   validator.
3. **Always check `az keyvault secret list --vault-name <vault>`** when
   a value "should be" X but reads as Y. The repo can be clean and
   the env can be clean and KV can still have a stale app-prefixed
   value no one remembers creating.
4. **"nana" = `gpt-5.4-nano`**, the only deployment in
   `po-aiservices-shared` (PoShared RG, East US). If you see a config
   value with `gpt-4o` or `gpt-4o-mini` or `gpt-5` for any Po-app
   pointing at `po-aiservices-shared.cognitiveservices.azure.com`,
   it's a stale override. Fix the per-app KV secret.

## Local launch reference (what works)
```powershell
Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
docker start azurite 2>$null; if ($LASTEXITCODE -ne 0) { docker compose up -d azurite }

$env:AZURE_TABLE_STORAGE_CONNECTION_STRING = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8...;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;"
$env:AZURE_BLOB_STORAGE_CONNECTION_STRING  = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8...;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;"
$env:AzureOpenAI__Endpoint       = "https://example.openai.azure.com/"
$env:AzureOpenAI__ApiKey         = "dev-placeholder"
$env:AzureOpenAI__DeploymentName = "gpt-4o-mini"
$env:Google__GeminiApiKey        = "dev-placeholder-gemini"
$env:ASPNETCORE_ENVIRONMENT      = "Development"
# Deliberately NOT setting $env:GoogleMaps__ApiKey — KV supplies it.

dotnet run --project src/Po.SeeReview.Api --launch-profile https --no-build
```

`/health/ready` should return all three probes `Healthy`:
- `azure_table_storage` (10002)
- `azure_blob_storage`  (10000)
- `google_maps_api`     (must reach `maps.googleapis.com` with a valid key)
