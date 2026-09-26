# AutoNosh

A smart auto-eat mod for food only — no potions or meads. It watches your active food
buffs and, when a slot is empty or about to need a refresh, eats the best food in your
inventory for your current profile. Food still expires at normal vanilla speed; this mod
only automates pressing the button you'd press anyway.

## Who needs to install this

**You only.** This only calls the same eat action a manual right-click-eat already uses,
on your own inventory and your own character. It doesn't change any game rule or touch
anything shared with other players, so nobody else in your group needs it installed.

## How it picks a food

Every couple of seconds, if you have an open food slot or one about to drop off, AutoNosh
scores every food item in your inventory (potions and meads are ignored) against your
active **food profile** and eats the best match:

- **Balanced** — highest total food value, no preference.
- **None** — keeps whatever you're already eating going rather than impose a preference
  of its own. See "None profile" below.
- **Heavy Stamina** — favors stamina food.
- **Massive Health** — favors health food.
- **Eitr/Magic** — favors eitr food.
- **Magic/Health** — favors eitr and health together, deprioritizing stamina.

If nothing in your inventory actually fits the active profile (e.g. Heavy Stamina but you
have no stamina food on you), it falls back through `FallbackPriority` — see "Fallback
priority" below — instead of just grabbing whatever scores highest overall.

Press the cycle key (default **F4**, rebindable in the controls menu) to step through the
six profiles at any time — useful for swapping from a base-building loadout to an
adventuring one on the fly.

## None profile

Pick your own three foods manually, and let AutoNosh just keep them topped up instead of
steering you toward a fixed stat weighting. When a food you're eating is due for a refresh:

- By default, it re-eats the exact same food if you still have one.
- If that food is gone, or `PreferBetterSameTypeFood` is turned on, it picks the best food
  of the same kind from your inventory instead. "Kind" is the fork color on the item's
  inventory icon: red (health), yellow (stamina), blue (eitr) or white (balanced). So if
  you were eating a weaker yellow food and a stronger one is available, it'll switch up,
  and a white food is replaced by another white food.
- If nothing of that kind is left either, it falls back through `FallbackPriority`, same
  as every other profile.

## Fallback priority

`FallbackPriority` sets the stat category order to try when a profile can't find a food
that matches what it actually wants (or None runs out of the same kind it was eating): it
tries each category in order and eats the best food from the first one that has anything
available, rather than just grabbing whatever scores highest combined. White (balanced)
foods count toward every category they provide a stat for, so a feast can satisfy a
Stamina-first order instead of being buried under Health. Default is
Health → Stamina → Eitr.

## Feasts and long-duration food

By default, AutoNosh compares foods purely by peak stat value, the same way at any point
in a food's life - the game's decay curve has the same shape regardless of duration, so
comparing peak values is the correct way to compare two freshly-eaten foods. That means a
feast's lower peak numbers are a real tradeoff by default, not just an artifact of its much
longer timer.

Turn on `ConsiderFeastDuration` to weight by peak value × duration instead. This can flip
the pick toward a longer-lasting feast even when its peak stats are lower than a shorter
food's, on the logic that it delivers more before you have to act again and ties up fewer
of your three food slots for a given stat spread. Off by default since it's a real
trade-off, not a strict improvement.

## When it acts

By default AutoNosh acts the moment the game allows a top-up at all — the same point a
food's tooltip timer turns eligible for a re-eat. Lower `EatThresholdPercent` if you'd
rather it hold off and let food run closer to empty before stepping in; it can only ever
wait *longer* than the game's own 50%-remaining floor, never act earlier than that.

## Notifications

All under `[Notifications]` in the config, on by default:

- **Eating** — a food matching your active profile was eaten.
- **Expiring** — an active food just crossed `EatThresholdPercent`, whether or not a
  replacement was found. Fires once per food, not on every check.
- **Fallback** — nothing in your inventory matched your profile's focus stat, so the next
  best food was eaten instead; or nothing edible was found at all.
- **Low food** — after an auto-eat, you have `LowFoodWarningCount` (default 3) or fewer of
  that food left, e.g. "Honey: 2 left, then Cooked meat". The "then" part, from
  `NotifyNextFood`, is what AutoNosh will eat in that slot once you run out. Set the count
  to 0 to turn the warning off.

## AFK pause

If you've had no input for a while (default 5 minutes), auto-eating pauses so it doesn't
burn through food while you're away from the keyboard. Food already active keeps decaying
at normal speed regardless — this never pauses expiration, only the auto-eating itself.

## Sickness

Bukeperries (and anything else that gives the same "feeling sick" effect) purge a random
active food once a second for the effect's duration. AutoNosh pauses entirely while that's
active so it doesn't just hand the purge a fresh food to remove on its next tick. This
isn't configurable — eating through it would only feed the purge.

## Requirements

- [BepInExPack Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
- [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)

## Installation

Install with a mod manager, or drop `AutoNosh.dll` into `BepInEx/plugins/`.

## Configuration

Settings live in `BepInEx/config/com.ragemedia.autonosh.cfg`, generated on first launch.
All settings are local preferences (not admin-synced) — set them however you like per
player.

| Section | Setting | Default | What it does |
|---|---|---|---|
| General | `Enabled` | `true` | Master toggle for auto-eating. |
| General | `CheckIntervalSeconds` | `2` | How often to check whether a slot needs refilling. |
| General | `EatThresholdPercent` | `50` | Act once a food drops to this % of its duration remaining. Lower = wait longer. |
| General | `VerboseLogging` | `false` | Debug logging. |
| Profile | `FoodProfile` | `Balanced` | Which stat to favor when picking a replacement food. |
| Profile | `CycleProfileKey` | `F4` | Key that cycles to the next profile in game. |
| Profile | `PreferBetterSameTypeFood` | `false` | None profile only: prefer a stronger same-kind food over re-eating the exact same one. |
| Profile | `FallbackPriority` | `HealthStaminaEitr` | Stat category order to try when a profile can't find a matching food. |
| Profile | `ConsiderFeastDuration` | `false` | Weight by peak value × duration instead of peak value alone. |
| AFK | `AfkPauseEnabled` | `true` | Pause auto-eating while idle. |
| AFK | `AfkThresholdMinutes` | `5` | Minutes of no input before it pauses. |
| Notifications | `NotifyEating` | `true` | Message when a profile-matching food is eaten. |
| Notifications | `NotifyExpiring` | `true` | Message once when a food crosses `EatThresholdPercent`. |
| Notifications | `NotifyFallback` | `true` | Message on next-best fallback or no food found. |
| Notifications | `LowFoodWarningCount` | `3` | Warn after eating when this many or fewer are left (0 = off). |
| Notifications | `NotifyNextFood` | `true` | Add the food that will be eaten next to the low-food warning. |
