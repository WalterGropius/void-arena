using UnityEngine;
using UnityEngine.InputSystem;

namespace HackAndSlash
{
    /// <summary>
    /// Arkham-style third-person combat camera: free orbit (mouse / right stick), crowd-aware framing that pulls back
    /// as the fight spreads out, wall collision, trauma shake, FOV punches, and blended cinematic shots for counters,
    /// takedowns and final blows. Press V for the classic top-down tactical view. Runs on unscaled time so it stays
    /// alive during hit-stop.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public Transform target;
        [Header("Orbit")]
        public float distance = 6.4f;
        public float maxCombatDistance = 9.8f;
        public float pitch = 20f;
        public float yaw;
        public float minPitch = -5f, maxPitch = 68f;
        public float mouseSensitivity = 0.1f;
        public float stickSpeed = 170f;
        public float followSharpness = 9f;
        public float baseFov = 52f;
        public float lead = 0.25f;
        [Header("Tactical view (V)")]
        public bool tactical;
        public float tacticalDistance = 13.5f, tacticalPitch = 52f;

        Camera cam;
        Vector3 focus;
        float trauma, fovKick, curDistance, collideDistance, idleInput, curPitch, curYaw;
        PlayerController player;

        // cinematic shot
        Vector3 shotFocus, shotView;
        float shotDistance, shotFov, shotTime, shotDuration, shotWeight;

        public Vector3 PlanarForward
        {
            get
            {
                Vector3 f = transform.forward; f.y = 0f;
                if (shotWeight > 0.01f || f.sqrMagnitude < 0.01f) f = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                return f.normalized;
            }
        }

        public Vector3 PlanarRight => Vector3.Cross(Vector3.up, PlanarForward);

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (target) player = target.GetComponent<PlayerController>();
            SnapToTarget();
        }

        void Start()
        {
            if (Application.isPlaying) LockCursor(true);
        }

        void OnDisable()
        {
            LockCursor(false);
        }

        static void LockCursor(bool on)
        {
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        public void SnapToTarget()
        {
            if (!target) return;
            focus = target.position + Vector3.up * 1.35f;
            trauma = 0f;
            fovKick = 0f;
            shotWeight = 0f;
            shotTime = shotDuration = 0f;
            curDistance = collideDistance = tactical ? tacticalDistance : distance;
            curPitch = tactical ? tacticalPitch : pitch;
            curYaw = yaw;
            Place(0f);
        }

        public void Shake(float amount) => trauma = Mathf.Clamp01(trauma + amount);
        public void Punch(float fovDelta) => fovKick += fovDelta;

        /// <summary>Blend to a framed shot: viewDir points from the focus towards the camera.</summary>
        public void Cinematic(Vector3 focusPoint, Vector3 viewDir, float dist, float fovDelta, float seconds)
        {
            if (tactical) return;
            shotFocus = focusPoint;
            shotView = viewDir.sqrMagnitude > 1e-4f ? viewDir.normalized : -transform.forward;
            shotDistance = dist;
            shotFov = fovDelta;
            shotDuration = Mathf.Max(0.2f, seconds);
            shotTime = 0f;
        }

        void Update()
        {
            ReadInput(Time.unscaledDeltaTime);
        }

        void ReadInput(float dt)
        {
            var kb = Keyboard.current;
            var ms = Mouse.current;
            var gp = Gamepad.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) LockCursor(false);
                if (kb.vKey.wasPressedThisFrame) { tactical = !tactical; if (tactical) shotDuration = 0f; }
            }
            if (ms != null && Cursor.lockState != CursorLockMode.Locked && Application.isFocused && ms.leftButton.wasPressedThisFrame) LockCursor(true);

            Vector2 look = Vector2.zero;
            if (ms != null && Cursor.lockState == CursorLockMode.Locked) look += ms.delta.ReadValue() * mouseSensitivity;
            if (gp != null)
            {
                Vector2 rs = gp.rightStick.ReadValue();
                if (rs.sqrMagnitude > 0.02f) look += new Vector2(rs.x, -rs.y) * (stickSpeed * dt);
            }
            if (tactical) look = Vector2.zero;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);
            idleInput = look.sqrMagnitude > 0.0001f ? 0f : idleInput + dt;
        }

        void LateUpdate()
        {
            if (!target) return;
            float dt = Time.unscaledDeltaTime;
            var gm = GameManager.I;

            // crowd framing: lean towards the fight and pull back as it spreads
            Vector3 centroid = Vector3.zero;
            float spread = 0f;
            int n = 0;
            if (gm != null)
            {
                foreach (var e in gm.enemies)
                {
                    if (e == null || !e.Targetable) continue;
                    Vector3 d = e.transform.position - target.position; d.y = 0f;
                    float m = d.magnitude;
                    if (m > 13f) continue;
                    centroid += e.transform.position;
                    spread = Mathf.Max(spread, m);
                    n++;
                }
            }
            Vector3 lean = Vector3.zero;
            if (n > 0)
            {
                centroid /= n;
                lean = Vector3.ClampMagnitude(centroid - target.position, 6f) * 0.22f;
                lean.y = 0f;
            }

            // autopilot / attract mode: orbit to keep the brawl in front of the lens
            if (player && player.autopilot && idleInput > 1.5f && n > 0 && !tactical)
            {
                Vector3 toCrowd = centroid - target.position; toCrowd.y = 0f;
                if (toCrowd.sqrMagnitude > 1f)
                {
                    float want = Mathf.Atan2(toCrowd.x, toCrowd.z) * Mathf.Rad2Deg + 25f;
                    yaw = Mathf.MoveTowardsAngle(yaw, want, 32f * dt);
                }
                pitch = Mathf.MoveTowards(pitch, 24f, 8f * dt);
            }

            Vector3 v = player ? player.Velocity : Vector3.zero;
            Vector3 desired = target.position + Vector3.up * 1.35f + lean + Vector3.ClampMagnitude(v * lead, 1.5f);
            focus = Vector3.Lerp(focus, desired, 1f - Mathf.Exp(-followSharpness * dt));

            float wantDist = tactical ? tacticalDistance : Mathf.Lerp(distance, maxCombatDistance, Mathf.Clamp01((spread - 3f) / 8f));
            curDistance = Mathf.Lerp(curDistance, wantDist, 1f - Mathf.Exp(-2.5f * dt));
            curPitch = Mathf.LerpAngle(curPitch, tactical ? tacticalPitch : pitch, 1f - Mathf.Exp(-14f * dt));
            curYaw = Mathf.LerpAngle(curYaw, tactical ? 0f : yaw, 1f - Mathf.Exp(-18f * dt));

            // cinematic weight
            if (shotDuration > 0f)
            {
                shotTime += dt;
                float inW = Mathf.Clamp01(shotTime / 0.16f);
                float outW = 1f - Mathf.Clamp01((shotTime - (shotDuration - 0.35f)) / 0.35f);
                shotWeight = Mathf.SmoothStep(0f, 1f, Mathf.Min(inW, outW));
                if (shotTime >= shotDuration) { shotDuration = 0f; shotWeight = 0f; }
            }
            else shotWeight = 0f;
            Place(dt);
        }

        void Place(float dt)
        {
            var orbit = Quaternion.Euler(curPitch, curYaw, 0f);
            Vector3 f = focus;
            Vector3 back = orbit * Vector3.back;
            float d = curDistance;
            float fov = baseFov;
            if (shotWeight > 0f)
            {
                f = Vector3.Lerp(f, shotFocus, shotWeight);
                back = Vector3.Slerp(back, shotView, shotWeight).normalized;
                d = Mathf.Lerp(d, shotDistance, shotWeight);
                fov += shotFov * shotWeight;
            }

            // keep the lens out of walls and props
            float allowed = d;
            if (Physics.SphereCast(f, 0.28f, back, out RaycastHit hit, d, Layers.CameraBlockers, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(1.2f, hit.distance - 0.1f);
            collideDistance = allowed < collideDistance ? allowed : Mathf.Lerp(collideDistance, allowed, 1f - Mathf.Exp(-3f * Mathf.Max(dt, 0.0001f)));
            if (dt <= 0f) collideDistance = allowed;

            Vector3 pos = f + back * collideDistance;
            if (pos.y < 0.35f) pos.y = 0.35f;
            var rot = Quaternion.LookRotation(f - pos, Vector3.up);

            float s = trauma * trauma;
            float t = Time.unscaledTime * 26f;
            Vector3 offset = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (0.9f * s);
            float roll = (Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * 6f * s;
            transform.SetPositionAndRotation(pos + rot * offset, rot * Quaternion.Euler(0f, 0f, roll));
            trauma = Mathf.Max(0f, trauma - dt * 1.7f);
            fovKick = Mathf.Lerp(fovKick, 0f, 1f - Mathf.Exp(-5f * dt));
            if (cam) cam.fieldOfView = (tactical ? 40f : fov) + fovKick;
        }
    }
}
