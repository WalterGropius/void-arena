using UnityEngine;

namespace HackAndSlash
{
    /// <summary>
    /// Procedurally synthesised sound effects (no audio files needed): combat, gadgets, UI, the radio, the midnight
    /// bell, and the "voices" (HALCYON's clean sine blips, Wren's band-limited radio blips). The score is <see cref="Music"/>.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx I { get; private set; }

        public AudioClip swing, swingHeavy, hit, hitHeavy, crit, clang, death, hurt, dash, nova, slam, spawn, cast, warn, drum, ting, charge;
        public AudioClip parry, stun, thunder, thunderFar, rain, takedown, thud, whoom, servo;
        public AudioClip rifle, blaster, tether, tetherHit, rip, glitch, typeKey, radioStatic, bell, uiMove, uiSelect, focusIn, focusOut;
        public AudioClip[] clatter, halBlips, wrenBlips;

        const int SR = 44100;
        AudioSource[] pool;
        int next;
        AudioSource rainSrc, uiSrc;
        AudioLowPassFilter rainFilter;
        GameManager gm;
        float lastClatter, rainTarget = 1f, rainCutoff = 22000f;

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
                // menu blips must sound while the game (and AudioListener) is paused
                uiSrc = holder.AddComponent<AudioSource>();
                uiSrc.playOnAwake = false;
                uiSrc.spatialBlend = 0f;
                uiSrc.ignoreListenerPause = true;
                // the rain gets its own object so a low-pass can muffle it indoors without touching anything else
                var rainGo = new GameObject("Rain");
                rainGo.transform.SetParent(transform, false);
                rainSrc = rainGo.AddComponent<AudioSource>();
                rainSrc.loop = true;
                rainSrc.playOnAwake = false;
                rainSrc.volume = 0.32f;
                rainFilter = rainGo.AddComponent<AudioLowPassFilter>();
                rainFilter.cutoffFrequency = 22000f;
            }
            if (swing == null) Generate();
            if (rain && !rainSrc.isPlaying)
            {
                rainSrc.clip = rain;
                rainSrc.Play();
            }
        }

        /// <summary>Rain bed level (0..1); muffled = heard through walls (the hotel).</summary>
        public void SetRain(float level, bool muffled)
        {
            rainTarget = Mathf.Clamp01(level);
            rainCutoff = muffled ? 700f : 22000f;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var c in new[] { swing, swingHeavy, hit, hitHeavy, crit, clang, death, hurt, dash, nova, slam, spawn, cast, warn, drum, ting, charge, parry, stun, thunder, thunderFar, rain, takedown, thud, whoom, servo,
                                      rifle, blaster, tether, tetherHit, rip, glitch, typeKey, radioStatic, bell, uiMove, uiSelect, focusIn, focusOut })
                if (c) Destroy(c);
            foreach (var arr in new[] { clatter, halBlips, wrenBlips })
                if (arr != null) foreach (var c in arr) if (c) Destroy(c);
        }

        void Update()
        {
            float k = 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
            if (rainSrc)
            {
                rainSrc.volume = Mathf.Lerp(rainSrc.volume, 0.32f * rainTarget * Settings.sfxVolume, k);
                if (gm) rainSrc.pitch = Mathf.Lerp(rainSrc.pitch, gm.SlowMoScale < 0.6f ? 0.55f : 1f, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            }
            if (rainFilter) rainFilter.cutoffFrequency = Mathf.Lerp(rainFilter.cutoffFrequency, rainCutoff, k);
        }

        public static void Play(AudioClip c, float volume = 1f, float pitch = 1f, float pitchVar = 0.06f)
        {
            if (I == null || c == null || I.pool == null) return;
            if (I.uiSrc && (c == I.uiMove || c == I.uiSelect))
            {
                I.uiSrc.pitch = pitch;
                I.uiSrc.PlayOneShot(c, volume * Settings.sfxVolume);
                return;
            }
            var s = I.pool[I.next];
            I.next = (I.next + 1) % I.pool.Length;
            s.clip = c;
            s.volume = volume * Settings.sfxVolume;
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

        public static void Clatter(float strength)
        {
            if (I == null || I.clatter == null || I.clatter.Length == 0) return;
            if (Time.unscaledTime - I.lastClatter < 0.03f) return;
            I.lastClatter = Time.unscaledTime;
            Play(I.clatter[Random.Range(0, I.clatter.Length)], Mathf.Lerp(0.08f, 0.32f, strength), Random.Range(0.85f, 1.25f), 0.05f);
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
            parry = Metal("parry", 1.1f, new[] { 1830f, 2475f, 3390f, 4410f, 5930f }, 3.2f, 23);
            stun = Crackle("stun", 0.5f, 24);
            thunder = Thunder("thunder", 4.5f, 25, true);
            thunderFar = Thunder("thunderFar", 5.5f, 26, false);
            rain = RainLoop("rain", 4f, 27);
            takedown = Impact("takedown", 0.7f, 120f, 30f, 7f, 1.6f, 14f, 28);
            thud = Impact("thud", 0.35f, 90f, 40f, 16f, 0.7f, 40f, 29);
            whoom = Swell("whoom", 0.9f, 30);
            clatter = new[] { Metal("clatter0", 0.22f, new[] { 2210f, 3120f, 4630f }, 26f, 31), Metal("clatter1", 0.25f, new[] { 1680f, 2850f, 3970f }, 22f, 32), Metal("clatter2", 0.18f, new[] { 2950f, 4120f, 6020f }, 30f, 33) };
            servo = Servo("servo", 0.45f, 34);
            rifle = Gunshot("rifle", 0.4f, 95f, 1f, 35);
            blaster = Blaster("blaster", 0.55f, 36);
            tether = Tether("tether", 0.28f, 37);
            tetherHit = Metal("tetherHit", 0.3f, new[] { 640f, 1530f, 2390f }, 14f, 38);
            rip = Rip("rip", 0.55f, 39);
            glitch = Glitch("glitch", 0.55f, 40);
            typeKey = TypeKey("typeKey", 0.05f, 41);
            radioStatic = RadioStatic("radioStatic", 1.1f, 42);
            bell = Bell("bell", 5.5f, 43);
            uiMove = Tone("uiMove", 0.045f, 2350f, 0f, 90f);
            uiSelect = Tone("uiSelect", 0.2f, 880f, 1320f, 18f);
            focusIn = Sweep("focusIn", 0.6f, 900f, 90f, 44);
            focusOut = Sweep("focusOut", 0.45f, 120f, 1100f, 45);
            float[] hal = { 430f, 465f, 505f, 540f, 585f, 620f };
            halBlips = new AudioClip[hal.Length];
            for (int i = 0; i < hal.Length; i++) halBlips[i] = HalBlip("hal" + i, 0.075f, hal[i]);
            float[] wren = { 250f, 280f, 310f, 335f, 370f, 400f };
            wrenBlips = new AudioClip[wren.Length];
            for (int i = 0; i < wren.Length; i++) wrenBlips[i] = WrenBlip("wren" + i, 0.065f, wren[i], (uint)(50 + i));
        }

        // ---- combat & gadgets

        static AudioClip Servo(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 2654435761u + 7u;
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR, u = t / dur;
                float f = Mathf.Lerp(380f, 620f, Mathf.Sin(u * Mathf.PI));
                phase += f / SR;
                float saw = (phase % 1f) * 2f - 1f;
                d[i] = (saw * 0.5f + Noise(ref rng) * 0.08f) * Mathf.Sin(Mathf.PI * u) * (0.75f + 0.25f * Mathf.Sin(t * 90f));
            }
            return Make(name, d);
        }

        static AudioClip Gunshot(string name, float dur, float body, float crack, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 1664525u + 1013904223u;
            float lp = 0f, lp2 = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float n = Noise(ref rng);
                lp += (n - lp) * 0.35f;
                lp2 += (n - lp2) * 0.04f;
                float hi = (n - lp) * crack * Mathf.Exp(-t * 70f);
                phase += 2f * Mathf.PI * (body + 60f * Mathf.Exp(-t * 40f)) / SR;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 26f);
                float room = lp2 * 2.2f * Mathf.Exp(-t * 7f);
                d[i] = Tanh((hi * 1.6f + thump * 1.3f + room) * 2f);
            }
            return Make(name, d);
        }

        static AudioClip Blaster(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 22695477u + 1u;
            float phase = 0f, sub = 0f, lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float f = 110f + 1500f * Mathf.Exp(-t * 22f);
                phase += f / SR;
                float sq = (phase % 1f) < 0.5f ? 1f : -1f;
                sub += 2f * Mathf.PI * (55f + 40f * Mathf.Exp(-t * 15f)) / SR;
                float n = Noise(ref rng);
                lp += (n - lp) * 0.2f;
                float v = sq * 0.35f * Mathf.Exp(-t * 9f) + Mathf.Sin(phase * 2f * Mathf.PI) * 0.5f * Mathf.Exp(-t * 7f)
                        + Mathf.Sin(sub) * 1.1f * Mathf.Exp(-t * 11f) + lp * 1.2f * Mathf.Exp(-t * 30f);
                d[i] = Tanh(v * 1.8f);
            }
            return Make(name, d);
        }

        static AudioClip Tether(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 69069u + 1u;
            float low = 0f, band = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR, u = t / dur;
                float fc = Mathf.Lerp(700f, 5200f, u);
                float f = 2f * Mathf.Sin(Mathf.PI * fc / SR);
                float high = Noise(ref rng) - low - 0.35f * band;
                band += f * high;
                low += f * band;
                phase += 2f * Mathf.PI * Mathf.Lerp(260f, 1300f, u * u) / SR;
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.4f));
                d[i] = (band * 0.7f + Mathf.Sin(phase) * 0.45f) * env;
            }
            return Make(name, d);
        }

        static AudioClip Rip(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 1103515245u + 12345u;
            float lp = 0f, gate = 0f;
            int period = 900;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                if (i % period == 0) { gate = 0.4f + 0.6f * Mathf.Abs(Noise(ref rng)); period = Mathf.Max(220, period - 40); }
                float n = Noise(ref rng);
                lp += (n - lp) * 0.5f;
                float tear = lp * gate * Mathf.Exp(-((i % period) / (float)period) * 5f);
                float ring = (Mathf.Sin(2f * Mathf.PI * 1250f * t) + Mathf.Sin(2f * Mathf.PI * 2210f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 3390f * t) * 0.3f) * 0.25f * Mathf.Exp(-t * 6f);
                float pop = t > dur * 0.72f ? Noise(ref rng) * Mathf.Exp(-(t - dur * 0.72f) * 40f) * 1.4f : 0f;
                d[i] = Tanh((tear * 1.6f + ring + pop) * 1.5f) * Mathf.Min(1f, (dur - t) * 20f);
            }
            return Make(name, d);
        }

        static AudioClip Sweep(string name, float dur, float f0, float f1, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 7u + 3u;
            float phase = 0f, lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR, u = t / dur;
                float f = Mathf.Lerp(f0, f1, 1f - (1f - u) * (1f - u));
                phase += 2f * Mathf.PI * f / SR;
                lp += (Noise(ref rng) - lp) * 0.05f;
                float env = Mathf.Sin(Mathf.PI * u);
                d[i] = (Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 0.5f) * 0.5f + lp * 2f) * env * env;
            }
            return Make(name, d);
        }

        // ---- the story's sounds

        static AudioClip Glitch(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 2891336453u + 1u;
            int segEnd = 0, mode = 0, hold = 1;
            float freq = 440f, phase = 0f, held = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                if (i >= segEnd)
                {
                    segEnd = i + (int)(SR * (0.012f + 0.05f * Mathf.Abs(Noise(ref rng))));
                    mode = (int)(Mathf.Abs(Noise(ref rng)) * 4f);
                    freq = 150f + 2400f * Mathf.Abs(Noise(ref rng));
                    hold = 1 + (int)(Mathf.Abs(Noise(ref rng)) * 24f);
                }
                phase += freq / SR;
                float v;
                switch (mode)
                {
                    case 0: v = (phase % 1f) < 0.5f ? 0.8f : -0.8f; break;
                    case 1: v = Noise(ref rng); break;
                    case 2: v = Mathf.Sin(phase * 2f * Mathf.PI) * 0.9f; break;
                    default: v = 0f; break;
                }
                if (i % hold == 0) held = Mathf.Round(v * 4f) / 4f; // decimate + bitcrush
                d[i] = held * (1f - i / (float)d.Length * 0.5f);
            }
            return Make(name, d);
        }

        static AudioClip TypeKey(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 747796405u + 1u;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                d[i] = Noise(ref rng) * Mathf.Exp(-t * 420f) + Mathf.Sin(2f * Mathf.PI * 1850f * t) * 0.5f * Mathf.Exp(-t * 130f);
            }
            return Make(name, d);
        }

        static AudioClip RadioStatic(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 2246822519u + 1u;
            float low = 0f, band = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR, u = t / dur;
                float f = 2f * Mathf.Sin(Mathf.PI * 1900f / SR);
                float high = Noise(ref rng) - low - 0.9f * band;
                band += f * high;
                low += f * band;
                float crackle = Noise(ref rng) > 0.996f ? Noise(ref rng) * 3f : 0f;
                float whistle = Mathf.Sin(2f * Mathf.PI * (1650f + 120f * Mathf.Sin(t * 3f)) * t) * 0.08f;
                float env = Mathf.Min(1f, u * 12f) * Mathf.Min(1f, (1f - u) * 6f);
                d[i] = (band * 0.8f + crackle + whistle) * env;
            }
            return Make(name, d);
        }

        /// <summary>A low tower bell (inharmonic partials of a real church bell), for the twelve strokes of midnight.</summary>
        static AudioClip Bell(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 1597334677u + 1u;
            float[] ratio = { 0.5f, 1f, 1.183f, 1.506f, 2f, 2.514f, 2.662f, 3.011f, 4.166f };
            float[] amp = { 0.9f, 0.7f, 0.55f, 0.35f, 0.4f, 0.22f, 0.18f, 0.15f, 0.08f };
            float[] dec = { 0.45f, 0.6f, 0.9f, 1.1f, 1.2f, 1.8f, 2.2f, 2.6f, 3.5f };
            const float f0 = 196f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float v = 0f;
                for (int k = 0; k < ratio.Length; k++)
                {
                    float beat = 1f + 0.002f * k * Mathf.Sin(t * (1.3f + k));
                    v += Mathf.Sin(2f * Mathf.PI * f0 * ratio[k] * beat * t + k) * amp[k] * Mathf.Exp(-t * dec[k]);
                }
                v += Noise(ref rng) * Mathf.Exp(-t * 80f) * 0.5f;
                d[i] = Tanh(v * 0.9f);
            }
            return Make(name, d);
        }

        /// <summary>HALCYON's voice: clean, calm sine blips with a soft second partial (it is very polite).</summary>
        static AudioClip HalBlip(string name, float dur, float f)
        {
            var d = new float[(int)(dur * SR)];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR, u = t / dur;
                float env = Mathf.Min(1f, t * 200f) * (1f - u) * (1f - u);
                d[i] = (Mathf.Sin(2f * Mathf.PI * f * t) + Mathf.Sin(2f * Mathf.PI * f * 2f * t) * 0.22f + Mathf.Sin(2f * Mathf.PI * f * 3.01f * t) * 0.06f) * env;
            }
            return Make(name, d);
        }

        /// <summary>Wren's voice: a warm detuned saw, band-limited like an old radio, with a little hiss.</summary>
        static AudioClip WrenBlip(string name, float dur, float f, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 1013904223u + 1u;
            float p1 = 0f, p2 = 0f, low = 0f, band = 0f;
            float fc = 2f * Mathf.Sin(Mathf.PI * 1250f / SR);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR, u = t / dur;
                float vib = 1f + 0.015f * Mathf.Sin(t * 38f);
                p1 += f * vib / SR; p2 += f * 1.007f * vib / SR;
                float x = ((p1 % 1f) * 2f - 1f) * 0.5f + ((p2 % 1f) * 2f - 1f) * 0.5f + Noise(ref rng) * 0.12f;
                float high = x - low - 0.6f * band;
                band += fc * high;
                low += fc * band;
                float env = Mathf.Min(1f, t * 250f) * (1f - u);
                d[i] = Tanh(band * 2.2f) * env;
            }
            return Make(name, d);
        }

        static AudioClip Tone(string name, float dur, float f0, float f1, float decay)
        {
            var d = new float[(int)(dur * SR)];
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR, u = t / dur;
                float f = f1 > 0f ? (u < 0.45f ? f0 : f1) : f0;
                phase += 2f * Mathf.PI * f / SR;
                float local = f1 > 0f ? (u < 0.45f ? t : t - dur * 0.45f) : t;
                d[i] = (Mathf.Sin(phase) + Mathf.Sin(phase * 2f) * 0.2f) * Mathf.Exp(-local * decay) * Mathf.Min(1f, local * 400f);
            }
            return Make(name, d);
        }

        static AudioClip Crackle(string name, float dur, uint seed)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 2654435761u + 19u;
            float hold = 0f, gate = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                if (i % 220 == 0) gate = Noise(ref rng) > 0.1f ? 1f : 0.15f;
                if (i % 3 == 0) hold = Noise(ref rng);
                phase += 2f * Mathf.PI * (110f + 40f * Mathf.Sin(t * 30f)) / SR;
                float buzz = Mathf.Sign(Mathf.Sin(phase)) * 0.25f;
                d[i] = (hold * gate + buzz) * Mathf.Exp(-t * 4.5f);
            }
            return Make(name, d);
        }

        static AudioClip Thunder(string name, float dur, uint seed, bool close)
        {
            var d = new float[(int)(dur * SR)];
            uint rng = seed * 1103515245u + 21u;
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float n = Noise(ref rng);
                lp += (n - lp) * (close ? 0.08f : 0.03f);
                lp2 += (lp - lp2) * 0.05f;
                float rumble = lp2 * 6f * (0.55f + 0.45f * Mathf.Sin(t * 3.1f + Mathf.Sin(t * 7.3f))) * Mathf.Exp(-t * (close ? 0.9f : 0.6f));
                float crack = close ? n * Mathf.Exp(-t * 9f) * 0.9f + lp * 2f * Mathf.Exp(-Mathf.Abs(t - 0.12f) * 18f) : 0f;
                float attack = Mathf.Clamp01(t / (close ? 0.01f : 0.4f));
                d[i] = Tanh((rumble + crack) * attack * 1.4f);
            }
            return Make(name, d);
        }

        static AudioClip RainLoop(string name, float dur, uint seed)
        {
            int n = (int)(dur * SR);
            var d = new float[n];
            uint rng = seed * 22695477u + 23u;
            float low = 0f, band = 0f;
            for (int i = 0; i < n; i++)
            {
                float x = Noise(ref rng);
                float f = 2f * Mathf.Sin(Mathf.PI * 2600f / SR);
                float high = x - low - 0.7f * band;
                band += f * high;
                low += f * band;
                float v = band * 0.5f + low * 0.15f;
                if (Noise(ref rng) > 0.9965f) v += Noise(ref rng) * 2.2f; // individual drops
                d[i] = v;
            }
            // crossfade the tail into the head so the loop is seamless
            int xf = SR / 4;
            for (int i = 0; i < xf; i++)
            {
                float k = i / (float)xf;
                d[i] = d[i] * k + d[n - xf + i] * (1f - k);
            }
            var trimmed = new float[n - xf];
            System.Array.Copy(d, trimmed, trimmed.Length);
            float peak = 0.0001f;
            for (int i = 0; i < trimmed.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(trimmed[i]));
            for (int i = 0; i < trimmed.Length; i++) trimmed[i] *= 0.8f / peak;
            var c = AudioClip.Create("sfx_" + name, trimmed.Length, 1, SR, false);
            c.SetData(trimmed, 0);
            return c;
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
