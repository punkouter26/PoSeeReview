# Po.SeeReview.WebTests

## Purpose

These are **in-memory API tests** that use a `WebApplicationFactory` with **fake service implementations** (no real Azure dependencies required). They run without Azurite, Azure Storage, or any network access.

**Use this project for:**
- Fast feedback on HTTP routing, request/response serialization, and controller logic
- Testing error handling paths where real storage would be awkward to configure
- CI checks that must run without infrastructure

## How it differs from `Po.SeeReview.IntegrationTests`

| | WebTests (this project) | IntegrationTests |
|---|---|---|
| Storage | In-memory fakes | Real Azurite emulator |
| Azure dependencies | None | Docker (Azurite) required |
| Speed | Fast | Slower |
| Scope | Controller + serialization | Full stack including repositories |
| Requires Docker | ❌ | ✅ |

## Where to add new tests

- **New API contract tests** (status codes, response shape, routing) → add here
- **Tests requiring real Table/Blob Storage** → add to `Po.SeeReview.IntegrationTests`

## Running

```bash
dotnet test tests/Po.SeeReview.WebTests
```
