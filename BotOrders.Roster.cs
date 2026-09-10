using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace BotOrders;

internal sealed record RosterAssignment(
    IReadOnlyList<int> PrimarySlots,
    IReadOnlyList<int> FakeSlots,
    int LivingCount,
    int FakeCount);

internal static class OrderRoster
{
    internal static int ComputeFakeCount(int n)
    {
        if (n < 4)
            return 0;

        int fake = Math.Min(Math.Max(2, n * 2 / 5), (n - 1) / 2);
        if (n - fake <= fake)
            fake = Math.Max(0, (n - 1) / 2);
        if (n - fake <= fake)
            return 0;
        return fake;
    }

    internal static RosterAssignment Assign(
        IReadOnlyList<CCSPlayerController> living,
        CsTeam team)
    {
        var ordered = living
            .Where(p => p.IsValid)
            .OrderBy(p => p.Slot)
            .ToList();

        int n = ordered.Count;
        int fakeCount = ComputeFakeCount(n);
        int primaryCount = n - fakeCount;

        var humans = ordered.Where(p => !p.IsBot).ToList();
        var bots = ordered.Where(p => p.IsBot).ToList();

        var primary = new List<CCSPlayerController>(primaryCount);
        var fake = new List<CCSPlayerController>(fakeCount);

        foreach (var human in humans)
        {
            if (primary.Count < primaryCount)
                primary.Add(human);
        }

        CCSPlayerController? c4Bot = null;
        if (team == CsTeam.Terrorist)
        {
            c4Bot = bots.FirstOrDefault(HasC4);
            if (c4Bot != null && !primary.Contains(c4Bot) && primary.Count < primaryCount)
                primary.Add(c4Bot);
        }

        foreach (var bot in bots)
        {
            if (primary.Contains(bot) || fake.Contains(bot))
                continue;
            if (primary.Count < primaryCount)
                primary.Add(bot);
            else
                fake.Add(bot);
        }

        if (team == CsTeam.Terrorist && c4Bot != null && fake.Contains(c4Bot) && primary.Count > 0)
        {
            var swap = primary.LastOrDefault(p => p.IsBot && !HasC4(p)) ?? primary[^1];
            primary.Remove(swap);
            fake.Remove(c4Bot);
            primary.Add(c4Bot);
            fake.Add(swap);
        }

        return new RosterAssignment(
            primary.Select(p => p.Slot).ToArray(),
            fake.Select(p => p.Slot).ToArray(),
            n,
            fake.Count);
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
