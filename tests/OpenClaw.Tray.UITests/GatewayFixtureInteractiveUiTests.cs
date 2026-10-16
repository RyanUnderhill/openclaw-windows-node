using OpenClaw.TestSupport.Gateway;

namespace OpenClaw.Tray.UITests;

public sealed partial class GatewayFixtureUiTests
{
    [GatewayFixtureUiFact]
    [Trait("Category", "GatewayFixture")]
    public async Task InteractiveChatShowsStreamedAnswerAndGatewayApproval()
    {
        await WithAppAsync(async run =>
        {
            await run.InvokeAsync("app.navigate", new { page = "chat" });
            await SelectSessionAsync(run, GatewayScenario.EmptyTitle, GatewayScenario.EmptySessionKey);
            await run.InvokeAsync("app.chat.send", new { threadId = GatewayScenario.EmptySessionKey, message = "Synthetic interactive UI proof." });
            await WaitUiAsync(run, () => IsVisibleInTimeline(run, "FIXTURE INTERACTIVE COMPLETE"), "interactive streamed final answer");
            await CaptureIfRequestedAsync(run, "interactive-chat-final.png");
            run.Gateway.Interactive!.Playback.Select("approval");
            await run.InvokeAsync("app.chat.send", new { threadId = GatewayScenario.EmptySessionKey, message = "Synthetic approval UI proof." });
            await WaitUiAsync(run, () => FindButton(run, "Allow once") is not null, "interactive approval prompt");
            await CaptureIfRequestedAsync(run, "interactive-chat-approval.png");
            Invoke(FindButton(run, "Allow once")!);
            await run.Gateway.WaitForCompletedRequestAsync("exec.approval.resolve");
            await WaitUiAsync(run, () => IsVisibleInTimeline(run, "Approval decision: allow-once."), "answer after approval");
            await CaptureIfRequestedAsync(run, "interactive-chat-after-approval.png");
        }, scenario: GatewayScenario.CreateInteractive());
    }
}
