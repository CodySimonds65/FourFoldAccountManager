using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Services;

public sealed class InGamePasswordCopyTests
{
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Classify_NoAccount_WhenSlotUnassigned() =>
        Assert.Equal(
            InGamePasswordCopy.Readiness.NoAccount,
            InGamePasswordCopy.Classify(null, new AccountCredentials("user", "secret")));

    [Fact]
    public void Classify_NoPassword_WhenNoCredentialsSaved() =>
        Assert.Equal(
            InGamePasswordCopy.Readiness.NoPassword,
            InGamePasswordCopy.Classify(Account, null));

    [Fact]
    public void Classify_NoPassword_WhenPasswordEmpty() =>
        Assert.Equal(
            InGamePasswordCopy.Readiness.NoPassword,
            InGamePasswordCopy.Classify(Account, new AccountCredentials("user", "")));

    [Fact]
    public void Classify_Ready_WhenPasswordPresent() =>
        Assert.Equal(
            InGamePasswordCopy.Readiness.Ready,
            InGamePasswordCopy.Classify(Account, new AccountCredentials("user", "secret")));

    [Fact]
    public void StillOurClipboard_True_WhenUnchanged() =>
        Assert.True(InGamePasswordCopy.StillOurClipboard("secret", "secret"));

    [Theory]
    [InlineData("something else")]
    [InlineData("")]
    [InlineData(null)]
    public void StillOurClipboard_False_WhenUserCopiedSomethingElse(string? clipboardNow) =>
        Assert.False(InGamePasswordCopy.StillOurClipboard(clipboardNow, "secret"));
}
