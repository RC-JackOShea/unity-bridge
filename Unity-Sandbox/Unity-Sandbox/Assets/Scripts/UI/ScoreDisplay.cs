using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Displays the current score in the top-right corner of the screen.
    /// Subscribes to ScoreManager.onScoreChanged to update the text.
    /// Uses TMP via reflection to avoid hard compile dependency.
    /// </summary>
    public class ScoreDisplay : MonoBehaviour
    {
        [SerializeField] private Game.Score.ScoreManager scoreManager;

        private object tmpComponent;
        private System.Reflection.PropertyInfo textProperty;
        private bool subscribed;

        private void Awake()
        {
            // Find TMP component via reflection
            var tmpType = FindType("TMPro.TextMeshProUGUI");
            if (tmpType != null)
            {
                tmpComponent = GetComponent(tmpType);
                if (tmpComponent != null)
                    textProperty = tmpType.GetProperty("text");
            }

            // Fallback to legacy Text
            if (tmpComponent == null)
            {
                var legacyText = GetComponent<UnityEngine.UI.Text>();
                if (legacyText != null)
                {
                    tmpComponent = legacyText;
                    textProperty = typeof(UnityEngine.UI.Text).GetProperty("text");
                }
            }

            UpdateDisplay(0);
        }

        // Start runs after all AddComponent + reflection field injection is complete,
        // so scoreManager is guaranteed to be set by this point.
        private void Start()
        {
            Subscribe();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (!subscribed && scoreManager != null)
            {
                scoreManager.onScoreChanged += UpdateDisplay;
                subscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (subscribed && scoreManager != null)
            {
                scoreManager.onScoreChanged -= UpdateDisplay;
                subscribed = false;
            }
        }

        private void UpdateDisplay(int score)
        {
            if (textProperty != null && tmpComponent != null)
                textProperty.SetValue(tmpComponent, "Score: " + score);
        }

        private static System.Type FindType(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }
    }
}
