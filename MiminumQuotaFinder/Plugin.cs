using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using BepInEx;
using HarmonyLib;
using LethalCompanyInputUtils.Api;
using UnityEngine;
using UnityEngine.InputSystem;
using static MinimumQuotaFinder.MathUtilities;

namespace MinimumQuotaFinder
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency("com.rune580.LethalCompanyInputUtils")]
    public class MinimumQuotaFinder : BaseUnityPlugin
    {
        private const string GUID = "com.github.riceisacereal.MinimumQuotaFinder";
        private const string NAME = "MinimumQuotaFinder";
        private const string VERSION = "1.1.3";

        public static MinimumQuotaFinder Instance
        {
            get;
            private set;
        }
        
        internal static HighlightInputClass InputActionsInstance = new();
        
        public Material wireframeMaterial;
        private bool _toggled = false;
        private int _highlightLockState = 0;
        private int previousQuota = -1;
        private int previousResult = -1;
        private HashSet<GrabbableObject> previousInclude;
        private HashSet<GrabbableObject> previousAllScraps;
        private Dictionary<MeshRenderer, Material[]> _highlightedObjects = new();
        
        private List<string> excludedItemNames = new() {"Shotgun", "Ammo", "Gift"};

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
        }

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
            if (!highlightContext.performed || GameNetworkManager.Instance.localPlayerController == null) return;
            
            if (!HUDManager.Instance.CanPlayerScan() || HUDManager.Instance.playerPingingScan > -0.5f) return;
            
            // Highlight if toggled, unhighlight otherwise
            if (!_toggled)
            {
                TurnOnHighlight(true, true);
            }
            else
            {
                TurnOffHighlight();
            }
        }

        public void TurnOnHighlight(bool displayIfCached, bool displayReason)
        {
            // Cancel the highlighting if the player is outside of their ship on a moon
            if (!CanHighlight(displayReason)) return;
            // Start the coroutine for highlighting, this will give back control after the first round of calculations
            GameNetworkManager.Instance.StartCoroutine(HighlightObjectsCoroutine(displayIfCached));
        }

        public void TurnOffHighlight()
        {
            // Try to acquire the lock
            int oldValue = Interlocked.CompareExchange(ref _highlightLockState, 1, 0);

            // Return early if the lock was already acquired by something else
            if (oldValue == 1)
            {
                return;
            }

            try
            {
                UnhighlightObjects();
            }
            finally
            {
                _toggled = false;
                // Set the lock free
                Interlocked.Exchange(ref _highlightLockState, 0);
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
            // At the counter, only values of scrap items with a value larger than 0 and are not unreachable (accidentally dropped over the railings)
            // are added to company credit
            List<GrabbableObject> allScrap = scope.GetComponentsInChildren<GrabbableObject>()
                .Where(obj => obj.itemProperties.isScrap && 
                              obj.scrapValue > 0 && 
                              obj.transform.position.y > minimumHeight &&
                              !excludedItemNames.Contains(obj.itemProperties.itemName)).ToList();
            
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
            // Try to acquire the lock
            int oldValue = Interlocked.CompareExchange(ref _highlightLockState, 1, 0);

            // Return early if lock was already acquired by something else
            if (oldValue == 1)
            {
                yield break;
            }
            
            try
            {
                // Show a message to indicate the starting of the calculations in case of big calculation
                if (displayIfCached)
                {
                    HUDManager.Instance.DisplayTip("MinimumQuotaFinder", "Calculating...");
                }

                List<GrabbableObject> allScrap = GetAllScrap(StartOfRound.Instance.currentLevelID);
                HashSet<GrabbableObject> toHighlight = new HashSet<GrabbableObject>();
                // Start a coroutine to calculate which items to highlight, this will give back control after all calculations are finished
                yield return GameNetworkManager.Instance.StartCoroutine(GetSetToHighlight(allScrap, toHighlight,
                    displayIfCached));

                List<GrabbableObject> deskScrap = GetDeskScrap();
                // Highlight the objects or untoggle if nothing should be highlighted
                if (toHighlight.Count + deskScrap.Count > 0)
                {
                    UnhighlightObjects();
                    HighlightObjects(toHighlight);
                    HighlightObjects(deskScrap);
                    _toggled = true;
                }
                else
                {
                    _toggled = false;
                }
            }
            finally
            {
                // Set the lock free
                Interlocked.Exchange(ref _highlightLockState, 0);
            }
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

            // If the amount sold has already reached the quota
            if (sold >= quota)
            {
                int difference = sold - quota;
                string colour = difference == 0 ? "#A5D971" : "#992403";
                HUDManager.Instance.DisplayTip("MinimumQuotaFinder",$"Quota has been reached (<color={colour}>{sold}</color>/{quota}).");
                yield break;
            }

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

        public bool IsToggled()
        {
            return _toggled;
        }

        public bool CanHighlight(bool displayReason)
        {
            bool onCompanyMoon = IsOnCompanyMoon(StartOfRound.Instance.currentLevelID);
            if (onCompanyMoon && Math.Abs(StartOfRound.Instance.companyBuyingRate - 1f) >= 0.001f)
            {
                if (displayReason)
                {
                    HUDManager.Instance.DisplayTip("MinimumQuotaFinder", "Buying rate is not at 100%, no scrap has been highlighted as the calculations would be inaccurate.");
                }
                return false;
            }
            if (!onCompanyMoon && !GameNetworkManager.Instance.localPlayerController.isInHangarShipRoom)
            {
                if (displayReason)
                {
                    HUDManager.Instance.DisplayTip("MinimumQuotaFinder", "Highlighting disabled, player is not on the ship");
                }
                return false;
            }

            return true;
        }

        private bool IsOnCompanyMoon(int levelID)
        {
            return StartOfRound.Instance.levels[levelID].name == "CompanyBuildingLevel";
        }
    }
    
    public class HighlightInputClass : LcInputActions 
    {
        [InputAction("<Keyboard>/h", Name = "Toggle scrap highlight")]
        public InputAction HighlightKey { get; set; }
    }
}