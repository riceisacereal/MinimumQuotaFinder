using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MinimumQuotaFinder;

public class MathUtilities
{
    private const int THRESHOLD = 300000;
    
    public static List<GrabbableObject> GetGreedyApproximation(List<GrabbableObject> allScrap, int target)
    {
        // Construct a list of Greedily chosen scrap to get a good low high-bound for the direct target
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
    
    public static IEnumerator GetIncludedCoroutine(List<GrabbableObject> allScrap, bool inverseTarget, int calculationTarget,
            HashSet<GrabbableObject> includedScrap)
    {
        // Subset sum/knapsack on total value of all scraps - quota + already paid quota
        int numItems = allScrap.Count;

        MemCell[] prev = new MemCell[calculationTarget + 1];
        MemCell[] current = new MemCell[calculationTarget + 1];
        for (int i = 0; i < prev.Length; i++)
        {
            prev[i] = new MemCell(0, new HashSet<GrabbableObject>());
        }
        
        int calculations = 0;
        for (int y = 1; y <= numItems; y++)
        {
            for (int x = 0; x <= calculationTarget; x++)
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
            current = new MemCell[calculationTarget + 1];
            
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
            calculations += calculationTarget;
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
            includedScrap.UnionWith(allScrap.Where(scrap => !prev[calculationTarget].Included.Contains(scrap)));
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