using UnityEngine;

namespace HackAndSlash
{
    /// <summary>Sweeps a searchlight (or anything) back and forth around its local Y and X axes.</summary>
    public class Sweeper : MonoBehaviour
    {
        public float yawAmplitude = 35f, yawSpeed = 0.25f, pitchAmplitude = 8f, pitchSpeed = 0.17f;
        Quaternion baseRot;
        float phase;

        void Awake()
        {
            baseRot = transform.localRotation;
            phase = Random.value * 10f;
        }

        void Update()
        {
            float t = Time.time + phase;
            transform.localRotation = baseRot * Quaternion.Euler(Mathf.Sin(t * pitchSpeed * Mathf.PI * 2f) * pitchAmplitude, Mathf.Sin(t * yawSpeed * Mathf.PI * 2f) * yawAmplitude, 0f);
        }
    }
}
