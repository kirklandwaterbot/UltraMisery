# UltraMisery 0.2.0 weapon balance

Local implementation of the weapon roadmap. Applies when UltraMisery is selected. This build has passed compilation and offline checks against the installed game assemblies; it has **not been playtested in Unity**.

## Implemented rules

| Weapon | Change |
| --- | --- |
| All revolvers | Direct, charged, ricocheted and coin-reflected shots cannot detonate cores or rockets. Hammer-boosted core beams and cannonballs retain their own interactions. |
| Piercer, normal and slab | Charged shot damage ×0.5; secondary recharge time ×2. Existing charged wall penetration retained. |
| Marksman, normal and slab | Coin recharge time ×2. Enemy selection prefers highest current HP, with nearest distance breaking ties. Targets resistant body parts rather than the authored weak point. Punched coin damage ×0.5. Native coin chaining and visibility filters remain. |
| Sharpshooter, normal and slab | Secondary recharge time ×2. |
| Regular shotguns | Primary damage ×0.5, pellet spread ×0.25. Zero equip/swap delay. All regular variants share a 1-second primary cooldown, enforced before the native firing body creates effects. Secondary actions retain their own cooldowns. Equip/readiness callbacks and temporary active-scene changes cannot reset the shared deadline. |
| Core Eject, both forms | Secondary cooldown ×2; core explosion and boosted-core damage ×0.5, including secondary explosions. |
| Pump Charge, both forms | Firing at three pumps produces the native explosion, then forces player death. Pumping itself does not. |
| Sawed-On, both forms | Attached saw breaks on the tenth nonlethal hit. Feedbacker parry and Knuckleblaster detachment are disabled. Chainsaw recharge time ×2. |
| Jackhammers | A hit against a living enemy (including a killing blow) overheats only its own variant for 7.5 seconds. Misses, scenery, corpses and core strikes do not trigger overheat. Primary damage ×0.5 and primary recovery rate ×0.5. Saw secondary animation recovery ×2; core eject retains native animation/primary follow-up timing. Existing native equip restrictions retained. |
| All nailguns/sawblades | Enemy damage ×1/12 for all variants, including nailbombs, Overheat saws and Jumpstart electricity/aftershocks: one third of the previous ×0.25 damage, applied once across overlapping scopes. Every sawblade variant fires at half its current rate while a player nailgun cable is attached to a living enemy, including after swapping. |
| Attractor nailgun | 25 maximum nails, one shared magnet with half its normal countdown lifetime and 1.5× supported load, firing rate ×2, ammo regeneration rate ×1.5. |
| Attractor sawblade | Five maximum saws, one shared magnet with half its normal countdown lifetime and 1.5× supported load, firing rate ×2, ammo regeneration rate ×1.5. |
| Overheat nailgun | One heatsink; primary and burst firing intervals ÷3; heat accumulation rate ×1/20. |
| Overheat sawblade | Secondary saw damage ×1/12 as part of the family-wide rule; native removal timers ×0.5. |
| Jumpstart, both forms | Ten-second unboosted connection charge; post-use recharge takes 7.5 seconds (×1.5). Conducting an attached target while the player is submerged is lethal. Electrical damage, including nail/saw contribution and aftershock, receives the family-wide ×1/12 reduction once. |
| Railcannons | Shared recharge time ×2; damage ×0.5. Electric fire underwater is lethal. Screwdriver spreads its native drill tick count over 15 seconds; its impact, ticks and punched damage are halved. Malicious direct hits and splash cannot detonate cores or rockets. |
| Rocket launchers | Primary rocket damage, travel speed and firing rate ×0.5; ridden rocket descent accumulation ×0.5 (twice the fuel duration). Whiplash cannot catch rockets. These changes include Firestarter primary rockets. |
| Freezeframe | Full freeze drains in 2.5 seconds (twice normal game drain) and replenishes in twenty seconds: 0.625/5 seconds per quarter. Only one rocket mount per airborne period; actual ground contact restores it. Enemy steps do not restore this budget. |
| SRS Cannon | Cannonball recharge time ×2; cannonball direct and explosion damage ×0.5. |
| Firestarter | Oil-covered floors halve player planar movement while walking, sliding or dashing, without compounding or changing jump/fall speed. Existing ignition and enemy fire protections retained. |
| Knuckleblaster | Shockwave radius and expansion speed ×0.5. |
| Whiplash | Every hooked living enemy uses the heavy-target pull direction: player toward enemy, including normally light enemies and Drones. Objects and native hook eligibility/release rules remain unchanged. Applies while UltraMisery is selected. |
| Movement | Five wall jumps and one extra airborne jump throughout UltraMisery. The final-wall-jump sound plays on wall jump five. The extra jump resets on ground contact. Native input locks and jump restrictions still apply. |

## Interpretation and implementation details

- Damage multipliers are relative to this mod's existing damage and resistance rules, not a bypass of those rules. Overlapping nerf sources apply once, so a core/rail interaction does not accidentally receive a quarter multiplier. Enemy damage is scaled at delivery to avoid rounding low integer explosion damage to zero. Existing self-explosion damage is retained; lethal penalties use the normal player damage/death path with dash invulnerability bypassed.
- Coins prefer an active collider marked Armor, then resistant head/limb tags under UltraMisery's existing critical inversion, then body colliders, excluding the authored weakPoint. Custom enemies with unusual hitboxes still need verification. Native line-of-sight and portal filtering are retained.
- "Underwater" follows the game's `UnderwaterController.inWater` state. Water touching only the enemy does not kill a dry player. Vanilla invincibility cheats/zero-damage assists are not overridden.
- Timers use game time and native physics/animation cadence, so observations can vary by a frame. The regular shotgun primary gate uses a fixed one-second game-time deadline and survives swapping; it is no longer derived from animation events.
- Enemy rockets are excluded from player rocket speed/fuel scaling. Oil refill is excluded from shared rocket resource edits. Existing enemy-combat rules were not rewritten.

## Verification

- Release build: zero errors, zero warnings.
- Weapon harness: 58 native patch target/field/IL checks, call replacement signature checks, highest-HP/tie ordering, 34 mode-on/off resource and timing cases, 16 overlapping damage-scope cases, and four drill duration cases.
- Existing regression harness: passed against the new DLL, including 279 attack-ownership target methods, enemy AI/damage/encounter policies, original Piercer spawn hook, and Angry integration metadata.
- These are offline checks. They do not execute Harmony patch installation inside Unity, physics collisions, animation events, prefab layouts or live gameplay. The DLL is a local test candidate, not a verified public release.

## Required in-game acceptance pass

Fully exit ULTRAKILL, import the local ZIP into a test profile, launch through the mod manager, and select UltraMisery. Check BepInEx/LogOutput.log for patch exceptions.

1. Compare Piercer normal/slab primary and charged damage; verify charged wall penetration and doubled secondary timers. For coins, use two enemies with unequal current HP, then equal HP at unequal distances; confirm selected hitbox and half coin-punch damage.
2. Fire each regular shotgun, swap away immediately and back, and attempt both fire buttons. Repeat after the reload begins. Verify normal reload, core recharge, narrow spread, ten-hit saw break, blocked arm interactions, and firing-only overpump death.
3. Fire green/yellow/red jackhammer hits, including killing blows and delayed hits completed on unequip. Only the fired variant should overheat for 7.5 seconds; the other two remain available. Miss into air, hit scenery/corpses and strike a core: none should trigger this overheat. Eject a core then charge/release primary to strike it, including a precharged primary; check native combo timing and unchanged doubled core recharge. Check saw secondary recovery before/after swapping.
4. Check attractor ammo caps, refill rates and shared single magnet and half-duration countdown on walls/enemies (both variants); overheat heat gain/rate and secondary saw damage/lifetime; Jumpstart charge, boosted nuke and underwater conduction.
5. Time the full rail recharge and Screwdriver drain. Check electric underwater death. Shoot Malicious directly at a core/rocket and next to one; neither path should trigger it. Check reflected rail/core damage remains halved once.
6. Check all three launchers' primary rockets, SRS recharge, Whiplash rejection, 2.5-second full freeze duration, twenty-second recharge and ride duration. Walk/slide/dash onto oil at different speeds; verify half planar speed, sustained contact without compounding, exit/reentry, jumping, removal of oil while stationary and mode-off behavior. A second airborne mount must fail until ground contact.
7. Check five wall jumps, one double jump, landing reset, input locks, death/restart and switching away from UltraMisery. Compare Knuckleblaster shockwave radius.

No game install, Thunderstore upload or GitHub push is part of this local build.

## Malicious Face corpse removal (0.2.5)

Native death events/bookkeeping remain intact; the head's renderers and colliders are disabled immediately and its GameObject is destroyed at the end of the frame. BreakCorpse no longer spawns stone debris while UltraMisery is active. Verify ordinary, cannonball, slam and jackhammer kills, room unlocks/kill counts, lack of corpse collision/crush behavior, and normal mode-off death behavior in game.

## 0.2.6 playtest additions

Rapidly cycle all three regular shotguns, including immediately after firing and alternating primary/secondary: no shot should bypass shared reload recovery or the 0.5-second equip gate. Check regular pump levels 0/1/2 for quarter-normal spread and three pumps for explosion plus death. Repeat overpump with the jackhammer on a miss and an enemy hit. Verify primary-to-core hammer combos still work, final wall-jump audio occurs only on jump five, and normal difficulty retains native behavior.

## 0.2.7 playtest additions

Shoot cores and rockets with each normal/slab revolver primary and secondary, direct and via coins/ricochets; verify no detonation. Check enemy damage, wallbang, hammer core strikes and cannonballs remain functional. Load magnets with nails/saws and confirm the 1.5× overload threshold while lifespan and count stay unchanged. Compare nail/saw/Jumptstart hit damage and nailbomb/aftershock damage with mode off, and time Conductor recharge from empty to full (7.5 seconds).

## 0.2.8 playtest additions (supersedes 0.2.6 timing)

Fire then cycle every regular variant: primary must remain blocked only until 1 second after the previous primary, while equip adds no delay. Use a different variant's core/pump/saw during that window and check its own recharge still applies. Repeated primary on one variant should recover at 1 second even with a longer animation; starting a secondary cancels any pending primary animation-readiness callback. Check mode-off native timing.

Same-version swap hotfix: also test rapid cycling while holding primary, multiple weapon copies with delayed firing, and a three-pump shot attempted during the shared cooldown. Rejected attempts must create neither pellets nor an overpump explosion/death, and must not extend the deadline. A fresh mission resets the clock; additive/temporary active-scene changes do not. These in-game cases require a full game restart and playtest.

Build Overheat heat from empty with both forms: nails take 20 times the base-game firing time and saws 160 primary shots versus 8. Spent-heatsink regeneration is unchanged.

Check Power horizontal/vertical greatsword swings: both projectiles use Projectile Homing.prefab and retain the beam, enemy target and native attack geometry. Check player/enemy targets, parries and mode-off behavior.

For Prison and Panopticon enemy combat, kill/remove targets during windup, burst and drone spawning; they must find other living enemies (including bosses/support enemies) without stalling. Spawn another enemy after an empty arena. Check black-hole direct hits and nearby lightning on root and limb colliders, multiple colliders per enemy, same-species victims, self-hit exclusion and no player damage from an enemy contact. Black-hole impact delivers the native enemy-explosion amount (100) through normal resistances; aura damage remains 1 every 0.5 seconds per enemy. Confirm ordinary player combat is unchanged.

- Guttertank and Big Johninator mines cannot be parried in UltraMisery, including the Feedbacker reward path; normal mine activation/explosion stays intact.
- Mirror Reaper hands become independent enemy-combat targets tied to their native Breakable durability and lifetime, with no kill-count registration. Their existing hurt zone can damage enemies, excluding the owner through normal attribution.
- Mirror Reaper prioritizes poison barrages on alternating ranged decisions; barrage animation speed doubles and post-barrage cooldown halves against both players and enemies. Other attack animations retain their speed.

Additional in-game acceptance: let multiple enemy types select and hit a Reaper hand, verify its normal durability/fade and unchanged kill count; verify its hurt zone damages other enemies but not its owner, including mode toggles and parries. Check native player hand damage with enemy combat off. Compare poison barrage animation and choice cadence in both combat modes. Try Feedbacker parries on both mine owners, checking no healing/hitstop and normal detonation; mode-off mines stay native.

Same-version nailgun follow-up: compare all nailgun damage with the previous build (one third) and native mode (one twelfth). Attach a cable with either form and cycle every sawblade variant: shot intervals double, including Overheat secondary recovery; regular nail firing rates stay unchanged. Check flying/broken/disabled cables, target death/removal, discharge, multiple cables, scene reload and mode-off behavior. Slowdown requires at least one live connection. Live gameplay verification remains pending.
