using UnityEngine;

namespace HackAndSlash
{
    /// <summary>Slow rotation and optional bobbing for arena decorations.</summary>
    public class Spinner : MonoBehaviour
    {
        public float degreesPerSecond = 6f;
        public float bobAmplitude;
        public float bobSpeed = 0.6f;
        Vector3 basePos;
        float phase;

        void Awake()
        {
            basePos = transform.localPosition;
            phase = Random.value * 10f;
        }

        void Update()
        {
            transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
            if (bobAmplitude > 0f)
                transform.localPosition = basePos + Vector3.up * (Mathf.Sin((Time.time + phase) * bobSpeed) * bobAmplitude);
        }
    }
}
