namespace Ghostbar.Backend;

public enum FailureAction { Switch, Stop }

public record ModelFailure(FailureAction Action, TimeSpan Cooldown, string Reason);

public class ModelRouter
{
    public const string Auto = "Auto";

    private static readonly string[] NonChatMarkers =
        ["embed", "imagen", "veo", "tts", "aqa", "audio", "image", "live", "robotics", "computer-use"];

    private readonly Dictionary<string, DateTimeOffset> blockedUntil = new();
    private readonly Func<DateTimeOffset> now;

    public ModelRouter(Func<DateTimeOffset>? now = null) => this.now = now ?? (() => DateTimeOffset.UtcNow);

    public static List<string> RankForChat(IEnumerable<string> models) =>
        models.Where(IsChatModel)
            .OrderBy(Tier)
            .ThenBy(m => m.Contains("preview", StringComparison.OrdinalIgnoreCase) || m.Contains("exp", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(m => m, StringComparer.Ordinal)
            .ToList();

    private static bool IsChatModel(string model) =>
        !NonChatMarkers.Any(marker => model.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static int Tier(string model)
    {
        if (model.Contains("flash-lite", StringComparison.OrdinalIgnoreCase)) return 1;
        if (model.Contains("flash", StringComparison.OrdinalIgnoreCase)) return 0;
        if (model.Contains("pro", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    public static List<string> Order(IEnumerable<string> available, IReadOnlyList<string> userOrder, IReadOnlyCollection<string> disabled)
    {
        var availableSet = available.ToHashSet();
        var pinned = userOrder.Where(availableSet.Contains);
        var rest = RankForChat(availableSet.Except(userOrder));
        return pinned.Concat(rest).Where(m => !disabled.Contains(m)).Distinct().ToList();
    }

    public List<string> Candidates(string selected, IReadOnlyList<string> order)
    {
        var all = (selected == Auto ? order : order.Prepend(selected)).Distinct().ToList();
        var open = all.Where(m => !IsBlocked(m)).ToList();
        return open.Count > 0 ? open : all.OrderBy(m => blockedUntil.GetValueOrDefault(m)).ToList();
    }

    public bool IsBlocked(string model) => blockedUntil.TryGetValue(model, out var until) && until > now();

    public void Fail(string model, ModelFailure failure)
    {
        if (failure.Cooldown > TimeSpan.Zero) blockedUntil[model] = now() + failure.Cooldown;
    }

    public void Succeed(string model) => blockedUntil.Remove(model);

    public Dictionary<string, DateTimeOffset> LongBlocks() =>
        blockedUntil.Where(b => b.Value - now() >= TimeSpan.FromHours(1)).ToDictionary();

    public void Restore(IReadOnlyDictionary<string, DateTimeOffset> saved)
    {
        foreach (var (model, until) in saved)
            if (until > now()) blockedUntil[model] = until;
    }

    public ModelFailure Classify(int status, string body) => status switch
    {
        401 => new(FailureAction.Stop, TimeSpan.Zero, "API key rejected"),
        429 when IsDailyLimit(body) => new(FailureAction.Switch, UntilNextPacificMidnight(), "daily quota used up"),
        429 => new(FailureAction.Switch, TimeSpan.FromMinutes(1), "rate limit"),
        400 => new(FailureAction.Switch, TimeSpan.FromMinutes(10), "request rejected"),
        403 => new(FailureAction.Switch, TimeSpan.FromMinutes(10), "no access"),
        404 => new(FailureAction.Switch, TimeSpan.FromHours(12), "model not found"),
        >= 500 => new(FailureAction.Switch, TimeSpan.FromSeconds(30), "server error"),
        _ => new(FailureAction.Stop, TimeSpan.Zero, $"HTTP {status}"),
    };

    private static bool IsDailyLimit(string body) =>
        body.Contains("PerDay", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("per day", StringComparison.OrdinalIgnoreCase);

    private TimeSpan UntilNextPacificMidnight()
    {
        var zone = PacificZone();
        var local = TimeZoneInfo.ConvertTime(now(), zone);
        var midnight = local.Date.AddDays(1);
        var next = new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
        return next - now();
    }

    private static TimeZoneInfo PacificZone()
    {
        foreach (var id in new[] { "America/Los_Angeles", "Pacific Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Pacific", TimeSpan.FromHours(-8), "Pacific", "Pacific");
    }
}
