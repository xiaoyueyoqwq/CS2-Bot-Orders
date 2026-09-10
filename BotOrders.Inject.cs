using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace BotOrders;

internal readonly record struct InjectClock(float RoundStart, bool FreezeEnded, float FreezeEnd);

/// <summary>
/// Writes CCSBot GoalPosition + RepathTimer. Does not Follow a human
/// pawn, does not write T bombsite-search order, does not Lock WASD.
/// </summary>
internal static class OrderInject
{
    internal static InjectKind KindFor(RoundTeamState state, CCSPlayerController bot)
    {
        if (!state.Current.AssignAllBots && state.Current.Action != ResolutionAction.HuntDropped)
            return InjectKind.None;

        bool ct = bot.Team == CsTeam.CounterTerrorist;
        return state.Current.Action switch
        {
            ResolutionAction.HuntDropped => InjectKind.HuntDropped,
            ResolutionAction.LeaveSite => InjectKind.LeaveSite,
            ResolutionAction.AllGo or ResolutionAction.KeepPrevious when state.Rank == OrderRanks.BombFact
                => ct ? InjectKind.RetakeSite : InjectKind.AllGoSite,
            ResolutionAction.AllGo or ResolutionAction.KeepPrevious
                => InjectKind.AllGoSite,
            _ => InjectKind.None,
        };
    }

    internal static void Apply(
        ILogger logger,
        RoundTeamState state,
        IEnumerable<CCSPlayerController> livingBots,
        InjectClock clock,
        bool log,
        HashSet<string> skipLogged)
    {
        var livingList = livingBots as IReadOnlyList<CCSPlayerController> ?? livingBots.ToList();
        float now = Server.CurrentTime;
        bool assign = state.Current.AssignAllBots
                      || state.Current.Action == ResolutionAction.HuntDropped;

        if (OrderCommands.IsInOpeningCollect(clock.RoundStart, now, clock.FreezeEnded))
        {
            if (assign)
            {
                state.ApplyRoster(OrderRoster.AllBots(livingList));
                ArmIgnoreRadio(livingList);
            }
            else
                ClearOverride(livingList);
            return;
        }

        if (!assign)
        {
            ClearOverride(livingList);
            return;
        }

        state.ApplyRoster(OrderRoster.AllBots(livingList));
        bool openingRush = OrderCommands.IsInTacticalSilence(clock.FreezeEnd, now, clock.FreezeEnded);

        foreach (var bot in livingList)
        {
            if (!bot.IsBot || bot.ControllingBot)
                continue;

            var kind = KindFor(state, bot);
            if (kind == InjectKind.None)
                continue;

            var pawn = bot.PlayerPawn.Value;
            var ccsBot = pawn?.Bot;
            if (pawn == null || !pawn.IsValid || ccsBot == null)
            {
                LogSkip(logger, skipLogged, log, bot, kind, state.PrimarySite, "no-bot", state.Current.Reason);
                continue;
            }

            bool carriesC4 = OrderRoster.HasC4(bot);
            bool punchFight = (openingRush
                               && (kind == InjectKind.AllGoSite || kind == InjectKind.RetakeSite))
                              || (carriesC4
                                  && bot.Team == CsTeam.Terrorist
                                  && kind == InjectKind.AllGoSite);
            if ((ccsBot.IsAttacking || ccsBot.IsEnemyVisible) && !punchFight)
            {
                LogSkip(logger, skipLogged, log, bot, kind, state.PrimarySite, "hold-fight", state.Current.Reason);
                continue;
            }

            if (!TryDestination(kind, state, out var dest, out Bombsite destSite, out string destSource))
            {
                LogSkip(logger, skipLogged, log, bot, kind, state.PrimarySite, "no-dest", state.Current.Reason);
                continue;
            }

            WriteGoal(pawn, ccsBot, dest);
            bool hadSkip = ClearSkips(skipLogged, bot.Slot);
            if (log || hadSkip)
            {
                OrderDump.Inject(
                    logger,
                    bot,
                    kind,
                    destSite,
                    dest,
                    state.Current.Reason,
                    ccsBot.IsFollowing,
                    ReadIgnoreRadio(pawn),
                    destSource,
                    carriesC4);
            }
        }
    }

    internal static void ClearOverride(IEnumerable<CCSPlayerController> livingBots)
    {
        foreach (var bot in livingBots)
        {
            if (!bot.IsBot)
                continue;
            var pawn = bot.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid)
                continue;
            SetIgnoreRadio(pawn, false);
        }
    }

    internal static void ArmIgnoreRadio(IEnumerable<CCSPlayerController> livingBots)
    {
        foreach (var bot in livingBots)
        {
            if (!bot.IsBot || bot.ControllingBot)
                continue;
            var pawn = bot.PlayerPawn.Value;
            var ccsBot = pawn?.Bot;
            if (pawn == null || !pawn.IsValid)
                continue;
            if (ccsBot != null)
            {
                ref bool following = ref ccsBot.IsFollowing;
                following = false;
            }

            SetIgnoreRadio(pawn, true);
        }
    }

    private static bool TryDestination(
        InjectKind kind,
        RoundTeamState state,
        out Vector dest,
        out Bombsite destSite,
        out string destSource)
    {
        dest = new Vector();
        destSite = state.PrimarySite;
        destSource = "none";
        if (kind == InjectKind.HuntDropped)
        {
            destSite = Bombsite.Unknown;
            if (OrderSites.TryC4Origin(out dest))
            {
                destSource = "c4";
                return true;
            }

            if (state.DroppedOrigin is WorldPoint point && point.IsUsable)
            {
                dest = point.ToVector();
                destSource = "dropper-origin";
                return true;
            }

            return false;
        }

        destSource = "bombsite";
        return OrderSites.TryBombsiteOrigin(state.PrimarySite, out dest);
    }

    private static void WriteGoal(CCSPlayerPawn pawn, CCSBot bot, Vector dest)
    {
        var goal = bot.GoalPosition;
        if (goal != null)
        {
            goal.X = dest.X;
            goal.Y = dest.Y;
            goal.Z = dest.Z;
        }

        ref bool following = ref bot.IsFollowing;
        following = false;
        ref bool stopping = ref bot.IsStopping;
        stopping = false;

        Expire(bot.RepathTimer);
        Start(bot.MustRunTimer, OrderCommands.MustRunSeconds);
        Start(bot.HurryTimer, OrderCommands.MustRunSeconds);
        SetIgnoreRadio(pawn, true);
    }

    private static void LogSkip(
        ILogger logger,
        HashSet<string> skipLogged,
        bool log,
        CCSPlayerController bot,
        InjectKind kind,
        Bombsite dest,
        string why,
        string reason)
    {
        string key = SkipKey(bot.Slot, why);
        bool first = skipLogged.Add(key);
        if (!log && !first)
            return;

        if (why == "hold-fight")
            OrderDump.InjectHoldFight(logger, bot, kind, dest, reason);
        else
            OrderDump.InjectFail(logger, bot, kind, dest, why);
    }

    private static bool ClearSkips(HashSet<string> skipLogged, int slot)
    {
        bool had = skipLogged.Remove(SkipKey(slot, "no-bot"))
                   | skipLogged.Remove(SkipKey(slot, "no-dest"))
                   | skipLogged.Remove(SkipKey(slot, "hold-fight"));
        return had;
    }

    private static string SkipKey(int slot, string why) => $"{slot}:{why}";

    private static void SetIgnoreRadio(CCSPlayerPawn pawn, bool value)
    {
        var radio = pawn.RadioServices;
        if (radio == null)
            return;
        ref bool ignore = ref radio.IgnoreRadio;
        ignore = value;
    }

    private static bool ReadIgnoreRadio(CCSPlayerPawn pawn)
    {
        var radio = pawn.RadioServices;
        return radio != null && radio.IgnoreRadio;
    }

    private static void Expire(CountdownTimer timer)
    {
        ref float duration = ref timer.Duration;
        duration = 0f;
        ref float timestamp = ref timer.Timestamp;
        timestamp = Server.CurrentTime;
        ref float timescale = ref timer.Timescale;
        timescale = 1f;
    }

    private static void Start(CountdownTimer timer, float seconds)
    {
        ref float duration = ref timer.Duration;
        duration = seconds;
        ref float timestamp = ref timer.Timestamp;
        timestamp = Server.CurrentTime;
        ref float timescale = ref timer.Timescale;
        timescale = 1f;
    }
}
