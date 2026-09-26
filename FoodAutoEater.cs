using System.Collections.Generic;
using System.Linq;

namespace AutoNosh
{
    /// <summary>
    /// Decides whether the local player needs a food top-up right now, and which
    /// inventory item to eat for it. Eating itself goes through
    /// <see cref="Player.ConsumeItem"/> - the same path a manual right-click-eat uses -
    /// so the vanilla 3-slot cap, the half-burn-time re-eat gate, and food decay speed
    /// are all untouched.
    /// </summary>
    internal static class FoodAutoEater
    {
        // Food instances already warned about via NotifyExpiring, so the warning fires
        // once per approach to empty rather than every check while it stays due.
        private static readonly HashSet<Player.Food> WarnedExpiring = new HashSet<Player.Food>();
        private static bool _warnedNoFood;

        internal static void TryAutoEat(Player player)
        {
            var activeFoods = player.GetFoods();
            WarnedExpiring.IntersectWith(activeFoods);

            if (AutoNoshPlugin.NotifyExpiring.Value)
            {
                foreach (var food in activeFoods)
                {
                    if (IsDueForReplacement(food) && WarnedExpiring.Add(food))
                    {
                        player.Message(MessageHud.MessageType.Center,
                            $"AutoNosh: {food.m_item.m_shared.m_name} is running low");
                    }
                }
            }

            var hasOpenSlot = activeFoods.Count < 3;
            var hasSlotDue = activeFoods.Any(IsDueForReplacement);
            if (!hasOpenSlot && !hasSlotDue)
            {
                return;
            }

            // Only ever consider items that CanEat() would actually allow right now.
            // Player.CanConsumeItem() always shows a "can't eat more X" message on a
            // rejected attempt - it has no silent-probe mode - so guessing and letting a
            // bad pick fail spams that message. Filtering here means ConsumeItem below is
            // only ever called on something that will succeed.
            var eatable = GetFoodCandidates(player)
                .Where(i => CanEatNow(activeFoods, i))
                .ToList();

            if (eatable.Count == 0)
            {
                if (AutoNoshPlugin.NotifyFallback.Value && !_warnedNoFood)
                {
                    _warnedNoFood = true;
                    player.Message(MessageHud.MessageType.Center, "AutoNosh: no food to eat");
                }
                return;
            }
            _warnedNoFood = false;

            var profile = AutoNoshPlugin.FoodProfileConfig.Value;

            // Most urgent active food due for replacement, if any - the one this tick
            // should address. An open slot with nothing active yet has no "type" to match.
            var target = activeFoods.Where(IsDueForReplacement).OrderBy(f => f.m_time).FirstOrDefault();
            var best = PickFood(profile, target?.m_item, eatable, out var isNormalPick, out var missingFocusName);

            if (!player.ConsumeItem(player.GetInventory(), best, checkWorldLevel: false))
            {
                return;
            }

            var name = best.m_shared.m_name;
            string message = null;
            if (isNormalPick)
            {
                if (AutoNoshPlugin.NotifyEating.Value)
                {
                    message = $"AutoNosh: eating {name}";
                }
            }
            else if (AutoNoshPlugin.NotifyFallback.Value)
            {
                var reason = string.IsNullOrEmpty(missingFocusName) ? "no matching food" : $"no {missingFocusName} food";
                message = $"AutoNosh: {reason}, eating next best: {name}";
            }

            // Center messages overwrite each other rather than queue (MessageHud.ShowMessage),
            // so the low-stock warning rides along as a second line of the same message.
            var lowStock = BuildLowStockWarning(player, profile, best);
            if (lowStock != null)
            {
                message = message == null ? $"AutoNosh: {lowStock}" : $"{message}\n{lowStock}";
            }

            if (message != null)
            {
                player.Message(MessageHud.MessageType.Center, message);
            }

            AutoNoshPlugin.LogVerbose($"Auto-ate {name} (profile {profile}, normal pick: {isNormalPick})");
        }

        // Profile-driven pick for one food slot. `target` is the food being replaced, or
        // null when filling an open slot - only the None profile cares about it.
        private static ItemDrop.ItemData PickFood(
            FoodProfile profile, ItemDrop.ItemData target, List<ItemDrop.ItemData> eatable,
            out bool isNormalPick, out string missingFocusName)
        {
            if (profile == FoodProfile.None)
            {
                return PickForNoneProfile(target, eatable, out isNormalPick, out missingFocusName);
            }

            var matching = eatable.Where(i => FoodProfiles.Matches(profile, i)).ToList();
            if (matching.Count > 0)
            {
                isNormalPick = true;
                missingFocusName = "";
                var weights = FoodProfiles.GetWeights(profile);
                return matching.OrderByDescending(i => ScoreCandidate(i, weights)).First();
            }

            isNormalPick = false;
            missingFocusName = FoodProfiles.FocusName(profile);
            return PickByFallbackPriority(eatable);
        }

        // None profile: keep whatever the player is already eating going rather than
        // impose a fixed stat weighting. Prefers the exact same item; PreferBetterSameTypeFood
        // switches that to "best food of the same kind" instead. Falls back to
        // FallbackPriority when the food type that's expiring has nothing left to replace it.
        private static ItemDrop.ItemData PickForNoneProfile(
            ItemDrop.ItemData target, List<ItemDrop.ItemData> eatable,
            out bool isNormalPick, out string missingFocusName)
        {
            missingFocusName = "";

            if (target == null)
            {
                isNormalPick = true;
                return PickByFallbackPriority(eatable);
            }

            if (!AutoNoshPlugin.PreferBetterSameTypeFood.Value)
            {
                var sameItem = eatable.FirstOrDefault(i => i.m_shared.m_name == target.m_shared.m_name);
                if (sameItem != null)
                {
                    isNormalPick = true;
                    return sameItem;
                }
            }

            var dominant = FoodProfiles.Classify(target);
            var sameType = eatable.Where(i => FoodProfiles.Classify(i) == dominant).ToList();
            if (sameType.Count > 0)
            {
                isNormalPick = true;
                return sameType.OrderByDescending(i => ScoreCandidate(i, FoodProfiles.WeightsForStat(dominant))).First();
            }

            isNormalPick = false;
            missingFocusName = FoodProfiles.StatKindName(dominant);
            return PickByFallbackPriority(eatable);
        }

        // "Honey: 2 left" once the stack of what was just eaten is at or under
        // LowFoodWarningCount, plus (with NotifyNextFood) what would fill this slot once
        // it's gone. Null when there's nothing to warn about.
        private static string BuildLowStockWarning(Player player, FoodProfile profile, ItemDrop.ItemData eaten)
        {
            var threshold = AutoNoshPlugin.LowFoodWarningCount.Value;
            if (threshold <= 0)
            {
                return null;
            }

            var name = eaten.m_shared.m_name;
            // matchWorldLevel: false, because the eat above ignores world level too.
            var left = player.GetInventory().CountItems(name, matchWorldLevel: false);
            if (left > threshold)
            {
                return null;
            }

            var warning = left == 0 ? $"that was your last {name}" : $"{name}: {left} left";
            if (!AutoNoshPlugin.NotifyNextFood.Value)
            {
                return warning;
            }

            // Once this item is gone, the slot it now occupies is what gets refilled. The
            // other active foods can't fill it - eating one of those just tops up its own
            // slot - so exclude them along with the item running out.
            var otherActive = new HashSet<string>(player.GetFoods()
                .Select(f => f.m_item.m_shared.m_name)
                .Where(n => n != name));
            var eatable = GetFoodCandidates(player)
                .Where(i => i.m_shared.m_name != name && !otherActive.Contains(i.m_shared.m_name))
                .ToList();

            if (eatable.Count == 0)
            {
                return $"{warning}, no other food to fall back on";
            }

            var next = PickFood(profile, eaten, eatable, out _, out _);
            return $"{warning}, then {next.m_shared.m_name}";
        }

        // Walks FallbackPriorityConfig's stat order and picks the best food from the first
        // category that has anything available. White (balanced) foods qualify for every
        // category they provide a stat for; every food has at least one stat, so together
        // the three categories cover `eatable` completely - this always finds something.
        private static ItemDrop.ItemData PickByFallbackPriority(List<ItemDrop.ItemData> eatable)
        {
            foreach (var kind in FoodProfiles.GetFallbackOrder(AutoNoshPlugin.FallbackPriorityConfig.Value))
            {
                var ofKind = eatable.Where(i => FoodProfiles.QualifiesFor(i, kind)).ToList();
                if (ofKind.Count > 0)
                {
                    return ofKind.OrderByDescending(i => ScoreCandidate(i, FoodProfiles.WeightsForStat(kind))).First();
                }
            }

            // Unreachable in practice (see above), but never leave without a pick.
            return eatable.OrderByDescending(i => ScoreCandidate(i, FoodProfiles.BalancedWeights)).First();
        }

        private static float ScoreCandidate(ItemDrop.ItemData item, FoodWeights weights)
        {
            return FoodProfiles.Score(item, weights, AutoNoshPlugin.ConsiderFeastDuration.Value);
        }

        private static List<ItemDrop.ItemData> GetFoodCandidates(Player player)
        {
            return player.GetInventory()
                .GetAllItemsOfType(ItemDrop.ItemData.ItemType.Consumable)
                .Where(IsFood)
                .ToList();
        }

        // Mirrors Player.CanEat() (same-name re-eat is gated by IsDueForReplacement
        // regardless of open slots; a different food needs an open slot or some other
        // active food already due). Kept in lockstep with that method - re-check against
        // .valheim-src/valheim/Player.cs if food behavior ever seems to drift.
        private static bool CanEatNow(List<Player.Food> activeFoods, ItemDrop.ItemData item)
        {
            foreach (var food in activeFoods)
            {
                if (food.m_item.m_shared.m_name == item.m_shared.m_name)
                {
                    return IsDueForReplacement(food);
                }
            }

            if (activeFoods.Any(IsDueForReplacement))
            {
                return true;
            }

            return activeFoods.Count < 3;
        }

        // The mod's own, tighter version of Food.CanEatAgain(): still requires the game's
        // 50%-remaining floor, but EatThresholdPercent can narrow the window further so
        // auto-eat waits closer to empty instead of acting the instant it's allowed to.
        private static bool IsDueForReplacement(Player.Food food)
        {
            if (!food.CanEatAgain())
            {
                return false;
            }

            var burnTime = food.m_item.m_shared.m_foodBurnTime;
            if (burnTime <= 0f)
            {
                return true;
            }

            var remainingFraction = food.m_time / burnTime;
            return remainingFraction <= AutoNoshPlugin.EatThresholdPercent.Value / 100f;
        }

        // Food-only filter: potions and meads are also Consumable, but carry a
        // m_consumeStatusEffect instead of any of these three, so this excludes them
        // without needing to special-case item names.
        private static bool IsFood(ItemDrop.ItemData item)
        {
            var shared = item.m_shared;
            return shared.m_food > 0f || shared.m_foodStamina > 0f || shared.m_foodEitr > 0f;
        }
    }
}
