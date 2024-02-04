using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using BepInEx;
using GameNetcodeStuff;
using HarmonyLib;
using LethalCompanyInputUtils.Api;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MinimumQuotaFinder
{
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
            if (StartOfRound.Instance.currentLevelID != 3 && !GameNetworkManager.Instance.localPlayerController.isInHangarShipRoom) return;
            
            // Patch to add a highlight instruction to the tips on the HUD after performing a scan
            string message = "Highlight Minimum Quota : [H]";
            
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
                MinimumQuotaFinder.Instance.TurnOnHighlight(false);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DepositItemsDesk), nameof(DepositItemsDesk.PlaceItemOnCounter))]
        public static void OnItemPlacedOnCounter(DepositItemsDesk __instance, PlayerControllerB playerWhoTriggered)
        {
            if (MinimumQuotaFinder.Instance.IsToggled())
            {
                MinimumQuotaFinder.Instance.TurnOnHighlight(false);
            }
        }
    }
    
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency("com.rune580.LethalCompanyInputUtils")]
    public class MinimumQuotaFinder : BaseUnityPlugin
    {
        private const string GUID = "com.github.riceisacereal.MinimumQuotaFinder";
        private const string NAME = "MinimumQuotaFinder";
        private const string VERSION = "1.0.1";

        public static MinimumQuotaFinder Instance
        {
            get;
            private set;
        }
        
        internal static HighlightInputClass InputActionsInstance = new();
        private const int THRESHOLD = 300000;
        
        public Material wireframeMaterial;
        private bool _toggled = false;
        private bool _highlightLock = false;
        private int previousQuota = -1;
        private int previousResult = -1;
        private HashSet<GrabbableObject> previousInclude;
        private HashSet<GrabbableObject> previousAllScraps;
        private Dictionary<MeshRenderer, Material[]> _highlightedObjects = new();

        // private int id = 65;
        
        private void Awake()
        {
            Instance = this;
            
            SetupKeybindCallbacks();
            CreateShader();
            
            Harmony harmony = new Harmony(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            Logger.LogInfo("MinimumQuotaFinder successfully loaded!");
        }

        
        private void SetupKeybindCallbacks()
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

        private void CreateShader()
        {
            // Load the shader from an AssetBundle file
            string bundlePath = Path.Join(Path.GetDirectoryName(Info.Location), "wireframe");
            AssetBundle shaderBundle = AssetBundle.LoadFromFile(bundlePath);
            Shader shader = shaderBundle.LoadAsset<Shader>("assets/wireframeshader.shader");
            
            // Create a material from the loaded shader
            wireframeMaterial = new Material(shader);
            shaderBundle.Unload(false);
        }
        
        private void OnHighlightKeyPressed(InputAction.CallbackContext highlightContext)
        {
            // Cancel key press if still calculating
            if (_highlightLock) return;
            
            if (!highlightContext.performed || GameNetworkManager.Instance.localPlayerController == null) return;
            
            if (!HUDManager.Instance.CanPlayerScan() || HUDManager.Instance.playerPingingScan > -0.5f) return;
            
            // Highlight if toggled, unhighlight otherwise
            if (!_toggled)
            {
                TurnOnHighlight(true);
            }
            else
            {
                TurnOffHighlight();
            }
        }

        public void TurnOnHighlight(bool displayIfCached)
        {
            // Cancel the highlighting if the player is outside of their ship on a moon
            if (StartOfRound.Instance.currentLevelID != 3 && !GameNetworkManager.Instance.localPlayerController.isInHangarShipRoom) return;
            // Start the coroutine for highlighting, this will give back control after the first round of calculations
            GameNetworkManager.Instance.StartCoroutine(HighlightObjectsCoroutine(displayIfCached));
            _toggled = true;
        }

        public void TurnOffHighlight()
        {
            UnhighlightObjects();
            _toggled = false;
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
            // At the counter, only values of scrap items with a value larger than 0 and are not unreachable (accidentally dropped over the railings)
            // are added to company credit
            List<GrabbableObject> allScrap = scope.GetComponentsInChildren<GrabbableObject>()
                .Where(obj => obj.itemProperties.isScrap && obj.scrapValue > 0 && obj.transform.position.y > minimumHeight).ToList();
            
            return allScrap;
        }

        private List<GrabbableObject> GetDeskScrap()
        {
            List<GrabbableObject> allScrap = new List<GrabbableObject>();
            DepositItemsDesk desk = FindObjectOfType<DepositItemsDesk>();
            if (desk != null)
            {
                allScrap.AddRange(desk.deskObjectsContainer.GetComponentsInChildren<GrabbableObject>()
                    .Where(obj => obj.itemProperties.isScrap));
            }

            return allScrap;
        }

        private int GetDeskScrapValue()
        {
            return GetDeskScrap().Sum(obj => obj.scrapValue);
        }

        private IEnumerator HighlightObjectsCoroutine(bool displayIfCached)
        {
            // Exit early if it is already calculating
            if (_highlightLock) yield break;
            
            // Acquire the highlight lock
            _highlightLock = true;
            
            // Show a message to indicate the starting of the calculations in case of big calculation
            if (displayIfCached)
            {
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder", "Calculating...");
            }

            List<GrabbableObject> allScrap = GetAllScrap(StartOfRound.Instance.currentLevelID);
            HashSet<GrabbableObject> toHighlight = new HashSet<GrabbableObject>();
            // Start a coroutine to calculate which items to highlight, this will give back control after all calculations are finished
            yield return GameNetworkManager.Instance.StartCoroutine(GetSetToHighlight(allScrap, toHighlight, displayIfCached));
            // Highlight the objects or untoggle if nothing should be highlighted
            if (toHighlight.Count > 0)
            {
                HighlightObjects(toHighlight);
                HighlightObjects(GetDeskScrap());
            }
            else
            {
                _toggled = false;
            }

            // Set the lock free
            _highlightLock = false;
        }
        
        private void HighlightObjects(IEnumerable<GrabbableObject> objectsToHighlight)
        {
            foreach (GrabbableObject obj in objectsToHighlight)
            {
                MeshRenderer[] renderers = obj.GetComponentsInChildren<MeshRenderer>();

                // Overwrite all the materials of all the MeshRenderers associated with the object with the wireframe material
                foreach (MeshRenderer renderer in renderers)
                {
                    if (_highlightedObjects.ContainsKey(renderer)) continue;
                    
                    _highlightedObjects.Add(renderer, renderer.materials);
                    
                    int materialLength = renderer.materials?.Length ?? 0;
                
                    Material[] newMaterials = new Material[materialLength];
                    Array.Fill(newMaterials, wireframeMaterial);
                
                    renderer.materials = newMaterials;
                }
            }
        }
        
        private void UnhighlightObjects()
        {
            // Put back the original materials of all the highlighted objects
            foreach (KeyValuePair<MeshRenderer, Material[]> objectEntry in _highlightedObjects)
            {
                // Skip the entry if the object can't be found anymore because it unloaded
                if (objectEntry.Key == null || objectEntry.Key.materials == null)
                    continue;
                
                objectEntry.Key.materials = objectEntry.Value;
            }
            
            // Clear the dictionary keeping track of the highlighted objects
            _highlightedObjects.Clear();
        }

        private IEnumerator GetSetToHighlight(List<GrabbableObject> allScrap, HashSet<GrabbableObject> includedScrap, bool displayIfCached)
        {
            // Retrieve value of currently sold scrap and quota
            int sold = TimeOfDay.Instance.quotaFulfilled + GetDeskScrapValue();
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
                includedScrap.UnionWith(previousInclude.Where(allScrap.Contains));
                if (displayIfCached)
                {
                    DisplayCalculationResult(includedScrap, sold, quota);
                }

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
            // Update the previous all scraps variable
            previousAllScraps = allScrap.ToHashSet();

            // Get the inverseTarget
            int inverseTarget = allScrap.Sum(scrap => scrap.scrapValue) - (quota - sold);
            
            // Determine which calculation is faster
            bool useInverseTarget = inverseTarget <= quota;
            if (useInverseTarget)
            {
                // Start a coroutine to calculate which objects to include
                yield return GameNetworkManager.Instance.StartCoroutine(
                    GetIncludedCoroutine(allScrap, true, inverseTarget, includedScrap));
            }
            else
            {
                // Get the direct target by doing a quick Greedy approximation
                List<GrabbableObject> greedyApproximation = GetGreedyApproximation(allScrap, quota - sold);
                int directTarget = greedyApproximation.Sum(scrap => scrap.scrapValue);
                // If the approximation found is equal to the actual target, include the approximation combination directly
                if (directTarget == quota - sold)
                {
                    includedScrap.UnionWith(greedyApproximation);
                }
                else
                {
                    // Start a coroutine to calculate which objects to include
                    yield return GameNetworkManager.Instance.StartCoroutine(
                        GetIncludedCoroutine(allScrap, false, directTarget, includedScrap));
                }
            }

            // Update the previous include variable
            previousInclude = includedScrap;
            
            // Display the results from the calculations
            DisplayCalculationResult(includedScrap, sold, quota);
            
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
            if (allScrap == null || previousInclude == null || previousAllScraps == null) return false;
            // Check if a new item was entered to the checked environment compared to the previous results
            return allScrap.ToHashSet().IsSubsetOf(previousAllScraps);
        }

        private List<GrabbableObject> GetGreedyApproximation(List<GrabbableObject> allScrap, int target)
        {
            // Construct a list of Greedily chosen scrap to get a good low high-bound for 
            List<GrabbableObject> greedyOrderedScrap = allScrap.OrderByDescending(scrap => scrap.scrapValue).ToList();
            List<GrabbableObject> greedyCombination = new List<GrabbableObject>();

            // Greedily add scrap to combination until adding another one would be above target
            int sum = 0;
            foreach (GrabbableObject scrap in greedyOrderedScrap)
            {
                // Add the piece of scrap if it will make the sum of the combination <= target
                int currentScrapValue = scrap.scrapValue;
                if (sum + currentScrapValue <= target)
                {
                    greedyCombination.Add(scrap);
                    sum += currentScrapValue;
                }
                // Else if the sum exceeds the target, break the loop
                else
                {
                    break;
                }
            }

            // Return the combination directly if we were somehow lucky enough to find the right combination right away
            if (greedyCombination.Sum(s => s.scrapValue) == target) return greedyCombination;

            // Attempt to find the smallest value of scrap we can add to exceed the target the least
            for (int i = greedyOrderedScrap.Count - 1; i >= 0; i--)
            {
                GrabbableObject currentScrap = greedyOrderedScrap[i];
                // Find from low to high the first scrap which when added to the combination, makes the sum higher than the target
                if (sum + currentScrap.scrapValue >= target)
                {
                    greedyCombination.Add(currentScrap);
                    break;
                }
            }

            return greedyCombination;
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
        
        private IEnumerator GetIncludedCoroutine(List<GrabbableObject> allScrap, bool inverseTarget, int target,
            HashSet<GrabbableObject> includedScrap)
        {
            // Subset sum/knapsack on total value of all scraps - quota + already paid quota
            int numItems = allScrap.Count;
  
            MemCell[] prev = new MemCell[target + 1];
            MemCell[] current = new MemCell[target + 1];
            for (int i = 0; i < prev.Length; i++)
            {
                prev[i] = new MemCell(0, new HashSet<GrabbableObject>());
            }
            
            int calculations = 0;
            for (int y = 1; y <= numItems; y++)
            {
                for (int x = 0; x <= target; x++)
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
                current = new MemCell[target + 1];
                
                // Check if the current best at target index is already equal to the target (most optimal)
                if (prev[target].Max == target)
                {
                    if (inverseTarget)
                    {
                        // If we are calculating the inverse target, prev[target].Included contains what to exclude
                        // So add scrap that is not in prev[target].Included to the final result
                        includedScrap.UnionWith(allScrap.Where(scrap => !prev[target].Included.Contains(scrap)));
                    }
                    else
                    {
                        // If we are calculating the direct target, prev[target].Included contains what to include
                        includedScrap.UnionWith(prev[target].Included);
                    }
                    
                    // Break coroutine
                    yield break;
                }
                
                // Add the amount of calculations to calculations, yield if the number of calculations surpass the threshold
                calculations += target;
                if (calculations > THRESHOLD)
                {
                    yield return null;
                    calculations = 0;
                }
            }
            
            // Calculation went through entire table, meaning a combination == target was not found
            if (inverseTarget)
            {
                // If inverse target was calculated, add the most optimal combination to the excluded set, and
                // add the opposite to the included set
                includedScrap.UnionWith(allScrap.Where(scrap => !prev[target].Included.Contains(scrap)));
            }
            else
            {
                // If direct target was calculated, start from the target index and loop until a Max >= target is found
                for (int i = target; i < prev.Length; i++)
                {
                    if (prev[i].Max >= target)
                    {
                        // If a suitable combination was found, add it to the set of included scrap and break the loop
                        includedScrap.UnionWith(prev[i].Included);
                        break;
                    }
                }
            }
            
            yield return null;
        }

        public bool IsToggled()
        {
            return _toggled;
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
