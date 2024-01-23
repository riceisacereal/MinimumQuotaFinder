using System.Collections.Generic;
using System.Linq;
using System.IO;
using BepInEx;
using LethalCompanyInputUtils.Api;
using Unity.Netcode;
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
            InputActionsInstance.SpawnScrap.performed += SpawnScrap;
        }

        public void ReloadShader(InputAction.CallbackContext reloadContext)
        {
            CreateShader();
        }

        public void SpawnScrap(InputAction.CallbackContext spawnContext)
        {
            Vector3 position = GameNetworkManager.Instance.localPlayerController.transform.position;
            GameObject val = Instantiate(StartOfRound.Instance.allItemsList.itemsList[65].spawnPrefab, position, Quaternion.identity);
            val.GetComponent<GrabbableObject>().fallTime = 0f;
            val.AddComponent<ScanNodeProperties>().scrapValue = 25;
            val.GetComponent<GrabbableObject>().SetScrapValue(25);
            val.GetComponent<NetworkObject>().Spawn(false);
            print(val.GetComponent<GrabbableObject>().itemProperties.isScrap);
        }

        public void CreateShader()
        {
            var bundlePath = Path.Join(Path.GetDirectoryName(Info.Location), "AssetBundles/wireframe");
            var shaderBundle = AssetBundle.LoadFromFile(bundlePath);
            foreach (var name in shaderBundle.GetAllAssetNames())
            {
                Logger.LogInfo(name);
            }
            var shader = shaderBundle.LoadAsset<Shader>("assets/shader.shader");
            Logger.LogInfo(shader);
            outlineMaterial = new Material(shader);
            // outlineMaterial = shaderBundle.LoadAsset<Material>("goldwireframe.mat");
            shaderBundle.Unload(false);
        }

        private List<GrabbableObject> DoDynamicProgramming(int sold, int quota, List<GrabbableObject> allShipScrap)
        {
            // Subset sum/knapsack on total value of all scraps - quota + already paid quota
            int numItems = allShipScrap.Count;
            int inverseTarget = allShipScrap.Sum(scrap => scrap.scrapValue) - (quota - sold);

            MemCell[,] mem = new MemCell[2, inverseTarget + 1];
            // int[,] mem = new int[numItems + 1, inverseTarget + 1];
            for (int i = 0; i < mem.GetLength(1); i++)
            {
                mem[0, i] = new MemCell(0, new List<GrabbableObject>());
            }
            
            
            for (int y = 1; y <= numItems; y++)
            {
                for (int x = 0; x <= inverseTarget; x++)
                {
                    int currentScrapValue = allShipScrap[y - 1].scrapValue;
                    if (x < currentScrapValue)
                    {
                        mem[1, x] = mem[0, x];
                        continue;
                    }

                    int include = currentScrapValue + mem[0, x - currentScrapValue].Max;
                    int exclude = mem[0, x].Max;

                    if (include > exclude)
                    {
                        mem[1, x] = new MemCell(include, mem[0, x - currentScrapValue].Included);
                    }
                    else
                    {
                        mem[1, x] = mem[0, x];
                    }
                }
                
                // Shift values up
                for (int x = 0; x <= inverseTarget; x++)
                {
                    mem[0, x] = mem[1, x];
                }
            }

            return mem[mem.GetLength(0) - 1, mem.GetLength(1) - 1].Included;
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
                return new List<GrabbableObject>();
            }
            // If total value of scrap isn't above quota
            int sumScrapValue = allShipScrap.Sum(scrap => scrap.scrapValue);
            if (sold + sumScrapValue < quota)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder",
                    $"Not enough scrap to reach quota ({sumScrapValue} < {quota - sold}). Sell everything.");
                return new List<GrabbableObject>();
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

            int sum = toHighlight.Sum(scrap => scrap.scrapValue);
            int difference = (sold + sum) - quota;
            string sign = difference > 0 ? "+" : "";
            string colour = difference == 0 ? "#A5D971" : "#BA4B2B";
            HUDManager.Instance.DisplayTip("MinimumQuotaFinder",
                $"Total value of optimal scrap combination found: <b>{sum}</b>." +
                $"Difference to quota: <color=\"{colour}\">{sign}{colour}</color>.");
            return toHighlight;
        }

        public void OnHighlightKeyPressed(InputAction.CallbackContext highlightContext)
        {
            if (!highlightContext.performed) return; 

            if (GameNetworkManager.Instance.localPlayerController == null)
                return;
            if (!HUDManager.Instance.CanPlayerScan() || HUDManager.Instance.playerPingingScan > -0.5f)
                return;
            // If not on company moon
            if (StartOfRound.Instance.currentLevelID != 3)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder","Not on the company moon.");
                return;
            }

            toggled = !toggled;
            if (toggled)
            {
                List<GrabbableObject> toHighlight = GetListToHighlight();
                HighlightObjects(toHighlight);
            }
            else
            {
                UnhighlightObjects();
            }
        }
        
        private List<GrabbableObject> GetAllShipScrap()
        {
            GameObject ship = GameObject.Find("/Environment");
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
                
                if (renderer == null) continue;
                if (highlightedObjects.ContainsKey(obj)) continue;
                
                highlightedObjects.Add(obj, renderer.materials);

                var materialLength = renderer.materials?.Length ?? 0;
                var newMaterials = new Material[materialLength];
                
                for (var i = 0; i < materialLength; i++)
                {
                    newMaterials[i] = Instantiate(outlineMaterial);
                }
                
                renderer.materials = newMaterials;
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

    public class MemCell
    {
        public int Max { get; }
        public List<GrabbableObject> Included { get; }

        public MemCell(int max, List<GrabbableObject> setToCopy)
        {
            Max = max;
            Included = new List<GrabbableObject>(setToCopy);
        }
    }
    
    public class HighlightInputClass : LcInputActions 
    {
        [InputAction("<Keyboard>/h", Name = "Toggle scrap highlight")]
        public InputAction HighlightKey { get; set; }
        
        [InputAction("<Keyboard>/j", Name = "Reload highlight shaders")]
        public InputAction ReloadShaderKey { get; set; }
        
        [InputAction("<Keyboard>/k", Name = "Spawn scrap")]
        public InputAction SpawnScrap { get; set; }
    }
}
