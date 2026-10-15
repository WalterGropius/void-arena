using UnityEngine;

namespace HackAndSlash
{
    /// <summary>
    /// The score, synthesised live on the audio thread (no audio files): a step sequencer driving detuned-saw pads
    /// that glide between chords (Vangelis), a delayed arpeggio (the Matrix), a bass line, synthesised drums, swelling
    /// brass and a formant choir for the Spire, a gramophone waltz for the hotel and a music-box lullaby for the
    /// credits, all through a small stereo reverb. Each cue has four intensity layers that fade in as fights
    /// escalate, and bullet time slows the whole tape down.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class Music : MonoBehaviour
    {
        public enum Cue { None, Title, Explore, Combat, Hotel, Construct, Spire, Glitch, Ending, Credits }

        public static Music I { get; private set; }

        // ---------------------------------------------------------------- style table

        class Style
        {
            public float bpm = 90f;
            public int beats = 4;              // 4/4 or 3/4
            public int barsPerChord = 1;
            public int[][] chords;             // MIDI notes, lowest first
            public float padCut = 1400f;
            public int arpRate = 2;            // notes per beat (0 = off)
            public int drums;                  // 0 none, 1 sparse, 2 driving, 3 epic, 4 waltz brushes, 5 industrial
            public int bass;                   // 0 none, 1 sustained, 2 eighths, 3 octave sixteenths, 4 waltz oom
            public int[] melody;               // pairs: scale degree (-99 rest), length in beats
            public int[] scale = { 0, 2, 3, 5, 7, 8, 10 };
            public int melodyRoot = 62;
            public int melodyVoice;            // 0 none, 1 sax lead, 2 music box, 3 old piano
            public bool brass, choir, gramophone, bellEveryBars, glitch;
            public float melodyMinIntensity, melodyMaxIntensity = 3f;
        }

        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };

        static Style Make(Cue c)
        {
            switch (c)
            {
                case Cue.Title:
                    return new Style
                    {
                        bpm = 66f, barsPerChord = 2, padCut = 900f, arpRate = 0, drums = 0, bass = 1, bellEveryBars = true,
                        chords = new[] { new[] { 50, 57, 62, 65 }, new[] { 46, 55, 58, 62 }, new[] { 50, 57, 62, 65 }, new[] { 45, 52, 57, 61 } },
                    };
                case Cue.Explore:
                    return new Style
                    {
                        bpm = 88f, barsPerChord = 2, padCut = 1300f, arpRate = 2, drums = 1, bass = 1, melodyVoice = 1, melodyRoot = 62, scale = Minor, melodyMaxIntensity = 1.5f,
                        chords = new[] { new[] { 50, 57, 62, 65 }, new[] { 46, 53, 58, 62 }, new[] { 45, 53, 57, 60 }, new[] { 43, 52, 55, 60 } },
                        melody = new[] { 4, 3, 3, 1, 2, 4, 5, 3, 4, 1, 4, 4, 2, 2, 3, 1, 4, 1, 6, 2, 5, 2, 4, 3, 2, 1, 0, 4 },
                    };
                case Cue.Combat:
                    return new Style
                    {
                        bpm = 124f, barsPerChord = 1, padCut = 2000f, arpRate = 4, drums = 2, bass = 3, brass = true,
                        chords = new[] { new[] { 45, 52, 57, 60 }, new[] { 41, 53, 57, 60 }, new[] { 48, 52, 55, 60 }, new[] { 43, 55, 59, 62 } },
                    };
                case Cue.Hotel:
                    return new Style
                    {
                        bpm = 92f, beats = 3, barsPerChord = 2, padCut = 900f, arpRate = 0, drums = 4, bass = 4, gramophone = true, melodyVoice = 3,
                        melodyRoot = 65, scale = Major, melodyMaxIntensity = 3f,
                        chords = new[] { new[] { 53, 57, 60 }, new[] { 50, 53, 57 }, new[] { 50, 55, 58, 65 }, new[] { 48, 52, 55, 58 }, new[] { 53, 57, 60 }, new[] { 50, 53, 57 }, new[] { 50, 55, 58, 65 }, new[] { 53, 57, 60 } },
                        melody = new[]
                        {
                            2, 1, 4, 1, 7, 1,   6, 2, 5, 1,   5, 1, 4, 1, 2, 1,   4, 3,
                            3, 1, 5, 1, 8, 1,   7, 2, 6, 1,   6, 1, 4, 1, 3, 1,   2, 3,
                            2, 1, 4, 1, 7, 1,   9, 2, 8, 1,   7, 1, 5, 1, 4, 1,   5, 3,
                            3, 1, 5, 1, 7, 1,   6, 1, 4, 1, 1, 1,   0, 3,   -99, 3,
                        },
                    };
                case Cue.Construct:
                case Cue.Glitch:
                    return new Style
                    {
                        bpm = 100f, barsPerChord = 2, padCut = 1100f, arpRate = 4, drums = 5, bass = 2, glitch = c == Cue.Glitch,
                        chords = new[] { new[] { 40, 52, 55, 59 }, new[] { 36, 52, 55, 60 }, new[] { 40, 52, 55, 59 }, new[] { 38, 50, 54, 57 } },
                    };
                case Cue.Spire:
                    return new Style
                    {
                        bpm = 132f, barsPerChord = 1, padCut = 2400f, arpRate = 4, drums = 3, bass = 3, brass = true, choir = true,
                        chords = new[] { new[] { 48, 55, 60, 63 }, new[] { 44, 56, 60, 63 }, new[] { 39, 55, 58, 63 }, new[] { 46, 53, 58, 62 }, new[] { 48, 55, 60, 63 }, new[] { 44, 56, 60, 63 }, new[] { 41, 56, 60, 65 }, new[] { 43, 55, 59, 62 } },
                    };
                case Cue.Ending:
                    return new Style
                    {
                        bpm = 66f, barsPerChord = 2, padCut = 1600f, arpRate = 2, drums = 0, bass = 1, choir = true, bellEveryBars = true,
                        chords = new[] { new[] { 48, 55, 60, 64 }, new[] { 45, 52, 57, 60 }, new[] { 41, 53, 57, 60 }, new[] { 43, 55, 59, 62 } },
                    };
                case Cue.Credits:
                    return new Style
                    {
                        bpm = 84f, beats = 3, barsPerChord = 2, padCut = 1100f, arpRate = 0, drums = 0, bass = 4, melodyVoice = 2, melodyRoot = 72, scale = Major,
                        chords = new[] { new[] { 48, 55, 60, 64 }, new[] { 45, 52, 57, 60 }, new[] { 41, 53, 57, 60 }, new[] { 43, 55, 59, 62 } },
                        melody = new[]
                        {
                            4, 2, 2, 1,   0, 3,   5, 2, 4, 1,   2, 3,   3, 2, 5, 1,   7, 2, 5, 1,   6, 2, 4, 1,   1, 3,
                            4, 2, 2, 1,   0, 2, 2, 1,   5, 1, 4, 1, 2, 1,   0, 3,   3, 2, 2, 1,   5, 2, 7, 1,   4, 3,   -99, 3,
                        },
                    };
                default:
                    return null;
            }
        }

        // ---------------------------------------------------------------- shared state (main thread writes, audio thread reads)

        volatile int requestedCue;
        volatile float targetIntensity = 1f, rate = 1f, volume = 0.8f;
        int sr = 48000;

        // ---------------------------------------------------------------- audio-thread state

        Style style;
        int currentCue = -1;
        float fade, fadeTarget = 1f;
        float intensity = 1f;
        double stepPos;
        int step, bar, chordIndex, melodyIndex;
        float melodyBeatsLeft;
        uint rng = 2463534242u;

        // pad: four chord tones x two detuned saws
        readonly float[] padFreq = new float[4], padTarget = new float[4], padPhase = new float[8];
        float padLow, padBand, padLfo;
        // arpeggio with a stereo delay
        float arpFreq, arpPhase, arpEnv;
        int arpIndex;
        readonly float[] delayL = new float[96000], delayR = new float[96000];
        int delayPos;
        // bass
        float bassFreq, bassPhase, subPhase, bassEnv, bassLow, bassBand;
        // drums
        float kickEnv, kickPhase, kickPitch, snareEnv, snarePhase, hatEnv, hatOpen, tomEnv, tomPhase, tomPitch, hatHp;
        // brass swell, choir, melody, bell
        float brassEnv, brassLow, brassBand;
        readonly float[] brassPhase = new float[3];
        float choirB1, choirL1, choirB2, choirL2;
        readonly float[] choirPhase = new float[4];
        float melFreq, melPhase, melPhase2, melEnv, melLow, melBand, melVib, melGate;
        float bellEnv, bellFreq, bellPhase1, bellPhase2;
        // gramophone wow and crackle
        float wow;
        // glitch stutter
        int stutterLen, stutterPos;
        readonly float[] stutter = new float[24000];
        bool stuttering;
        int stutterLeft;
        // reverb (Freeverb-lite)
        readonly Comb[] combL = new Comb[4], combR = new Comb[4];
        readonly AllPass[] apL = new AllPass[2], apR = new AllPass[2];

        class Comb
        {
            readonly float[] buf; int pos; float store;
            public Comb(int n) { buf = new float[n]; }
            public float Process(float x, float fb, float damp)
            {
                float y = buf[pos];
                store = y * (1f - damp) + store * damp;
                buf[pos] = x + store * fb;
                if (++pos >= buf.Length) pos = 0;
                return y;
            }
        }

        class AllPass
        {
            readonly float[] buf; int pos;
            public AllPass(int n) { buf = new float[n]; }
            public float Process(float x)
            {
                float b = buf[pos];
                buf[pos] = x + b * 0.5f;
                if (++pos >= buf.Length) pos = 0;
                return b - x;
            }
        }

        // ---------------------------------------------------------------- lifecycle

        readonly Style[] styles = new Style[(int)Cue.Credits + 1];

        void Awake()
        {
            I = this;
            for (int c = 0; c < styles.Length; c++) styles[c] = Make((Cue)c);
            sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            float k = sr / 44100f;
            int[] cl = { 1116, 1188, 1277, 1356 };
            for (int i = 0; i < 4; i++) { combL[i] = new Comb((int)(cl[i] * k)); combR[i] = new Comb((int)((cl[i] + 23) * k)); }
            apL[0] = new AllPass((int)(556 * k)); apL[1] = new AllPass((int)(441 * k));
            apR[0] = new AllPass((int)(579 * k)); apR[1] = new AllPass((int)(464 * k));
            var src = GetComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.clip = null;
            src.ignoreListenerPause = true; // the score keeps playing under the pause menu
            src.Play();
        }

        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            volume = Settings.musicVolume;
            var gm = GameManager.I;
            float slow = gm ? gm.SlowMoScale : 1f;
            if (gm && gm.player) slow = Mathf.Min(slow, gm.player.FocusScale);
            float want = slow < 0.6f ? 0.82f : 1f;
            if (gm && gm.Paused) want = 1f;
            rate = Mathf.Lerp(rate, want, 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
        }

        /// <summary>Crossfades to a cue (no-op if it is already playing).</summary>
        public void Play(Cue c) => requestedCue = (int)c;

        /// <summary>0 = calm (pads), 1 = + arpeggio, 2 = + drums and bass, 3 = everything.</summary>
        public void SetIntensity(int level) => targetIntensity = Mathf.Clamp(level, 0, 3);

        public Cue Current => (Cue)Mathf.Max(0, currentCue);

        // ---------------------------------------------------------------- synthesis

        float Noise()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return rng / (float)uint.MaxValue * 2f - 1f;
        }

        static float Mtof(float m) => 440f * Mathf.Pow(2f, (m - 69f) / 12f);
        static float Saw(float p) => (p - Mathf.Floor(p)) * 2f - 1f;
        static float Tanh(float x) { float x2 = x * x; return x * (27f + x2) / (27f + 9f * x2); }

        void LoadStyle(int cue)
        {
            currentCue = cue;
            style = cue >= 0 && cue < styles.Length ? styles[cue] : null; // built on the main thread in Awake
            step = 0; bar = 0; chordIndex = 0; melodyIndex = 0; melodyBeatsLeft = 0f; stepPos = 0.0;
            if (style == null) return;
            var ch = style.chords[0];
            for (int i = 0; i < 4; i++) padFreq[i] = padTarget[i] = Mtof(ch[Mathf.Min(i, ch.Length - 1)] + (i >= ch.Length ? 12 : 0));
            OnStep();
        }

        int[] Chord => style.chords[chordIndex % style.chords.Length];

        void OnStep()
        {
            int stepsPerBar = style.beats * 4;
            int s = step % stepsPerBar;
            if (s == 0)
            {
                if (bar % style.barsPerChord == 0)
                {
                    chordIndex = (bar / style.barsPerChord) % style.chords.Length;
                    var ch = Chord;
                    for (int i = 0; i < 4; i++) padTarget[i] = Mtof(ch[Mathf.Min(i, ch.Length - 1)] + (i >= ch.Length ? 12 : 0));
                    if (style.brass && intensity > 2.2f) brassEnv = 1f;
                }
                if (style.bellEveryBars && bar % 2 == 0) { bellEnv = 1f; bellFreq = Mtof(Chord[0] + 24); }
            }
            float lvl = intensity;
            var c = Chord;
            int root = c[0];

            // arpeggio: up through the chord across two octaves
            if (style.arpRate > 0 && lvl > 0.5f && (s % (4 / style.arpRate)) == 0)
            {
                int n = c.Length;
                int idx = arpIndex++ % (n * 2);
                int note = c[idx % n] + 12 + (idx >= n ? 12 : 0);
                if (style.glitch && Noise() > 0.6f) note += 12;
                arpFreq = Mtof(note);
                arpEnv = 1f;
            }

            // bass
            switch (style.bass)
            {
                case 1: if (s == 0) { bassFreq = Mtof(root - 12); bassEnv = 1f; } break;
                case 2: if (s % 2 == 0) { bassFreq = Mtof(root - 12); bassEnv = 0.9f; } break;
                case 3: if (lvl > 1.5f || s % 4 == 0) { bassFreq = Mtof(root - 12 + ((s / 2) % 2 == 1 ? 12 : 0)); bassEnv = 0.85f; } break;
                case 4: if (s == 0) { bassFreq = Mtof(root - 12); bassEnv = 1f; } else if (s == 4 || s == 8) { bassFreq = Mtof(c[Mathf.Min(1, c.Length - 1)] - 12); bassEnv = 0.45f; } break;
            }

            // drums
            if (lvl > 1.5f)
            {
                bool hard = lvl > 2.5f;
                switch (style.drums)
                {
                    case 1:
                        if (s == 0 || s == 10) Kick(0.7f);
                        if (s == 8) snareEnv = 0.45f;
                        if (s % 2 == 0) Hat(0.35f, false);
                        break;
                    case 2:
                        if (s % 4 == 0 || (hard && s == 10)) Kick(1f);
                        if (s == 4 || s == 12) snareEnv = 1f;
                        Hat(s % 2 == 1 ? 0.8f : 0.4f, hard && s % 8 == 6);
                        break;
                    case 3:
                        if (s == 0 || s == 3 || s == 8 || (hard && (s == 10 || s == 11))) Kick(1f);
                        if (s == 4 || s == 12) snareEnv = 1f;
                        if (s >= 13) Tom(Mtof(43 - (s - 13) * 4));
                        if (hard || s % 2 == 0) Hat(0.6f, false);
                        break;
                    case 4:
                        if (s == 0) Kick(0.5f);
                        if (s == 4 || s == 8) { snareEnv = 0.3f; Hat(0.5f, true); }
                        break;
                    case 5:
                        if (s == 0 || s == 7 || s == 8 || (hard && s == 14)) Kick(0.95f);
                        if (s == 4 || s == 12) snareEnv = 0.8f;
                        if (s % 4 == 2) Hat(0.9f, true);
                        break;
                }
            }

            // melody
            if (style.melody != null && style.melodyVoice > 0)
            {
                melodyBeatsLeft -= 0.25f;
                if (melodyBeatsLeft <= 0.001f)
                {
                    int deg = style.melody[melodyIndex];
                    float len = style.melody[melodyIndex + 1];
                    melodyIndex = (melodyIndex + 2) % style.melody.Length;
                    melodyBeatsLeft += len;
                    if (deg > -50)
                    {
                        int oct = deg >= 0 ? deg / 7 : -1 + (deg + 1) / 7;
                        int d = ((deg % 7) + 7) % 7;
                        melFreq = Mtof(style.melodyRoot + oct * 12 + style.scale[d]);
                        melEnv = 1f;
                        melVib = 0f;
                        melGate = len;
                        melPhase2 = 0f;
                    }
                    else melGate = 0f;
                }
            }

            // glitch: occasionally freeze and stutter the last slice of output
            if (style.glitch && s % 4 == 0 && !stuttering && Noise() > 0.55f) { stuttering = true; stutterLeft = (int)(sr * 0.25f); stutterPos = 0; stutterLen = 0; }

            step++;
            if (step % stepsPerBar == 0) bar++;
        }

        void Kick(float v) { kickEnv = v; kickPhase = 0f; kickPitch = 1f; }
        void Hat(float v, bool open) { hatEnv = v; hatOpen = open ? 1f : 0f; }
        void Tom(float f) { tomEnv = 0.8f; tomPitch = f; tomPhase = 0f; }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float dt = 1f / sr;
            float vol = volume;
            int req = requestedCue;
            for (int i = 0; i < data.Length; i += channels)
            {
                // cue changes: fade out, swap, fade in
                if (req != currentCue)
                {
                    fade -= dt / 0.9f;
                    if (fade <= 0f || style == null) { fade = 0f; LoadStyle(req); }
                }
                else if (fade < 1f) fade = Mathf.Min(1f, fade + dt / 1.6f);

                if (style == null)
                {
                    for (int c = 0; c < channels; c++) data[i + c] = 0f;
                    continue;
                }

                float r = rate;
                intensity += (targetIntensity - intensity) * dt * 0.6f;
                float lvl = intensity;

                // sequencer clock (sixteenth notes), slowed with the tape in bullet time
                stepPos += r * style.bpm / 60.0 * 4.0 / sr;
                if (stepPos >= 1.0) { stepPos -= 1.0; OnStep(); }

                // gramophone wow
                float pitch = r;
                if (style.gramophone) { wow += dt * 0.55f; if (wow > 100f) wow -= 100f; pitch *= 1f + 0.004f * Mathf.Sin(wow * 6.2831f) + 0.0015f * Mathf.Sin(wow * 31f); }

                // ---- pad (two detuned saws per voice, gliding, through a breathing low-pass)
                float pad = 0f;
                for (int v = 0; v < 4; v++)
                {
                    padFreq[v] += (padTarget[v] - padFreq[v]) * dt * 2.2f;
                    float f = padFreq[v] * pitch;
                    padPhase[v * 2] += f * 1.003f * dt;
                    padPhase[v * 2 + 1] += f * 0.997f * dt;
                    if (padPhase[v * 2] > 1f) padPhase[v * 2] -= 1f;
                    if (padPhase[v * 2 + 1] > 1f) padPhase[v * 2 + 1] -= 1f;
                    pad += Saw(padPhase[v * 2]) + Saw(padPhase[v * 2 + 1]);
                }
                padLfo += dt * 0.11f;
                if (padLfo > 1f) padLfo -= 1f;
                float cut = style.padCut * (0.65f + 0.35f * Mathf.Sin(padLfo * 6.2831f)) * (0.8f + 0.12f * lvl);
                float fc = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(cut, sr * 0.2f) / sr);
                float hp = pad * 0.12f - padLow - 0.7f * padBand;
                padBand += fc * hp;
                padLow += fc * padBand;
                float padOut = padLow * 0.55f;

                // ---- arpeggio (pulse, quick decay)
                float arp = 0f;
                if (arpEnv > 0.0005f)
                {
                    arpPhase += arpFreq * pitch * dt;
                    if (arpPhase > 1f) arpPhase -= 1f;
                    float pw = 0.3f + 0.15f * Mathf.Sin(padLfo * 12f);
                    arp = ((arpPhase < pw ? 1f : -1f) * 0.6f + Mathf.Sin(arpPhase * 6.2831f) * 0.4f) * arpEnv;
                    arpEnv *= 1f - dt * 9f;
                }
                float arpGain = Mathf.Clamp01(lvl - 0.3f) * 0.16f;

                // ---- bass (saw + sub, resonant low-pass)
                float bass = 0f;
                if (bassEnv > 0.0005f)
                {
                    bassPhase += bassFreq * pitch * dt;
                    subPhase += bassFreq * 0.5f * pitch * dt;
                    if (bassPhase > 1f) bassPhase -= 1f;
                    if (subPhase > 1f) subPhase -= 1f;
                    float x = Saw(bassPhase) * 0.6f + Mathf.Sin(subPhase * 6.2831f) * 0.9f;
                    float bfc = 2f * Mathf.Sin(Mathf.PI * (180f + 900f * bassEnv) / sr);
                    float bh = x - bassLow - 0.5f * bassBand;
                    bassBand += bfc * bh;
                    bassLow += bfc * bassBand;
                    bass = bassLow * bassEnv;
                    bassEnv *= 1f - dt * (style.bass == 1 ? 0.6f : style.bass == 4 ? 3f : 5f);
                }
                float bassGain = style.bass == 1 ? 0.22f : Mathf.Clamp01(lvl - 1f) * 0.32f;

                // ---- drums
                float drums = 0f;
                if (kickEnv > 0.0005f)
                {
                    kickPitch *= 1f - dt * 28f;
                    kickPhase += (48f + 140f * kickPitch) * dt;
                    if (kickPhase > 1f) kickPhase -= 1f;
                    drums += Mathf.Sin(kickPhase * 6.2831f) * kickEnv * 1.1f;
                    kickEnv *= 1f - dt * 7f;
                }
                float n = Noise();
                if (snareEnv > 0.0005f)
                {
                    snarePhase += 190f * pitch * dt;
                    if (snarePhase > 1f) snarePhase -= 1f;
                    drums += (n * 0.7f + Mathf.Sin(snarePhase * 6.2831f) * 0.35f) * snareEnv * 0.55f;
                    snareEnv *= 1f - dt * 16f;
                }
                if (hatEnv > 0.0005f)
                {
                    float h = n - hatHp;
                    hatHp += (n - hatHp) * 0.35f;
                    drums += h * hatEnv * 0.18f;
                    hatEnv *= 1f - dt * (hatOpen > 0.5f ? 9f : 45f);
                }
                if (tomEnv > 0.0005f)
                {
                    tomPhase += tomPitch * dt;
                    if (tomPhase > 1f) tomPhase -= 1f;
                    drums += Mathf.Sin(tomPhase * 6.2831f) * tomEnv * 0.7f;
                    tomEnv *= 1f - dt * 8f;
                }

                // ---- brass swell (CS-80): three saws a fifth apart, opening filter
                float brass = 0f;
                if (style.brass && brassEnv > 0.0005f)
                {
                    var ch = Chord;
                    float x = 0f;
                    for (int b = 0; b < 3; b++)
                    {
                        brassPhase[b] += Mtof(ch[Mathf.Min(b, ch.Length - 1)] + 12) * pitch * dt * (1f + 0.002f * b);
                        if (brassPhase[b] > 1f) brassPhase[b] -= 1f;
                        x += Saw(brassPhase[b]);
                    }
                    float swell = Mathf.Sin(Mathf.Min(1f, (1f - brassEnv) * 4f) * 1.5708f);
                    float bfc = 2f * Mathf.Sin(Mathf.PI * (400f + 2600f * swell) / sr);
                    float bh = x * 0.3f - brassLow - 0.4f * brassBand;
                    brassBand += bfc * bh;
                    brassLow += bfc * brassBand;
                    brass = brassLow * brassEnv;
                    brassEnv *= 1f - dt * 0.9f;
                }

                // ---- choir ("aah": two formant band-passes over saws on the chord)
                float choir = 0f;
                if (style.choir && lvl > 1.8f)
                {
                    var ch = Chord;
                    float x = 0f;
                    for (int v = 0; v < 4; v++)
                    {
                        float vib = 1f + 0.006f * Mathf.Sin((padLfo * 37f + v) * 6.2831f);
                        choirPhase[v] += Mtof(ch[Mathf.Min(v, ch.Length - 1)] + 12) * vib * pitch * dt;
                        if (choirPhase[v] > 1f) choirPhase[v] -= 1f;
                        x += Saw(choirPhase[v]);
                    }
                    float f1 = 2f * Mathf.Sin(Mathf.PI * 720f / sr), f2 = 2f * Mathf.Sin(Mathf.PI * 1150f / sr);
                    float h1 = x * 0.2f - choirL1 - 0.25f * choirB1; choirB1 += f1 * h1; choirL1 += f1 * choirB1;
                    float h2 = x * 0.2f - choirL2 - 0.3f * choirB2; choirB2 += f2 * h2; choirL2 += f2 * choirB2;
                    choir = (choirB1 + choirB2 * 0.7f) * Mathf.Clamp01(lvl - 1.8f);
                }

                // ---- melody voices
                float mel = 0f;
                if (melEnv > 0.0005f && style.melodyVoice > 0)
                {
                    float f = melFreq * pitch;
                    melVib += dt;
                    switch (style.melodyVoice)
                    {
                        case 1: // late-night sax: band-passed saw with breath and vibrato
                        {
                            float vib = 1f + 0.008f * Mathf.Sin(melVib * 5.5f * 6.2831f) * Mathf.Min(1f, melVib * 2f);
                            melPhase += f * vib * dt;
                            if (melPhase > 1f) melPhase -= 1f;
                            float x = Saw(melPhase) + Noise() * 0.08f;
                            float mf = 2f * Mathf.Sin(Mathf.PI * 1300f / sr);
                            float mh = x - melLow - 0.5f * melBand;
                            melBand += mf * mh;
                            melLow += mf * melBand;
                            float attack = Mathf.Min(1f, melPhase2 * 6f);
                            melPhase2 += dt;
                            mel = melBand * 0.6f * melEnv * attack;
                            melEnv *= 1f - dt * 0.35f;
                            break;
                        }
                        case 2: // music box: two inharmonic partials, plucked
                        {
                            melPhase += f * dt; melPhase2 += f * 4.02f * dt;
                            if (melPhase > 1f) melPhase -= 1f;
                            if (melPhase2 > 1f) melPhase2 -= 1f;
                            mel = (Mathf.Sin(melPhase * 6.2831f) + Mathf.Sin(melPhase2 * 6.2831f) * 0.3f * melEnv) * melEnv;
                            melEnv *= 1f - dt * 2.2f;
                            break;
                        }
                        default: // an old upright through a gramophone horn
                        {
                            melPhase += f * dt;
                            if (melPhase > 1f) melPhase -= 1f;
                            float p = melPhase * 6.2831f;
                            mel = (Mathf.Sin(p) + Mathf.Sin(p * 2f) * 0.45f * melEnv + Mathf.Sin(p * 3f) * 0.2f * melEnv) * melEnv;
                            melEnv *= 1f - dt * 1.6f;
                            break;
                        }
                    }
                }
                float melGain = lvl >= style.melodyMinIntensity && lvl <= style.melodyMaxIntensity + 0.5f ? (style.melodyVoice == 1 ? 0.14f : 0.2f) : 0f;

                // ---- the bell
                float bell = 0f;
                if (bellEnv > 0.0005f)
                {
                    bellPhase1 += bellFreq * pitch * dt; bellPhase2 += bellFreq * 2.76f * pitch * dt;
                    if (bellPhase1 > 1f) bellPhase1 -= 1f;
                    if (bellPhase2 > 1f) bellPhase2 -= 1f;
                    bell = (Mathf.Sin(bellPhase1 * 6.2831f) + Mathf.Sin(bellPhase2 * 6.2831f) * 0.4f) * bellEnv;
                    bellEnv *= 1f - dt * 0.5f;
                }

                // ---- mix
                float padGain = 0.32f * (style.gramophone ? 0.5f : 1f);
                float dry = padOut * padGain + bass * bassGain + drums * 0.42f + brass * 0.2f + choir * 0.1f + mel * melGain + bell * 0.09f;
                float arpOut = arp * arpGain;

                // arp delay (dotted eighth), ping-pong
                int dlen = Mathf.Clamp((int)(sr * 60f / style.bpm * 0.75f / Mathf.Max(0.5f, r)), 1, delayL.Length - 1);
                int rp = delayPos - dlen; if (rp < 0) rp += delayL.Length;
                float dl = delayL[rp], dr = delayR[rp];
                delayL[delayPos] = arpOut + dr * 0.42f;
                delayR[delayPos] = dl * 0.42f;
                if (++delayPos >= delayL.Length) delayPos = 0;

                float left = dry + arpOut + dl * 0.6f;
                float right = dry + arpOut * 0.7f + dr * 0.6f;

                if (style.gramophone)
                {
                    // narrow band + crackle
                    float crackle = Noise() > 0.9993f ? Noise() * 0.5f : 0f;
                    left = left * 0.8f + crackle; right = right * 0.8f + crackle;
                }

                // reverb
                float input = (left + right) * 0.5f * 0.35f;
                float rl = 0f, rr = 0f;
                for (int k = 0; k < 4; k++) { rl += combL[k].Process(input, 0.84f, 0.25f); rr += combR[k].Process(input, 0.84f, 0.25f); }
                for (int k = 0; k < 2; k++) { rl = apL[k].Process(rl); rr = apR[k].Process(rr); }
                left += rl * 0.5f; right += rr * 0.5f;

                // glitch stutter: record a slice, then loop it for a moment
                if (style.glitch)
                {
                    if (stuttering)
                    {
                        if (stutterPos < stutter.Length && stutterLen < 2400) { stutter[stutterLen++] = left; }
                        else
                        {
                            float sv = stutter[stutterPos % Mathf.Max(1, stutterLen)];
                            left = right = Mathf.Round(sv * 6f) / 6f;
                        }
                        stutterPos++;
                        if (--stutterLeft <= 0) { stuttering = false; stutterLen = 0; }
                    }
                }

                float g = fade * vol * 0.55f;
                left = Tanh(left * g);
                right = Tanh(right * g);
                if (channels == 1) data[i] = (left + right) * 0.5f;
                else
                {
                    data[i] = left;
                    data[i + 1] = right;
                    for (int c = 2; c < channels; c++) data[i + c] = 0f;
                }
            }
        }
    }
}
