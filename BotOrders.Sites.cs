using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace BotOrders;

internal readonly record struct WorldPoint(float X, float Y, float Z)
{
    internal bool IsUsable => OrderCommands.IsUsableDest(X, Y);

    internal Vector ToVector() => new(X, Y, Z);

    internal static WorldPoint? From(Vector? origin)
    {
        if (origin == null)
            return null;
        if (!OrderCommands.IsUsableDest(origin.X, origin.Y))
            return null;
        return new WorldPoint(origin.X, origin.Y, origin.Z);
    }
}

internal static class OrderSites
{
    internal static bool TryBombsiteOrigin(Bombsite site, out Vector origin)
    {
        origin = new Vector();
        if (site is not (Bombsite.A or Bombsite.B))
            return false;

        CBombTarget? match = null;
        CBombTarget? fallback = null;
        foreach (var target in Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target"))
        {
            if (target == null || !target.IsValid)
                continue;

            bool isB = target.IsBombSiteB;
            if ((site == Bombsite.B && isB) || (site == Bombsite.A && !isB))
            {
                match = target;
                break;
            }

            fallback ??= target;
        }

        var chosen = match ?? fallback;
        return TryWorldCenter(chosen, out origin);
    }

    internal static bool TryC4Origin(out Vector origin)
    {
        origin = new Vector();
        foreach (var planted in Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4"))
        {
            if (planted == null || !planted.IsValid)
                continue;
            if (TryWorldCenter(planted, out origin))
                return true;
        }

        var carried = CarriedC4Indexes();
        foreach (var c4 in Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4"))
        {
            if (c4 == null || !c4.IsValid)
                continue;
            if (carried.Contains(c4.Index))
                continue;
            if (TryWorldCenter(c4, out origin))
                return true;
        }

        return false;
    }

    internal static bool TryWorldCenter(CBaseEntity? entity, out Vector origin)
    {
        origin = new Vector();
        if (entity == null || !entity.IsValid || entity.AbsOrigin is not Vector pos)
            return false;

        var collision = entity.Collision;
        var mins = collision?.Mins;
        var maxs = collision?.Maxs;
        if (mins != null && maxs != null)
        {
            origin = new Vector(
                pos.X + (mins.X + maxs.X) * 0.5f,
                pos.Y + (mins.Y + maxs.Y) * 0.5f,
                pos.Z + (mins.Z + maxs.Z) * 0.5f);
            if (OrderCommands.IsUsableDest(origin.X, origin.Y))
                return true;
        }

        origin = new Vector(pos.X, pos.Y, pos.Z);
        return OrderCommands.IsUsableDest(origin.X, origin.Y);
    }

    internal static Bombsite SiteFromPlantedC4()
    {
        CPlantedC4? planted = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4"))
        {
            if (entity == null || !entity.IsValid)
                continue;
            planted = entity;
            break;
        }

        if (planted == null)
            return Bombsite.Unknown;

        var fromIndex = OrderCommands.FromPlantedSiteIndex(planted.BombSite);
        if (fromIndex != Bombsite.Unknown)
            return fromIndex;

        if (planted.AbsOrigin == null)
            return Bombsite.Unknown;

        return NearestBombsite(planted.AbsOrigin);
    }

    internal static Bombsite NearestBombsite(Vector pos)
    {
        float bestA = float.MaxValue;
        float bestB = float.MaxValue;
        bool haveA = TryBombsiteOrigin(Bombsite.A, out var a);
        bool haveB = TryBombsiteOrigin(Bombsite.B, out var b);
        if (haveA)
            bestA = Dist2(pos, a);
        if (haveB)
            bestB = Dist2(pos, b);
        if (!haveA && !haveB)
            return Bombsite.Unknown;
        if (haveA && (!haveB || bestA <= bestB))
            return Bombsite.A;
        return Bombsite.B;
    }

    private static HashSet<uint> CarriedC4Indexes()
    {
        var carried = new HashSet<uint>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (player is not { IsValid: true, PawnIsAlive: true })
                continue;
            var pawn = player.PlayerPawn.Value;
            var weapons = pawn?.WeaponServices?.MyWeapons;
            if (weapons == null)
                continue;
            foreach (var handle in weapons)
            {
                var weapon = handle.Value;
                if (weapon == null || !weapon.IsValid)
                    continue;
                if (!string.Equals(weapon.DesignerName, "weapon_c4", StringComparison.OrdinalIgnoreCase))
                    continue;
                carried.Add(weapon.Index);
            }
        }

        return carried;
    }

    private static Vector Copy(Vector v) => new(v.X, v.Y, v.Z);

    private static float Dist2(Vector a, Vector b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
