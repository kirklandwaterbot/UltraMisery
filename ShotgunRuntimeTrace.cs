using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    // Bounded diagnostic trace for a live cooldown bypass that offline tests did
    // not reproduce. Observes actual pellet spawns independently of Shoot's gate.
    internal static class ShotgunRuntimeTrace
    {
        private static int attempts, decisions, pelletGroups, blockedInputs;
        private static int lastPelletFrame = -1, lastPelletGun;
        private static float lastAccepted = -1f, lastAcceptedReal = -1f, nextBlockedLog;
        internal static void Attempt(Shotgun gun)
        {
            if (attempts++ < 40) Write("enter", gun, "");
        }
        internal static void Decision(Shotgun gun, bool accepted)
        {
            if (decisions++ < 40)
                Write(accepted ? "accept" : "reject", gun,
                    $" gameGap={(lastAccepted < 0f ? -1f : Time.time - lastAccepted):F4} realGap={(lastAcceptedReal < 0f ? -1f : Time.realtimeSinceStartup - lastAcceptedReal):F4}");
            if (accepted) { lastAccepted = Time.time; lastAcceptedReal = Time.realtimeSinceStartup; }
        }
        internal static void Input(bool native, bool result)
        {
            if (!native || result || blockedInputs >= 12 || Time.realtimeSinceStartup < nextBlockedLog) return;
            nextBlockedLog = Time.realtimeSinceStartup + .25f;
            blockedInputs++;
            Write("input-blocked", null, "");
        }
        internal static void Pellet(Projectile projectile)
        {
            if (pelletGroups >= 40 || !projectile.sourceWeapon) return;
            var gun = projectile.sourceWeapon.GetComponent<Shotgun>();
            if (!gun) return;
            int id = gun.GetInstanceID();
            if (lastPelletFrame == Time.frameCount && lastPelletGun == id) return;
            lastPelletFrame = Time.frameCount; lastPelletGun = id; pelletGroups++;
            Write("pellets-observed", gun, "");
        }
        internal static void Reset(string reason)
        {
            attempts = decisions = pelletGroups = blockedInputs = 0;
            lastPelletFrame = -1; lastAccepted = lastAcceptedReal = -1f; nextBlockedLog = 0f;
            Plugin.Log?.LogInfo("Shotgun trace reset: " + reason);
            foreach (string method in new[] { "Update", "Shoot" })
            {
                var info = Harmony.GetPatchInfo(WeaponPatchReflection.Method(typeof(Shotgun), method));
                Plugin.Log?.LogInfo("Shotgun trace patches " + method + ": " + (info == null ? "NONE" :
                    string.Join(", ", info.Prefixes.Concat(info.Transpilers).Concat(info.Postfixes).Select(p => p.owner + "/" + p.PatchMethod.DeclaringType.Name + "." + p.PatchMethod.Name))));
            }
        }
        private static void Write(string kind, Shotgun gun, string detail) => Plugin.Log?.LogInfo(
            $"Shotgun trace {kind}: active={WeaponBalanceRules.Active} gun={(gun ? gun.GetInstanceID() : 0)} variant={(gun ? gun.variation : -1)} frame={Time.frameCount} game={Time.time:F4} real={Time.realtimeSinceStartup:F4} scale={Time.timeScale:F3} ready={ShotgunBalance.SharedReady}{detail}");
    }
    [HarmonyPatch(typeof(Shotgun), "Shoot")]
    internal static class ShotgunTraceEntryBalancePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Shotgun __instance) => ShotgunRuntimeTrace.Attempt(__instance);
    }
    [HarmonyPatch(typeof(Projectile), "Start")]
    internal static class ShotgunTracePelletBalancePatch
    {
        private static void Prefix(Projectile __instance) => ShotgunRuntimeTrace.Pellet(__instance);
    }
}
