using Xunit;

namespace FourFoldAccountManager.Desktop.Tests;

public sealed class WpfTestHostTests
{
    [Fact]
    public void StartDispatcherPropagatesAnInitializationFailureInsteadOfHangingOrCrashing()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WpfTestHost.StartDispatcher(() => throw new InvalidOperationException("boom")));

        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal("boom", inner.Message);
    }
}
