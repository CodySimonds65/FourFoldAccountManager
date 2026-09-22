using System.Text.Json;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Data;

public sealed class AccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly LocalDataPaths _paths;

    public AccountStore(LocalDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.AccountsFilePath))
        {
            return Array.Empty<AccountProfile>();
        }

        try
        {
            await using var stream = new FileStream(
                _paths.AccountsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var accounts = await JsonSerializer.DeserializeAsync<List<AccountProfile>>(
                stream,
                JsonOptions,
                cancellationToken);

            if (accounts is null)
            {
                throw new InvalidDataException("Account data must contain a JSON array.");
            }

            return Validate(accounts);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Account data is malformed. The original file has been left unchanged.",
                exception);
        }
    }

    public async Task SaveAsync(
        IReadOnlyCollection<AccountProfile> accounts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var validatedAccounts = Validate(accounts);
        Directory.CreateDirectory(_paths.DataRoot);

        var temporaryPath = Path.Combine(
            _paths.DataRoot,
            $".accounts-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    validatedAccounts,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_paths.AccountsFilePath))
            {
                File.Replace(temporaryPath, _paths.AccountsFilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, _paths.AccountsFilePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static IReadOnlyList<AccountProfile> Validate(IEnumerable<AccountProfile> accounts)
    {
        var seenIds = new HashSet<Guid>();
        var validated = new List<AccountProfile>();

        foreach (var account in accounts)
        {
            if (account is null || account.Id == Guid.Empty)
            {
                throw new InvalidDataException("Every account must have a non-empty ID.");
            }

            if (!seenIds.Add(account.Id))
            {
                throw new InvalidDataException("Account IDs must be unique.");
            }

            try
            {
                var label = AccountProfileRules.NormalizeLabel(account.Label);
                var rankingUsername = account.RankingUsername?.Trim();
                if (rankingUsername is { Length: 0 } || account.RankingPlayerId is <= 0)
                {
                    throw new ArgumentException("Ranking identity is invalid.");
                }

                validated.Add(account with { Label = label, RankingUsername = rankingUsername });
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Every account must have a valid label.", exception);
            }
        }

        return validated;
    }
}
