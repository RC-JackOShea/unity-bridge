/// Delete this file to remove AI test infrastructure.
using System.Collections.Generic;
using UnityEngine;

namespace Game.BridgeTests
{
    /// <summary>
    /// Bridge-callable test methods for the score system.
    /// Depends on Game.Score — game code must never reference this class.
    /// </summary>
    public static class ScoreSystemTests
    {
        private static readonly Vector3 TriggerPosition = new Vector3(3, 0.5f, 3);
        private static readonly Vector3 SafePosition = new Vector3(0, 1f, 0);

        /// <summary>
        /// Bridge-callable: run full 10-cycle score test.
        /// Moves player into the trigger zone and back out 10 times,
        /// using manual physics simulation to fire OnTriggerEnter/Exit.
        /// Returns a JSON report of each cycle.
        /// </summary>
        public static string RunScoreTest()
        {
            var manager = Game.Score.ScoreSystemSetup.RuntimeManager;
            if (manager == null)
                return "{\"success\":false,\"error\":\"ScoreManager not initialized\"}";

            var player = GameObject.Find("TestPlayer");
            if (player == null)
                return "{\"success\":false,\"error\":\"TestPlayer not found\"}";

            var zone = GameObject.Find("ScoreTriggerZone");
            if (zone == null)
                return "{\"success\":false,\"error\":\"ScoreTriggerZone not found — was it already consumed?\"}";

            // Verify player has the Player tag
            string playerTag;
            try { playerTag = player.tag; }
            catch { playerTag = "Untagged"; }

            if (playerTag != "Player")
                return string.Format("{{\"success\":false,\"error\":\"TestPlayer tag is '{0}', expected 'Player'\"}}", playerTag);

            // Prepare: make kinematic so teleports are clean, disable gravity interference
            var rb = player.GetComponent<Rigidbody>();
            bool wasKinematic = false;
            if (rb != null)
            {
                wasKinematic = rb.isKinematic;
                rb.isKinematic = true;
            }

            // Reset score
            manager.ResetScore();

            // Take control of physics simulation
            bool oldAutoSim = Physics.autoSimulation;
            Physics.autoSimulation = false;

            var cycles = new List<string>();

            try
            {
                // Start from safe position
                player.transform.position = SafePosition;
                Physics.SyncTransforms();
                Physics.Simulate(0.02f);

                for (int i = 1; i <= 10; i++)
                {
                    // Move INTO trigger zone
                    player.transform.position = TriggerPosition;
                    Physics.SyncTransforms();
                    Physics.Simulate(0.02f);
                    Physics.Simulate(0.02f); // extra step to ensure detection

                    int scoreAfterEnter = manager.CurrentScore;

                    // Move OUT of trigger zone
                    player.transform.position = SafePosition;
                    Physics.SyncTransforms();
                    Physics.Simulate(0.02f);
                    Physics.Simulate(0.02f);

                    // Check if zone still exists (Unity overloads == for destroyed objects)
                    bool zoneAlive = GameObject.Find("ScoreTriggerZone") != null;

                    cycles.Add(string.Format(
                        "{{\"cycle\":{0},\"score\":{1},\"zoneExists\":{2}}}",
                        i, scoreAfterEnter, zoneAlive ? "true" : "false"));
                }
            }
            finally
            {
                // Restore physics and rigidbody state
                Physics.autoSimulation = oldAutoSim;
                if (rb != null)
                    rb.isKinematic = wasKinematic;
            }

            int finalScore = manager.CurrentScore;
            bool finalZone = GameObject.Find("ScoreTriggerZone") != null;

            return string.Format(
                "{{\"success\":true,\"finalScore\":{0},\"zoneExists\":{1},\"cycles\":[{2}]}}",
                finalScore,
                finalZone ? "true" : "false",
                string.Join(",", cycles.ToArray()));
        }

        /// <summary>
        /// Bridge-callable: get current score state without modifying anything.
        /// </summary>
        public static string GetScoreState()
        {
            var manager = Game.Score.ScoreSystemSetup.RuntimeManager;
            if (manager == null)
                return "{\"success\":false,\"error\":\"ScoreManager not initialized\"}";

            bool zoneAlive = GameObject.Find("ScoreTriggerZone") != null;
            return string.Format(
                "{{\"success\":true,\"currentScore\":{0},\"zoneExists\":{1}}}",
                manager.CurrentScore,
                zoneAlive ? "true" : "false");
        }
    }
}
