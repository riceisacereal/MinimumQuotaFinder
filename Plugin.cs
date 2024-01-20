using System.Linq;
using System.Collections.Generic;
using System.IO;
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
        public Material outlineMaterial;
        private Dictionary<GrabbableObject, Material> highlightedObjects = new Dictionary<GrabbableObject, Material>();
        private bool toggled = false;
        
        private void Awake()
        {
            SetupKeybindCallbacks();
            CreateShader();
            
            Logger.LogInfo("Plugin is loaded!");
        }
        
        public void SetupKeybindCallbacks()
        {
            InputActionsInstance.HighlightKey.performed += OnHighlightKeyPressed;
        }

        public void CreateShader()
        {
            var bundlePath = Path.Join(Path.GetDirectoryName(Info.Location), "AssetBundles/outlineshader.shader");
            var shaderBundle = AssetBundle.LoadFromFile(bundlePath);
            Logger.LogInfo(shaderBundle.GetAllAssetNames());
            var shader = shaderBundle.LoadAsset<Shader>("outlineshader.shader");
            Logger.LogInfo("Shader is loaded!");
            Logger.LogInfo(shader);
            outlineMaterial = new Material(shader);
            Logger.LogInfo("Material has been created!");
            //
            // // Create a new material with the outline shader
            // outlineMaterial = new Material(ReadShaderCode(shaderPath));
        }

        public void OnHighlightKeyPressed(InputAction.CallbackContext highlightContext)
        {
            if (!highlightContext.performed) return; 
            // Add more context checks if desired
            
            Logger.LogInfo("Button pressed!");

            toggled = !toggled;
 
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

            var shipItems = GetShipItems();

            if (toggled)
            {
                HighlightObjects(shipItems);
            }
            else
            {
                UnhighlightObjects();
            }

            Logger.LogInfo("Plugin is loaded!");
        }
        
        private static List<GrabbableObject> GetShipItems()
        {
            var ship = GameObject.Find("/Environment/HangarShip");
            // Get all objects that can be picked up from inside the ship. Also remove items which technically have
            // scrap value but don't actually add to your quota.
            var loot = ship.GetComponentsInChildren<GrabbableObject>()
                .Where(obj => obj.name != "ClipboardManual" && obj.name != "StickyNoteItem" && obj.name != "Key(Clone)" && obj.name != "Key").ToList();
            // loot.Do(scrap => ShipLoot.Log.LogDebug($"{scrap.name} - ${scrap.scrapValue}"));

            return loot;
        }

        private void HighlightObjects(List<GrabbableObject> objectsToHighlight)
        {
            foreach (var obj in objectsToHighlight)
            {
                Renderer renderer = obj.mainObjectRenderer;
                print(obj.parentObject);
                
                if (renderer == null) continue;
                if (highlightedObjects.ContainsKey(obj)) continue;
                
                highlightedObjects.Add(obj, renderer.material);
                renderer.material = outlineMaterial;
            }
        }

        private void UnhighlightObjects()
        {
            var toRemove = new List<GrabbableObject>();
            foreach (var objectEntry in highlightedObjects)
            {
                objectEntry.Key.mainObjectRenderer.material = objectEntry.Value;
                toRemove.Add(objectEntry.Key);
            }

            foreach (var obj in toRemove)
            {
                highlightedObjects.Remove(obj);
            }
        }
        
        // Read shader code from file
        private static string ReadShaderCode(string filePath)
        {
            try
            {
                // Read all lines from the file
                var lines = File.ReadAllLines(filePath);

                // Concatenate lines into a single string
                return string.Join("\n", lines);
            }
            catch (IOException e)
            {
                Debug.LogError($"Error reading shader code from file: {e.Message}");
                return null;
            }
        }
    }
    
    public class HighlightInputClass : LcInputActions 
    {
        [InputAction("<Keyboard>/h", Name = "Calculate and highlight minimum scrap")]
        public InputAction HighlightKey { get; set; }
    }
}
