# CS2-Bot-Orders

[English](README.md) | 简体中文

一个 [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) 插件，让 CS2 Bot 服从**人类的选点指令**——V/C 轮盘的「侦测 A/B」、经典无线电、准星标记——方式是往原版 Bot 脑（`CCSBot`）里写意图。它不用 WASD 傀儡化 Bot、不 `Follow` 玩家、不重写枪法/寻路/经济：人类做判断，走位和交火仍交给原有 Bot AI。

当前版本：**0.1.9**

本仓库是独立开发仓。[CS2-Bot-Improver](https://github.com/xiaoyueyoqwq/CS2-Bot-Improver) 是合包分发仓——不要在 Improver 的功能分支上开发本插件。

## 功能

- **选点指令。** 队内任何存活真人可以认领 A 或 B（V 盘「侦测 A/B」或重映射的轮盘项）。无争议认领——哪怕只有一个真人——全队存活 Bot 即去该点。多数票胜出；1-1 绝不把队伍拆开（T 方由持包真人的票破平；否则保持上一拍编制或不注入）。
- **世界事实压过口令。** `bomb_planted`、ping 命中 C4 实体、地上/掉落的包都按事实处理，压过廉价口令。口头报包（「我在 A 下了」「包掉了」「侦测炸弹」……）一律**忽略**——只认引擎事件和实体命中。
- **标点警示。** 中键 ping 告诉 1500 单位内的 Bot「这里有情况」（`LookAtSpot`，5 秒）；警告 ping（`type=13` 或 urgent）表示「有敌人，架枪」（`LookAtSpot` + `LastEnemyPosition`，8 秒）。ping 永远不会写成 `GoalPosition`——不会把 Bot 拉到标记点上。
- **掉包处理。** CT（或 ping 中包但点位未知）围绕地上的包集结——全部存活 Bot 在包约 280 单位外 `GuardNearby`。T 方**死亡**掉包时，全图最近的一个 Bot 去捡，其余守卫；T **活人**扔包不集结（扔包不能变成最高优先级指挥）。
- **已验证清点。** 真人喊「Bombsite clear」/「Area clear」且其 `LastPlaceName` 硬归属该点时，Bot 撤离该点——前提是包没有下在那里。

## 指令入口

并行监听三条通道（都不拦截——其他插件和游戏系统照常收到）：

| 通道 | 服务端入口 | 处理的 token |
|---|---|---|
| V/C 轮盘 | `playerchatwheel` 命令（`CW.*`） | `GoA`/`aplan`、`GoB`/`bplan` → 选点；`BombsiteClear`、`SectorClear` → 清点；口头报包（`PlantedAt*`、`PlantingAt*`、`wePlanted`、`BombAt`、`DroppedBomb`、`PickedUpC4`/`WeHaveTheBomb`）→ 记录并忽略 |
| Z 经典无线电 | `player_radio` 事件 | Slot 只记录**尚未映射**（`RadioSlots` 故意留空，等实机 dump 填表——CS2 里 `go_a`/`go_b` 本来就常失效） |
| 准星标记 | `player_ping` 事件 | 普通 ping → 注视警示；警告 ping → 敌情警示；ping 中 `weapon_c4`/`planted_c4` → 包事实 |
| — | `chatwheel_ping` 命令 | 仅记录（诊断用） |

只有 T/CT 上的存活真人（或正在接管 Bot 的真人）算指挥。死人保留最后一张票到回合清空；同一 SteamID 接管 Bot 后仍可下令。Bot 自己发的无线电永远不当指令。

## 回合时间线

| 阶段 | 时钟 | 廉价口令 | 注入 |
|---|---|---|---|
| 开局收集 | `round_start` → `round_freeze_end`（事件不到则 15 秒封顶） | 记账；同一人改 A/B 立即生效 | 冻结中不写 Goal；有编制则先开 `IgnoreRadio` + 清 Follow；解冻瞬间 flush |
| 无线电静默 | 解冻 → +5 秒 | 已有票的队忽略改口；**第一下指令仍收下** | 保持 / 第一下立刻注入 |
| 正式阶段 | 解冻 +5 秒后 | 逐条仲裁 | 每 1 秒重算并复写 |

## 仲裁

每回合每队每个真人 SteamID 只保留一张票（后写覆盖——没有投票界面，没有倒计时）。`OrderArbitration.Resolve` 是纯函数，把活票 + 包事实 + 持包人 + 上一拍编制折叠成唯一决议；插件加载时会跑一张结算表自检。优先级（数字越小越高）：

0. 回合清空 → 20. 包事实 → 30. 已验证清点 → 40. 选点认领 → 60. 不注入。

平分处理：无争议 → 全员去；多数 → 全员去；T 方 1-1 → 持包真人的票；其余 1-1 → 保持上一拍编制或不注入。后到的指令抽不走正在执行的 Bot——整表重算，不是后到覆盖。

## 注入层

复用原版 `CCSBot` 字段，不写新寻路：

- `GoalPosition` ← `func_bomb_target` 碰撞盒中心（拒绝 `(0,0,0)`）、地上 C4 实体位置、或丢包者坐标
- `RepathTimer` 过期，`MustRunTimer`/`HurryTimer` 拉满 8 秒，清 `IsFollowing`/`IsStopping`
- 人类编制存续期间 `IgnoreRadio` 置位，Bot 不会被彼此的原生无线电拽走
- 已在交火（`IsAttacking`/`IsEnemyVisible`）的 Bot 跳过——解冻冲锋和 T 持包 Bot 除外：持包 Bot 的 Goal 永远照写
- `FetchAndGuard` 编制：全图最近 1 人捡包（仅 T，回合内粘滞），其余 Bot 按黄金角散布在包约 280 单位处守卫

## 运行要求

- CounterStrikeSharp **API ≥ 304**（构建基于 `CounterStrikeSharp.API` 1.0.373）
- .NET **10**

## 构建与安装

```text
dotnet build -c Release
```

把 `bin/Release/net10.0/BotOrders.dll`（需要符号则连 `.pdb`）拷到：

```text
game/csgo/addons/counterstrikesharp/plugins/BotOrders/
```

CSS 热重载即可，不需要整服重启。

## 命令

| 命令 | 说明 |
| --- | --- |
| `css_botorders` | 打印回合状态：live/collect/静默时钟、各队 rank/action/primary/reason、包事实、票、捡包/守卫编制 |

插件加载时会跑仲裁自检，失败打 `[BotOrders] arbitration self-check fail …`。

## 调参常量

全部集中在 `BotOrders.Commands.cs`：

| 常量 | 值 | 含义 |
|---|---|---|
| `OpeningCollectSeconds` | 15 | `round_freeze_end` 不到时开局收集的封顶时长 |
| `TacticalSilenceSeconds` | 5 | 解冻后廉价改口的静默时长 |
| `MustRunSeconds` | 8 | 每次写 Goal 附带的 MustRun/Hurry 时长 |
| `AlertNearbyUnits` | 1500 | 标点警示半径 |
| `GuardOffsetUnits` | 280 | 围绕落包的守卫半径 |
| `PingTypeWarning` | 13 | 视为「有敌人」的 ping 类型 |
| `AttentionLookSeconds` / `EnemyLookSeconds` | 5 / 8 | 普通/敌情 ping 的注视时长 |

## 设计说明

威胁模型按社区服设计，不按五排：假设队友会乱喊、误触、对着干。用结构（而不是 troll 黑名单）保证廉价口令抢不走全队——事实 > 已验证清点 > 选点认领；对干时要么停住要么跟包走，绝不把 Bot 对半分。完整设计文档（中文）见 `docs/notes-bot-command-following.md`。

## 许可证

[GNU Affero General Public License v3.0](LICENSE)
