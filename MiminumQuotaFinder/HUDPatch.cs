using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MinimumQuotaFinder;

[HarmonyPatch]
    internal class HUDPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HUDManager), nameof(HUDManager.Awake))]
        public static void OnAwake(HUDManager __instance)
        {
            // Patch to add a highlight instruction to the tips on the HUD after the creation of a HUDManger
            
            // Find the first available tip row and put the message in there
            int i = 0;
            while (i < __instance.controlTipLines.Length && __instance.controlTipLines[i].text != "")
            {
                i++;
            }

            if (i < __instance.controlTipLines.Length)
            {
                __instance.controlTipLines[i].text = "Highlight Minimum Quota : [H]";
            }
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HUDManager), nameof(HUDManager.PingScan_performed))]
        public static void OnPing(HUDManager __instance, InputAction.CallbackContext context)
        {
            // Don't show message if you're not in the ship on a moon
            if (!MinimumQuotaFinder.Instance.CanHighlight(false)) return;
            
            // Patch to add a highlight instruction to the tips on the HUD after performing a scan
            const string message = "Highlight Minimum Quota : [H]";
            
            int i = 0;
            while (i < __instance.controlTipLines.Length && __instance.controlTipLines[i].text != "")
            {
                if (__instance.controlTipLines[i].text == message)
                {
                    return;
                }
                i++;
            }

            if (i < __instance.controlTipLines.Length)
            {
                __instance.controlTipLines[i].text = message;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.SceneManager_OnLoad))]
        public static void OnChangeLevel(StartOfRound __instance, ulong clientId, string sceneName, LoadSceneMode loadSceneMode, AsyncOperation asyncOperation)
        {
            if (sceneName == "CompanyBuilding")
            {
                MinimumQuotaFinder.Instance.TurnOnHighlight(true, false);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DepositItemsDesk), nameof(DepositItemsDesk.PlaceItemOnCounter))]
        public static void OnItemPlacedOnCounter(DepositItemsDesk __instance, PlayerControllerB playerWhoTriggered)
        {
            if (MinimumQuotaFinder.Instance.IsToggled())
            {
                MinimumQuotaFinder.Instance.TurnOnHighlight(false, false);
            }
        }
    }