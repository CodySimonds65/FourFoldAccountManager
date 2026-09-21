using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Panel;

namespace FourFoldAccountManager.Core.Tests;

[TestClass]
public sealed class PanelLayoutPolicyTests
{
    [TestMethod]
    [DataRow(PanelLayout.OneByTwo, 2, 0)]
    [DataRow(PanelLayout.TwoByOne, 1, 1)]
    [DataRow(PanelLayout.TwoByTwo, 2, 2)]
    [DataRow(PanelLayout.TwoByThree, 2, 3)]
    public void GetSlotsPerRow_PreservesEveryLayoutShape(
        PanelLayout layout,
        int firstRowSlots,
        int secondRowSlots)
    {
        var rows = PanelLayoutPolicy.GetSlotsPerRow(layout);

        Assert.AreEqual(firstRowSlots, rows[0]);
        Assert.AreEqual(secondRowSlots == 0 ? 1 : 2, rows.Count);
        if (secondRowSlots > 0)
        {
            Assert.AreEqual(secondRowSlots, rows[1]);
        }
    }

    [TestMethod]
    public void GetVisibleSlotCount_ForTwoByThree_ReturnsFive()
    {
        Assert.AreEqual(5, PanelLayoutPolicy.GetVisibleSlotCount((PanelLayout)3));
    }

    [TestMethod]
    public void GetSlotsPerRow_ForTwoByThree_ReturnsTwoThenThree()
    {
        CollectionAssert.AreEqual(
            new[] { 2, 3 },
            PanelLayoutPolicy.GetSlotsPerRow((PanelLayout)3).ToArray());
    }

    [TestMethod]
    public void Assign_AllowsTheFifthSlotAndPreservesEarlierAssignments()
    {
        var firstAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fifthAccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var settings = new PanelSettings(
            (PanelLayout)3,
            [firstAccountId, null, null, null, null]);

        var updated = PanelLayoutPolicy.Assign(settings, slotIndex: 4, fifthAccountId);

        CollectionAssert.AreEqual(
            new Guid?[] { firstAccountId, null, null, null, fifthAccountId },
            updated.SlotAccountIds.ToArray());
    }

    [TestMethod]
    public void Assign_RejectsASixthSlot()
    {
        var settings = new PanelSettings((PanelLayout)3, new Guid?[5]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PanelLayoutPolicy.Assign(settings, slotIndex: 5, Guid.NewGuid()));
    }
}
