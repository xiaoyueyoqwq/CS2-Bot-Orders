using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace BotOrders;

internal sealed record RosterAssignment(
    IReadOnlyList<int> PrimarySlots,
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
        return new RosterAssignment(bots.Select(p => p.Slot).ToArray(), living.Count);
    }

    internal static CCSPlayerController? BotC4Holder(IReadOnlyList<CCSPlayerController> living)
    {
        return living.FirstOrDefault(p => p.IsValid && p.IsBot && HasC4(p));
    }

    internal static RosterAssignment None(int livingCount)
    {
        return new RosterAssignment(Array.Empty<int>(), livingCount);
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
