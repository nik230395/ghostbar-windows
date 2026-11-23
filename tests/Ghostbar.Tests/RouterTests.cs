using Ghostbar.Backend;

namespace Ghostbar.Tests;

public class RouterTests
{
    private DateTimeOffset now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private ModelRouter Router() => new(() => now);

    [Fact]
    public void Rank_FlashFirst_ThenLite_ThenPro_SkipsNonChatModels()
    {
        var ranked = ModelRouter.RankForChat([
            "gemini-2.5-pro", "gemini-2.5-flash-lite", "text-embedding-004", "gemini-2.5-flash",
            "imagen-4", "gemini-3.0-flash", "gemini-2.5-flash-preview-tts", "gemma-3-27b",
        ]);

        Assert.Equal(["gemini-3.0-flash", "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.5-pro", "gemma-3-27b"], ranked);
    }

    [Fact]
    public void Rank_StableBeforePreview() =>
        Assert.Equal(["gemini-2.5-flash", "gemini-3.0-flash-preview"],
            ModelRouter.RankForChat(["gemini-3.0-flash-preview", "gemini-2.5-flash"]));

    [Fact]
    public void Order_UserOrderFirst_NewModelsAppended_DisabledRemoved()
    {
        var order = ModelRouter.Order(
            ["a-flash", "b-pro", "c-flash-lite", "new-flash"],
            ["b-pro", "a-flash", "gone"],
            ["c-flash-lite"]);

        Assert.Equal(["b-pro", "a-flash", "new-flash"], order);
    }

    [Fact]
    public void Candidates_SelectedFirst_ThenOrder()
    {
        Assert.Equal(["m2", "m1", "m3"], Router().Candidates("m2", ["m1", "m2", "m3"]));
        Assert.Equal(["m1", "m2", "m3"], Router().Candidates(ModelRouter.Auto, ["m1", "m2", "m3"]));
    }

    [Fact]
    public void Candidates_SkipBlockedUntilCooldownEnds()
    {
        var router = Router();
        router.Fail("m1", router.Classify(429, "{}"));
        Assert.Equal(["m2"], router.Candidates(ModelRouter.Auto, ["m1", "m2"]));

        now = now.AddMinutes(2);
        Assert.Equal(["m1", "m2"], router.Candidates(ModelRouter.Auto, ["m1", "m2"]));
    }

    [Fact]
    public void Candidates_AllBlocked_ReturnsSoonestFirstInsteadOfNothing()
    {
        var router = Router();
        router.Fail("m1", router.Classify(404, ""));
        router.Fail("m2", router.Classify(429, ""));

        Assert.Equal(["m2", "m1"], router.Candidates(ModelRouter.Auto, ["m1", "m2"]));
    }

    [Theory]
    [InlineData(401, FailureAction.Stop, 0)]
    [InlineData(429, FailureAction.Switch, 60)]
    [InlineData(400, FailureAction.Switch, 600)]
    [InlineData(403, FailureAction.Switch, 600)]
    [InlineData(404, FailureAction.Switch, 43200)]
    [InlineData(500, FailureAction.Switch, 30)]
    [InlineData(503, FailureAction.Switch, 30)]
    [InlineData(418, FailureAction.Stop, 0)]
    public void Classify_StatusCodes(int status, FailureAction action, int cooldownSeconds)
    {
        var failure = Router().Classify(status, "{}");
        Assert.Equal(action, failure.Action);
        Assert.Equal(TimeSpan.FromSeconds(cooldownSeconds), failure.Cooldown);
    }

    [Fact]
    public void Classify_DailyQuota_BlocksUntilMidnightPacific()
    {
        var body = """{"error":{"code":429,"details":[{"violations":[{"quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier"}]}]}}""";
        var failure = Router().Classify(429, body);

        Assert.Equal(FailureAction.Switch, failure.Action);
        Assert.Equal(TimeSpan.FromHours(20), failure.Cooldown);
    }

    [Fact]
    public void Succeed_ClearsCooldown_AndLongBlocksPersist()
    {
        var router = Router();
        router.Fail("m1", router.Classify(429, "{}"));
        router.Fail("m2", router.Classify(404, ""));
        Assert.Equal(["m2"], router.LongBlocks().Keys);

        router.Succeed("m1");
        Assert.False(router.IsBlocked("m1"));

        var restored = Router();
        restored.Restore(router.LongBlocks());
        Assert.True(restored.IsBlocked("m2"));
    }
}
