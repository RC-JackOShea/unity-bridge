using UnityEngine;

namespace Game.Score
{
    /// <summary>
    /// Trigger zone that adds points to the ScoreManager when a Player-tagged
    /// object enters, then destroys itself.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ScoreTrigger : MonoBehaviour
    {
        [SerializeField] private ScoreManager scoreManager;
        [SerializeField] private int pointValue = 10;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            if (scoreManager != null)
                scoreManager.AddScore(pointValue);

            Destroy(gameObject);
        }
    }
}
