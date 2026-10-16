using System.Collections.Concurrent;
using System.Security.Cryptography;
using OpenClaw.Chat;
using OpenClaw.Shared;
using OpenClaw.TestSupport;
using OpenClaw.TestSupport.Gateway;
using OpenClawTray.Chat;

namespace OpenClaw.Tray.Tests;

[Collection("Chat telemetry")]
public sealed class InteractiveGatewayChatTests
{
    [Theory]
    [InlineData("streaming")]
    [InlineData("tools")]
    [InlineData("tool-error")]
    [InlineData("approval")]
    [InlineData("queued")]
    public async Task FixtureDrivesRealProviderThroughRunAndHistory(string scenario)
    {
        using var temp = new TempDirectory();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var server = await FixtureGatewayServer.StartAsync(GatewayScenario.CreateInteractive(), token,
            eventDelay: TimeSpan.FromMilliseconds(30));
        server.Interactive!.Playback.Select(scenario == "queued" ? "slow" : scenario);
        using var client = new OpenClawGatewayClient(server.Endpoint.AbsoluteUri, token, NullLogger.Instance,
            identityPath: temp.Combine("identity"), ignoreStoredDeviceToken: true, persistHandshakeDeviceTokens: false);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.SessionsUpdated += (_, sessions) => { if (sessions.Length == 5) ready.TrySetResult(); };
        await client.ConnectAsync();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var bridge = new GatewayClientChatBridge(client);
        await using var provider = new OpenClawChatDataProvider(bridge, null, temp.Combine("tools.json"),
            temp.Combine("attachments.json"), temp.Combine("last-chat.json"));
        var snapshots = new ConcurrentQueue<ChatDataSnapshot>();
        provider.Changed += (_, args) => snapshots.Enqueue(args.Snapshot);
        await provider.LoadAsync();
        var key = GatewayScenario.EmptySessionKey;
        await provider.SendMessageAsync(key, "Synthetic provider proof.");
        if (scenario == "queued")
            await provider.SendMessageAsync(key, "Synthetic queued turn.");
        if (scenario == "approval")
        {
            await WaitAsync(() => Latest(snapshots, key)?.PendingPermission is not null);
            var permission = Latest(snapshots, key)!.PendingPermission!;
            await provider.RespondToPermissionAsync(key, permission.RequestId, "allow-once");
        }
        var turns = scenario == "queued" ? 2 : 1;
        await WaitAsync(() => Latest(snapshots, key)?.Entries.Count(e =>
            e.Kind == ChatTimelineItemKind.Assistant && e.Text.Contains("FIXTURE INTERACTIVE COMPLETE") && !e.IsStreaming) == turns);
        var timeline = Latest(snapshots, key)!;
        Assert.Equal(turns, timeline.Entries.Count(e => e.Kind == ChatTimelineItemKind.User));
        Assert.Equal(turns, timeline.Entries.Count(e => e.Kind == ChatTimelineItemKind.Assistant && e.Text.Contains("FIXTURE INTERACTIVE COMPLETE")));
        if (scenario is "tools" or "tool-error")
            Assert.Contains(timeline.Entries, e => e.Kind == ChatTimelineItemKind.ToolCall);
        if (scenario == "approval") Assert.Null(timeline.PendingPermission);
        var history = await client.RequestChatHistoryAsync(key);
        Assert.Contains(history.Messages, m => m.Text.Contains("FIXTURE INTERACTIVE COMPLETE"));
        Assert.Empty(server.UnexpectedRequests);
        await client.DisconnectAsync();
    }

    private static ChatTimelineState? Latest(ConcurrentQueue<ChatDataSnapshot> snapshots, string key) =>
        snapshots.LastOrDefault()?.Timelines.GetValueOrDefault(key);

    private static async Task WaitAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }
}
