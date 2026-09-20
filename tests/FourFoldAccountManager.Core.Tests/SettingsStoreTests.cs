using System.Text;
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
    public async Task LoadAsync_WhenFileIsMissing_ReturnsDefaultTwoByTwoSettings()
    {
        var settings = await _store.LoadAsync();

        Assert.AreEqual(PanelLayout.TwoByTwo, settings.Layout);
        CollectionAssert.AreEqual(new Guid?[4], settings.SlotAccountIds.ToArray());
    }

    [TestMethod]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsLayoutAndSlotAssignments()
    {
        var expected = new PanelSettings(
            PanelLayout.TwoByOne,
            [Guid.NewGuid(), null, Guid.NewGuid(), null]);

        await _store.SaveAsync(expected);
        var actual = await _store.LoadAsync();

        Assert.AreEqual(expected.Layout, actual.Layout);
        CollectionAssert.AreEqual(expected.SlotAccountIds.ToArray(), actual.SlotAccountIds.ToArray());
    }

    [TestMethod]
    public async Task LoadAsync_WhenJsonIsMalformed_PreservesOriginalBytes()
    {
        var original = Encoding.UTF8.GetBytes("{\"layout\":");
        await File.WriteAllBytesAsync(_paths.SettingsFilePath, original);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync());

        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(_paths.SettingsFilePath));
    }

    [TestMethod]
    public async Task LoadAsync_WhenAccountIsAssignedToMultipleSlots_ThrowsWithoutChangingFile()
    {
        var duplicateId = Guid.NewGuid();
        var json = $$"""
            {"layout":2,"slotAccountIds":["{{duplicateId}}","{{duplicateId}}",null,null]}
            """;
        await File.WriteAllTextAsync(_paths.SettingsFilePath, json);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync());

        Assert.AreEqual(json, await File.ReadAllTextAsync(_paths.SettingsFilePath));
    }
}
