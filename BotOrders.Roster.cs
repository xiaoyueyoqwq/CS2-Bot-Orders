using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace BotOrders;

internal sealed record RosterAssignment(
    IReadOnlyList<int> PrimarySlots,
    IReadOnlyList<int> GuardSlots,
    int LivingCount);

internal static class OrderRoster
{
    internal static RosterAssignment AllBots(IReadOnlyList<CCSPlayerController> living)
    {
        var bots = living.Where(p => p.IsValid && p.IsBot).ToList();
        bots.Sort((a, b) =>
        {
            bool aC4 = HasC4(a);
            bool bC4 = HasC4(b);
            if (aC4 != bC4)
                return aC4 ? -1 : 1;
            return a.Slot.CompareTo(b.Slot);
        });
        return new RosterAssignment(bots.Select(p => p.Slot).ToArray(), Array.Empty<int>(), living.Count);
    }

    internal static RosterAssignment FetchAndGuard(
        IReadOnlyList<CCSPlayerController> living,
        Vector dest,
        int? stickyFetchSlot,
        bool allowFetch,
        out int? fetchSlot)
    {
        var fetcher = allowFetch ? PickFetcher(living, dest, stickyFetchSlot) : null;
        fetchSlot = fetcher?.Slot;
        int[] fetchSlots = fetcher != null ? new[] { fetcher.Slot } : Array.Empty<int>();
        var guards = new List<int>();
        foreach (var bot in living)
        {
            if (!bot.IsValid || !bot.IsBot || bot.ControllingBot)
                continue;
            if (fetchSlot is int taken && bot.Slot == taken)
                continue;
            guards.Add(bot.Slot);
        }

        guards.Sort();
        return new RosterAssignment(fetchSlots, guards, living.Count);
    }

    internal static CCSPlayerController? PickFetcher(
        IReadOnlyList<CCSPlayerController> living,
        Vector dest,
        int? stickyFetchSlot)
    {
        var candidates = living
            .Where(p => p.IsValid && p.IsBot && !p.ControllingBot)
            .ToList();
        if (candidates.Count == 0)
            return null;

        if (stickyFetchSlot is int sticky)
        {
            var held = candidates.FirstOrDefault(p => p.Slot == sticky);
            if (held != null)
                return held;
        }

        candidates.Sort((a, b) =>
        {
            int byDist = Distance2d(a, dest).CompareTo(Distance2d(b, dest));
            return byDist != 0 ? byDist : a.Slot.CompareTo(b.Slot);
        });
        return candidates[0];
    }

    internal static float Distance2d(CCSPlayerController player, Vector dest)
    {
        var origin = player.PlayerPawn.Value?.AbsOrigin;
        if (origin == null)
            return float.MaxValue;
        float dx = origin.X - dest.X;
        float dy = origin.Y - dest.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    internal static CCSPlayerController? BotC4Holder(IReadOnlyList<CCSPlayerController> living)
    {
        return living.FirstOrDefault(p => p.IsValid && p.IsBot && HasC4(p));
    }

    internal static RosterAssignment None(int livingCount)
    {
        return new RosterAssignment(Array.Empty<int>(), Array.Empty<int>(), livingCount);
    }

    internal static ulong? HumanC4HolderSteamId(IReadOnlyList<CCSPlayerController> living)
    {
        var holder = living.FirstOrDefault(p => p.IsValid && !p.IsBot && HasC4(p));
        return holder?.SteamID is ulong id && id != 0 ? id : null;
    }

    internal static bool HasC4(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return false;

        var weapons = pawn.WeaponServices?.MyWeapons;
        if (weapons == null)
            return false;

        foreach (var handle in weapons)
        {
            var weapon = handle.Value;
            if (weapon != null && weapon.IsValid
                && weapon.DesignerName.Equals("weapon_c4", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
