using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Score
{
    /// <summary>
    /// Auto-creates the score system at runtime: ScoreManager instance,
    /// ScoreDisplay HUD in top-right corner, and an example trigger zone.
    /// </summary>
    public class ScoreSystemSetup : MonoBehaviour
    {
        private static ScoreManager runtimeManager;
        private static GameObject triggerZone;

        /// <summary>Accessor for bridge test harnesses. Game code should not use this.</summary>
        public static ScoreManager RuntimeManager => runtimeManager;

        private static readonly Vector3 TriggerPosition = new Vector3(3, 0.5f, 3);
        private static readonly Vector3 SafePosition = new Vector3(0, 1f, 0);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Setup()
        {
            try
            {
                runtimeManager = ScriptableObject.CreateInstance<ScoreManager>();
                runtimeManager.name = "RuntimeScoreManager";

                CreateScoreHUD(runtimeManager);
                triggerZone = CreateScoreTrigger(runtimeManager, TriggerPosition, 1, 10);
                CreateTestPlayer();

                Debug.Log("[ScoreSystem] Setup complete");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ScoreSystem] Setup failed: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static void CreateTestPlayer()
        {
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "TestPlayer";
            player.transform.position = SafePosition;

            try { player.tag = "Player"; }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ScoreSystem] Could not set Player tag: " + e.Message);
            }

            var rb = player.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        private static void CreateScoreHUD(ScoreManager manager)
        {
            var existingCanvas = Object.FindObjectOfType<Canvas>();
            Canvas canvas;
            if (existingCanvas != null)
            {
                canvas = existingCanvas;
            }
            else
            {
                var canvasGo = new GameObject("ScoreCanvas");
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 50;
                canvasGo.AddComponent<CanvasScaler>();
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            var scoreGo = new GameObject("ScoreDisplay");
            scoreGo.transform.SetParent(canvas.transform, false);

            var rt = scoreGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.7f, 0.9f);
            rt.anchorMax = new Vector2(0.98f, 0.98f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmpType = FindType("TMPro.TextMeshProUGUI");
            if (tmpType != null)
            {
                var tmp = scoreGo.AddComponent(tmpType);
                var textProp = tmpType.GetProperty("text");
                if (textProp != null) textProp.SetValue(tmp, "Score: 0");
                var fsProp = tmpType.GetProperty("fontSize");
                if (fsProp != null) fsProp.SetValue(tmp, 36f);
                var colorProp = tmpType.GetProperty("color");
                if (colorProp != null) colorProp.SetValue(tmp, Color.white);
                var alignProp = tmpType.GetProperty("alignment");
                if (alignProp != null)
                {
                    var alignType = FindType("TMPro.TextAlignmentOptions");
                    if (alignType != null)
                    {
                        var topRight = System.Enum.Parse(alignType, "TopRight");
                        alignProp.SetValue(tmp, topRight);
                    }
                }
            }
            else
            {
                var text = scoreGo.AddComponent<Text>();
                text.text = "Score: 0";
                text.fontSize = 36;
                text.color = Color.white;
                text.alignment = TextAnchor.UpperRight;
                text.font = Font.CreateDynamicFontFromOSFont("Arial", 36);
            }

            var display = scoreGo.AddComponent<Game.UI.ScoreDisplay>();
            var smField = typeof(Game.UI.ScoreDisplay).GetField("scoreManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (smField != null)
                smField.SetValue(display, manager);

            if (EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                var moduleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (moduleType != null)
                    esGo.AddComponent(moduleType);
                else
                    esGo.AddComponent<StandaloneInputModule>();
            }
        }

        private static GameObject CreateScoreTrigger(ScoreManager manager, Vector3 position, int points, int maxUses)
        {
            var go = new GameObject("ScoreTriggerZone");
            go.transform.position = position;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = Vector3.one * 0.8f;
            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(1f, 0.84f, 0f); // Gold

            var sphereCollider = visual.GetComponent<Collider>();
            if (sphereCollider != null) Object.Destroy(sphereCollider);

            var boxCollider = go.AddComponent<BoxCollider>();
            boxCollider.isTrigger = true;
            boxCollider.size = Vector3.one * 1.5f;

            var trigger = go.AddComponent<ScoreTrigger>();
            var smField = typeof(ScoreTrigger).GetField("scoreManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (smField != null)
                smField.SetValue(trigger, manager);
            var pvField = typeof(ScoreTrigger).GetField("pointValue",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (pvField != null)
                pvField.SetValue(trigger, points);
            var muField = typeof(ScoreTrigger).GetField("maxUses",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (muField != null)
                muField.SetValue(trigger, maxUses);

            return go;
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
