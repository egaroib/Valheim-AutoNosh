using HarmonyLib;
using UnityEngine;

namespace AutoNosh.Patches
{
    // Player.SetControls(Vector3, bool, bool, bool, bool, bool, bool, bool, bool, bool, bool, bool = false)
    // is fed raw input every frame by PlayerController, which only exists for the local
    // player - so this only ever sees real input, never replicated state from other peers.
    // Used purely to know whether the player is present, never to change any control.
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class PlayerSetControlsPatch
    {
        private static void Postfix(
            Player __instance,
            Vector3 movedir,
            bool attack,
            bool attackHold,
            bool secondaryAttack,
            bool secondaryAttackHold,
            bool block,
            bool blockHold,
            bool jump,
            bool crouch,
            bool dodge)
        {
            if (__instance != Player.m_localPlayer)
            {
                return;
            }

            var active = movedir != Vector3.zero
                         || attack || attackHold
                         || secondaryAttack || secondaryAttackHold
                         || block || blockHold
                         || jump || crouch || dodge;

            if (active)
            {
                AfkTracker.NoteActivity();
            }
        }
    }
}
