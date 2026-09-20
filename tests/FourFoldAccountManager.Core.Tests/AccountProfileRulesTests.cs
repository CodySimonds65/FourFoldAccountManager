using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Tests;

[TestClass]
public sealed class AccountProfileRulesTests
{
    [TestMethod]
    public void Create_TrimsLabelAndGeneratesId()
    {
        var account = AccountProfile.Create("  Main  ");

        Assert.AreEqual("Main", account.Label);
        Assert.AreNotEqual(Guid.Empty, account.Id);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void NormalizeLabel_RejectsBlankInput(string label)
    {
        Assert.Throws<ArgumentException>(
            () => AccountProfileRules.NormalizeLabel(label));
    }

    [TestMethod]
    public void NormalizeLabel_RejectsLabelsLongerThanSixtyCharacters()
    {
        var label = new string('x', 61);

        Assert.Throws<ArgumentException>(
            () => AccountProfileRules.NormalizeLabel(label));
    }
}
