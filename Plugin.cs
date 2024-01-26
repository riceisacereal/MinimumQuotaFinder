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
        public Material wireframeMaterial;
        private Dictionary<GrabbableObject, Material[]> _highlightedObjects = new();
        private bool _toggled;
        
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
            val.GetComponent<NetworkObject>().Spawn();
            print(val.GetComponent<GrabbableObject>().itemProperties.isScrap);
        }

        public void CreateShader()
        {
            string bundlePath = Path.Join(Path.GetDirectoryName(Info.Location), "AssetBundles/wireframe");
            AssetBundle shaderBundle = AssetBundle.LoadFromFile(bundlePath);
            Shader shader = shaderBundle.LoadAsset<Shader>("assets/wireframeshader.shader");
            
            wireframeMaterial = new Material(shader);
            shaderBundle.Unload(false);
        }

        private HashSet<GrabbableObject> DoDynamicProgramming(int sold, int quota, List<GrabbableObject> allScrap)
        {
            // Subset sum/knapsack on total value of all scraps - quota + already paid quota
            int numItems = allScrap.Count;
            int inverseTarget = allScrap.Sum(scrap => scrap.scrapValue) - (quota - sold);

            MemCell[] prev = new MemCell[inverseTarget + 1];
            MemCell[] current = new MemCell[inverseTarget + 1];
            for (int i = 0; i < prev.Length; i++)
            {
                prev[i] = new MemCell(0, new HashSet<GrabbableObject>());
            }
            
            for (int y = 1; y <= numItems; y++)
            {
                for (int x = 0; x <= inverseTarget; x++)
                {
                    int currentScrapValue = allScrap[y - 1].scrapValue;
                    if (x < currentScrapValue)
                    {
                        current[x] = prev[x];
                        continue;
                    }

                    int include = currentScrapValue + prev[x - currentScrapValue].Max;
                    int exclude = prev[x].Max;

                    if (include > exclude)
                    {
                        HashSet<GrabbableObject> newList = new HashSet<GrabbableObject>(
                            prev[x - currentScrapValue].Included.Append(allScrap[y - 1]));
                        current[x] = new MemCell(include, newList);
                    }
                    else
                    {
                        current[x] = prev[x];
                    }
                }
                prev = current;
            }

            return current[^1].Included;
        }

        private List<GrabbableObject> GetListToHighlight(List<GrabbableObject> allScrap)
        {
            // Retrieve value of currently sold scrap and quota
            int sold = TimeOfDay.Instance.quotaFulfilled;
            int quota = TimeOfDay.Instance.profitQuota;
            
            Logger.LogInfo(sold + " " + quota);
            
            // Retrieve all scrap in ship
            // If no scrap in ship
            if (allScrap == null || allScrap.Count == 0)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder","No scrap detected within the ship.");
                return new List<GrabbableObject>();
            }
            // If total value of scrap isn't above quota
            int sumScrapValue = allScrap.Sum(scrap => scrap.scrapValue);
            if (sold + sumScrapValue < quota)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder",
                    $"Not enough scrap to reach quota ({sumScrapValue} < {quota - sold}). Sell everything.");
                return new List<GrabbableObject>();
            }
            
            allScrap.Sort((x, y) => x.NetworkObjectId.CompareTo(y.NetworkObjectId));
            HashSet<GrabbableObject> excludedScrap = DoDynamicProgramming(sold, quota, allScrap);
            List<GrabbableObject> toHighlight = allScrap.Where(scrap => !excludedScrap.Contains(scrap)).ToList();

            int sum = toHighlight.Sum(scrap => scrap.scrapValue);
            int difference = (sold + sum) - quota;
            string colour = difference == 0 ? "#A5D971" : "#992403";
            HUDManager.Instance.DisplayTip("MinimumQuotaFinder",
                $"Optimal scrap combination found: {sum + sold} ({sold} already sold). " +
                $"<color={colour}>{difference}</color> over quota. ");
            return toHighlight;
        }

        public void OnHighlightKeyPressed(InputAction.CallbackContext highlightContext)
        {
            if (!highlightContext.performed) return; 

            if (GameNetworkManager.Instance.localPlayerController == null)
                return;
            if (!HUDManager.Instance.CanPlayerScan() || HUDManager.Instance.playerPingingScan > -0.5f)
                return;

            _toggled = !_toggled;
            if (_toggled)
            {
                List<GrabbableObject> toHighlight = GetListToHighlight(GetAllScrap(StartOfRound.Instance.currentLevelID));
                HighlightObjects(toHighlight);
            }
            else
            {
                UnhighlightObjects();
            }
        }
        
        private static List<GrabbableObject> GetAllScrap(int level)
        {
            GameObject scope = GameObject.Find(level == 3 ?
                // Scan all scrap in the environment when on the moon
                "/Environment" :
                // Scan only scrap in the ship when elsewhere
                "/Environment/HangarShip");
            // Get all objects that are scrap
            // At the counter, only values of scrap items are added to company credit
            List<GrabbableObject> allScrap = scope.GetComponentsInChildren<GrabbableObject>()
                .Where(obj => obj.itemProperties.isScrap).ToList();
            
            return allScrap;
        }

        private void HighlightObjects(List<GrabbableObject> objectsToHighlight)
        {
            foreach (GrabbableObject obj in objectsToHighlight)
            {
                Renderer renderer = obj.mainObjectRenderer;
                
                if (renderer == null) continue;
                if (_highlightedObjects.ContainsKey(obj)) continue;
                
                _highlightedObjects.Add(obj, renderer.materials);

                int materialLength = renderer.materials?.Length ?? 0;
                Material[] newMaterials = new Material[materialLength];
                
                for (int i = 0; i < materialLength; i++)
                {
                    newMaterials[i] = Instantiate(wireframeMaterial);
                }
                
                renderer.materials = newMaterials;
            }
        }

        private void UnhighlightObjects()
        {
            List<GrabbableObject> toRemove = new List<GrabbableObject>();
            foreach (KeyValuePair<GrabbableObject, Material[]> objectEntry in _highlightedObjects)
            {
                objectEntry.Key.mainObjectRenderer.materials = objectEntry.Value;
                toRemove.Add(objectEntry.Key);
            }

            foreach (GrabbableObject obj in toRemove)
            {
                _highlightedObjects.Remove(obj);
            }
        }
    }

    public class MemCell
    {
        public int Max { get; }
        public HashSet<GrabbableObject> Included { get; }

        public MemCell(int max, HashSet<GrabbableObject> included)
        {
            Max = max;
            Included = included;
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
