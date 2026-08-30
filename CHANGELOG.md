# TraumaCore 1.5.0

## Added

- Added corpse wound inspection with detailed entry, exit, organ, bone, and projectile information.
- Added corpse dragging with smoother positioning and improved ragdoll stability.
- Added bullet travel through tissue, wound depth, bullet diameter, bone resistance, exit wounds, and fragmentation to the damage calculation.
- Added expanded death-screen hit reports with wound paths, hit locations, penetration details, and bleed damage.
- Added live clot timers and blood-loss DPS information to TraumaCore bleeding effects.
- Added a recommended-defaults button at the top of the F12 configuration menu.

## Changed

- Rebalanced direct and delayed damage so deeper wounds deal more damage while grazing hits still remain meaningful.
- Bleed strength now scales with the bullet wound instead of using fixed damage values.
- Bleeding now uses native light and heavy bleed effects, naturally clots over time, and responds to the correct medical items.
- Bruising, impact shock, spinal fractures, and heart wounds now use native EFT effects with clearer names and safer UI behavior.
- Heart wounds now account for wound path, entrance and exit damage, bullet size, and wound severity.
- Improved blood effects, pain sounds, damage feedback timing, and bleed kill attribution.
- Simplified armor handling to work with EFT penetration results while TraumaCore controls post-penetration wound damage.
- Updated the default balance settings for the new damage and bleeding model.

## Fixed

- Fixed older profile health effects causing profiles to load forever after upgrading.
- Fixed several corpse inspection and dragging issues that could stretch, launch, or invalidate ragdolls.
- Fixed corpse dragging releasing incorrectly while prone by checking from the camera position.
- Fixed rapid repeated blood overlays and pain sounds during ongoing bleeding.
- Improved compatibility with quests, death attribution, profile loading, and EFT health menus.
