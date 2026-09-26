namespace AutoNosh
{
    /// <summary>
    /// A named weighting of health/stamina/eitr used to score candidate foods.
    /// Cycled in-game with the configurable hotkey, or set directly in the config file.
    /// </summary>
    public enum FoodProfile
    {
        Balanced,
        None,
        HeavyStamina,
        MassiveHealth,
        EitrMagic,
        MagicHealth,
    }

    /// <summary>
    /// A food's kind, matching the fork color on its inventory icon: Health is red,
    /// Stamina is yellow, Eitr is blue, Balanced is white. Used by the None profile to
    /// find "the same kind of food" and by fallback priority to rank stat categories.
    /// Balanced is never an entry in a fallback order; white foods join the Health,
    /// Stamina and Eitr groups they contribute to instead.
    /// </summary>
    internal enum FoodStatKind
    {
        Health,
        Stamina,
        Eitr,
        Balanced,
    }

    /// <summary>
    /// Order to try stat categories in when nothing in inventory matches what's actually
    /// wanted, so the mod still eats something sensible instead of the arbitrary
    /// highest-combined-value item.
    /// </summary>
    public enum FallbackPriority
    {
        HealthStaminaEitr,
        HealthEitrStamina,
        StaminaHealthEitr,
        StaminaEitrHealth,
        EitrHealthStamina,
        EitrStaminaHealth,
    }

    internal readonly struct FoodWeights
    {
        internal readonly float Health;
        internal readonly float Stamina;
        internal readonly float Eitr;

        internal FoodWeights(float health, float stamina, float eitr)
        {
            Health = health;
            Stamina = stamina;
            Eitr = eitr;
        }
    }

    internal static class FoodProfiles
    {
        // Neutral weighting: used by the Balanced profile, and as a last-resort scorer
        // when nothing else applies.
        internal static readonly FoodWeights BalancedWeights = new FoodWeights(1f, 1f, 1f);

        internal static FoodWeights GetWeights(FoodProfile profile)
        {
            switch (profile)
            {
                case FoodProfile.HeavyStamina:
                    return new FoodWeights(0.3f, 2f, 0.3f);
                case FoodProfile.MassiveHealth:
                    return new FoodWeights(2f, 0.3f, 0.3f);
                case FoodProfile.EitrMagic:
                    return new FoodWeights(0.2f, 0.2f, 2.5f);
                case FoodProfile.MagicHealth:
                    return new FoodWeights(1.5f, 0.2f, 1.5f);
                case FoodProfile.Balanced:
                case FoodProfile.None:
                default:
                    return BalancedWeights;
            }
        }

        /// <summary>
        /// Whether a candidate food actually emphasizes the stat this profile cares about.
        /// Balanced has no focus stat, so anything edible matches. None is handled
        /// separately by FoodAutoEater and never calls this.
        /// </summary>
        internal static bool Matches(FoodProfile profile, ItemDrop.ItemData item)
        {
            var shared = item.m_shared;
            switch (profile)
            {
                case FoodProfile.HeavyStamina:
                    return shared.m_foodStamina > 0f;
                case FoodProfile.MassiveHealth:
                    return shared.m_food > 0f;
                case FoodProfile.EitrMagic:
                case FoodProfile.MagicHealth:
                    return shared.m_foodEitr > 0f;
                case FoodProfile.Balanced:
                case FoodProfile.None:
                default:
                    return true;
            }
        }

        // The decay curve (Player.UpdateFood's Pow(remaining/duration, 0.3)) has the same
        // shape regardless of duration, so peak value alone is the correct comparison
        // between two freshly-eaten foods - a feast's lower peak is a real tradeoff, not
        // an artifact of its longer timer. considerDuration opts into a different
        // question instead: "how much does this deliver before I have to act again,"
        // which is where a feast's much longer burn time can outweigh a lower peak.
        internal static float Score(ItemDrop.ItemData item, FoodWeights weights, bool considerDuration)
        {
            var shared = item.m_shared;
            var peak = shared.m_food * weights.Health
                       + shared.m_foodStamina * weights.Stamina
                       + shared.m_foodEitr * weights.Eitr;
            return considerDuration ? peak * shared.m_foodBurnTime : peak;
        }

        /// <summary>
        /// Human-readable name of the stat a profile favors, for notification text.
        /// Empty for Balanced/None, which have no single fixed focus stat.
        /// </summary>
        internal static string FocusName(FoodProfile profile)
        {
            switch (profile)
            {
                case FoodProfile.HeavyStamina:
                    return "Stamina";
                case FoodProfile.MassiveHealth:
                    return "Health";
                case FoodProfile.EitrMagic:
                case FoodProfile.MagicHealth:
                    return "Eitr";
                case FoodProfile.Balanced:
                case FoodProfile.None:
                default:
                    return "";
            }
        }

        internal static FoodProfile Next(FoodProfile current)
        {
            var values = (FoodProfile[])System.Enum.GetValues(typeof(FoodProfile));
            var index = System.Array.IndexOf(values, current);
            return values[(index + 1) % values.Length];
        }

        // Same rule, same order, as the fork color the game draws in InventoryGrid.UpdateGui,
        // so the kind here always agrees with the icon the player sees.
        internal static FoodStatKind Classify(ItemDrop.ItemData item)
        {
            var shared = item.m_shared;
            if (shared.m_food < shared.m_foodEitr / 2f && shared.m_foodStamina < shared.m_foodEitr / 2f)
            {
                return FoodStatKind.Eitr;
            }
            if (shared.m_foodStamina < shared.m_food / 2f)
            {
                return FoodStatKind.Health;
            }
            if (shared.m_food < shared.m_foodStamina / 2f)
            {
                return FoodStatKind.Stamina;
            }
            return FoodStatKind.Balanced;
        }

        // Whether a food belongs in a fallback group: its own kind, or - for white foods -
        // any stat it actually provides.
        internal static bool QualifiesFor(ItemDrop.ItemData item, FoodStatKind kind)
        {
            var itemKind = Classify(item);
            if (itemKind == kind)
            {
                return true;
            }
            if (itemKind != FoodStatKind.Balanced)
            {
                return false;
            }

            var shared = item.m_shared;
            switch (kind)
            {
                case FoodStatKind.Health:
                    return shared.m_food > 0f;
                case FoodStatKind.Stamina:
                    return shared.m_foodStamina > 0f;
                case FoodStatKind.Eitr:
                    return shared.m_foodEitr > 0f;
                default:
                    return false;
            }
        }

        internal static FoodWeights WeightsForStat(FoodStatKind kind)
        {
            switch (kind)
            {
                case FoodStatKind.Stamina:
                    return GetWeights(FoodProfile.HeavyStamina);
                case FoodStatKind.Eitr:
                    return GetWeights(FoodProfile.EitrMagic);
                case FoodStatKind.Health:
                    return GetWeights(FoodProfile.MassiveHealth);
                case FoodStatKind.Balanced:
                default:
                    return BalancedWeights;
            }
        }

        internal static string StatKindName(FoodStatKind kind)
        {
            switch (kind)
            {
                case FoodStatKind.Stamina:
                    return "Stamina";
                case FoodStatKind.Eitr:
                    return "Eitr";
                case FoodStatKind.Balanced:
                    return "Balanced";
                case FoodStatKind.Health:
                default:
                    return "Health";
            }
        }

        internal static FoodStatKind[] GetFallbackOrder(FallbackPriority priority)
        {
            switch (priority)
            {
                case FallbackPriority.HealthEitrStamina:
                    return new[] { FoodStatKind.Health, FoodStatKind.Eitr, FoodStatKind.Stamina };
                case FallbackPriority.StaminaHealthEitr:
                    return new[] { FoodStatKind.Stamina, FoodStatKind.Health, FoodStatKind.Eitr };
                case FallbackPriority.StaminaEitrHealth:
                    return new[] { FoodStatKind.Stamina, FoodStatKind.Eitr, FoodStatKind.Health };
                case FallbackPriority.EitrHealthStamina:
                    return new[] { FoodStatKind.Eitr, FoodStatKind.Health, FoodStatKind.Stamina };
                case FallbackPriority.EitrStaminaHealth:
                    return new[] { FoodStatKind.Eitr, FoodStatKind.Stamina, FoodStatKind.Health };
                case FallbackPriority.HealthStaminaEitr:
                default:
                    return new[] { FoodStatKind.Health, FoodStatKind.Stamina, FoodStatKind.Eitr };
            }
        }
    }
}
