using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Panel;

namespace FourFoldAccountManager.Core.Tests;

[TestClass]
public sealed class PanelLayoutPolicyTests
{
    [TestMethod]
    [DataRow(PanelLayout.OneByTwo, 1, 2, 2)]
    [DataRow(PanelLayout.TwoByOne, 2, 1, 2)]
    [DataRow(PanelLayout.TwoByTwo, 2, 2, 4)]
    public void GetDimensions_ReturnsRowsColumnsAndCapacity(
        PanelLayout layout,
        int rows,
        int columns,
        int capacity)
    {
        Assert.AreEqual(rows, PanelLayoutPolicy.GetDimensions(layout).Rows);
        Assert.AreEqual(columns, PanelLayoutPolicy.GetDimensions(layout).Columns);
        Assert.AreEqual(capacity, PanelLayoutPolicy.GetVisibleSlotCount(layout));
    }

    [TestMethod]
    public void Assign_WhenAccountAlreadyExists_MovesItToRequestedSlot()
    {
        var accountId = Guid.NewGuid();
        var settings = new PanelSettings(PanelLayout.TwoByTwo, [accountId, null, null, null]);

        var updated = PanelLayoutPolicy.Assign(settings, slotIndex: 2, accountId);

        CollectionAssert.AreEqual(new Guid?[] { null, null, accountId, null }, updated.SlotAccountIds.ToArray());
        CollectionAssert.AreEqual(new Guid?[] { accountId, null, null, null }, settings.SlotAccountIds.ToArray());
    }

    [TestMethod]
    public void WithLayout_PreservesAssignmentsInHiddenSlots()
    {
        var settings = new PanelSettings(
            PanelLayout.TwoByTwo,
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);

        var updated = PanelLayoutPolicy.WithLayout(settings, PanelLayout.OneByTwo);

        Assert.AreEqual(2, PanelLayoutPolicy.GetVisibleSlotCount(updated.Layout));
        CollectionAssert.AreEqual(settings.SlotAccountIds.ToArray(), updated.SlotAccountIds.ToArray());
    }

    [TestMethod]
    public void ClearAccount_RemovesAccountFromEverySlot()
    {
        var accountId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var settings = new PanelSettings(PanelLayout.TwoByTwo, [accountId, otherId, accountId, null]);

        var updated = PanelLayoutPolicy.ClearAccount(settings, accountId);

        CollectionAssert.AreEqual(new Guid?[] { null, otherId, null, null }, updated.SlotAccountIds.ToArray());
    }

    [TestMethod]
    public void Assign_WhenSlotIndexIsOutsideFourSlots_Throws()
    {
        var settings = new PanelSettings(PanelLayout.TwoByTwo, new Guid?[4]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PanelLayoutPolicy.Assign(settings, slotIndex: 4, Guid.NewGuid()));
    }
}
