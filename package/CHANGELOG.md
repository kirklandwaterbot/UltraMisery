# Changelog

## 0.2.8 � local test candidate

- Same-version tuning: regular shotgun primary shots now share a 1-second cooldown, with instant swapping and independent alt-fire. Overheat heat buildup from firing now takes 20 times the base-game effort; spent-heatsink recharge is unchanged.
- Same-version balance follow-up: all nailgun-family damage is divided by three again (1/12 of base-game damage). Every sawblade variant has twice its firing interval while a player cable is attached to a living enemy; ordinary nail rates stay unchanged. Build, 96 native-hook checks and offline policy/regression tests passed; live gameplay remains unverified.
- Regular shotgun equip/swap delay removed. Primary shots share a fixed 1-second cooldown; secondary actions no longer check or extend that shared gate. Removed animation-derived lockouts and regular animation slowdown.
- Same-version local swap hotfix: enforce primary admission inside the native firing body, before effects; preserve the shared deadline across temporary active-scene changes. Rejected overpump attempts do not force death. Version remains 0.2.8; Unity playtesting is pending.
- Overheat heat gain from firing is 1/20 of the base-game amount for nails and sawblades; heatsink replenishment stays unchanged.
- Power greatsword orbs use the same homing prefab as other modified enemies; the paired geometry and connected beam are retained.
- Flesh Prison and Panopticon reacquire living enemies across species and recover delayed attacks interrupted by target loss in enemy combat.
- Their black holes retarget after kills, apply enemy impact damage without the native player-only damage path, and detect child hitboxes for aura damage once per enemy per tick.

- Guttertank and Big Johninator mines cannot be parried in UltraMisery, including the Feedbacker reward path; normal mine activation/explosion stays intact.
- Mirror Reaper hands become independent enemy-combat targets tied to their native Breakable durability and lifetime, with no kill-count registration. Their existing hurt zone can damage enemies, excluding the owner through normal attribution.
- Mirror Reaper prioritizes poison barrages on alternating ranged decisions; barrage animation speed doubles and post-barrage cooldown halves against both players and enemies. Other attack animations retain their speed.

- Clean build, 92 native hook/field/IL checks, targeted gameplay-policy tests and the existing combat regression suite passed. Live Unity gameplay remains unverified.

## 0.2.7 — local test candidate

- Normal and slab revolver shots cannot detonate cores or rockets, including charged, ricocheted and coin-targeted shots. Hammer-boosted cores and cannonball interactions remain available.
- Each magnet supports 50% more load before overloading. The one-magnet limit and halved countdown lifetime stay unchanged.
- All nailgun-family damage is reduced by 75%, including nails, nailbombs, sawblades, Overheat attacks and Jumpstart electricity/aftershocks. This replaces earlier halves rather than stacking with them; enemy nails and shotgun chainsaws keep separate rules.
- Jumpstart/Conductor recharge after use is 50% longer (7.5 seconds), separate from its existing ten-second attached charge-up.
- Clean build and offline native-hook, beam classification, load-capacity, combined damage and recharge timing checks passed. Live gameplay testing remains pending.

## 0.2.6 — local test candidate

- Regular shotguns now share a recovery deadline across all variants and primary/secondary shots; equip and ReadyGun callbacks cannot shorten it. Equip delay increases from 0.25 to 0.5 seconds.
- Regular pellet spread is halved again, to one quarter of the base game spread.
- Both Pump Charge forms retain their native overpump explosion before forced player death. Pumping itself still does not kill the player.
- Final-wall-jump sound now plays on the fifth wall jump. Jump counts and the extra airborne jump are unchanged.
- Clean build and offline hook, shared-cooldown, spread and explosion-order checks passed. Live swapping, explosion and audio testing remain pending.

## 0.2.5 — local test candidate

- Jackhammer overheat now starts on a confirmed hit against a living enemy, including a killing blow and delayed hit completion. Only the fired variant overheats for 7.5 seconds; misses, scenery, corpses and core strikes do not trigger it.
- Removes the added jackhammer core-eject animation/primary lockout, restoring native core eject into primary hammer timing. The existing doubled core cooldown stays unchanged.
- Malicious Faces no longer leave a visible/collidable head or corpse, including stone fragments from corpse-breaking kills. Native death processing runs before deferred object destruction.
- Clean build and offline native hook, hit/miss/variant, core cooldown and existing balance checks passed. Live combo timing, kill counts and encounter progression still need in-game testing.

## 0.2.4 — local test candidate

- Freezeframe drains at twice the normal game rate while frozen: 2.5 seconds per full meter (0.625 seconds per quarter). Full recharge remains twenty seconds.
- Oil-covered floors halve player planar movement, including walking, sliding and dashing. The reduction does not compound each tick, does not affect jump/fall velocity, and stops applying off oil.
- Oil detection refreshes every physics tick, including when oil is removed under a stationary player.
- Clean build and offline patch, freeze-duration, repeated-contact and vertical-preservation checks passed. Live movement testing remains pending.

## 0.2.3 — local test candidate

- Attractor nailgun and sawblade magnets now have half their normal countdown lifetime after attaching.
- Repeated countdown activation does not halve lifetime again. Other timed objects and native timer-pause cheats are unchanged.

## 0.2.2 — local test candidate

- Enabling cheats bypasses all six quote-trigger restarts and allows secret-level entry, including native unlock markers, elevators, scripted transitions and direct S/Prime scene loads.
- A queued punishment restart is canceled if cheats become enabled before the reload starts.
- Secret entry permitted with cheats remains permitted through that scene load even if the game resets its cheat flag during the transition. Admission does not carry over to later loads.
- Clean build, cheats-on/off policy checks, one-load admission checks and existing offline regressions passed. In-game testing remains pending.

## 0.2.1 — local test candidate

- Six authored out-of-bounds quote triggers now force a full mission restart, including the 1-S fallback quote. Exact messages were verified in the installed level bundles.
- Secret entrances now restart the mission instead of silently blocking the transition. Direct S/Prime loads and already-loaded secret scenes are covered; normal mission exits remain available.
- Restarts discard checkpoint progress and transferred player position. Repeated trigger callbacks are coalesced. Secret-scene fallback uses the previous regular mission or 0-1, preventing reload loops.
- Corrects the startup log to display the actual plugin version.
- Clean Release build, 136 restart policy cases, all six installed quote fixtures, native hook checks and existing regressions passed offline. Unity scene reload/playtesting remains pending.

## 0.2.0 — local test candidate

- Implements the weapon rebalance across normal and alternate revolvers, shotguns, nailguns/sawblades, railcannons and rocket launchers.
- Adds fired-variant jackhammer overheat, lethal overpump/electrical-water penalties, shared single-magnet capacity and persistent shotgun swap recovery.
- Reworks coin enemy priority to highest current HP with distance as a tiebreaker, and replaces weak-point targeting with resistant hitboxes.
- Adds five wall jumps, one extra airborne jump, one rocket ride per landing, and a smaller Knuckleblaster shockwave.
- Preserves charged Piercer wall penetration and existing Firestarter oil mechanics. Combined weapon damage nerfs apply once rather than multiplying repeatedly.
- Clean Release build and offline weapon/regression checks. Unity gameplay has not been verified; this candidate has not been published.

## 0.1.0

- Initial public beta of the UltraMisery difficulty overhaul.
- Includes campaign difficulty selection and optional Angry Level Loader integration.
- Includes enemy buffs, attack and resistance changes, supported encounter additions and enemy-versus-enemy combat rules.
- Includes damage-only Cerberus slams and Hideous Mass cannon explosion handling for AI combat.
- Adds AI target recovery for prolonged damage or navigation failures.
