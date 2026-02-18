using TrailMateCenter.Models;
using TrailMateCenter.Storage;
using Xunit;

namespace TrailMateCenter.Tests;

public sealed class SqliteStoreTests
{
    [Fact]
    public async Task UpsertMessage_Preserves_TeamChat_Fields()
    {
        var root = Path.Combine(Path.GetTempPath(), "TrailMateCenter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "store.db");

        try
        {
            var store = new SqliteStore(dbPath);
            await store.InitializeAsync(CancellationToken.None);

            var message = new MessageEntry
            {
                Direction = MessageDirection.Outgoing,
                From = "PC",
                To = "broadcast",
                ChannelId = 1,
                Channel = "1",
                Text = "team",
                Status = MessageDeliveryStatus.Succeeded,
                IsTeamChat = true,
                TeamConversationKey = "1122334455667788:11223344",
            };

            await store.UpsertMessageAsync(message, CancellationToken.None);

            var loaded = await store.LoadMessagesAsync(CancellationToken.None);
            var restored = Assert.Single(loaded);
            Assert.True(restored.IsTeamChat);
            Assert.Equal("1122334455667788:11223344", restored.TeamConversationKey);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
