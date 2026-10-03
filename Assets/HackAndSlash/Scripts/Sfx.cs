using UnityEngine;

namespace HackAndSlash
{
    /// <summary>
    /// Procedurally synthesised sound effects (no audio files needed) plus music playback.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx I { get; private set; }

        public AudioClip swing, swingHeavy, hit, hitHeavy, crit, clang, death, hurt, dash, nova, slam, spawn, cast, warn, drum, ting, charge;

        const int SR = 44100;
        AudioSource[] pool;
        int next;
        AudioSource musicSrc;
        GameManager gm;

        public void Init(GameManager g)
        {
            I = this;
            gm = g;
            if (pool == null)
            {
                var holder = new GameObject("SfxPool");
                holder.transform.SetParent(transform, false);
                pool = new AudioSource[24];
                for (int i = 0; i < pool.Length; i++)
                {
                    var s = holder.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 0f;
                    pool[i] = s;
                }
                musicSrc = holder.AddComponent<AudioSource>();
                musicSrc.loop = true;
                musicSrc.playOnAwake = false;
                musicSrc.volume = 0.3f;
            }
            if (swing == null) Generate();
            if (gm.music && !musicSrc.isPlaying)
            {
                musicSrc.clip = gm.music;
                musicSrc.Play();
            }
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var c in new[] { swing, swingHeavy, hit, hitHeavy, crit, clang, death, hurt, dash, nova, slam, spawn, cast, warn, drum, ting, charge })
                if (c) Destroy(c);
        }

        void Update()
        {
            if (musicSrc && gm)
            {
                float target = gm.SlowMoScale < 0.6f ? 0.8f : 1f;
                musicSrc.pitch = Mathf.Lerp(musicSrc.pitch, target, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            }
        }

        public static void Play(AudioClip c, float volume = 1f, float pitch = 1f, float pitchVar = 0.06f)
        {
            if (I == null || c == null || I.pool == null) return;
            var s = I.pool[I.next];
            I.next = (I.next + 1) % I.pool.Length;
            s.clip = c;
            s.volume = volume;
            s.pitch = pitch * (1f + Random.Range(-pitchVar, pitchVar));
            s.Play();
        }

        public static void Hit(bool isCrit, bool heavy, bool armored)
        {
            if (I == null) return;
            Play(heavy ? I.hitHeavy : I.hit, 0.85f, heavy ? 0.9f : 1f, 0.1f);
            if (isCrit) Play(I.crit, 0.45f, 1f, 0.05f);
            if (armored) Play(I.clang, 0.3f, 0.8f, 0.1f);
        }

        public static void Footstep()
        {
            if (I == null || I.gm == null || I.gm.footsteps == null || I.gm.footsteps.Length == 0) return;
            Play(I.gm.footsteps[Random.Range(0, I.gm.footsteps.Length)], 0.22f, 1f, 0.08f);
        }

        // ---------------------------------------------------------------- synthesis

        void Generate()
        {
            swing = Whoosh("swing", 0.22f, 500f, 2600f, 900f, 0.55f, 1);
            swingHeavy = Whoosh("swingHeavy", 0.36f, 250f, 1500f, 400f, 0.5f, 2);
            dash = Whoosh("dash", 0.2f, 1400f, 4200f, 1800f, 0.8f, 3);
            hit = Impact("hit", 0.25f, 180f, 50f, 22f, 0.9f, 55f, 11);
            hitHeavy = Impact("hitHeavy", 0.42f, 140f, 38f, 12f, 1.1f, 30f, 12);
            crit = Metal("crit", 0.5f, new[] { 1250f, 1874f, 2630f, 3310f }, 9f, 13);
            clang = Metal("clang", 0.55f, new[] { 820f, 1290f, 2010f }, 7f, 14);
            ting = Metal("ting", 0.7f, new[] { 2600f, 3900f, 5210f }, 5f, 15);
            death = Death("death", 0.65f, 16);
            hurt = Hurt("hurt", 0.32f, 17);
            nova = Boom("nova", 1.9f, 70f, 26f, 1.8f, true, 18);
            slam = Boom("slam", 0.9f, 95f, 34f, 4f, false, 19);
            drum = Boom("drum", 1.0f, 78f, 44f, 5f, false, 20);
            spawn = Swell("spawn", 0.7f, 21);
            cast = Zap("cast", 0.35f, 22);
            warn = Warn("warn", 0.28f);
            charge = Charge("charge", 0.55f);
        }

        static float Noise(ref uint s)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return s / (float)uint.MaxValue * 2f - 1f;
        }

        static AudioClip Make(string name, float[] d)
        {
            float peak = 0.0001f;
            for (int i = 0; i < d.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            float k = 0.9f / peak;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
            // tiny fade to avoid clicks
            int f = Mathf.Min(64, d.Length / 4);
            for (int i = 0; i < f; i++) { d[i] *= i / (float)f; d[d.Length - 1 - i] *= i / (float)f; }
            var c = AudioClip.Create("sfx_" + name, d.Length, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        static float Tanh(float x) => (float)System.Math.Tanh(x);

        static AudioClip Whoosh(string name, float dur, float f0, float f1, float f2, float q, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 7919u + 1u;
            float low = 0f, band = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float u = i / (float)d.Length;
                float fc = u < 0.45f ? Mathf.Lerp(f0, f1, u / 0.45f) : Mathf.Lerp(f1, f2, (u - 0.45f) / 0.55f);
                float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(fc, 8000f) / SR);
                float x = Noise(ref rng);
                float high = x - low - q * band;
                band += f * high;
                low += f * band;
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.55f));
                d[i] = band * env * env;
            }
            return Make(name, d);
        }

        static AudioClip Impact(string name, float dur, float fStart, float fEnd, float decay, float noiseAmt, float noiseDecay, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 104729u + 3u;
            float phase = 0f, lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float freq = fEnd + (fStart - fEnd) * Mathf.Exp(-t * 28f);
                phase += 2f * Mathf.PI * freq / SR;
                float tone = Mathf.Sin(phase) * Mathf.Exp(-t * decay);
                float n = Noise(ref rng);
                lp += (n - lp) * 0.3f;
                float crunch = Mathf.Round(lp * 6f) / 6f;
                float nz = crunch * noiseAmt * Mathf.Exp(-t * noiseDecay);
                d[i] = Tanh((tone * 1.2f + nz) * 1.8f);
            }
            return Make(name, d);
        }

        static AudioClip Metal(string name, float dur, float[] freqs, float decay, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 15485863u + 5u;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float v = 0f;
                for (int k = 0; k < freqs.Length; k++)
                    v += Mathf.Sin(2f * Mathf.PI * freqs[k] * t + k) * Mathf.Exp(-t * decay * (1f + k * 0.35f)) / (k + 1f);
                v += Noise(ref rng) * Mathf.Exp(-t * 90f) * 0.6f;
                d[i] = v;
            }
            return Make(name, d);
        }

        static AudioClip Death(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 32452843u + 7u;
            float phase = 0f, lp = 0f, hold = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float freq = Mathf.Lerp(320f, 40f, Mathf.Sqrt(t / dur));
                phase += 2f * Mathf.PI * freq / SR;
                if (i % 7 == 0) hold = Noise(ref rng);
                lp += (hold - lp) * 0.25f;
                float v = Mathf.Sin(phase) * Mathf.Exp(-t * 5f) * 0.8f + lp * Mathf.Exp(-t * 6f) * 1.2f;
                d[i] = Tanh(v * 2.2f);
            }
            return Make(name, d);
        }

        static AudioClip Hurt(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 49979687u + 9u;
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float freq = Mathf.Lerp(150f, 80f, t / dur);
                phase += freq / SR;
                float saw = (phase % 1f) * 2f - 1f;
                float v = saw * Mathf.Exp(-t * 9f) + Noise(ref rng) * Mathf.Exp(-t * 30f) * 0.5f;
                d[i] = Tanh(v * 3f);
            }
            return Make(name, d);
        }

        static AudioClip Boom(string name, float dur, float fStart, float fEnd, float decay, bool shimmer, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 86028121u + 11u;
            float phase = 0f, lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float freq = fEnd + (fStart - fEnd) * Mathf.Exp(-t * 6f);
                phase += 2f * Mathf.PI * freq / SR;
                float sub = Mathf.Sin(phase) * Mathf.Exp(-t * decay);
                float n = Noise(ref rng);
                lp += (n - lp) * 0.06f;
                float rumble = lp * 3f * Mathf.Exp(-t * decay * 1.3f);
                float crack = n * Mathf.Exp(-t * 45f) * 0.7f;
                float v = sub * 1.3f + rumble + crack;
                if (shimmer)
                    v += (Mathf.Sin(2f * Mathf.PI * 880f * t) + Mathf.Sin(2f * Mathf.PI * 1318f * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * 1760f * t) * 0.5f)
                         * 0.12f * Mathf.Exp(-t * 1.6f) * (0.6f + 0.4f * Mathf.Sin(t * 40f));
                d[i] = Tanh(v * 1.6f);
            }
            return Make(name, d);
        }

        static AudioClip Swell(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 179424673u + 13u;
            float low = 0f, band = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float u = i / (float)d.Length;
                float fc = Mathf.Lerp(200f, 3200f, u * u);
                float f = 2f * Mathf.Sin(Mathf.PI * fc / SR);
                float x = Noise(ref rng);
                float high = x - low - 0.4f * band;
                band += f * high;
                low += f * band;
                phase += 2f * Mathf.PI * Mathf.Lerp(110f, 330f, u * u) / SR;
                float env = u * u * (u > 0.9f ? (1f - u) / 0.1f : 1f);
                d[i] = (band + Mathf.Sin(phase) * 0.4f) * env;
            }
            return Make(name, d);
        }

        static AudioClip Zap(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 2038074743u + 17u;
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float freq = Mathf.Lerp(1100f, 220f, Mathf.Sqrt(t / dur));
                phase += freq / SR;
                float sq = (phase % 1f) < 0.5f ? 1f : -1f;
                d[i] = (sq * 0.5f + Mathf.Sin(phase * 2f * Mathf.PI) * 0.5f + Noise(ref rng) * 0.15f) * Mathf.Exp(-t * 7f);
            }
            return Make(name, d);
        }

        static AudioClip Warn(string name, float dur)
        {
            var d = new float[(int)(dur * SR)];
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float freq = t < dur * 0.45f ? 520f : 780f;
                phase += 2f * Mathf.PI * freq / SR;
                float env = Mathf.Exp(-((t % (dur * 0.45f)) * 14f));
                d[i] = (Mathf.Sin(phase) + Mathf.Sin(phase * 2.01f) * 0.3f) * env;
            }
            return Make(name, d);
        }

        static AudioClip Charge(string name, float dur)
        {
            var d = new float[(int)(dur * SR)];
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float u = t / dur;
                float freq = Mathf.Lerp(200f, 900f, u * u);
                phase += 2f * Mathf.PI * freq / SR;
                float trem = 0.6f + 0.4f * Mathf.Sin(t * 60f);
                d[i] = (Mathf.Sin(phase) + Mathf.Sin(phase * 1.5f) * 0.4f) * trem * Mathf.Sin(Mathf.PI * u);
            }
            return Make(name, d);
        }
    }
}
