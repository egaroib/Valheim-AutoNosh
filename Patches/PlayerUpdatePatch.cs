using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AutoNosh.Patches
{
    // Player.Update() early-returns for every Player instance that isn't the locally
    // owned character (it checks m_nview.IsOwner() itself), but we still guard on
    // m_localPlayer here since Update runs every frame and the check must stay cheap.
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class PlayerUpdatePatch
    {
        private static float _nextEatCheck;

        private static void Postfix(Player __instance)
        {
            if (!Player.m_localPlayerExists || __instance != Player.m_localPlayer)
            {
                return;
            }

            if (ZInput.GetButtonDown(AutoNoshPlugin.CycleProfileButtonName))
            {
                CycleProfile(__instance);
            }

            if (!AutoNoshPlugin.Enabled.Value)
            {
                return;
            }

            if (Time.time < _nextEatCheck)
            {
                return;
            }
            _nextEatCheck = Time.time + AutoNoshPlugin.CheckIntervalSeconds.Value;

            if (AutoNoshPlugin.AfkPauseEnabled.Value
                && AfkTracker.IdleSeconds >= AutoNoshPlugin.AfkThresholdMinutes.Value * 60f)
            {
                AutoNoshPlugin.LogVerbose($"Skipping auto-eat, idle {AfkTracker.IdleSeconds:0}s.");
                return;
            }

            // Bukeperries (and anything else using the same status effect) purge a random
            // food slot once a second for the effect's duration via SE_Puke.RemoveOneFood().
            // Without this, an open slot from that purge gets refilled immediately, which
            // just hands the new food straight back to the purge on its next tick.
            if (__instance.GetSEMan().GetStatusEffects().Any(se => se is SE_Puke))
            {
                AutoNoshPlugin.LogVerbose("Skipping auto-eat, player is feeling sick.");
                return;
            }

            FoodAutoEater.TryAutoEat(__instance);
        }

        private static void CycleProfile(Player player)
        {
            var next = FoodProfiles.Next(AutoNoshPlugin.FoodProfileConfig.Value);
            AutoNoshPlugin.FoodProfileConfig.Value = next;
            AutoNoshPlugin.LogVerbose($"Food profile cycled to {next}.");
            player.Message(MessageHud.MessageType.Center, $"AutoNosh: {next}");
        }
    }
}
