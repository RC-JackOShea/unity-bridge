using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Spawns a Canvas with a Button in the bottom-left corner at runtime.
/// Clicking the button logs "You Clicked Me!" to the console.
/// Uses InputSystemUIInputModule so the bridge's virtual Touchscreen can trigger clicks.
/// </summary>
public static class SpawnClickableButton
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnSceneLoaded()
    {
        // --- EventSystem with Input System UI module ---
        var existingES = Object.FindFirstObjectByType<EventSystem>();
        if (existingES == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();
        }
        else
        {
            // Ensure the existing EventSystem uses the new Input System module
            if (existingES.GetComponent<InputSystemUIInputModule>() == null)
            {
                // Remove legacy module if present
                var legacy = existingES.GetComponent<StandaloneInputModule>();
                if (legacy != null) Object.Destroy(legacy);
                existingES.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        // --- Canvas ---
        var cam = Camera.main;

        var canvasGo = new GameObject("TestCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // --- Button ---
        var btnGo = new GameObject("ClickMeButton");
        btnGo.transform.SetParent(canvasGo.transform, false);

        var btnImage = btnGo.AddComponent<Image>();
        btnImage.color = new Color(0.2f, 0.6f, 1f, 1f); // bright blue

        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnImage;

        // Position: bottom-left corner with some padding
        var rect = btnGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(20f, 20f);
        rect.sizeDelta = new Vector2(200f, 80f);

        // --- Label ---
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(btnGo.transform, false);

        var text = labelGo.AddComponent<Text>();
        text.text = "Click Me";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 28;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;

        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        // --- Click handler ---
        btn.onClick.AddListener(() =>
        {
            Debug.Log("You Clicked Me!");
        });

        Debug.Log("[SpawnClickableButton] Button created in bottom-left corner.");
    }
}
