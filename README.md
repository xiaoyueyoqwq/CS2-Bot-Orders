# CS2-Bot-Orders

English | [简体中文](README.zh-CN.md)

A [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) plugin that makes CS2 bots obey **human site orders** — chatwheel "Ping Site A/B", classic radio, and middle-click pings — by injecting intent into the stock bot brain (`CCSBot`). It does not puppet WASD, does not `Follow` players, and does not rewrite aim, pathing, or economy: humans make the call, the existing bot AI still does the walking and fighting.

Current version: **0.1.9**

This is a standalone development repo. [CS2-Bot-Improver](https://github.com/xiaoyueyoqwq/CS2-Bot-Improver) is the combined distribution — do not develop this plugin on Improver feature branches.

## What it does

- **Site orders.** Any living human on the team can claim A or B (V-wheel "Ping Site A/B", or remapped chatwheel entries). An uncontested claim — even a single human's — sends every living bot to that site. Majority votes win; a 1-1 tie never splits the team (on T, the human bomb-carrier's claim breaks the tie; otherwise the previous assignment holds or nothing is injected).
- **World facts outrank orders.** `bomb_planted`, a ping landing on the C4 entity, and a loose/dropped bomb are treated as facts that override cheap chatter. Verbal bomb claims ("I planted at A", "Bomb was dropped", "Ping Bomb", …) are **ignored** — only engine events and entity hits count.
- **Ping alerts.** A middle-click ping tells bots within 1500 units "something here" (`LookAtSpot`, 5s); a warning ping (`type=13` or urgent) means "enemy here, hold" (`LookAtSpot` + `LastEnemyPosition`, 8s). Pings never become `GoalPosition` — bots are not walked onto the marker.
- **Dropped C4.** CTs (or a bomb pinged without a known site) rally around the loose bomb — all living bots `GuardNearby` at ~280 units. On T, a **death** drop sends the map-nearest bot to fetch while the rest guard; a living T dropping the bomb does **not** rally (a dropped bomb must not become a max-priority command).
- **Verified clear.** "Bombsite clear" / "Area clear" from a human whose `LastPlaceName` hard-belongs to that site pulls bots off it — only while the bomb isn't planted there.

## Command intake

Three parallel channels are listened to (none are blocked — other plugins/game systems still see them):

| Channel | Server entry | Handled tokens |
|---|---|---|
| V/C chatwheel | `playerchatwheel` command (`CW.*`) | `GoA`/`aplan`, `GoB`/`bplan` → site claim; `BombsiteClear`, `SectorClear` → clear; bomb-related verbals (`PlantedAt*`, `PlantingAt*`, `wePlanted`, `BombAt`, `DroppedBomb`, `PickedUpC4`/`WeHaveTheBomb`) → logged + ignored |
| Z classic radio | `player_radio` event | Slots are logged but **not mapped yet** (`RadioSlots` is intentionally empty until a live dump fills it — `go_a`/`go_b` are often dead in CS2 anyway) |
| Crosshair ping | `player_ping` event | Location ping → look-at alert; warning ping → enemy alert; ping on `weapon_c4`/`planted_c4` → bomb fact |
| — | `chatwheel_ping` command | Logged only (diagnostics) |

Only living humans (or a human currently controlling a bot) on T/CT count as commanders. Dead humans keep their last ballot until round wipe; the same SteamID taking over a bot can still issue orders. Bot-originated radio is never treated as a command.

## Round timeline

| Phase | Clock | Cheap orders | Injection |
|---|---|---|---|
| Opening collect | `round_start` → `round_freeze_end` (15s cap if the event never fires) | Recorded; same-human A/B changes are instant | No Goal writes while frozen; if an assignment exists, `IgnoreRadio` is armed and Follow cleared; flush on thaw |
| Tactical silence | freeze end → +5s | Change-of-mind ignored for teams that already have ballots; a **first** order is still accepted | Held / first-order injects immediately |
| Live | after thaw +5s | Arbitrated per claim | Recomputed and re-injected every 1s |

## Arbitration

Each round, each team keeps one ballot per human SteamID (last write wins — no vote UI, no countdown). `OrderArbitration.Resolve` is a pure function that folds live ballots + bomb fact + C4 holder + previous assignment into a single resolution; it self-checks a settlement table at plugin load. Priority (lower rank wins):

0. Round wipe → 20. Bomb fact → 30. Verified clear → 40. Site claim → 60. No-inject.

Tie handling: uncontested → all go; majority → all go; 1-1 on T → human C4-holder's claim; 1-1 otherwise → keep previous assignment or inject nothing. Late orders never pull bots off an in-progress execution — the table is recomputed whole, not last-wins.

## Injection layer

Writes reuse the stock `CCSBot` fields — no new pathing:

- `GoalPosition` ← bombsite collision-box center (rejects `(0,0,0)`), the loose C4's entity position, or the dropper's last origin
- `RepathTimer` expired, `MustRunTimer`/`HurryTimer` armed for 8s, `IsFollowing`/`IsStopping` cleared
- `IgnoreRadio` set while a human assignment is live, so bots can't be dragged off by each other's radio
- Bots already fighting (`IsAttacking`/`IsEnemyVisible`) are skipped — except during the post-thaw rush and for a T bot carrying the bomb, which always gets its Goal written
- `FetchAndGuard` roster: map-nearest bot fetches (T only, sticky per round), everyone else guards ~280 units around the bomb on a golden-angle spread

## Requirements

- CounterStrikeSharp **API ≥ 304** (built against `CounterStrikeSharp.API` 1.0.373)
- .NET **10**

## Build & install

```text
dotnet build -c Release
```

Copy `bin/Release/net10.0/BotOrders.dll` (and `.pdb` for symbols) into:

```text
game/csgo/addons/counterstrikesharp/plugins/BotOrders/
```

CSS hot-reload is enough; a full server restart is not required.

## Commands

| Command | Description |
| --- | --- |
| `css_botorders` | Dump round state: live/collect/silence clocks, per-team rank/action/primary/reason, bomb fact, ballots, fetch/guard rosters |

Load also runs an arbitration self-check; failures are logged as `[BotOrders] arbitration self-check fail …`.

## Tuning constants

All in `BotOrders.Commands.cs`:

| Constant | Value | Meaning |
|---|---|---|
| `OpeningCollectSeconds` | 15 | Cap on opening collect if `round_freeze_end` never arrives |
| `TacticalSilenceSeconds` | 5 | Post-thaw silence for cheap change-of-mind |
| `MustRunSeconds` | 8 | MustRun/Hurry timer on every goal write |
| `AlertNearbyUnits` | 1500 | Radius for ping alerts |
| `GuardOffsetUnits` | 280 | Guard ring radius around a loose bomb |
| `PingTypeWarning` | 13 | Ping type treated as "enemy here" |
| `AttentionLookSeconds` / `EnemyLookSeconds` | 5 / 8 | Look-at durations for attention vs enemy pings |

## Design notes

Threat model is a public server, not a five-stack: assume teammates will spam, mis-click, and contradict each other. The structure (not a troll list) makes cheap orders unable to steal the team — facts > verified clears > claims; a contested tie stalls or follows the bomb carrier rather than splitting bots. The full design document (Chinese) is `docs/notes-bot-command-following.md`.

## License

[GNU Affero General Public License v3.0](LICENSE)
