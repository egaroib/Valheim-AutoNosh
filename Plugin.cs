using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace AutoNosh
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Auto-eating is a purely local decision: it calls the same Player.ConsumeItem path a
    // manual right-click-eat already uses, on the local player's own inventory. It never
    // touches ZDOs, drop tables, or anything another peer's client would need to agree on,
    // so nobody else in a group needs this installed. See references/multiplayer.md.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.Minor)]
    public class AutoNoshPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.ragemedia.autonosh";
        public const string PluginName = "AutoNosh";
        public const string PluginVersion = "0.5.0";

        internal const string CycleProfileButtonName = "AutoNosh_CycleProfile";

        internal static AutoNoshPlugin Instance;
        internal static ConfigEntry<bool> VerboseLogging;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> CheckIntervalSeconds;
        internal static ConfigEntry<float> EatThresholdPercent;
        internal static ConfigEntry<FoodProfile> FoodProfileConfig;
        internal static ConfigEntry<KeyCode> CycleProfileKey;
        internal static ConfigEntry<bool> PreferBetterSameTypeFood;
        internal static ConfigEntry<FallbackPriority> FallbackPriorityConfig;
        internal static ConfigEntry<bool> ConsiderFeastDuration;
        internal static ConfigEntry<bool> AfkPauseEnabled;
        internal static ConfigEntry<float> AfkThresholdMinutes;

        internal static ConfigEntry<bool> NotifyEating;
        internal static ConfigEntry<bool> NotifyExpiring;
        internal static ConfigEntry<bool> NotifyFallback;
        internal static ConfigEntry<int> LowFoodWarningCount;
        internal static ConfigEntry<bool> NotifyNextFood;

        private readonly Harmony _harmony = new Harmony(PluginGuid);

        private void Awake()
        {
            Instance = this;
            BindConfig();
            RegisterKeybind();
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            VerifyPatches();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private void BindConfig()
        {
            VerboseLogging = Config.Bind(
                "General", "VerboseLogging", false,
                "Log detailed per-event output. Noisy; for debugging only.");

            Enabled = Config.Bind(
                "General", "Enabled", true,
                "Master toggle for auto-eating.");

            CheckIntervalSeconds = Config.Bind(
                "General", "CheckIntervalSeconds", 2f,
                new ConfigDescription(
                    "How often to re-check whether a food slot needs refilling.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            EatThresholdPercent = Config.Bind(
                "General", "EatThresholdPercent", 50f,
                new ConfigDescription(
                    "Auto-eat when a food's remaining time drops to this percent of its total " +
                    "duration. 50 (default) acts the moment the game allows a top-up at all; " +
                    "lower it to wait longer and let food run closer to empty first. The game " +
                    "itself never allows a top-up above 50%, so values here only ever narrow " +
                    "that window, never widen it.",
                    new AcceptableValueRange<float>(1f, 50f)));

            FoodProfileConfig = Config.Bind(
                "Profile", "FoodProfile", FoodProfile.Balanced,
                "Which stat this favors when picking a replacement food. Balanced picks the " +
                "highest total food value; None keeps whatever you're already eating (see " +
                "PreferBetterSameTypeFood); the rest favor stamina, health, or eitr food. All " +
                "fall back to FallbackPriority when nothing in your inventory fits.");

            CycleProfileKey = Config.Bind(
                "Profile", "CycleProfileKey", KeyCode.F4,
                "Key that cycles FoodProfile to the next preset in game.");

            PreferBetterSameTypeFood = Config.Bind(
                "Profile", "PreferBetterSameTypeFood", false,
                "Only affects the None profile. False (default): re-eat the exact same food " +
                "you already had whenever it's available. True: eat the best food of the same " +
                "kind (fork color: red health, yellow stamina, blue eitr, white balanced) " +
                "instead, even if it's a different item you have.");

            FallbackPriorityConfig = Config.Bind(
                "Profile", "FallbackPriority", FallbackPriority.HealthStaminaEitr,
                "Stat category order to try when a profile (or None with nothing of the same " +
                "kind left) can't find a matching food - picks the best food from the first " +
                "category that has anything available.");

            ConsiderFeastDuration = Config.Bind(
                "Profile", "ConsiderFeastDuration", false,
                "False (default): pick by peak stat value only, so a food's numbers are " +
                "compared the same way whether it lasts 20 minutes or 50. True: weight by " +
                "peak value times duration, which can make a longer-lasting feast with lower " +
                "peak stats win over a shorter, higher-peak food that needs re-eating sooner.");

            AfkPauseEnabled = Config.Bind(
                "AFK", "AfkPauseEnabled", true,
                "Stop auto-eating while the player has been idle for AfkThresholdMinutes. " +
                "Food already active keeps decaying normally either way.");

            AfkThresholdMinutes = Config.Bind(
                "AFK", "AfkThresholdMinutes", 5f,
                new ConfigDescription(
                    "Minutes of no input before auto-eating pauses.",
                    new AcceptableValueRange<float>(1f, 60f)));

            NotifyEating = Config.Bind(
                "Notifications", "NotifyEating", true,
                "Show a message when AutoNosh eats a food that matches your active profile.");

            NotifyExpiring = Config.Bind(
                "Notifications", "NotifyExpiring", true,
                "Show a message once when an active food drops to EatThresholdPercent " +
                "remaining, whether or not a replacement is found.");

            NotifyFallback = Config.Bind(
                "Notifications", "NotifyFallback", true,
                "Show a message when no inventory food matches your active profile's focus " +
                "stat and AutoNosh eats the next-best food instead, or when it can't find any " +
                "food to eat at all.");

            LowFoodWarningCount = Config.Bind(
                "Notifications", "LowFoodWarningCount", 3,
                new ConfigDescription(
                    "After AutoNosh eats a food, warn when you have this many or fewer of it " +
                    "left in your inventory. 0 turns the warning off.",
                    new AcceptableValueRange<int>(0, 50)));

            NotifyNextFood = Config.Bind(
                "Notifications", "NotifyNextFood", true,
                "Add to the low-food warning which food AutoNosh will eat in that slot once " +
                "you run out, or that there's nothing left to fall back on.");
        }

        private void RegisterKeybind()
        {
            InputManager.Instance.AddButton(PluginGuid, new ButtonConfig
            {
                Name = CycleProfileButtonName,
                Config = CycleProfileKey,
                HintToken = "$autonosh_cycle_profile",
            });
        }

        /// <summary>
        /// Reports what Harmony actually bound, and errors loudly on any [HarmonyPatch]
        /// class whose target method could not be resolved.
        ///
        /// This exists because a patch aimed at a method that no longer exists compiles
        /// cleanly and then does nothing at runtime. Without this check, a broken mod and
        /// a working mod produce identical logs.
        /// </summary>
        private void VerifyPatches()
        {
            var bound = _harmony.GetPatchedMethods().ToList();
            LogInfo($"Harmony bound {bound.Count} target(s):");
            foreach (var m in bound)
            {
                LogInfo($"  bound  {m.DeclaringType?.FullName}.{m.Name}");
            }

            var unresolved = 0;
            var patchClasses = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), true).Any());

            foreach (var type in patchClasses)
            {
                var attrs = type.GetCustomAttributes(typeof(HarmonyPatch), true)
                    .Cast<HarmonyPatch>()
                    .Select(a => a.info)
                    .ToList();

                HarmonyMethod merged;
                try
                {
                    merged = HarmonyMethod.Merge(attrs);
                }
                catch (Exception e)
                {
                    LogError($"  UNRESOLVED  {type.Name}: could not merge patch attributes: {e.Message}");
                    unresolved++;
                    continue;
                }

                // Patches targeting constructors, getters, or resolved via TargetMethod()
                // cannot be checked this way. Skip rather than report a false alarm.
                if (merged?.declaringType == null || string.IsNullOrEmpty(merged.methodName))
                {
                    continue;
                }

                var target = AccessTools.Method(
                    merged.declaringType, merged.methodName, merged.argumentTypes);

                if (target == null)
                {
                    unresolved++;
                    LogError($"  UNRESOLVED  {type.Name} -> " +
                             $"{merged.declaringType.Name}.{merged.methodName} does not exist. " +
                             "This patch will never run. The game version likely changed; " +
                             "re-check the method against the decompiled source.");
                }
            }

            if (unresolved > 0)
            {
                LogError($"{PluginName}: {unresolved} patch target(s) failed to resolve. " +
                         "This mod is loaded but not fully functional.");
            }
            else if (bound.Count == 0)
            {
                LogWarning($"{PluginName}: loaded, but bound zero patches. " +
                           "Expected if this mod only adds content via Jotunn.");
            }
            else
            {
                LogInfo($"{PluginName} v{PluginVersion} ready.");
            }
        }

        // Consistent prefix so log triage is a grep, not an inference.
        internal static void LogInfo(string msg) => Instance.Logger.LogInfo($"[{PluginName}] {msg}");
        internal static void LogWarning(string msg) => Instance.Logger.LogWarning($"[{PluginName}] {msg}");
        internal static void LogError(string msg) => Instance.Logger.LogError($"[{PluginName}] {msg}");

        internal static void LogVerbose(string msg)
        {
            if (VerboseLogging != null && VerboseLogging.Value)
            {
                Instance.Logger.LogInfo($"[{PluginName}] {msg}");
            }
        }
    }
}
