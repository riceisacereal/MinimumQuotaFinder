using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using LethalCompanyInputUtils.Api;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MinimumQuotaFinder
{
    [BepInPlugin("com.github.riceisacereal.MinimumQuotaFinder", "MinimumQuotaFinder", "0.1.0")]
    [BepInDependency("com.rune580.LethalCompanyInputUtils")]
    public class MinimumQuotaFinder : BaseUnityPlugin
    {
        internal static HighlightInputClass InputActionsInstance = new();
        public Material outlineMaterial;
        private Dictionary<GrabbableObject, Material[]> highlightedObjects = new();
        private bool toggled;
        
        private void Awake()
        {
            SetupKeybindCallbacks();
            CreateShader();
            
            Logger.LogInfo("Plugin is loaded!");
        }
        
        public void SetupKeybindCallbacks()
        {
            InputActionsInstance.HighlightKey.performed += OnHighlightKeyPressed;
            InputActionsInstance.ReloadShaderKey.performed += ReloadShader;
        }

        public void ReloadShader(InputAction.CallbackContext reloadContext)
        {
            CreateShader();
        }

        public void CreateShader()
        {
            var bundlePath = Path.Join(Path.GetDirectoryName(Info.Location), "outlineshader.shader");
            var shaderBundle = AssetBundle.LoadFromFile(bundlePath);
            Logger.LogInfo(shaderBundle.GetAllAssetNames());
            var shader = shaderBundle.LoadAsset<Shader>("outlineshader.shader");
            Logger.LogInfo(shader);
            outlineMaterial = new Material(shader);
            shaderBundle.Unload(false);
        }

        private List<GrabbableObject> GetExcludedList(int[,] mem, List<GrabbableObject> allSchipScrap)
        {
            List<GrabbableObject> excludedScrap = new List<GrabbableObject>();

            int y = mem.GetLength(0);
            int x = mem.GetLength(1);
            while (x >= 0 && y >= 1)
            {
                int currentScrapValue = allSchipScrap[y - 1].scrapValue;
                // Excluded
                if (mem[y, x] == mem[y - 1, x])
                {
                    y--;
                }
                else if (mem[y, x] == mem[y - 1, x - currentScrapValue] + currentScrapValue)
                {
                    excludedScrap.Add(allSchipScrap[y]);
                    y--;
                    x -= currentScrapValue;
                }
            }

            return excludedScrap;
        }

        private List<GrabbableObject> DoDynamicProgramming(int sold, int quota, List<GrabbableObject> allShipScrap)
        {
            // Subset sum/knapsack on total value of all scraps - quota - already paid quota
            int numItems = allShipScrap.Count;
            int inverseTarget = allShipScrap.Sum(scrap => scrap.scrapValue) - quota - sold;

            int[,] mem = new int[numItems + 1, inverseTarget + 1];

            for (int y = 1; y <= numItems; y++)
            {
                for (int x = 0; x <= inverseTarget; x++)
                {
                    int currentScrapValue = allShipScrap[y - 1].scrapValue;
                    if (x < currentScrapValue)
                    {
                        mem[y, x] = mem[y - 1, x];
                        continue;
                    }

                    int include = currentScrapValue + mem[y - 1, x - currentScrapValue];
                    int exclude = mem[y - 1, x];

                    mem[y, x] = System.Math.Max(include, exclude);
                }
            }

            return GetExcludedList(mem, allShipScrap);
        }

        private List<GrabbableObject> GetListToHighlight()
        {
            // Retrieve value of currently sold scrap and quota
            int sold = TimeOfDay.Instance.quotaFulfilled;
            int quota = TimeOfDay.Instance.profitQuota;
            
            // Retrieve all scrap in ship
            List<GrabbableObject> allShipScrap = GetAllShipScrap();
            // If no scrap in ship
            if (allShipScrap == null || allShipScrap.Count == 0)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder","No scrap detected within the ship.");
                return null;
            }
            // If total value of scrap isn't above quota
            int sumScrapValue = allShipScrap.Sum(scrap => scrap.scrapValue);
            if (sold + sumScrapValue < quota)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder",
                    $"Not enough scrap to reach quota ({sumScrapValue} < {quota - sold}). Sell everything.");
                return null;
            }
            
            allShipScrap.Sort((x, y) => x.NetworkObjectId.CompareTo(y.NetworkObjectId));
            List<GrabbableObject> excludedScrap = DoDynamicProgramming(sold, quota, allShipScrap);
            List<GrabbableObject> toHighlight = new List<GrabbableObject>();
            foreach (GrabbableObject scrap in allShipScrap)
            {
                if (!excludedScrap.Contains(scrap))
                {
                    toHighlight.Add(scrap);
                }
            }

            HUDManager.Instance.DisplayTip("MinimumQuotaFinder",
                $"Total value of optimal scrap combination found: {toHighlight.Sum(scrap => scrap.scrapValue)}.");
            return toHighlight;
        }

        public void OnHighlightKeyPressed(InputAction.CallbackContext highlightContext)
        {
            if (!highlightContext.performed) return; 
            // Add more context checks if desired
            
            Logger.LogInfo("Button pressed!");

            toggled = !toggled;
 
            // Your executing code here
            
            if (GameNetworkManager.Instance.localPlayerController == null)
                return;
            if (!HUDManager.Instance.CanPlayerScan() || HUDManager.Instance.playerPingingScan > -0.5f)
                return;
            // Only allow this special scan to work while inside the ship.
            // if (!StartOfRound.Instance.inShipPhase && !GameNetworkManager.Instance.localPlayerController.isInHangarShipRoom)
            //     return;
            // If on company moon
            if (StartOfRound.Instance.currentLevelID != 3)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder","Not on the company moon.");
                return;
            }
            
            List<GrabbableObject> toHighlight = GetListToHighlight();
            // TODO: Highlight scrap
        }

            if (toggled)
            {
                HighlightObjects(shipItems);
            }
            else
            {
                UnhighlightObjects();
            }
        }
        
        private List<GrabbableObject> GetAllShipScrap()
        {
            GameObject ship = GameObject.Find("/Environment/HangarShip");
            // Get all objects that is scrap within the ship
            // At the counter, only values of scrap items are added to company credit
            List<GrabbableObject> allScrap = ship.GetComponentsInChildren<GrabbableObject>()
                .Where(obj => obj.itemProperties.isScrap).ToList();
            
            return allScrap;
        }

        private void HighlightObjects(List<GrabbableObject> objectsToHighlight)
        {
            foreach (var obj in objectsToHighlight)
            {
                Renderer renderer = obj.mainObjectRenderer;
                print(renderer.materials.Length);
                
                if (renderer == null) continue;
                if (highlightedObjects.ContainsKey(obj)) continue;
                
                highlightedObjects.Add(obj, renderer.materials);

                var newMaterials = new Material[renderer.materials.Length];
                for (var i = 0; i < newMaterials.Length; i++)
                {
                    newMaterials[i] = Instantiate(outlineMaterial);
                }
                renderer.materials = new Material[renderer.materials.Length];
            }
        }

        private void UnhighlightObjects()
        {
            var toRemove = new List<GrabbableObject>();
            foreach (var objectEntry in highlightedObjects)
            {
                objectEntry.Key.mainObjectRenderer.materials = objectEntry.Value;
                toRemove.Add(objectEntry.Key);
            }

            foreach (var obj in toRemove)
            {
                highlightedObjects.Remove(obj);
            }
        }
    }
    
    public class HighlightInputClass : LcInputActions 
    {
        [InputAction("<Keyboard>/h", Name = "Calculate and highlight minimum scrap needed to reach quota")]
        public InputAction HighlightKey { get; set; }
        
        [InputAction("<Keyboard>/j", Name = "Reload highlight shaders")]
        public InputAction ReloadShaderKey { get; set; }
    }
}
