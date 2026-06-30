# Azure OpenAI Deployments — PoSeeReview

> **Read this before changing `AzureOpenAI:DeploymentName` in `kv-poshared`,
> the `PoSeeReview--AzureOpenAI--DeploymentName` app-prefixed mirror, or
> `StartupSecretValidator.KnownGoodDeployments`.**

## TL;DR

- The app calls **one** model today: **`gpt-5.4-nano`**.
- The KV secret **`PoSeeReview--AzureOpenAI--DeploymentName`** is an
  app-prefixed mirror of the shared `AzureOpenAI--DeploymentName`. The
  prefix pass wins. **Both** must agree.
- "**nana**" (slang) = `gpt-5.4-nano`. If you see this in a chat or
  commit message, that's what it means.

## Where the model lives

| What | Value |
|---|---|
| Azure subscription | the same one that hosts `PoShared` RG |
| Resource group | `PoShared` |
| Cognitive Services account | `po-aiservices-shared` |
| Region | East US |
| Deployment name | `gpt-5.4-nano` |
| Model | `gpt-5.4-nano`, version `2026-03-17` |
| SKU | `GlobalStandard`, capacity `1` |
| State | `Running` / `provisioningState: Succeeded` |
| Last verified | 2026-06-14 |

## Verify the live deployment list

```bash
# 1. Confirm the deployment exists and is healthy
az cognitiveservices account deployment list \
  --resource-group PoShared \
  --name po-aiservices-shared \
  --query "[].{name:name, state:properties.deploymentState, model:properties.model.name, version:properties.model.version, sku:sku.name, capacity:sku.capacity}" \
  -o tsv
```

Expected output: a single row for `gpt-5.4-nano` with `state=Running` and `capacity=1`.

```bash
# 2. Confirm the KV secrets are in sync
az keyvault secret show --vault-name kv-poshared --name AzureOpenAI--DeploymentName             --query "value" -o tsv
az keyvault secret show --vault-name kv-poshared --name PoSeeReview--AzureOpenAI--DeploymentName --query "value" -o tsv
```

Both commands should print `gpt-5.4-nano`. If they differ, the prefix-pass
load picks the app-prefixed one. Fix the one that's wrong; the app
will pick up the new value on next launch.

```bash
# 3. Confirm what the running API is actually using
curl -k https://localhost:5001/api/diag | jq '.config[] | select(.key | startswith("AzureOpenAI"))'
```

`AzureOpenAI:DeploymentName` should be `gpt-5.4-nano`.

## When to update this file

- A new deployment is provisioned in `po-aiservices-shared` for this app.
- The deployment is renamed or deleted.
- A different model is added as a fallback (e.g. a `gpt-4o-mini` mirror
  for cost reasons).
- The Cognitive Services account or resource group is renamed/moved.

When you update the deployment list, also update:

1. `kv-poshared` → `AzureOpenAI--DeploymentName` (shared)
2. `kv-poshared` → `PoSeeReview--AzureOpenAI--DeploymentName` (app-prefixed)
3. `infra/modules/secrets.bicep` → the hard-coded `value: 'gpt-5.4-nano'`
   (or replace with a parameter fed from `main.parameters.json`)
4. `StartupSecretValidator.KnownGoodDeployments` — add the new name
   **before** rotating KV, so the validator's prod throw doesn't fire
   during the transition
5. This file — record the verification command output + date

## What NOT to do

- ❌ Don't set the deployment name to `gpt-4o` (it does not exist in
  `po-aiservices-shared`; the previous stale value was the 2026-06-15
  incident — see `posereview-config-and-fixes-2026-06-15.md`).
- ❌ Don't set it to `gpt-4o-mini` for the same reason.
- ❌ Don't bypass the app-prefixed secret by setting the dev launch
  env-var `$env:AzureOpenAI__DeploymentName` in a release. The env var
  shadows KV and the production deploy will be misconfigured.
- ❌ Don't add a deployment to `KnownGoodDeployments` without verifying
  it's actually in the resource.

## Cross-references

- Validator source: `src/PoSeeReview.Api/HostedServices/StartupSecretValidator.cs`
  → `KnownGoodDeployments` HashSet
- Bicep module: `infra/modules/secrets.bicep` → `azureOpenAIDeploymentName`
- Session memory: `.github/copilot/memories/repo/posereview-config-and-fixes-2026-06-15.md`
  → "Deployment-name drift (fixed 2026-06-15)" section
