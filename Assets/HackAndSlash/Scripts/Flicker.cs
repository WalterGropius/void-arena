using UnityEngine;

namespace HackAndSlash
{
    /// <summary>Torch-style light flicker.</summary>
    [RequireComponent(typeof(Light))]
    public class Flicker : MonoBehaviour
    {
        public float baseIntensity = 2.2f;
        public float amount = 0.35f;
        public float speed = 9f;
        Light l;
        float seed;

        void Awake()
        {
            l = GetComponent<Light>();
            seed = Random.value * 100f;
        }

        void Update()
        {
            float n = Mathf.PerlinNoise(seed, Time.time * speed) - 0.5f;
            l.intensity = baseIntensity * (1f + n * 2f * amount);
        }
    }
}
