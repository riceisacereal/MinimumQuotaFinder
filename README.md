# MinimumQuotaFinder
This mod calculates and highlights the minimum total value of scraps that you can sell to still reach the quota.

The same in essence as: [ScrapCalculator](https://thunderstore.io/c/lethal-company/p/granny/ScrapCalculator/)\
Scrap auto-selling mods: [SellMyScrap](https://thunderstore.io/c/lethal-company/p/Zehs/SellMyScrap/), [SellFromTerminal](https://thunderstore.io/c/lethal-company/p/stormytuna/SellFromTerminal/)

## _‼ What our mod does better_
- Always finds the most optimal answer quickly (5ms to 1s depending on quota and number of items) using an [optimized algorithm](#explanation-of-algorithm).
- Cool highlighting shader

## Quirks of the way the mod works
- When you're on the company moon, all scrap in the environment is taken into account. Otherwise only scrap within the ship is considered.
  - Pros: You can take items outside the ship on the moon and they will still be included in the calculation. ?Scrap on the counter but not sold yet will be considered correctly as well?
  - Cons: If you drop an item in the water or somewhere else unreachable on the company moon, it will still count.
- This is a client-side mod, which means that there is a small chance of different items being highlighted for different people. We have several methods in place to prevent this from happening, but you never know.
  - If this happens, report an issue and tell us how it happened.

## Explanation of Algorithm
### The problem
The problem this mod tries to solve - finding the most optimal combination of scrap that is the closest to the quota, is a variant of the [subset sum problem](https://en.wikipedia.org/wiki/Subset_sum_problem) and has a computational complexity of [NP-Hard](https://en.wikipedia.org/wiki/NP-hardness), which in a very generalized way of saying means that there is no easy formula to get the answer right away and that we have to find it by "trying every combination".

The number of possible combinations for all the scrap you have grows [exponentially](https://math.stackexchange.com/a/3788314), by the time you have 20 pieces of scrap you will have to check over 1 million combinations, and 1 billion when you have 30 pieces of scrap. When you're up to 5k+ quota it will take half your lifetime to check all combinations with even if you check 1 million combinations per second.

Algorithms for NP-hard problems utilize ways to [ignore certain combinations](https://en.wikipedia.org/wiki/Decision_tree_pruning), and [storing the results of calculations that are repeated multiple times](https://en.wikipedia.org/wiki/Memoization). An example of the former would be, if you have a quota of 1000, and we know that the highest value a scrap can have is 210, we could ignore any combinations that have less than 5 pieces of scrap, since they would never reach the quota (4 * 210 = 840 < 1000).

### The Algorithm
The algorithm we've implemented is very similar to the solution to the [0-1 knapsack problem](https://en.wikipedia.org/wiki/Knapsack_problem#0-1_knapsack_problem) which uses [Dynamic Programming](https://en.wikipedia.org/wiki/Dynamic_programming) and memoization. The only difference is that the knapsack problem tries to find the maximum <= a threshold, since we need the minimum of >= quota, we ran the knapsack solution on a threshold of (total value of scrap owned - quota) to find what we should *exclude*.

### Computation Time Scaling
The 2 factors that determine how long it takes to calculate an answer are the value of the quota, the number of scrap, and the total value of that scrap. The algorithm goes through a table the size of (total value of scrap owned - quota) * number of scrap, so the bigger the difference between your quota and the total value of all your scrap, and the more scrap you have, the longer it takes to calculate.

## Credits
This mod was built using the [BepInEx mod template](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html). Part of [ShipLoot mod](https://github.com/tinyhoot/ShipLoot)'s code was taken as a starting point. [InputUtils](https://thunderstore.io/c/lethal-company/p/Rune580/LethalCompany_InputUtils/) was used to make the keybinding.

## Installation
[Thunderstore link]()
