using UnityEngine;

namespace HackAndSlash
{
    /// <summary>Angled top-down follow camera with trauma-based shake and FOV punches. Runs on unscaled time so it stays alive during hit-stop.</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public Transform target;
        public float distance = 13.5f;
        public float pitch = 52f;
        public float yaw;
        public float followSharpness = 7f;
        public float baseFov = 40f;
        public float lead = 0.35f;

        Camera cam;
        Vector3 focus;
        float trauma, fovKick;
        PlayerController player;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (target) player = target.GetComponent<PlayerController>();
            SnapToTarget();
        }

        public void SnapToTarget()
        {
            if (!target) return;
            focus = target.position + Vector3.up;
            trauma = 0f;
            fovKick = 0f;
            Place(0f);
        }

        public void Shake(float amount) => trauma = Mathf.Clamp01(trauma + amount);
        public void Punch(float fovDelta) => fovKick += fovDelta;

        void LateUpdate()
        {
            if (!target) return;
            float dt = Time.unscaledDeltaTime;
            Vector3 v = player ? player.Velocity : Vector3.zero;
            Vector3 desired = target.position + Vector3.up + Vector3.ClampMagnitude(v * lead, 2.5f);
            focus = Vector3.Lerp(focus, desired, 1f - Mathf.Exp(-followSharpness * dt));
            Place(dt);
        }

        void Place(float dt)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = focus - rot * Vector3.forward * distance;
            float s = trauma * trauma;
            float t = Time.unscaledTime * 26f;
            Vector3 offset = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (1.6f * s);
            float roll = (Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * 7f * s;
            transform.SetPositionAndRotation(pos + rot * offset, rot * Quaternion.Euler(0f, 0f, roll));
            trauma = Mathf.Max(0f, trauma - dt * 1.7f);
            fovKick = Mathf.Lerp(fovKick, 0f, 1f - Mathf.Exp(-5f * dt));
            if (cam) cam.fieldOfView = baseFov + fovKick;
        }
    }
}
