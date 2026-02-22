using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.UI
{
    /// <summary>
    /// Instantiates the PauseMenu prefab at runtime and wires up the controller.
    /// Attach to an empty GameObject or use RuntimeInitializeOnLoadMethod.
    /// </summary>
    public class PauseMenuSetup : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Setup()
        {
            // Load and instantiate prefab
            var prefab = Resources.Load<GameObject>("PauseMenu");
            if (prefab == null)
            {
                // Try loading from AssetDatabase path via direct instantiation
                Debug.Log("[PauseMenu] Prefab not in Resources, creating from scratch");
                CreatePauseMenuFromCode();
                return;
            }

            var instance = Instantiate(prefab);
            instance.name = "PauseMenu";
            WireController(instance);
        }

        private static void CreatePauseMenuFromCode()
        {
            // Create Canvas
            var canvasGo = new GameObject("PauseCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // Create overlay
            var overlay = new GameObject("Overlay");
            overlay.transform.SetParent(canvasGo.transform, false);
            var overlayImg = overlay.AddComponent<UnityEngine.UI.Image>();
            overlayImg.color = new Color(0, 0, 0, 0.6f);
            var overlayRT = overlay.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            // Create menu panel
            var panel = new GameObject("MenuPanel");
            panel.transform.SetParent(overlay.transform, false);
            var panelImg = panel.AddComponent<UnityEngine.UI.Image>();
            panelImg.color = new Color(0.176f, 0.176f, 0.176f, 1f);
            var panelRT = panel.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.3f, 0.25f);
            panelRT.anchorMax = new Vector2(0.7f, 0.75f);
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;

            // Title
            CreateTMPText(panel.transform, "TitleText", "PAUSED", 48,
                new Vector2(0.1f, 0.75f), new Vector2(0.9f, 0.95f));

            // Buttons
            CreateButton(panel.transform, "ResumeButton", "Resume",
                new Color(0.298f, 0.686f, 0.314f), new Vector2(0.2f, 0.55f), new Vector2(0.8f, 0.7f));
            CreateButton(panel.transform, "SettingsButton", "Settings",
                new Color(0.129f, 0.588f, 0.953f), new Vector2(0.2f, 0.35f), new Vector2(0.8f, 0.5f));
            CreateButton(panel.transform, "QuitButton", "Quit",
                new Color(0.957f, 0.263f, 0.212f), new Vector2(0.2f, 0.15f), new Vector2(0.8f, 0.3f));

            // Ensure EventSystem
            if (EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                // Try new Input System module first
                var moduleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (moduleType != null)
                    esGo.AddComponent(moduleType);
                else
                    esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // Add controller
            var controller = canvasGo.AddComponent<PauseMenuController>();

            // Wire menuPanel reference via reflection (it's a private serialized field)
            var field = typeof(PauseMenuController).GetField("menuPanel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
                field.SetValue(controller, overlay);

            // Wire button click events
            var resumeBtn = canvasGo.transform.Find("Overlay/MenuPanel/ResumeButton");
            if (resumeBtn != null)
            {
                var btn = resumeBtn.GetComponent<UnityEngine.UI.Button>();
                if (btn != null)
                    btn.onClick.AddListener(controller.OnResumeClicked);
            }

            var settingsBtn = canvasGo.transform.Find("Overlay/MenuPanel/SettingsButton");
            if (settingsBtn != null)
            {
                var btn = settingsBtn.GetComponent<UnityEngine.UI.Button>();
                if (btn != null)
                    btn.onClick.AddListener(controller.OnSettingsClicked);
            }

            var quitBtn = canvasGo.transform.Find("Overlay/MenuPanel/QuitButton");
            if (quitBtn != null)
            {
                var btn = quitBtn.GetComponent<UnityEngine.UI.Button>();
                if (btn != null)
                    btn.onClick.AddListener(controller.OnQuitClicked);
            }

            // Start hidden
            overlay.SetActive(false);

            Debug.Log("[PauseMenu] Created and configured from code");
        }

        private static void WireController(GameObject instance)
        {
            var controller = instance.GetComponentInChildren<PauseMenuController>();
            if (controller == null)
                controller = instance.AddComponent<PauseMenuController>();

            Debug.Log("[PauseMenu] Instantiated from prefab");
        }

        private static void CreateTMPText(Transform parent, string name, string text, int fontSize,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // Try TMP first
            var tmpType = FindType("TMPro.TextMeshProUGUI");
            if (tmpType != null)
            {
                var tmp = go.AddComponent(tmpType);
                var textProp = tmpType.GetProperty("text");
                if (textProp != null) textProp.SetValue(tmp, text);
                var fsProp = tmpType.GetProperty("fontSize");
                if (fsProp != null) fsProp.SetValue(tmp, (float)fontSize);
                var colorProp = tmpType.GetProperty("color");
                if (colorProp != null) colorProp.SetValue(tmp, Color.white);
                var alignProp = tmpType.GetProperty("alignment");
                if (alignProp != null)
                {
                    var alignType = FindType("TMPro.TextAlignmentOptions");
                    if (alignType != null)
                    {
                        var centerVal = System.Enum.Parse(alignType, "Center");
                        alignProp.SetValue(tmp, centerVal);
                    }
                }
            }
            else
            {
                var uiText = go.AddComponent<UnityEngine.UI.Text>();
                uiText.text = text;
                uiText.fontSize = fontSize;
                uiText.color = Color.white;
                uiText.alignment = TextAnchor.MiddleCenter;
                uiText.font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
            }

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void CreateButton(Transform parent, string name, string label,
            Color bgColor, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = bgColor;
            go.AddComponent<UnityEngine.UI.Button>();

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Button label
            CreateTMPText(go.transform, name + "Text", label, 24,
                Vector2.zero, Vector2.one);
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
