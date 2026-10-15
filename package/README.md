# UltraMisery

A punishing custom difficulty for **ULTRAKILL**, built on the game's unfinished **ULTRAKILL MUST DIE** ruleset.

Malicious Faces leave no head/corpse or corpse fragments when killed while UltraMisery is selected.

## Public beta — 0.2.8

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

## Forced mission restarts in 0.2.1

While UltraMisery is selected, triggering these authored HUD messages restarts the mission from the beginning, discarding checkpoint progress: "PIPE CLIP LIVES" (0-1), "WHAT'S UPDOOR?" (0-2), "LOOKS LIKE YOU'RE AT WIT'S END" (1-S), "OFF THE BEATEN TRACK" (2-4), "YUP, THAT'S A CAVITY" (3-1), and "THE FILTH IS GONE, BUT THE MEMORY REMAINS" (4-3). With cheats enabled, these quote penalties are disabled and secret-level entry is allowed.

With cheats disabled, entering a secret exit restarts the current mission. Direct S/Prime scene loads are redirected too. If a loader bypasses the normal loading API, a scene-entry guard returns to the previous regular mission, or 0-1 when none is available, preventing a secret-scene restart loop. Ordinary exits remain usable. These rules have passed offline checks; the actual scene reload still needs an in-game test.

## Weapon rebalance in 0.2.0

- Both revolver forms have doubled secondary recharge times and cannot detonate cores or rockets, including via coins. Charged Piercer damage is halved and wall penetration remains. Coins prioritize highest current HP, then nearest distance, and target resistant hitboxes; punched coin damage is halved.
- Regular shotguns have half primary damage, zero equip/swap delay and a 1-second shared primary cooldown. Alt-fire retains its own cooldown; regular pellet spread is one quarter of normal. Core damage is halved and secondary cooldown doubled. Firing at three pumps produces the native explosion and then forces player death. Attached chainsaws break after ten nonlethal hits, cannot be parried/detached by the arms, and recharge twice as slowly.
- A jackhammer hit on a living enemy overheats only the fired variant for 7.5 seconds, including killing blows. Misses, scenery, corpses and striking a core do not trigger this penalty. Core eject retains native primary follow-up timing; core recharge remains doubled.
- Attractor has 25 nails or five saws, one magnet with half its normal countdown lifetime and 50% more supported load, double fire rate and 1.5x ammo regeneration. Overheat nailgun has one heatsink, triple fire rate and heat accumulation at 1/20 of the base-game rate; secondary Overheat saw lifetime is halved. All nailgun-family damage, including sawblades and Jumpstart electricity, is one third of the previous mod damage (1/12 of base-game damage remains). Every sawblade variant fires at half its current rate while a player Conductor cable is attached to a living enemy, even after swapping; regular nail firing rates are unchanged.
- Jumpstart/Conductor takes ten seconds to charge without boosts, recharges in 7.5 seconds after use (50% longer), and is lethal while conducting underwater.
- Railcannons deal half damage and recharge twice as slowly. Electric fire underwater is lethal; Screwdriver drilling lasts 15 seconds; Malicious cannot detonate cores or rockets.
- Rockets deal half damage, travel/fire half as fast, have twice the ride fuel duration and cannot be caught by Whiplash. Freezeframe drains at twice the normal game rate (2.5 seconds for a full meter); recharge remains twenty seconds for a full meter; one rocket ride is allowed before landing. SRS recharge takes twice as long. Oil-covered floors halve player movement speed while walking, sliding or dashing; vertical jump/fall speed is unchanged.
- Five wall jumps and one double jump apply throughout UltraMisery. Knuckleblaster shockwave radius is halved.

This local candidate passed compilation and offline regression checks. Weapon physics, animation events, hitbox selection and live Harmony installation still require an in-game test after a full restart.

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

The final-wall-jump sound plays on the fifth wall jump while UltraMisery is selected. Five wall jumps and the extra airborne jump remain available.

- Overheat heat buildup from firing is 1/20 of the base-game rate, for both nails and sawblades; spent-heatsink recharge is unchanged.
- Power greatsword orbs use the shared homing prefab while retaining the paired swing and connecting beam.
- In enemy-versus-enemy mode, Prison/Panopticon reacquire living enemies and recover interrupted attacks; black holes damage enemy hitboxes rather than applying their player-only impact behavior.

- Guttertank and Big Johninator mines cannot be parried in UltraMisery, including the Feedbacker reward path; normal mine activation/explosion stays intact.
- Mirror Reaper hands become independent enemy-combat targets tied to their native Breakable durability and lifetime, with no kill-count registration. Their existing hurt zone can damage enemies, excluding the owner through normal attribution.
- Mirror Reaper prioritizes poison barrages on alternating ranged decisions; barrage animation speed doubles and post-barrage cooldown halves against both players and enemies. Other attack animations retain their speed.
