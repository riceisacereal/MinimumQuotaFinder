# Changelog

## Unreleased
### Features
- Added instruction for highlighting to the HUD when first joining a file and when scanning within the ship or on the company moon
- Blocked highlighting when not on the ship when not on the company moon (you can still unhighlight)
- Count scrap on the desk on the company moon as sold as these can't be taken off the desk
### Bug fixes
- Fixed render distance of wireframe material on certain scrap items
- Fixed wireframe material on moving parts of scrap items
### Documentation
- Add instructions in case of selling wrong scrap
- Add more explanation and justification for assuming 100% buy rate
## v1.0.1 2024-1-30
### Structural
- Change file structure
  - Moved `wireframe` out of AssetBundle folder and into root because Thunderstore somehow got rid of the folder and placed it in root
### Documentation
- Emphasize the keybind in README
- Change description and version number in `.csproj`
## v1.0.0 2024-1-29
- Initial version