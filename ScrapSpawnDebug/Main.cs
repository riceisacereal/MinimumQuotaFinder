using BepInEx;
using BepInEx.Configuration;
using LethalCompanyInputUtils.Api;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScrapSpawnDebug
{
    [BepInPlugin("ScrapSpawnDebug", "ScrapSpawnDebug", "0.0.1")]
    [BepInDependency("com.rune580.LethalCompanyInputUtils")]
    public class ScrapSpawnDebug : BaseUnityPlugin
    {
        internal static HighlightInputClass InputActionsInstance = new();
        private int id = 65;
        
        private ConfigEntry<string> configGreeting;
        private ConfigEntry<bool> configDisplayGreeting;
        
        private void Awake()
        {
            configGreeting = Config.Bind("General",
                                         "GreetingText",
                                         "Hello, world!",
                                         "A greeting text to show when the game is launched");

            configDisplayGreeting = Config.Bind("General.Toggles", 
                                                "DisplayGreeting",
                                                true,
                                                "Whether or not to show the greeting text");
            
            SetupKeybindCallbacks();
            Logger.LogInfo("ScrapSpawnDebug successfully loaded!");
        }
        
        private void SetupKeybindCallbacks()
        {
            InputActionsInstance.SpawnScrap.performed += SpawnScrap;
            InputActionsInstance.UpdateId.performed += UpdateId;
        }

        public void UpdateId(InputAction.CallbackContext spawnContext)
        {
            id--;
            Logger.LogInfo($"New ID is: {id}, with name {StartOfRound.Instance.allItemsList.itemsList[id].itemName}");
        }
        
        public void SpawnScrap(InputAction.CallbackContext spawnContext)
        {
            Vector3 position = GameNetworkManager.Instance.localPlayerController.transform.position;
            GameObject val = Instantiate(StartOfRound.Instance.allItemsList.itemsList[id].spawnPrefab, position, Quaternion.identity);
            int value = new System.Random().Next(10, 25);
            val.GetComponent<GrabbableObject>().fallTime = 0f;
            val.AddComponent<ScanNodeProperties>().scrapValue = value;
            val.GetComponent<GrabbableObject>().SetScrapValue(value);
            val.GetComponent<Unity.Netcode.NetworkObject>().Spawn();

            configDisplayGreeting.Value = false;
        }
        
        public class HighlightInputClass : LcInputActions 
        {
            [InputAction("<Keyboard>/k", Name = "Spawn scrap")]
            public InputAction SpawnScrap { get; set; }
            
            [InputAction("<Keyboard>/j", Name = "Update scrap")]
            public InputAction UpdateId { get; set; }
        }
    }
}