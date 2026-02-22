using System;
using UnityEngine;

namespace Game.Score
{
    /// <summary>
    /// ScriptableObject-based score manager. Tracks an integer score,
    /// provides AddScore, and fires onScoreChanged when the score updates.
    /// Create via Assets > Create > Game > ScoreManager.
    /// </summary>
    [CreateAssetMenu(fileName = "ScoreManager", menuName = "Game/ScoreManager")]
    public class ScoreManager : ScriptableObject
    {
        [NonSerialized] private int currentScore;

        public event Action<int> onScoreChanged;

        public int CurrentScore => currentScore;

        public void AddScore(int points)
        {
            currentScore += points;
            onScoreChanged?.Invoke(currentScore);
        }

        public void ResetScore()
        {
            currentScore = 0;
            onScoreChanged?.Invoke(currentScore);
        }

        private void OnEnable()
        {
            currentScore = 0;
        }

        /// <summary>
        /// Static accessor for bridge-driven testing.
        /// Finds the first ScoreManager asset loaded at runtime.
        /// </summary>
        public static string GetScoreState()
        {
            var managers = Resources.FindObjectsOfTypeAll<ScoreManager>();
            if (managers.Length == 0)
                return "{\"success\":false,\"error\":\"No ScoreManager instance found\"}";

            var mgr = managers[0];
            return string.Format("{{\"success\":true,\"currentScore\":{0}}}", mgr.currentScore);
        }
    }
}
