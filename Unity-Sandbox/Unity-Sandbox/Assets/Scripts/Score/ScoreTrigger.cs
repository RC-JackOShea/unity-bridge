using UnityEngine;

namespace Game.Score
{
    /// <summary>
    /// Trigger zone that awards 1 point each time a Player-tagged object
    /// enters then exits. After maxUses cycles the zone destroys itself.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ScoreTrigger : MonoBehaviour
    {
        [SerializeField] private ScoreManager scoreManager;
        [SerializeField] private int pointValue = 1;
        [SerializeField] private int maxUses = 10;

        private int usesRemaining;
        private bool playerInside;

        private void Awake()
        {
            usesRemaining = maxUses;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;
            if (playerInside || usesRemaining <= 0)
                return;

            playerInside = true;
            usesRemaining--;

            if (scoreManager != null)
                scoreManager.AddScore(pointValue);

            Debug.Log(string.Format("[ScoreTrigger] +{0} point. Uses remaining: {1}", pointValue, usesRemaining));

            if (usesRemaining <= 0)
            {
                Debug.Log("[ScoreTrigger] All uses consumed — destroying zone");
                gameObject.SetActive(false); // Immediate visual removal
                Destroy(gameObject);         // Deferred cleanup
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;
            playerInside = false;
        }
    }
}
