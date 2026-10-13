# UltraMisery

![UltraMisery icon](package/icon.png)

Developer setup: [Building from source](BUILDING.md).

A punishing custom difficulty for **ULTRAKILL**, built on the game's unfinished **ULTRAKILL MUST DIE** ruleset.

## Public beta — 0.1.0

This is an experimental gameplay overhaul. Expect bugs, compatibility issues and uneven encounter coverage. Build and offline regression checks do not guarantee that every enemy, level or custom map works correctly. Back up your saves and use a separate mod-manager profile before playing.

## Getting started

1. Install UltraMisery through r2modman or Thunderstore Mod Manager. BepInEx is installed as a dependency.
2. Launch ULTRAKILL using the mod manager.
3. Choose **ULTRAMISERY** in the difficulty menu, rather than the separate UKMD entry.
4. Fully restart the game after updating the mod.

For manual installation, install BepInEx first, then copy the included `BepInEx` folder into your game directory.

## Difficulty changes

- Forced Sanded, Radiant and enrage behavior, with progression-sensitive exceptions such as blood-tree Puppets.
- Enemy-specific attack changes, homing-projectile replacements, faster attack chaining and boss behavior adjustments.
- Enemy vulnerability multipliers are inverted into reciprocal resistance factors: for example, a 2x weakness becomes 0.5x damage. Existing resistances remain.
- Enemy oil, ignition, environmental-damage and fall-damage protections, with exceptions for required progression and instant-kill hazards.
- Additional Idol, Deathcatcher and Stalker support enemies in supported encounters.
- Recognized decorative Cerberus, Mannequin, Power and Ferryman statues can introduce additional enemies in supported campaign scenes.
- P-rank time allowance is doubled. Non-P results do not receive an ordinary completion rank.
- Additional secret-boss kill requirements and restrictions on secret exits and level pickups.
- Natural campaign soap is removed; Sandbox spawning remains available.

## Enemy-versus-enemy battles

Enable cheats and **Enemies Attack Each Other** to use AI combat. Sandbox alone does not enable enemy friendly fire.

- Same-species opponents and support enemies can be targeted.
- Enemies can prioritize the Idol or Deathcatcher protecting their target.
- Attributed damage can restore the attacker's health, capped at maximum HP. Same-species damage grants no lifesteal.
- Eligible AI parries and stalemate-healing adjustments are supported.
- Enemies switch to another valid target after 12 seconds without damage, or 6 seconds of failed navigation without damage. Abandoned targets receive a temporary 20-second avoidance period. No alternative means the current target is retained.

## Custom levels and compatibility

Angry Level Loader integration is optional. When installed, it can expose UltraMisery for custom levels. Campaign-specific encounter injection, statue conversion and secret-boss requirements do not extend universally to custom maps.

Avoid combining this beta with other mods that replace enemy AI, damage rules, difficulty selection or projectile behavior unless you are testing compatibility. Other mods are not included in this package.

## Known limitations

- Full campaign coverage, every airborne attack, every boss intro and every enemy matchup have not been certified through live testing.
- Navigation still requires reachable terrain; omniscient awareness does not let enemies move through walls.
- Out-of-bounds detection depends on available level boundaries and is not a complete anti-exploit system.
- Custom or restructured statue models and nonstandard encounter scripts may not be recognized.

When reporting an issue, include the UltraMisery version, level, reproduction steps, enabled mods and relevant BepInEx log excerpt. Remove private information before sharing logs.

## Disabling

Select another difficulty to stop UltraMisery gameplay rules. To remove the plugin completely, disable or uninstall it in your mod manager and restart ULTRAKILL. Manual installations can remove `BepInEx/plugins/UltraMisery/UltraMisery.dll`.

This mod patches behavior at runtime and does not replace the game's `Assembly-CSharp.dll`.
