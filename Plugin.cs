using System.Collections;
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
        private const int THRESHOLD = 300000;
        
        private int previousQuota = -1;
        private int previousResult = -1;
        private HashSet<GrabbableObject> previousInclude;
        private HashSet<GrabbableObject> previousExclude;
        public Material wireframeMaterial;
        private Dictionary<GrabbableObject, Material[]> _highlightedObjects = new();
        private bool _toggled = false;
        private bool _highlightLock = false;
        
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
            // GameObject scope = GameObject.Find(
            //     // Scan all scrap in the environment when on the moon
            //     "/Environment/HangarShip");
            // // Get all objects that are scrap
            // // At the counter, only values of scrap items are added to company credit
            // List<GrabbableObject> allScrap = scope.GetComponentsInChildren<GrabbableObject>()
            //     .Where(obj => obj.itemProperties.isScrap).ToList();
            // allScrap.ForEach(o => o.Update());
            // allScrap.Sort((o1, o2) => o1.transform.position.y.CompareTo(o2.transform.position.y));
            // if (allScrap.Count <= 0) return;
            // Transform transform = allScrap[0].transform;
            // if (transform != null) Logger.LogInfo(transform.position.y);
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
        
        public void OnHighlightKeyPressed(InputAction.CallbackContext highlightContext)
        {
            if (_highlightLock) return;
            
            if (!highlightContext.performed || GameNetworkManager.Instance.localPlayerController == null) return;
            
            if (!HUDManager.Instance.CanPlayerScan() || HUDManager.Instance.playerPingingScan > -0.5f) return;
            
            _toggled = !_toggled;
            
            if (_toggled)
            {
                List<GrabbableObject> allScrap = GetAllScrap(StartOfRound.Instance.currentLevelID);
                GameNetworkManager.Instance.StartCoroutine(HighlightObjectsCoroutine(allScrap));
            }
            else
            {
                UnhighlightObjects();
            }

        }
        
        private List<GrabbableObject> GetAllScrap(int level)
        {
            const float minimumHeight = -30f;
            GameObject scope = GameObject.Find(level == 3 ?
                // Scan all scrap in the environment when on the moon
                "/Environment" :
                // Scan only scrap in the ship when elsewhere
                "/Environment/HangarShip");

            // Get all objects that are scrap
            // At the counter, only values of scrap items are added to company credit
            List<GrabbableObject> allScrap = scope.GetComponentsInChildren<GrabbableObject>()
                .Where(obj => obj.itemProperties.isScrap && obj.transform.position.y > minimumHeight).ToList();
            
            DepositItemsDesk desk = FindObjectOfType<DepositItemsDesk>();
            if (desk != null)
            {
                allScrap.AddRange(desk.deskObjectsContainer.GetComponentsInChildren<GrabbableObject>()
                    .Where(obj => obj.itemProperties.isScrap && obj.transform.position.y > minimumHeight).ToList());
            }
            
            return allScrap;
        }

        private IEnumerator HighlightObjectsCoroutine(List<GrabbableObject> allScrap)
        {
            if (_highlightLock) yield break;
            
            _highlightLock = true;
            HUDManager.Instance.DisplayTip("MinimumQuotaFinder", "Calculating...");
            HashSet<GrabbableObject> toHighlight = new HashSet<GrabbableObject>();
            yield return GameNetworkManager.Instance.StartCoroutine(GetSetToHighlight(allScrap, toHighlight));
            
            if (toHighlight.Count > 0)
            {
                HighlightObjects(toHighlight);
            }
            else
            {
                _toggled = false;
            }

            _highlightLock = false;
        }
        
        private void HighlightObjects(HashSet<GrabbableObject> objectsToHighlight)
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
            foreach (KeyValuePair<GrabbableObject, Material[]> objectEntry in _highlightedObjects)
            {
                if (objectEntry.Key == null || objectEntry.Key.mainObjectRenderer == null ||
                    objectEntry.Key.mainObjectRenderer.materials == null)
                    continue;
                objectEntry.Key.mainObjectRenderer.materials = objectEntry.Value;
            }
            
            _highlightedObjects.Clear();
        }

        private IEnumerator GetSetToHighlight(List<GrabbableObject> allScrap, HashSet<GrabbableObject> toHighlight)
        {
            // Retrieve value of currently sold scrap and quota
            int sold = TimeOfDay.Instance.quotaFulfilled;
            int quota = TimeOfDay.Instance.profitQuota;
            
            // Retrieve all scrap in ship
            // If no scrap in ship
            if (allScrap == null || allScrap.Count == 0)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder","No scrap detected within the ship.");
                yield break;
            }
            
            // Return old scan if quota is still the same
            if (quota == previousQuota && !SoldExcluded(allScrap) && !ThrewAwayIncluded(allScrap, sold) &&
                (SubsetOfPrevious(allScrap) || quota == previousResult))
            {
                // If no wrong scrap was sold, filter out sold from previous combination and return
                // Else recalculate
                toHighlight.UnionWith(previousInclude.Where(allScrap.Contains));
                DisplayCalculationResult(toHighlight, sold, quota);
                yield break;
            }
            
            previousQuota = quota;
            
            // If total value of scrap isn't above quota
            int sumScrapValue = allScrap.Sum(scrap => scrap.scrapValue);
            if (sold + sumScrapValue < quota)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder",
                    $"Not enough scrap to reach quota ({sumScrapValue + sold} < {quota}). Sell everything.");
                yield break;
            }
            
            allScrap.Sort((x, y) => x.NetworkObjectId.CompareTo(y.NetworkObjectId));
            HashSet<GrabbableObject> excludedScrap = new HashSet<GrabbableObject>();
            yield return GameNetworkManager.Instance.StartCoroutine(DoDynamicProgrammingCoroutine(allScrap, sold, quota, excludedScrap));
            Logger.LogInfo("Count of excluded: " + excludedScrap.Count);
            previousExclude = excludedScrap;
            toHighlight.UnionWith(allScrap.Where(scrap => !excludedScrap.Contains(scrap)));
            previousInclude = toHighlight;
            
            DisplayCalculationResult(toHighlight, sold, quota);
            yield return null;
        }
        
        private bool SoldExcluded(List<GrabbableObject> allScrap)
        {
            if (previousExclude == null) return false;
            return previousExclude.Any(scrap => !allScrap.ToHashSet().Contains(scrap));
        }

        private bool ThrewAwayIncluded(List<GrabbableObject> allScrap, int sold)
        {
            if (previousInclude == null) return false;
            // Get list of scrap that still exists
            // If they don't add up to the previousResult anymore -> one scrap got lost
            int sum = previousInclude.Intersect(allScrap.ToHashSet()).Sum(scrap => scrap.scrapValue);

            return sum + sold != previousResult;
        }

        private bool SubsetOfPrevious(List<GrabbableObject> allScrap)
        {
            if (allScrap == null || previousInclude == null || previousExclude == null) return false;

            return allScrap.ToHashSet().IsSubsetOf(previousExclude.Union(previousInclude));
        }
        
        private void DisplayCalculationResult(IEnumerable<GrabbableObject> toHighlight, int sold, int quota)
        {
            int result = toHighlight.Sum(scrap => scrap.scrapValue) + sold;
            previousResult = result;
            int difference = result - quota;
            string colour = difference == 0 ? "#A5D971" : "#992403";
            HUDManager.Instance.DisplayTip("MinimumQuotaFinder",
                $"Optimal scrap combination found: {result} ({sold} already sold). " +
                $"<color={colour}>{difference}</color> over quota. ");
        }

        private IEnumerator DoDynamicProgrammingCoroutine(List<GrabbableObject> allScrap, int sold, int quota, HashSet<GrabbableObject> excludedScrap)
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
            
            int calculations = 0;
            
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
                
                calculations += inverseTarget;
                if (calculations > THRESHOLD)
                {
                    yield return null;
                    calculations = 0;
                }
            }
            
            excludedScrap.UnionWith(current[^1].Included);
            yield return null;
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
