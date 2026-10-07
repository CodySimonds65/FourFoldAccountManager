using System.Text.Json;
using FourFoldAccountManager.Core.LiveFeed;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.LiveFeed;

public sealed class LiveFeedPrivacyTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The login message carries the game's account id, the username and the admin flag. A plugin must only ever learn
    // that one of the user's own accounts logged in, and a failed login must not read as a login.
    [Fact]
    public void TheLoginEventCarriesNothingButTheAccountAndTheTime()
    {
        var accountId = Guid.NewGuid();
        var frame = MirrorWriter.Batch(MirrorWriter.Message(GameMessageDecoder.AuthResponseType, new MirrorWriter()
            .Bool(true).VarInt(987654).String("SecretLogin").Bool(true).String("").ToArray()));

        var gameEvent = Assert.Single(GameMessageDecoder.DecodeBatch(frame).Events);
        var post = LiveFeedPayloads.ForPlugins(accountId, gameEvent, DateTimeOffset.UnixEpoch);

        Assert.NotNull(post);
        Assert.Equal("session.loggedIn", post.Value.Name);
        var json = JsonSerializer.SerializeToElement(post.Value.Data, post.Value.Data.GetType(), Json);
        Assert.Equal(new[] { "accountId", "at" }, json.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.DoesNotContain("SecretLogin", json.GetRawText());
        Assert.Null(LiveFeedPayloads.ForPlugins(accountId, new LoggedIn(false), DateTimeOffset.UnixEpoch));
    }
}
