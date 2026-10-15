# Forced mission restarts — 0.2.2

While UltraMisery is selected, the following six HUD-message triggers force a full mission reload from the beginning. Checkpoint progress is discarded. With cheats enabled, quote penalties and secret-entry restrictions are bypassed; normal secret discovery/unlock behavior is restored. Ordinary difficulty modes are unaffected.

| Scene | Quote, followed by T. HAKITA |
| --- | --- |
| 0-1 | PIPE CLIP LIVES |
| 0-2 | WHAT'S UPDOOR? |
| 1-S | LOOKS LIKE YOU'RE AT WIT'S END |
| 2-4 | OFF THE BEATEN TRACK |
| 3-1 | YUP, THAT'S A CAVITY |
| 4-3 | THE FILTH IS GONE, BUT THE MEMORY REMAINS |

All six strings were read directly from the installed campaign scene bundles. They use `HudMessage.PlayMessage`, with `$` separating the quote from its attribution. The hook checks the scene and complete message rather than coordinates or a broad substring search. It tolerates whitespace, rich-text tags, curly quotes and the game's `$` line separator. Deactivation calls, inactive enabled-only calls and already-consumed one-time messages retain their native behavior. Custom-map messages are excluded.

## Secret entry (cheats disabled)

- Secret elevator entry (`FinalPit`) and actual `SecretMissionPit` entry request a restart before the native pit disables control or saves a completion. Discovery-only `halfUnlock` markers remain blocked without restarting merely for approaching an entrance.
- Scripted string and numbered secret transitions request the same restart.
- `SceneHelper.LoadSceneAsync` redirects S/Prime requests to a fresh load of the active regular mission, including level-select requests. Canonical `Level n-S`, `Level P-n` and `Assets/Scenes/...unity` forms are recognized. The existing Prime restriction is retained.
- A single-scene load callback catches secret scenes loaded by bypassing that API. UltraMisery also checks if it is selected while already inside a secret scene.
- If already in a secret scene or launching one from a menu, use the last regular mission observed this session, then the native previous regular scene. With neither available, load 0-1. Never reload the secret scene itself in an endless loop.
- Normal campaign exits, regular scene loads, menus and checkpoint restarts are not rejected by the secret-destination policy. Custom map names are not classified as native secret destinations; attempts to load an actual native secret destination are still rejected.

## Restart lifecycle

`MissionRestartRules` coalesces duplicate callbacks within a scene. Trigger-driven reloads leave the current callback and start on the next frame. If a native load is already in progress, the guard waits for `PendingScene` to clear; if the player has already left that scene, the stale request is discarded. Leaving UltraMisery or enabling cheats before the reload starts cancels a queued trigger request. A secret entry permitted while cheats were enabled remains permitted through that load even if the native game resets its cheat flag; the permission is consumed by that one scene load.

Reloads use the native full-scene loading path, not `StatsManager.Restart` (checkpoint recovery). Persistent position-transfer objects are removed so the player starts at the authored spawn; map stores are reset after the load, following the full mission restart flow. The new scene gets a fresh request guard.

## Verification and remaining playtest

- Release build passed with zero warnings/errors.
- All six serialized quote messages matched the policy.
- 234 quote/mode/cheats/formatting/destination cases passed, including wrong-scene and unrelated-text negatives, path aliases, ordinary exits and loop-safe fallback destinations.
- Native patch signatures/fields passed along with the previous weapon checks and full offline regression suite.
- Unity was not launched. Actual Harmony installation, asynchronous scene timing and checkpoint state must still be checked in-game. No game installation or publication was performed.

After a full game restart with this build, test each quote trigger after taking a checkpoint: the mission should start over, not respawn at that checkpoint. Test secret entrances, direct secret level selection, a loader bypassing SceneHelper, repeated trigger overlaps, both cheat states, enabling cheats during a queued restart, native cheat reset during secret entry, and an ordinary exit. Confirm other difficulties behave normally and check BepInEx/LogOutput.log for patch exceptions.
