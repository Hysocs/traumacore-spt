# TraumaCore

TraumaCore is a damage and armor overhaul for SPT that makes shot placement, bleeding, bones, and repeated armor hits more important.

Instead of treating every hit to a body part the same way, TraumaCore checks where the bullet travels inside the body. A shot can strike the brain, heart, spine, or a major limb bone. Hits that miss those areas still cause normal wounds and bleeding based on the body part that was struck.

The default settings are designed around PvE. Accurate shots to vital areas end fights quickly, chest wounds become dangerous when they stack, and shooting arms or legs is less effective than shooting center mass. Players use more forgiving organ settings than AI and scavs by default.

## Features

### Anatomical Hitboxes

- Brain and lower-brain hitboxes that move with the head
- A fatal cervical-spine hitbox through the neck
- A lower spine running from the chest to the pelvis
- A heart hitbox inside the chest
- Major bones in the arms and legs

Brain and cervical-spine hits are fatal. Heart hits cause extremely fast bleeding. Lower-spine hits cause a spinal fracture, while hits to limb bones can fracture the affected arm or leg.

The hitboxes follow the character's movement and animations. They do not make the entire head or chest count as an organ hit—the bullet must pass through the smaller internal area.

### Wounds and Bleeding

Most bullet damage is converted into wounds instead of being applied immediately.

- Chest wounds stack with repeated hits.
- Heart wounds cause fast bleeding that cannot be treated normally.
- Face, stomach, arm, and leg wounds bleed at different rates.
- Ordinary bleeding becomes weaker after the last hit.
- Blacked body parts pass some damage toward connected healthy parts.
- Arms transfer less damage than legs or the stomach.

This makes center-mass hits reliable without making every chest shot an instant kill. Limb shots can still be lethal, but generally require more hits or time than vital-area damage.

### Armor

Armor either stops a shot or allows it through based on the ammunition, armor class, material, condition, and covered area.

When armor stops a shot, that location becomes weakened. Repeated hits near the same point deal increasing armor damage, up to five times the normal durability loss. This does not guarantee penetration, but it prevents the same small area from behaving like untouched armor after several impacts.

Stopped rounds can also cause a temporary Bruised effect that reduces movement speed and stamina recovery.

### Feedback

- Custom icons for Bruised, Heart Wound, and Spinal Fracture
- Blood particles emitted from the wound location
- A small amount of blood when shooting a recently killed body
- Death and agony sounds for delayed bleed-out deaths
- Optional hitbox and intersection renderer for testing

The debug renderer is disabled by default.

## Default Balance

TraumaCore is enabled for players, AI, and scavs by default.

AI and scavs use the complete organ system. Players begin with brain and lower-spine hitboxes enabled, while player heart and cervical-spine hitboxes are disabled to reduce sudden deaths from AI fire.

Default damage linkage is:

- Arms: `0.20`
- Legs: `1.00`
- Stomach: `0.75`

These values and the individual player and AI hitboxes can be changed through F12.

## Usage

Press F12 to open the BepInEx Configuration Manager and select **TraumaCore**.

The menu includes separate controls for player and AI hitboxes, bleeding, damage linkage, armor behavior, blood effects, logging, and effect-testing buttons.

The configuration file is:

`BepInEx/config/com.hysocs.traumacore.cfg`

Existing configuration files keep their saved values after an update. Use the reset button beside a setting or remove the old configuration file to apply new defaults.

## Installation

Extract the release archive into the SPT installation directory. The package installs:

`BepInEx/plugins/Hysocs-TraumaCore/TraumaCore.dll`

TraumaCore has no server component.

## Compatibility

TraumaCore changes EFT's health, armor, fracture, and movement behavior. Other damage or armor overhauls may conflict if they modify the same systems.

## Building

Building requires the .NET SDK and SPT 4.1.2 at `C:\SPT\4.1.2`, unless `SptRoot` is supplied as an MSBuild property.

```powershell
dotnet build TraumaCore.sln -c Release -p:SkipDeploy=true
```

The client-only release archive is created under `dist/`.

## License

MIT
