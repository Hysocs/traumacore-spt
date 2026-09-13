# TraumaCore

TraumaCore is an SPT damage and armor overhaul that adds anatomical hitboxes, location-based wounds and bleeding, fractures, and progressive armor damage to make shot placement and repeated hits more meaningful.

## Limb geometry

Arm and leg calibration is finalized in `LimbBoneGeometry`; these dimensions no longer have F12 controls. Each arm has one upper-arm bone and two forearm bones. Each leg has one thigh bone and one lower-leg bone.

Both thighs taper from 55 mm at the hip to 45 mm at the knee. Both lower legs taper from 45 mm at the knee to 37.5 mm at the ankle. Upper arms taper from 36.69 mm at the shoulder to 30.32 mm at the elbow, incorporating two successive 10% shoulder increases. These sizes are diameters.

Tuned bone dimensions and offsets use millimeters rounded to at most two decimals, converted to meters for Unity. This keeps the constants readable without rounding away small anatomical adjustments.
The calibration mannequin, entity ESP, and wound inspector share `AnatomyOverlayGeometry` and `AnatomyOverlayRenderer`. Mannequin and corpse limb anchors map onto their displayed mesh skeletons before calibration offsets are applied. Change anatomy shapes in the shared geometry builder and their drawing behavior in the shared renderer.

The animated death-screen model also uses the shared anatomy geometry and renderer. It follows the saved wound-inspection Trajectories, Anatomy, Anchors, and Hide Back Hits settings. Preview projection respects the model texture's UV rectangle, and wounds update after animation. New limb wounds record their upper/lower segment anchor name so forearm and calf hits follow the matching animated bone; older records fall back to their original body-part anchor.

## Spine geometry

Spine calibration is finalized in `SpineBoneGeometry`; anatomy endpoint, thickness, and movement-step controls are no longer exposed in F12. Cervical diameters taper from 70 mm at the brain endpoint to 55 mm at the chest endpoint. Thoracic diameters taper from 60 mm at the chest to 50 mm at the pelvis. The saved torso-relative offsets apply to the original tuned anchors, and rendering and hit detection share the resulting tapered shapes.

The cylinder intersection checks can be executed with the .NET 10 SDK and the local SPT dependencies:

```powershell
dotnet run --project Tests/Geometry/Geometry.csproj -p:SkipDeploy=true -p:SkipPackage=true -p:NuGetAudit=false
```
