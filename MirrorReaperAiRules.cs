using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal sealed class ReaperHandIdentity : MonoBehaviour
    {
        internal ReaperHandCombat Hand;
    }

    // A disabled identifier is an aiming/damage adapter only. It never runs
    // EnemyIdentifier.Start, registers a kill, gains buffs or spawns enemies.
    internal sealed class ReaperHandCombat : MonoBehaviour
    {
        internal static readonly HashSet<ReaperHandCombat> Hands = new HashSet<ReaperHandCombat>();
        internal EnemyIdentifier Owner, Target;
        internal Breakable Breakable;
        internal GroundWave Wave;
        private HurtZone zone;
        private Collider hitbox;
        private EnemyIdentifierIdentifier link;
        private GameObject proxy;
        private int layer;
        private string originalTag;
        private bool applied;
        private AffectedSubjects affected;
        internal bool Live => this && Wave && Wave.lifetime > 0f && Breakable && !Breakable.broken && gameObject.activeInHierarchy;

        internal void Initialize()
        {
            Wave = GetComponent<GroundWave>();
            Owner = LifestealRules.Resolve(Wave);
            if (!Owner || Owner.enemyType != EnemyType.MirrorReaper) { enabled = false; return; }
            Breakable = GetComponentInChildren<Breakable>();
            zone = GetComponentInChildren<HurtZone>();
            hitbox = Breakable ? Breakable.GetComponent<Collider>() : null;
            if (!hitbox || !zone) { enabled = false; return; }
            layer = hitbox.gameObject.layer; originalTag = hitbox.tag; affected = zone.affected;
            Hands.Add(this);
            Update();
        }
        private void CreateTarget()
        {
            proxy = new GameObject("UltraMisery Reaper hand target");
            proxy.transform.SetParent(transform, false);
            proxy.AddComponent<ReaperHandIdentity>().Hand = this;
            Target = proxy.AddComponent<EnemyIdentifier>();
            Target.enabled = false;
            Target.enemyType = EnemyType.MirrorReaper;
            Target.dontCountAsKills = true;
            Target.ignorePlayer = true;
            Target.overrideCenter = proxy.transform;
            Target.immuneToFriendlyFire = false;
            link = hitbox.gameObject.AddComponent<EnemyIdentifierIdentifier>();
            link.enabled = false;
            SupportEnemyCombatRules.EnsureVisionTarget(Target);
        }
        private void Update()
        {
            if (!hitbox || !zone) { Restore(); return; }
            bool active = LifestealRules.AiCombatAllowed && Live;
            if (active)
            {
                if (!Target) CreateTarget();
                Target.dead = false; Target.health = Breakable.durability;
                proxy.transform.position = hitbox.bounds.center;
                proxy.SetActive(true);
                link.eid = Target;
                hitbox.gameObject.layer = 10; hitbox.tag = "Body";
                if (zone.affected != AffectedSubjects.EnemiesOnly) zone.affected = AffectedSubjects.All;
                applied = true;
            }
            else Restore();
        }
        private void Restore()
        {
            if (Target) Target.dead = true;
            if (proxy) proxy.SetActive(false);
            if (!applied) return;
            if (link) link.eid = null;
            if (hitbox) { hitbox.gameObject.layer = layer; hitbox.tag = originalTag; }
            if (zone) zone.affected = affected;
            applied = false;
        }
        internal void Damage(float amount)
        {
            if (!Live || amount <= 0f) return;
            Breakable.Break(amount);
            if (Target) { Target.health = Breakable.durability; Target.dead = !Live; }
        }
        private void OnDisable() { Restore(); Hands.Remove(this); }
        private void OnEnable() { if (Wave) Hands.Add(this); }
        private void OnDestroy() { Restore(); Hands.Remove(this); }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "Awake")]
    internal static class ReaperHandIdentityBalancePatch
    {
        private static bool Prefix(EnemyIdentifier __instance) => !__instance.GetComponent<ReaperHandIdentity>();
    }
    [HarmonyPatch(typeof(EnemyIdentifier), "DeliverDamage")]
    internal static class ReaperHandDamageBalancePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(EnemyIdentifier __instance, float multiplier)
        {
            var identity = __instance.GetComponent<ReaperHandIdentity>();
            if (!identity) return true;
            if (LifestealRules.AiCombatAllowed && identity.Hand && LifestealRules.Attacker != identity.Hand.Owner)
                identity.Hand.Damage(multiplier);
            return false;
        }
    }
    [HarmonyPatch(typeof(GroundWave), "Start")]
    internal static class ReaperHandSpawnBalancePatch
    {
        private static void Postfix(GroundWave __instance)
        {
            if (!UltraMiseryState.IsActive || __instance.GetComponent<ReaperHandCombat>()) return;
            var owner = LifestealRules.Resolve(__instance);
            if (owner && owner.enemyType == EnemyType.MirrorReaper) __instance.gameObject.AddComponent<ReaperHandCombat>().Initialize();
        }
    }
    [HarmonyPatch(typeof(EnemyIdentifier), "UpdateTarget")]
    internal static class ReaperHandTargetBalancePatch
    {
        private sealed class Timer { public Timer() { } internal float Next; }
        private static readonly ConditionalWeakTable<EnemyIdentifier, Timer> Timers = new ConditionalWeakTable<EnemyIdentifier, Timer>();
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EnemyIdentifier __instance)
        {
            if (!LifestealRules.AiCombatAllowed || ULTRAKILL.Cheats.BlindEnemies.Blind || __instance.dead ||
                AiCombatRules.SupportRole(__instance) || __instance.GetComponent<ReaperHandIdentity>()) return;
            var timer = Timers.GetOrCreateValue(__instance);
            if (Time.time < timer.Next) return;
            timer.Next = Time.time + .25f;
            var current = __instance.target;
            float nearest = current != null && current.isValid ? (current.position - __instance.transform.position).sqrMagnitude : float.PositiveInfinity;
            ReaperHandCombat best = null;
            foreach (var hand in ReaperHandCombat.Hands)
            {
                if (!hand || !hand.Live || !hand.Target || hand.Target.dead || !hand.Target.gameObject.activeInHierarchy || hand.Owner == __instance) continue;
                float distance = (hand.Target.transform.position - __instance.transform.position).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance; best = hand;
            }
            if (best) __instance.target = new EnemyTarget(best.Target);
        }
    }

    internal static class ReaperBarrageRules
    {
        internal static float Speed(float native, bool active) => active ? native * 2f : native;
        internal static float Cooldown(float native, bool active) => active ? native * .5f : native;
        private sealed class Alternation { public Alternation() { } internal bool NextBarrage = true; }
        private static readonly ConditionalWeakTable<MirrorReaper, Alternation> Turns = new ConditionalWeakTable<MirrorReaper, Alternation>();
        internal static bool TakeBarrage(MirrorReaper reaper)
        {
            var turn = Turns.GetOrCreateValue(reaper);
            bool barrage = turn.NextBarrage; turn.NextBarrage = !barrage;
            return barrage;
        }
    }
    [HarmonyPatch(typeof(MirrorReaper), "PickRangedAttack")]
    internal static class ReaperBarrageChoiceBalancePatch
    {
        private static readonly MethodInfo Barrage = WeaponPatchReflection.Method(typeof(MirrorReaper), "ProjectileBarrage");
        private static bool Prefix(MirrorReaper __instance, EnemyIdentifier ___eid, ref float ___attacksSinceModeChange)
        {
            if (!UltraMiseryState.IsActive || !___eid || ___eid.dead || ___eid.target == null || !___eid.target.isValid || !ReaperBarrageRules.TakeBarrage(__instance)) return true;
            Barrage.Invoke(__instance, null);
            ___attacksSinceModeChange += 1f;
            return false;
        }
    }
    [HarmonyPatch(typeof(MirrorReaper), "ProjectileBarrage")]
    internal static class ReaperBarrageSpeedBalancePatch
    {
        private static void Postfix(Animator ___anim, ref float ___cooldown)
        {
            if (!UltraMiseryState.IsActive) return;
            if (___anim) ___anim.SetFloat("ProjectileBarrageSpeed", ReaperBarrageRules.Speed(___anim.GetFloat("ProjectileBarrageSpeed"), true));
            ___cooldown = ReaperBarrageRules.Cooldown(___cooldown, true);
        }
    }
}
