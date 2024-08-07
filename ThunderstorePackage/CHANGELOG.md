# Changelog

## v1.1.4
- Fix bug where direct target would be taken as actual target to calculate leading to suboptimal results

## v1.1.3
- Fix edge case where the lowest valued scrap would be highlighted when the quota was already reached

## v1.1.2
- Remove scrap spawning functionality used for debugging that was accidentally left in

## v1.1.1
### Features
- Shotguns, Ammo, and Gifts will no longer be taken into account for the calculating/highlighting.

## v1.1.0
### Features
- Added instruction for highlighting to the HUD tooltips (top right corner) when first joining a file and when scanning within the ship or on the company moon
- Made it so that highlighting can not be triggered when outside the ship on non-company moons (you can still unhighlight)
- Made it so that scrap on the counter at the company moon is counted as sold directly as these can't be taken off the desk
- Added auto-recalculation when the wrong scrap is put on the counter
### Optimizations
- Added direct target calculation with greedy approximation on top of inverse target calculation
- Added a way for the algorithm to terminate early if an optimal solution has already been found
### Bug fixes
- Fixed render distance of wireframe material on all scrap items
- Fixed wireframe material on moving parts of scrap items
- Made highlight button spam proof
- Stopped highlighting on company moon when buying rate is not at 100% to avoid inaccuracies
### Documentation
- README
  - Added instructions in case of selling wrong scrap
  - Added more explanation and justification for assuming 100% buy rate
- Add CHANGELOG

## v1.0.1 2024-1-30
### Structural
- Changed file structure
  - Moved `wireframe` out of AssetBundle folder and into root because Thunderstore somehow got rid of the folder and placed it in root
### Documentation
- Emphasized the keybind in README
- Changed description and version number in `.csproj`

## v1.0.0 2024-1-29
- Initial version