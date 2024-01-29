using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        private const int THRESHOLD = 300000;
        
        private int previousQuota = -1;
        private int previousResult = -1;
        private HashSet<GrabbableObject> previousInclude;
        private HashSet<GrabbableObject> previousExclude;
        public Material wireframeMaterial;
        private Dictionary<GrabbableObject, Material[]> _highlightedObjects = new();
        private bool _toggled = false;
        private bool _highlightLock = false;

        // private int id = 65;
        
        private void Awake()
        {
            SetupKeybindCallbacks();
            CreateShader();
            
            Logger.LogInfo("MinimumQuotaFinder successfully loaded!");
        }
        
        public void SetupKeybindCallbacks()
        {
            InputActionsInstance.HighlightKey.performed += OnHighlightKeyPressed;
            // InputActionsInstance.SpawnScrap.performed += SpawnScrap;
            // InputActionsInstance.UpdateId.performed += UpdateId;
        }

        // public void UpdateId(InputAction.CallbackContext spawnContext)
        // {
        //     id--;
        //     Logger.LogInfo("New ID is: " + id);
        // }
        //
        // public void SpawnScrap(InputAction.CallbackContext spawnContext)
        // {
        //     Vector3 position = GameNetworkManager.Instance.localPlayerController.transform.position;
        //     GameObject val = Instantiate(StartOfRound.Instance.allItemsList.itemsList[id].spawnPrefab, position, Quaternion.identity);
        //     int value = new System.Random().Next(10, 25);
        //     val.GetComponent<GrabbableObject>().fallTime = 0f;
        //     val.AddComponent<ScanNodeProperties>().scrapValue = value;
        //     val.GetComponent<GrabbableObject>().SetScrapValue(value);
        //     val.GetComponent<NetworkObject>().Spawn();
        // }

        public void CreateShader()
        {
            // Load the shader from an AssetBundle file
            string bundlePath = Path.Join(Path.GetDirectoryName(Info.Location), "AssetBundles/wireframe");
            AssetBundle shaderBundle = AssetBundle.LoadFromFile(bundlePath);
            Shader shader = shaderBundle.LoadAsset<Shader>("assets/wireframeshader.shader");
            
            // Create a material from the loaded shader
            wireframeMaterial = new Material(shader);
            shaderBundle.Unload(false);
        }
        
        public void OnHighlightKeyPressed(InputAction.CallbackContext highlightContext)
        {
            // Cancel key press if still calculating
            if (_highlightLock) return;
            
            if (!highlightContext.performed || GameNetworkManager.Instance.localPlayerController == null) return;
            
            if (!HUDManager.Instance.CanPlayerScan() || HUDManager.Instance.playerPingingScan > -0.5f) return;
            
            // Toggle the highlighting
            _toggled = !_toggled;
            
            // Highlight if toggled, unhighlight otherwise
            if (_toggled)
            {
                // Start the coroutine for highlighting, this will give back control after the first round of calculations
                GameNetworkManager.Instance.StartCoroutine(HighlightObjectsCoroutine());
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

            if (scope == null)
            {
                return new List<GrabbableObject>();
            }

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

        private IEnumerator HighlightObjectsCoroutine()
        {
            // Exit early if it is already calculating
            if (_highlightLock) yield break;
            
            // Acquire the highlight lock
            _highlightLock = true;
            
            // Show a message to indicate the starting of the calculations in case of big calculation
            HUDManager.Instance.DisplayTip("MinimumQuotaFinder", "Calculating...");
            
            List<GrabbableObject> allScrap = GetAllScrap(StartOfRound.Instance.currentLevelID);
            HashSet<GrabbableObject> toHighlight = new HashSet<GrabbableObject>();
            // Start a coroutine to calculate which items to highlight, this will give back control after all calculations are finished
            yield return GameNetworkManager.Instance.StartCoroutine(GetSetToHighlight(allScrap, toHighlight));
            // Highlight the objects or untoggle if nothing should be highlighted
            if (toHighlight.Count > 0)
            {
                HighlightObjects(toHighlight);
            }
            else
            {
                _toggled = false;
            }

            // Set the lock free
            _highlightLock = false;
        }
        
        private void HighlightObjects(HashSet<GrabbableObject> objectsToHighlight)
        {
            foreach (GrabbableObject obj in objectsToHighlight)
            {
                // It is possible for objects to not have their MeshRenderers set in the object. This finds and sets it
                if (obj.mainObjectRenderer == null)
                {
                    obj.mainObjectRenderer = obj.GetComponentInChildren<MeshRenderer>();
                }
                
                Renderer renderer = obj.mainObjectRenderer;

                // If no renderer could be found skip the item, the user probably unloaded the item
                if (renderer == null)
                {
                    continue;
                }
                
                // Don't highlight if the object is already highlighted
                if (_highlightedObjects.ContainsKey(obj))
                {
                    continue;
                }
                
                // Add the object to the highlighting dictionary to later return its materials
                _highlightedObjects.Add(obj, renderer.materials);
                
                // Overwrite all the materials of the object with the wireframe material
                int materialLength = renderer.materials?.Length ?? 0;
                
                Material[] newMaterials = new Material[materialLength];
                Array.Fill(newMaterials, wireframeMaterial);
                
                renderer.materials = newMaterials;
            }
        }
        
        private void UnhighlightObjects()
        {
            // Put back the original materials of all the highlighted objects
            foreach (KeyValuePair<GrabbableObject, Material[]> objectEntry in _highlightedObjects)
            {
                // Skip the entry if the object can't be found anymore because it unloaded
                if (objectEntry.Key == null || objectEntry.Key.mainObjectRenderer == null ||
                    objectEntry.Key.mainObjectRenderer.materials == null)
                    continue;
                objectEntry.Key.mainObjectRenderer.materials = objectEntry.Value;
            }
            
            // Clear the dictionary keeping track of the highlighted objects
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
            
            // Check if the situation changed in a way that requires a recalculation
            if (quota == previousQuota && !ThrewAwayIncludedOrSoldExcluded(allScrap, sold) &&
                (SubsetOfPrevious(allScrap) || quota == previousResult))
            {
                // If no recalculation is needed highlight the objects from the previous result
                toHighlight.UnionWith(previousInclude.Where(allScrap.Contains));
                DisplayCalculationResult(toHighlight, sold, quota);
                yield break;
            }
            
            // Update the previous quota
            previousQuota = quota;
            
            // If total value of scrap isn't above quota
            int sumScrapValue = allScrap.Sum(scrap => scrap.scrapValue);
            if (sold + sumScrapValue < quota)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder",
                    $"Not enough scrap to reach quota ({sumScrapValue + sold} < {quota}). Sell everything.");
                yield break;
            }
            
            // Sort the items on their id in hopes of getting more consistent results
            allScrap.Sort((x, y) => x.NetworkObjectId.CompareTo(y.NetworkObjectId));
            
            // Start a coroutine to calculate which objects to include or exclude
            HashSet<GrabbableObject> excludedScrap = new HashSet<GrabbableObject>();
            yield return GameNetworkManager.Instance.StartCoroutine(DoDynamicProgrammingCoroutine(allScrap, sold, quota, excludedScrap));
            toHighlight.UnionWith(allScrap.Where(scrap => !excludedScrap.Contains(scrap)));
            
            // Update the previous include and exclude variables
            previousExclude = excludedScrap;
            previousInclude = toHighlight;
            
            // Display the results from the calculations
            DisplayCalculationResult(toHighlight, sold, quota);
            
            // Indicate that this coroutine finished my yielding one last time
            yield return null;
        }

        private bool ThrewAwayIncludedOrSoldExcluded(List<GrabbableObject> allScrap, int sold)
        {
            if (previousInclude == null) return true;
            // Get list of previously included scrap that still exists
            // If they don't add up to the previousResult anymore -> a included scrap was lost or an excluded scrap was sold
            int sum = previousInclude.Intersect(allScrap.ToHashSet()).Sum(scrap => scrap.scrapValue);

            return sum + sold != previousResult;
        }

        private bool SubsetOfPrevious(List<GrabbableObject> allScrap)
        {
            if (allScrap == null || previousInclude == null || previousExclude == null) return false;
            // Check if a new item was entered to the checked environment compared to the previous results
            return allScrap.ToHashSet().IsSubsetOf(previousExclude.Union(previousInclude));
        }
        
        private void DisplayCalculationResult(IEnumerable<GrabbableObject> toHighlight, int sold, int quota)
        {
            // Calculate the total of the highlighted scrap and the already sold scrap
            int result = toHighlight.Sum(scrap => scrap.scrapValue) + sold;
            // Update the results
            previousResult = result;
            // Display the message and change the color of the number based on the difference between the result and the quota
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
                    // Copy the previous data if the current amount is lower than the value of the scrap
                    if (x < currentScrapValue)
                    {
                        current[x] = prev[x];
                        continue;
                    }

                    // Calculate the totals for the current item being included or excluded
                    int include = currentScrapValue + prev[x - currentScrapValue].Max;
                    int exclude = prev[x].Max;

                    // If the total is higher when including the item, add the item to the included of the cell, otherwise copy the previous data
                    if (include > exclude)
                    {
                        HashSet<GrabbableObject> newSet = new HashSet<GrabbableObject>(prev[x - currentScrapValue].Included);
                        newSet.Add(allScrap[y - 1]);
                        current[x] = new MemCell(include, newSet);
                    }
                    else
                    {
                        current[x] = prev[x];
                    }
                }
                // Update the previous and clear the current
                prev = current;
                current = new MemCell[inverseTarget + 1];
                
                // Add the amount of calculations to calculations, yield if the number of calculations surpass the threshold
                calculations += inverseTarget;
                if (calculations > THRESHOLD)
                {
                    yield return null;
                    calculations = 0;
                }
            }
            
            // Update the excluded scrap by return the included of the highest quota and yield one last time to indicate that the coroutine finished
            excludedScrap.UnionWith(prev[^1].Included);
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
        
        // [InputAction("<Keyboard>/k", Name = "Spawn scrap")]
        // public InputAction SpawnScrap { get; set; }
        //
        // [InputAction("<Keyboard>/j", Name = "Update scrap")]
        // public InputAction UpdateId { get; set; }
    }
}
