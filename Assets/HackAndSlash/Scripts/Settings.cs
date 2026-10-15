using UnityEngine;

namespace HackAndSlash
{
    /// <summary>Player options, difficulty modifiers, story progress and arcade records (persisted with PlayerPrefs).</summary>
    public static class Settings
    {
        public static Difficulty difficulty = Difficulty.Normal;
        public static float mouseSensitivity = 1f;
        public static float stickSensitivity = 1f;
        public static bool invertY;
        public static float musicVolume = 0.8f;
        public static float sfxVolume = 1f;
        public static bool commentary = true;
        public static bool screenShake = true;
        static bool loaded;

        public const int ChapterCount = 6; // prologue + five chapters

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            difficulty = (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt("va.difficulty", (int)Difficulty.Normal), 0, 3);
            mouseSensitivity = PlayerPrefs.GetFloat("va.mouse", 1f);
            stickSensitivity = PlayerPrefs.GetFloat("va.stick", 1f);
            invertY = PlayerPrefs.GetInt("va.invertY", 0) == 1;
            musicVolume = PlayerPrefs.GetFloat("va.music", 0.8f);
            sfxVolume = PlayerPrefs.GetFloat("va.sfx", 1f);
            commentary = PlayerPrefs.GetInt("va.commentary", 1) == 1;
            screenShake = PlayerPrefs.GetInt("va.shake", 1) == 1;
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("va.difficulty", (int)difficulty);
            PlayerPrefs.SetFloat("va.mouse", mouseSensitivity);
            PlayerPrefs.SetFloat("va.stick", stickSensitivity);
            PlayerPrefs.SetInt("va.invertY", invertY ? 1 : 0);
            PlayerPrefs.SetFloat("va.music", musicVolume);
            PlayerPrefs.SetFloat("va.sfx", sfxVolume);
            PlayerPrefs.SetInt("va.commentary", commentary ? 1 : 0);
            PlayerPrefs.SetInt("va.shake", screenShake ? 1 : 0);
            PlayerPrefs.Save();
        }

        // ---------------------------------------------------------------- difficulty

        public static string DifficultyName(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return "CIVILIAN";
                case Difficulty.Normal: return "ASSOCIATE";
                case Difficulty.Hard: return "VOID";
                default: return "KNIGHTMARE";
            }
        }

        public static string DifficultyBlurb(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return "Generous counter windows. Robots pull their punches.";
                case Difficulty.Normal: return "The intended experience. Counter indicators on.";
                case Difficulty.Hard: return "No counter indicators. Read the robots, not the lightning.";
                default: return "No indicators, double damage, no healing between waves. HALCYON is delighted.";
            }
        }

        /// <summary>Damage robots deal to the player.</summary>
        public static float EnemyDamage => difficulty == Difficulty.Easy ? 0.6f : difficulty == Difficulty.Normal ? 1f : difficulty == Difficulty.Hard ? 1.35f : 2f;
        /// <summary>Scales attack wind-up times (lower = faster robots).</summary>
        public static float WindupScale => difficulty == Difficulty.Easy ? 1.25f : difficulty == Difficulty.Normal ? 1f : difficulty == Difficulty.Hard ? 0.88f : 0.8f;
        /// <summary>Scales how many robots the director lets swing at once.</summary>
        public static int AggressionBonus => difficulty == Difficulty.Easy ? -1 : difficulty == Difficulty.Normal ? 0 : 1;
        public static float EnemyHealth => difficulty == Difficulty.Easy ? 0.8f : difficulty == Difficulty.Knightmare ? 1.25f : 1f;
        public static bool CounterIndicators => difficulty <= Difficulty.Normal;
        public static float WaveHeal => difficulty == Difficulty.Easy ? 40f : difficulty == Difficulty.Normal ? 25f : difficulty == Difficulty.Hard ? 15f : 0f;
        public static float OrbChance => difficulty == Difficulty.Easy ? 0.35f : difficulty == Difficulty.Normal ? 0.2f : difficulty == Difficulty.Hard ? 0.12f : 0.06f;
        /// <summary>Extra counter leniency in seconds (counters are accepted a bit before the indicator shows).</summary>
        public static float CounterLeniency => difficulty == Difficulty.Easy ? 0.12f : 0f;
        public static float ScoreMultiplier => difficulty == Difficulty.Easy ? 0.5f : difficulty == Difficulty.Normal ? 1f : difficulty == Difficulty.Hard ? 1.5f : 2.5f;

        // ---------------------------------------------------------------- progress

        public static int StoryUnlocked
        {
            get => PlayerPrefs.GetInt("va.story", 0);
            set { if (value > StoryUnlocked) { PlayerPrefs.SetInt("va.story", value); PlayerPrefs.Save(); } }
        }

        public static bool StoryCompleted
        {
            get => PlayerPrefs.GetInt("va.storyDone", 0) == 1;
            set { PlayerPrefs.SetInt("va.storyDone", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static long BestScore(District d, Difficulty diff) => long.TryParse(PlayerPrefs.GetString("va.best." + d + "." + diff, "0"), out long v) ? v : 0;
        public static int BestWave(District d, Difficulty diff) => PlayerPrefs.GetInt("va.wave." + d + "." + diff, 0);

        /// <summary>Records an arcade run. Returns true for a new high score.</summary>
        public static bool RecordArcade(District d, Difficulty diff, long score, int wave)
        {
            bool best = score > BestScore(d, diff);
            if (best) PlayerPrefs.SetString("va.best." + d + "." + diff, score.ToString());
            if (wave > BestWave(d, diff)) PlayerPrefs.SetInt("va.wave." + d + "." + diff, wave);
            PlayerPrefs.Save();
            return best;
        }

        /// <summary>Arkham challenge-map style medals for an arcade run (0..3).</summary>
        public static int Medals(int wavesCleared) => wavesCleared >= 12 ? 3 : wavesCleared >= 8 ? 2 : wavesCleared >= 4 ? 1 : 0;

        public static string DistrictName(District d)
        {
            switch (d)
            {
                case District.Rooftop: return "THE LONG RAIN";
                case District.Streets: return "LANTERN STREET";
                case District.Hotel: return "THE GRAND HALCYON";
                case District.Construct: return "THE CONSTRUCT";
                default: return "THE SPIRE";
            }
        }

        public static string DistrictBlurb(District d)
        {
            switch (d)
            {
                case District.Rooftop: return "A helipad in a thunderstorm, eleven years long.";
                case District.Streets: return "Flooded neon, noodle bars, nobody home.";
                case District.Hotel: return "Immaculate. Symmetrical. Please wipe your feet.";
                case District.Construct: return "White void. Nothing is real, especially the Auditors.";
                default: return "Above the clouds, under the Monolith. Midnight is near.";
            }
        }
    }
}
