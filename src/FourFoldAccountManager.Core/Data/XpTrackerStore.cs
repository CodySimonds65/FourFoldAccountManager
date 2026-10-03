using System.Text.Json;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Data;

public sealed record XpStoredAccount(
    Guid AccountId,
    int PlayerId,
    DateTimeOffset SampledAt,
    PlayerProgressSnapshot Snapshot,
    IReadOnlyList<XpGainInterval> Intervals);

public sealed class XpTrackerStore
{
    private readonly LocalDataPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public XpTrackerStore(LocalDataPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<IReadOnlyDictionary<Guid, XpStoredAccount>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IReadOnlyCollection<XpStoredAccount> accounts,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteCoreAsync(accounts, now, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var accounts = (await ReadCoreAsync(cancellationToken)).Values
                .Where(account => account.AccountId != accountId).ToArray();
            if (File.Exists(_paths.XpTrackerFilePath))
            {
                await WriteCoreAsync(accounts, DateTimeOffset.UtcNow, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<Guid, XpStoredAccount>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.XpTrackerFilePath))
        {
            return new Dictionary<Guid, XpStoredAccount>();
        }

        try
        {
            await using var stream = File.OpenRead(_paths.XpTrackerFilePath);
            var data = await JsonSerializer.DeserializeAsync<TrackerFile>(stream, AtomicJsonFile.Options, cancellationToken);
            if (data is not { Version: 1, Accounts: not null } ||
                data.Accounts.Any(account => account is null || account.AccountId == Guid.Empty ||
                    account.PlayerId <= 0 || account.Snapshot is null ||
                    string.IsNullOrWhiteSpace(account.Snapshot.Username) ||
                    account.Snapshot.Classes is null || account.Snapshot.InvalidClasses is null ||
                    account.Snapshot.Classes.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) ||
                    account.Snapshot.InvalidClasses.Any(string.IsNullOrWhiteSpace) ||
                    account.Intervals is null || account.Intervals.Any(interval => interval is null)) ||
                data.Accounts.Select(account => account.AccountId).Distinct().Count() != data.Accounts.Count)
            {
                return new Dictionary<Guid, XpStoredAccount>();
            }

            return data.Accounts.ToDictionary(account => account.AccountId);
        }
        catch (JsonException)
        {
            return new Dictionary<Guid, XpStoredAccount>();
        }
    }

    private async Task WriteCoreAsync(
        IReadOnlyCollection<XpStoredAccount> accounts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var file = new TrackerFile(1, accounts.Select(account => account with
        {
            Intervals = account.Intervals.Where(interval => interval.To > now - TimeSpan.FromHours(1)).ToArray()
        }).ToList());
        await AtomicJsonFile.WriteAsync(_paths.XpTrackerFilePath, file, cancellationToken);
    }

    private sealed record TrackerFile(int Version, List<XpStoredAccount> Accounts);
}
