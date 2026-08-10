# TraumaCore

TraumaCore is a client-side damage and armor overhaul for SPT. It adds anatomical hitboxes, damage-driven bleeding, connected body-part damage, localized armor wear, and a small set of related injury effects.

EFT normally resolves most character damage through broad body-part health pools. TraumaCore keeps those body parts and EFT's equipment coverage, but adds smaller shapes attached to the animated skeleton. Shots are tested along their path through the body, so an organ or bone is only affected when the projectile line intersects its custom geometry.

The default balance is intended for PvE. Brain and cervical-spine hits are immediately fatal, heart wounds cause rapid permanent bleeding, and ordinary chest or limb wounds kill through accumulated blood loss rather than a large amount of immediate bullet damage. Arms have low damage linkage, while legs and the stomach transfer more damage toward connected parts.

## Hitboxes

TraumaCore adds the following geometry:

- Two overlapping ellipsoids form the brain and lower brain lobes.
- A capsule connects the lower brain area to the upper chest as the cervical spine.
- A second capsule continues from the chest through the stomach to the pelvis.
- A box inside the chest represents the heart.
- Capsules follow the major arm and leg bones, including bridging segments between separated EFT rig bones.

The shapes follow EFT's animated head, ribcage, pelvis, and limb transforms. They do not replace the game's visible body-part colliders; those colliders still determine whether a shot hit the head, chest, stomach, arm, or leg before TraumaCore performs its internal intersection tests.

Brain and cervical-spine intersections are fatal. A thoracic or lower-spine intersection applies a native-compatible spinal fracture to the chest or stomach. Limb-bone intersections apply EFT fractures to the struck limb.

## Damage and Bleeding

By default, 15% of a bullet's adjusted damage is applied immediately. The remaining threat comes from wounds created according to the hit location and original bullet damage.

- Heart wounds create fast, permanent bleeding and a dedicated Heart Wound status effect.
- Chest wounds stack and become more severe with repeated hits.
- Face, stomach, arm, and leg wounds produce location-scaled bleeding.
- Non-heart wounds decay toward 10% strength over five seconds.
- Blood-loss blockers reduce treatable bleeding without removing permanent heart bleeding.
- Bleeding damage can pass through linked body parts, with reduced retention after crossing blacked parts.

Default linkage multipliers are `0.20` for arms, `1.00` for legs, and `0.75` for the stomach. Damage crossing one, two, or three-or-more blacked parts retains `85%`, `60%`, or `30%` respectively.

Procedural blood particles emit from the attached wound location and can leave EFT blood impacts on the environment. Corpses can produce a small amount of additional blood when shot, limited by a cached blood reserve and an eight-second fade.

## Armor

TraumaCore replaces EFT's coupled armor resistance and damage-reduction result with a binary penetration roll. EFT still selects the covered area, armor component, plate, material, armor class, and current durability. TraumaCore uses those values with the ammunition's penetration power to decide whether the shot penetrates or stops.

- A penetrating round continues with 65% of its remaining penetration power.
- A stopped round deals no direct body damage and can apply a temporary Bruised effect.
- Armor materials retain their individual destructibility values.
- A stopped hit creates a localized weak spot attached to that armor component.
- Repeated stopped hits within 4.5 cm of the same point deal increasing durability damage: `2x`, `3x`, `4x`, then a `5x` cap.
- Weak spots increase durability loss only; they do not directly increase penetration chance.

The Bruised effect reduces movement speed and stamina recovery for up to 15 seconds. Bruised, Heart Wound, and Spinal Fracture use custom status icons.

## Default Balance

Custom body trauma and armor penetration are enabled for players and AI/scavs by default, but their hitbox balance differs:

| Setting | Player | AI / Scav |
| --- | ---: | ---: |
| Trauma damage multiplier | `0.25` | `1.00` |
| Brain hitbox | Enabled | Enabled |
| Heart hitbox | Disabled | Enabled |
| Cervical spine | Disabled | Enabled |
| Thoracic/lower spine | Enabled | Enabled |

This keeps the full anatomical model on AI while reducing sudden organ lethality against the player. Every target system and hitbox can be enabled or disabled separately through F12.

World blood effects are enabled by default. Anatomical debug rendering is disabled by default. Debug logging remains enabled to help diagnose hit classification and armor results.

## Feedback and Debugging

Non-head TraumaCore deaths use EFT death or agony voice lines with weighted variation so delayed bleed-outs remain audible when a target moves out of view. Fatal head hits have a smaller chance to play a death vocalization.

The optional organ renderer shows the live brain, heart, spine, limb-bone, armor weak-spot, and projectile-intersection geometry. Actual intersections draw a thick line from the surface hit to the internal contact point, along with a colored sphere and marker. This renderer is intended for testing and is not required during normal play.

## Usage

Press F12 to open the BepInEx Configuration Manager and select **TraumaCore**.

Settings are grouped by global damage, player hitboxes, scav/AI hitboxes, bleed balance, damage linkage, visuals, logging, and effect-testing controls.

The generated configuration file is:

`BepInEx/config/com.traumacore.client.cfg`

Changing source defaults does not overwrite values already saved in this file. Use the reset button beside an option or remove the old configuration file to adopt new defaults.

## Installation

Extract the release archive into the SPT installation directory. The package installs:

`BepInEx/plugins/Hysocs-TraumaCore/TraumaCore.dll`

TraumaCore has no server component.

## Compatibility

TraumaCore patches EFT's shot, health, fracture, movement, and armor calculations. Other mods that replace the same damage or armor methods may override TraumaCore or produce combined behavior. Compatibility should be confirmed before using another health or armor overhaul alongside it.

## Building

Building requires the .NET SDK and SPT 4.1.2 at `C:\SPT\4.1.2`, unless `SptRoot` is supplied as an MSBuild property.

```powershell
dotnet build TraumaCore.sln -c Release -p:SkipDeploy=true
```

The client-only release archive is created under `dist/`.

## License

MIT
