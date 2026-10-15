using UnityEngine;

namespace HackAndSlash
{
    /// <summary>The attract-mode fighter (title screen, [P]): reads the fight like a very good human with a reaction delay.</summary>
    public partial class PlayerController
    {
        float apCounterAt = -1f, apThink, apGadgetThink;

        void Autopilot()
        {
            Vector3 pos = transform.position;
            Vector3 mv = Vector3.zero;
            Vector3 threatAway = Vector3.zero;
            bool threatened = false, counterable = false;
            int close = 0, mid = 0;
            Enemy shieldInFront = null, armoredNear = null, downedNear = null, shooter = null, drone = null;
            foreach (var e in gm.enemies)
            {
                if (e == null || !e.Targetable) continue;
                Vector3 to = Flat(e.transform.position - pos);
                float d = to.magnitude;
                if (d < 3.6f) close++;
                if (d < 8.5f) mid++;
                if (e.CanBeCountered && (d < counterRange || e.CurrentState == Enemy.State.Aim)) counterable = true;
                if (e.UnblockableIncoming && e.IsThreatening(pos)) { threatened = true; threatAway -= to.normalized; }
                if (e.ShieldUp && d < 4f && !e.IsDowned) shieldInFront = e;
                if (e.IsArmored && d < 4.5f) armoredNear = e;
                if (e.IsDowned && d < 4f) downedNear = e;
                if (e.IsRangedThreat && e.type != EnemyType.Drone && d > 5f) shooter = e;
                if (e.type == EnemyType.Drone) drone = e;
            }
            foreach (var p in gm.projectiles)
                if (p && !p.reflected && (p.transform.position - Chest).magnitude < (p.IsBullet ? 5f : 3.6f)) counterable = true;

            // holding something: rip it, or throw it into the crowd
            if (state == State.Hold && held != null)
            {
                apGadgetThink -= Time.unscaledDeltaTime;
                if (apGadgetThink <= 0f)
                {
                    apGadgetThink = Random.Range(0.25f, 0.5f);
                    if (held.type == EnemyType.Drone ? mid >= 3 && Random.value < 0.7f : mid >= 3 && Random.value < 0.5f) tetherBuf = 0.2f;
                    else strikeBuf = 0.2f;
                }
                moveInput = Vector3.zero;
                return;
            }

            // react to counter windows like a (very good) human: a short, random reaction delay
            if (counterable)
            {
                if (apCounterAt < 0f) apCounterAt = Time.unscaledTime + Random.Range(0.08f, 0.24f);
                if (Time.unscaledTime >= apCounterAt) { counterBuf = 0.2f; apCounterAt = -1f; }
            }
            else apCounterAt = -1f;

            apThink -= Time.unscaledDeltaTime;
            apGadgetThink -= Time.unscaledDeltaTime;
            var target2 = PickTarget(Vector3.zero, 30f);
            if (threatened && dashCd <= 0f)
            {
                Vector3 away = threatAway.sqrMagnitude > 0.01f ? threatAway.normalized : -transform.forward;
                Vector3 side = Vector3.Cross(Vector3.up, away);
                mv = (away + side * 0.6f).normalized;
                evadeBuf = 0.2f;
            }
            else if (!counterable)
            {
                if (CascadeTargets >= 3 && mid >= 3) cascadeBuf = 0.2f;
                else if (gm.SpecialReady && target2 != null) takedownBuf = 0.2f;
                else if (downedNear != null && close <= 1) { takedownBuf = 0.2f; mv = Flat(downedNear.BodyCenter - pos).normalized * 0.3f; }
                else if (drone != null && tetherCd <= 0f && apGadgetThink <= 0f) { apGadgetThink = 1.2f; mv = Flat(drone.transform.position - pos).normalized * 0.2f; tetherBuf = 0.2f; }
                else if (shooter != null && Ammo > 0 && apGadgetThink <= 0f && Random.value < 0.6f) { apGadgetThink = 0.9f; QuickfireRequested(); }
                else if ((armoredNear != null || shieldInFront != null) && stunCd <= 0f) { mv = Flat((armoredNear ? armoredNear : shieldInFront).transform.position - pos).normalized * 0.3f; stunBuf = 0.2f; }
                else if (Health < maxHealth * 0.35f && gm.orbs.Count > 0 && gm.orbs[0] != null) mv = Flat(gm.orbs[0].transform.position - pos).normalized;
                else if (target2 != null)
                {
                    Vector3 to = Flat(target2.transform.position - pos);
                    if (to.magnitude > freeflowRange) mv = to.normalized;
                    else if (apThink <= 0f)
                    {
                        mv = to.normalized * 0.6f;
                        strikeBuf = 0.2f;
                        apThink = Random.Range(0.02f, 0.12f);
                    }
                }
                else
                {
                    Vector3 home = Flat(-pos);
                    if (home.magnitude > 2f) mv = home.normalized * 0.6f;
                }
            }
            moveInput = mv;
        }
    }
}
