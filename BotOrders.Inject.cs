using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace BotOrders;

/// <summary>
/// 0.1.0 is listen-only. Writes to CSGameState / CCSBot wait on the
/// search-order dump. CT must never reuse T bombsite-search order.
/// </summary>
internal static class OrderInject
{
    internal static InjectKind KindFor(RoundTeamState state, CCSPlayerController bot)
    {
        bool inFake = state.FakeSlots.Contains(bot.Slot);
        bool contact = state.ContactSlots.Contains(bot.Slot);
        bool ct = bot.Team == CsTeam.CounterTerrorist;

        if (state.Rank == OrderRanks.ExplicitSiteChange)
            return InjectKind.AllGoSite;
        if (state.Rank == OrderRanks.PlantedOrBombPing)
            return ct ? InjectKind.RetakeSite : InjectKind.AllGoSite;
        if (state.Rank == OrderRanks.SiteClear)
            return InjectKind.LeaveSite;
        if (state.Rank != OrderRanks.OpeningSplit)
            return InjectKind.None;

        if (inFake)
        {
            if (contact)
                return InjectKind.StayFight;
            if (state.FakeEmpty)
                return InjectKind.AllGoSite;
            return ct ? InjectKind.CheckSite : InjectKind.FakeRushSite;
        }

        return ct ? InjectKind.StackSite : InjectKind.RushSite;
    }

    internal static void Apply(ILogger logger, RoundTeamState state, IEnumerable<CCSPlayerController> livingBots)
    {
        foreach (var bot in livingBots)
        {
            if (!bot.IsBot)
                continue;

            var kind = KindFor(state, bot);
            Bombsite dest = kind switch
            {
                InjectKind.FakeRushSite or InjectKind.CheckSite or InjectKind.StayFight => state.OtherSite,
                InjectKind.LeaveSite => state.PrimarySite,
                _ => state.PrimarySite,
            };

            logger.LogInformation(
                "[BotOrders] inject-skip bot={Name} slot={Slot} kind={Kind} dest={Dest} team={Team}",
                bot.PlayerName,
                bot.Slot,
                kind,
                dest,
                bot.Team);
        }
    }
}
