using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    private string _root = string.Empty;
    private LocalDataPaths _paths = null!;
    private SettingsStore _store = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "FourFoldAccountManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new LocalDataPaths(_root);
        _store = new SettingsStore(_paths);
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
    public async Task LoadAsync_WithLegacyFourSlotSettings_PadsAnEmptyFifthSlot()
    {
        var firstAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var json = $$"""
            {"layout":2,"slotAccountIds":["{{firstAccountId}}",null,null,null]}
            """;
        await File.WriteAllTextAsync(_paths.SettingsFilePath, json);

        var settings = await _store.LoadAsync();

        CollectionAssert.AreEqual(
            new Guid?[] { firstAccountId, null, null, null, null },
            settings.SlotAccountIds.ToArray());
        Assert.AreEqual(0.6, settings.TwoByThreeTopRowFraction, 0.0001);
    }

    [TestMethod]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsFiveSlotsAndTopRowFraction()
    {
        var fifthAccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var expected = new PanelSettings(
            (PanelLayout)3,
            [null, null, null, null, fifthAccountId])
        {
            TwoByThreeTopRowFraction = 0.7
        };

        await _store.SaveAsync(expected);
        var actual = await _store.LoadAsync();

        Assert.AreEqual((PanelLayout)3, actual.Layout);
        CollectionAssert.AreEqual(expected.SlotAccountIds.ToArray(), actual.SlotAccountIds.ToArray());
        Assert.AreEqual(0.7, actual.TwoByThreeTopRowFraction, 0.0001);
    }

    [TestMethod]
    public async Task SaveAsync_WithOutOfRangeTopRowFraction_RejectsSettings()
    {
        var settings = new PanelSettings(PanelLayout.TwoByTwo, new Guid?[5])
        {
            TwoByThreeTopRowFraction = 0.95
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.SaveAsync(settings));
    }
}
