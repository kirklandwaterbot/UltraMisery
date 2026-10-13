using UnityEngine;

namespace UltraMisery
{
    internal sealed class UltraMiseryDuelWatch : MonoBehaviour
    {
        internal AiStalemateRules.Duel Duel;
        private void LateUpdate()
        {
            if (Duel == null) return;
            if (!Duel.Valid) { Duel = null; return; }
            Duel.Sample();
        }
    }
    internal static class AiStalemateRules
    {
        internal static bool AllowsHitLifesteal(EnemyType attacker, EnemyType victim) => attacker != victim;
        internal static float HealingScale(float elapsed) => System.Math.Max(0f, System.Math.Min(1f, 1f - System.Math.Max(0f, elapsed - 15f) / 30f));
        internal sealed class Duel
        {
            internal EnemyIdentifier A, B;
            internal Enemy BodyA, BodyB;
            internal float Since, LowWater, Threshold, NextSample;
            internal bool Damping;
            internal bool Valid => LifestealRules.AiCombatAllowed && A && B && BodyA && BodyB && !A.dead && !B.dead &&
                A.target != null && B.target != null && A.target.enemyIdentifier == B && B.target.enemyIdentifier == A;
            internal void Sample()
            {
                if (Time.time < NextSample) return;
                NextSample = Time.time + 0.25f;
                float health = BodyA.health + BodyB.health;
                // Once damping begins, its own damage progress must not reset it.
                if (!Damping && health <= LowWater - Threshold)
                {
                    Since = Time.time;
                    LowWater = health;
                }
                if (!Damping && Time.time - Since >= 15f)
                {
                    Damping = true;
                    Plugin.Log.LogInfo($"AI stalemate: reducing lifesteal/parry healing equally for {A.enemyType} #{A.GetInstanceID()} and {B.enemyType} #{B.GetInstanceID()} over 30 seconds.");
                }
            }
        }
        internal static float Scale(EnemyIdentifier enemy)
        {
            if (!LifestealRules.AiCombatAllowed || !enemy || enemy.target == null || !enemy.target.isEnemy) return 1f;
            EnemyIdentifier other = enemy.target.enemyIdentifier;
            if (!other || other == enemy || other.dead || other.target == null || other.target.enemyIdentifier != enemy) return 1f;
            var watch = enemy.GetComponent<UltraMiseryDuelWatch>() ?? enemy.gameObject.AddComponent<UltraMiseryDuelWatch>();
            if (watch.Duel != null && watch.Duel.Valid)
                return watch.Duel.Damping ? HealingScale(Time.time - watch.Duel.Since) : 1f;
            var partner = other.GetComponent<UltraMiseryDuelWatch>() ?? other.gameObject.AddComponent<UltraMiseryDuelWatch>();
            Enemy a = enemy.GetComponent<Enemy>(), b = other.GetComponent<Enemy>();
            if (!a || !b) return 1f;
            var duel = new Duel { A = enemy, B = other, BodyA = a, BodyB = b, Since = Time.time,
                LowWater = a.health + b.health,
                Threshold = Mathf.Max(0.01f, (LifestealRules.State(enemy).MaximumHealth + LifestealRules.State(other).MaximumHealth) * 0.01f) };
            watch.Duel = partner.Duel = duel;
            return 1f;
        }
    }
}
