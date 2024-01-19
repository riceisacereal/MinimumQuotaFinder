using System.Linq;
using BepInEx;
using LethalCompanyInputUtils.Api;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MinimumQuotaFinder
{
    [BepInPlugin("com.github.riceisacereal.minimum-quota-finder", "MinimumQuotaFinder", "0.1.0")]
    [BepInDependency("com.rune580.LethalCompanyInputUtils")]
    public class MinimumQuotaFinder : BaseUnityPlugin
    {
        internal static HighlightInputClass InputActionsInstance = new();
        
        private void Awake()
        {
            SetupKeybindCallbacks();
            
            Logger.LogInfo("Plugin is loaded!");
        }
        
        public void SetupKeybindCallbacks()
        {
            InputActionsInstance.HighlightKey.performed += OnHighlightKeyPressed;
        }

        public void OnHighlightKeyPressed(InputAction.CallbackContext highlightContext)
        {
            if (!highlightContext.performed) return; 
            // Add more context checks if desired
 
            // Your executing code here
            
            // Retrieve value of currently sold scrap
            var sold = TimeOfDay.Instance.quotaFulfilled;
            // Subset sum on total value of all scraps - quota - already paid quota
            var quota = TimeOfDay.Instance.profitQuota;
            
            if (GameNetworkManager.Instance.localPlayerController == null)
                return;
            if (!HUDManager.Instance.CanPlayerScan() || HUDManager.Instance.playerPingingScan > -0.5f)
                return;
            // Only allow this special scan to work while inside the ship.
            if (!StartOfRound.Instance.inShipPhase && !GameNetworkManager.Instance.localPlayerController.isInHangarShipRoom)
                return;

            var value = CalculateLootValue();
        }
        
        private float CalculateLootValue()
        {
            GameObject ship = GameObject.Find("/Environment/HangarShip");
            // Get all objects that can be picked up from inside the ship. Also remove items which technically have
            // scrap value but don't actually add to your quota.
            var loot = ship.GetComponentsInChildren<GrabbableObject>()
                .Where(obj => obj.name != "ClipboardManual" && obj.name != "StickyNoteItem" && obj.name != "Key(Clone)" && obj.name != "Key").ToList();
            
            
            // loot.Do(scrap => ShipLoot.Log.LogDebug($"{scrap.name} - ${scrap.scrapValue}"));
            return loot.Sum(scrap => scrap.scrapValue);
        }
    }
    
    public class HighlightInputClass : LcInputActions 
    {
        [InputAction("<Keyboard>/h", Name = "Calculate and highlight minimum scrap")]
        public InputAction HighlightKey { get; set; }
    }
}
