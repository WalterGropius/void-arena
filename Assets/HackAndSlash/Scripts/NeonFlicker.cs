using UnityEngine;

namespace HackAndSlash
{
    /// <summary>Neon sign buzz: mostly steady, with the occasional stutter.</summary>
    public class NeonFlicker : MonoBehaviour
    {
        public Color emission = Color.white;
        public Light linkedLight;
        Renderer r;
        MaterialPropertyBlock mpb;
        float baseLight, glitch;

        void Awake()
        {
            r = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();
            if (linkedLight) baseLight = linkedLight.intensity;
        }

        void Update()
        {
            glitch -= Time.deltaTime;
            if (glitch <= 0f && Random.value < 0.004f) glitch = Random.Range(0.08f, 0.35f);
            float k = glitch > 0f ? (Random.value < 0.5f ? 0.15f : 1f) : 1f;
            if (r)
            {
                mpb.SetColor("_EmissionColor", emission * k);
                r.SetPropertyBlock(mpb);
            }
            if (linkedLight) linkedLight.intensity = baseLight * k;
        }
    }
}
