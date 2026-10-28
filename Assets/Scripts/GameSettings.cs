using UnityEngine;

namespace Roguelike
{
    /// <summary>
    /// Player settings kept between sessions (PlayerPrefs).
    /// </summary>
    public static class GameSettings
    {
        private const string VSyncKey = "VSync";

        /// <summary>
        /// True to sync the Game mode to the monitor refresh rate. The Benchmark mode is always uncapped.
        /// </summary>
        public static bool VSync
        {
            get => PlayerPrefs.GetInt(VSyncKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Applies the frame rate settings of a mode: uncapped for benchmarks, V-Sync setting for a regular game.
        /// </summary>
        /// <param name="pBenchmark">True for a benchmark session.</param>
        public static void ApplyFrameRate(bool pBenchmark)
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = !pBenchmark && VSync ? 1 : 0;
        }
    }
}
