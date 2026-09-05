using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Identity;
using PoSeeReview.Api.Storage;
using PoSeeReview.Api.Telemetry;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Table-backed daily generation budget.
/// <para>
/// Two rows are charged per generation: the principal's, and the app-wide
/// <see cref="GenerationBudgetEntity.ServiceRowKey"/> row. They are separate entities in the
/// same partition, so this is two optimistic-concurrency updates rather than one transaction —
/// acceptable because the failure mode of a torn pair is one unit of drift on a daily counter,
/// not a wrong answer to a user.
/// </para>
/// </summary>
public sealed class GenerationBudgetService(
    TableServiceClient tableServiceClient,
    IOptions<AzureStorageOptions> storageOptions,
    IOptions<GenerationBudgetOptions> budgetOptions,
    ICurrentRequestIdentityAccessor identityAccessor,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    ILogger<GenerationBudgetService> logger) : IGenerationBudgetService
{
    private readonly TableClient _table =
        tableServiceClient.GetTableClient(storageOptions.Value.BudgetTableName);

    private readonly GenerationBudgetOptions _options = budgetOptions.Value;

    public async Task<GenerationBudgetDto> GetBudgetAsync(CancellationToken cancellationToken = default)
    {
        var day = CurrentDay();
        var partitionKey = GenerationBudgetEntity.PartitionKeyFor(day);

        var used = await ReadCountAsync(partitionKey, PrincipalRowKey(), cancellationToken);
        var serviceUsed = await ReadCountAsync(partitionKey, GenerationBudgetEntity.ServiceRowKey, cancellationToken);

        return Describe(used, serviceUsed, day);
    }

    public async Task<BudgetReservation> TryReserveAsync(CancellationToken cancellationToken = default)
    {
        var day = CurrentDay();
        var partitionKey = GenerationBudgetEntity.PartitionKeyFor(day);
        var principalRowKey = PrincipalRowKey();

        if (!_options.Enabled)
        {
            // Still counted, so /diag reports real usage in every environment — enforcement is
            // the only thing the switch turns off.
            var countedUser = await IncrementAsync(partitionKey, principalRowKey, +1, int.MaxValue, cancellationToken);
            var countedService = await IncrementAsync(partitionKey, GenerationBudgetEntity.ServiceRowKey, +1, int.MaxValue, cancellationToken);
            return new BudgetReservation(BudgetDecision.Allowed, Describe(countedUser, countedService, day));
        }

        // Service ceiling first: when the app is out of capacity, charging the user's personal
        // allowance for a request that is going to be refused anyway would take their own quota
        // for nothing.
        var serviceUsed = await IncrementAsync(
            partitionKey, GenerationBudgetEntity.ServiceRowKey, +1, _options.DailyServiceLimit, cancellationToken);

        if (serviceUsed < 0)
        {
            PoSeeReviewTelemetry.GenerationBudgetRejections.Add(1,
                [new KeyValuePair<string, object?>("scope", "service")]);
            logger.LogWarning("App-wide daily generation ceiling of {Limit} reached", _options.DailyServiceLimit);

            var userUsed = await ReadCountAsync(partitionKey, principalRowKey, cancellationToken);
            var snapshot = Describe(userUsed, _options.DailyServiceLimit, day);
            return new BudgetReservation(BudgetDecision.ServiceExhausted, snapshot);
        }

        var used = await IncrementAsync(
            partitionKey, principalRowKey, +1, _options.DailyPerUserLimit, cancellationToken);

        if (used < 0)
        {
            // The service unit charged above has to come back, or a user hitting their own cap
            // repeatedly would drain the app-wide ceiling for everyone else.
            await IncrementAsync(partitionKey, GenerationBudgetEntity.ServiceRowKey, -1, int.MaxValue, cancellationToken);

            PoSeeReviewTelemetry.GenerationBudgetRejections.Add(1,
                [new KeyValuePair<string, object?>("scope", "user")]);

            var snapshot = Describe(_options.DailyPerUserLimit, serviceUsed, day);
            return new BudgetReservation(BudgetDecision.UserExhausted, snapshot);
        }

        return new BudgetReservation(BudgetDecision.Allowed, Describe(used, serviceUsed, day));
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        var day = CurrentDay();
        var partitionKey = GenerationBudgetEntity.PartitionKeyFor(day);

        try
        {
            await IncrementAsync(partitionKey, PrincipalRowKey(), -1, int.MaxValue, cancellationToken);
            await IncrementAsync(partitionKey, GenerationBudgetEntity.ServiceRowKey, -1, int.MaxValue, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            // The comic already exists and the user already has it. Losing a refund costs one
            // unit of quota; throwing here would turn a successful cache hit into a 500.
            logger.LogWarning(ex, "Failed to refund a generation budget unit; the counter may run one high today");
        }
    }

    /// <summary>
    /// Applies <paramref name="delta"/> under optimistic concurrency, refusing to cross
    /// <paramref name="limit"/>. Returns the new count, or -1 when the limit blocked the change.
    /// </summary>
    private async Task<int> IncrementAsync(
        string partitionKey,
        string rowKey,
        int delta,
        int limit,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _options.MaxConcurrencyRetries; attempt++)
        {
            try
            {
                var existing = await TryGetAsync(partitionKey, rowKey, cancellationToken);

                if (existing is null)
                {
                    if (delta > 0 && delta > limit)
                    {
                        return -1;
                    }

                    var created = new GenerationBudgetEntity
                    {
                        PartitionKey = partitionKey,
                        RowKey = rowKey,
                        Count = Math.Max(0, delta)
                    };

                    // AddEntity, not Upsert: a 409 here means a concurrent writer created the
                    // row first, which the retry loop then reads and updates.
                    await _table.AddEntityAsync(created, cancellationToken);
                    return created.Count;
                }

                var next = existing.Count + delta;

                if (delta > 0 && next > limit)
                {
                    return -1;
                }

                existing.Count = Math.Max(0, next);
                await _table.UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace, cancellationToken);
                return existing.Count;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                // Lost the race; re-read and try again.
            }
        }

        // Fail open. A contended counter must not become an outage on the app's primary action;
        // the shortfall is visible as missing budget telemetry rather than as user-facing errors.
        logger.LogWarning(
            "Generation budget counter {RowKey} stayed contended after {Attempts} attempts; allowing the request uncounted",
            rowKey, _options.MaxConcurrencyRetries);
        return 0;
    }

    private async Task<GenerationBudgetEntity?> TryGetAsync(
        string partitionKey, string rowKey, CancellationToken cancellationToken)
    {
        try
        {
            return await _table.GetEntityAsync<GenerationBudgetEntity>(partitionKey, rowKey, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<int> ReadCountAsync(string partitionKey, string rowKey, CancellationToken cancellationToken)
    {
        var entity = await TryGetAsync(partitionKey, rowKey, cancellationToken);
        return entity?.Count ?? 0;
    }

    private GenerationBudgetDto Describe(int used, int serviceUsed, DateOnly day) => new()
    {
        Used = Math.Max(0, used),
        DailyLimit = _options.DailyPerUserLimit,
        Remaining = _options.Enabled ? Math.Max(0, _options.DailyPerUserLimit - used) : _options.DailyPerUserLimit,
        ResetsAt = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        ServiceHasCapacity = !_options.Enabled || serviceUsed < _options.DailyServiceLimit
    };

    private DateOnly CurrentDay() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>
    /// The key a generation is charged to. An authenticated principal is the honest unit; the
    /// IP fallback only ever applies outside Production, where anonymous callers are possible.
    /// </summary>
    private string PrincipalRowKey()
    {
        var userId = identityAccessor.GetCurrentUserId();
        if (!userId.IsAnonymous)
        {
            return GenerationBudgetEntity.RowKeyFor(userId.Value);
        }

        var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        return GenerationBudgetEntity.RowKeyFor(string.IsNullOrWhiteSpace(ip) ? "anonymous" : $"ip:{ip}");
    }
}
