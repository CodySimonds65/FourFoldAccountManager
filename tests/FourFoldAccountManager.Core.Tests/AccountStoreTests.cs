using System.Text;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Tests;

[TestClass]
public sealed class AccountStoreTests
{
    private string _root = string.Empty;
    private AccountStore _store = null!;
    private LocalDataPaths _paths = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "FourFoldAccountManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new LocalDataPaths(_root);
        _store = new AccountStore(_paths);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_WhenFileIsMissing_ReturnsEmptyList()
    {
        var accounts = await _store.LoadAsync();

        Assert.AreEqual(0, accounts.Count);
    }

    [TestMethod]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsAccountMetadata()
    {
        AccountProfile[] accounts =
        [
            new(Guid.Parse("7c70bd2a-819d-4b4f-b55c-152520e7a6a2"), "Main", true, 0),
            new(Guid.Parse("a1dd06f8-e5a4-4155-9964-855478ed22c2"), "Farm", false, 1)
        ];

        await _store.SaveAsync(accounts);
        var loaded = await _store.LoadAsync();

        CollectionAssert.AreEqual(accounts, loaded.ToArray());
    }

    [TestMethod]
    public async Task LoadAsync_WhenIdsAreDuplicated_ThrowsWithoutChangingFile()
    {
        var json = """
            [{"id":"11111111-1111-1111-1111-111111111111","label":"A","isFavorite":false,"sortOrder":0},
             {"id":"11111111-1111-1111-1111-111111111111","label":"B","isFavorite":false,"sortOrder":1}]
            """;
        await File.WriteAllTextAsync(_paths.AccountsFilePath, json);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync());

        Assert.AreEqual(json, await File.ReadAllTextAsync(_paths.AccountsFilePath));
    }

    [TestMethod]
    public async Task LoadAsync_WhenIdIsEmpty_ThrowsWithoutChangingFile()
    {
        var json = """
            [{"id":"00000000-0000-0000-0000-000000000000","label":"A","isFavorite":false,"sortOrder":0}]
            """;
        await File.WriteAllTextAsync(_paths.AccountsFilePath, json);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync());

        Assert.AreEqual(json, await File.ReadAllTextAsync(_paths.AccountsFilePath));
    }

    [TestMethod]
    public async Task SaveAsync_WhenIdsAreDuplicated_ThrowsAndLeavesExistingFileUntouched()
    {
        AccountProfile[] valid = [AccountProfile.Create("Existing")];
        await _store.SaveAsync(valid);
        var original = await File.ReadAllBytesAsync(_paths.AccountsFilePath);
        var duplicateId = Guid.Parse("f031bb88-65bc-4f68-80a8-b7434e6df010");
        AccountProfile[] invalid =
        [
            new(duplicateId, "One", false, 0),
            new(duplicateId, "Two", false, 1)
        ];

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.SaveAsync(invalid));

        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(_paths.AccountsFilePath));
    }

    [TestMethod]
    public async Task LoadAsync_WhenJsonIsMalformed_PreservesOriginalBytes()
    {
        var original = Encoding.UTF8.GetBytes("{\"incomplete\":");
        await File.WriteAllBytesAsync(_paths.AccountsFilePath, original);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync());

        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(_paths.AccountsFilePath));
    }
}
