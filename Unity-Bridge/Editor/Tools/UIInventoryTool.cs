using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityBridge
{
    /// <summary>
    /// Scans all canvases in a scene and outputs the complete UI tree as JSON.
    /// Includes RectTransform, visual properties, layout groups, and interaction bindings.
    /// </summary>
    public static class UIInventoryTool
    {
        private const int MAX_DEPTH = 50;

        public static string GetUIInventory(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return "{\"error\":\"scenePath is required\"}";

            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (asset == null)
                return "{\"error\":\"Scene not found: " + Esc(scenePath) + "\"}";

            Scene scene = default;
            bool wasAlreadyLoaded = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == scenePath) { scene = s; wasAlreadyLoaded = true; break; }
            }

            try
            {
                if (!wasAlreadyLoaded)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                if (!scene.IsValid() || !scene.isLoaded)
                    return "{\"error\":\"Failed to open scene\"}";

                var roots = scene.GetRootGameObjects();
                var canvasEntries = new List<string>();

                foreach (var root in roots)
                {
                    var canvases = root.GetComponentsInChildren<Canvas>(true);
                    foreach (var canvas in canvases)
                    {
                        if (canvas.transform.parent != null)
                        {
                            var parentCanvas = canvas.transform.parent.GetComponentInParent<Canvas>();
                            if (parentCanvas != null) continue; // Skip nested canvases, they're children
                        }
                        canvasEntries.Add(SerializeCanvas(canvas));
                    }
                }

                return string.Format(
                    "{{\"scenePath\":\"{0}\",\"canvases\":[{1}]}}",
                    Esc(scenePath), string.Join(",", canvasEntries.ToArray())
                );
            }
            finally
            {
                if (!wasAlreadyLoaded && scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static string SerializeCanvas(Canvas canvas)
        {
            string renderMode = canvas.renderMode.ToString();
            bool hasRaycaster = canvas.GetComponent<GraphicRaycaster>() != null;
            string refCamera = "null";
            if (canvas.worldCamera != null)
                refCamera = "\"" + Esc(canvas.worldCamera.name) + "\"";

            var children = new List<string>();
            foreach (Transform child in canvas.transform)
                children.Add(SerializeUIElement(child.gameObject, 0));

            return string.Format(
                "{{\"name\":\"{0}\",\"renderMode\":\"{1}\",\"sortOrder\":{2},\"referenceCamera\":{3},\"hasGraphicRaycaster\":{4},\"children\":[{5}]}}",
                Esc(canvas.gameObject.name), Esc(renderMode), canvas.sortingOrder,
                refCamera, hasRaycaster ? "true" : "false",
                string.Join(",", children.ToArray())
            );
        }

        private static string SerializeUIElement(GameObject go, int depth)
        {
            if (depth > MAX_DEPTH) return "{\"name\":\"[MAX_DEPTH]\"}";

            var sb = new System.Text.StringBuilder();
            sb.Append("{\"name\":\"").Append(Esc(go.name)).Append("\"");
            sb.Append(",\"activeSelf\":").Append(go.activeSelf ? "true" : "false");

            // RectTransform
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    ",\"rectTransform\":{{\"anchorMin\":{{\"x\":{0},\"y\":{1}}},\"anchorMax\":{{\"x\":{2},\"y\":{3}}},\"pivot\":{{\"x\":{4},\"y\":{5}}},\"anchoredPosition\":{{\"x\":{6},\"y\":{7}}},\"sizeDelta\":{{\"x\":{8},\"y\":{9}}}}}",
                    rt.anchorMin.x, rt.anchorMin.y, rt.anchorMax.x, rt.anchorMax.y,
                    rt.pivot.x, rt.pivot.y, rt.anchoredPosition.x, rt.anchoredPosition.y,
                    rt.sizeDelta.x, rt.sizeDelta.y);
            }

            // Image
            var img = go.GetComponent<Image>();
            if (img != null)
            {
                string spriteName = img.sprite != null ? Esc(img.sprite.name) : "null";
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    ",\"image\":{{\"color\":\"#{0}\",\"sprite\":\"{1}\",\"type\":\"{2}\",\"raycastTarget\":{3}}}",
                    ColorUtility.ToHtmlStringRGBA(img.color), spriteName, img.type.ToString(),
                    img.raycastTarget ? "true" : "false");
            }

            // Text (legacy)
            var text = go.GetComponent<Text>();
            if (text != null)
            {
                sb.AppendFormat(",\"text\":{{\"content\":\"{0}\",\"fontSize\":{1},\"color\":\"#{2}\",\"alignment\":\"{3}\"}}",
                    Esc(text.text), text.fontSize,
                    ColorUtility.ToHtmlStringRGBA(text.color), text.alignment.ToString());
            }

            // TMP_Text (via reflection to avoid hard dependency)
            SerializeTMP(go, sb);

            // Layout groups
            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                sb.AppendFormat(",\"layoutGroup\":{{\"type\":\"Horizontal\",\"spacing\":{0},\"childAlignment\":\"{1}\"}}",
                    hlg.spacing, hlg.childAlignment.ToString());
            }
            var vlg = go.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                sb.AppendFormat(",\"layoutGroup\":{{\"type\":\"Vertical\",\"spacing\":{0},\"childAlignment\":\"{1}\"}}",
                    vlg.spacing, vlg.childAlignment.ToString());
            }
            var glg = go.GetComponent<GridLayoutGroup>();
            if (glg != null)
            {
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    ",\"layoutGroup\":{{\"type\":\"Grid\",\"cellSize\":{{\"x\":{0},\"y\":{1}}},\"spacing\":{{\"x\":{2},\"y\":{3}}}}}",
                    glg.cellSize.x, glg.cellSize.y, glg.spacing.x, glg.spacing.y);
            }

            // ContentSizeFitter
            var csf = go.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                sb.AppendFormat(",\"contentSizeFitter\":{{\"horizontal\":\"{0}\",\"vertical\":\"{1}\"}}",
                    csf.horizontalFit.ToString(), csf.verticalFit.ToString());
            }

            // LayoutElement
            var le = go.GetComponent<LayoutElement>();
            if (le != null)
            {
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    ",\"layoutElement\":{{\"minWidth\":{0},\"minHeight\":{1},\"preferredWidth\":{2},\"preferredHeight\":{3},\"flexibleWidth\":{4},\"flexibleHeight\":{5}}}",
                    le.minWidth, le.minHeight, le.preferredWidth, le.preferredHeight,
                    le.flexibleWidth, le.flexibleHeight);
            }

            // CanvasGroup
            var cg = go.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    ",\"canvasGroup\":{{\"alpha\":{0},\"interactable\":{1},\"blocksRaycasts\":{2}}}",
                    cg.alpha, cg.interactable ? "true" : "false", cg.blocksRaycasts ? "true" : "false");
            }

            // Interactive elements
            SerializeInteraction(go, sb);

            // Children
            var childEntries = new List<string>();
            foreach (Transform child in go.transform)
                childEntries.Add(SerializeUIElement(child.gameObject, depth + 1));

            if (childEntries.Count > 0)
                sb.Append(",\"children\":[").Append(string.Join(",", childEntries.ToArray())).Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        private static void SerializeInteraction(GameObject go, System.Text.StringBuilder sb)
        {
            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                var listeners = ExtractListeners(btn.onClick);
                sb.AppendFormat(",\"interaction\":{{\"type\":\"Button\",\"interactable\":{0},\"listeners\":[{1}]}}",
                    btn.interactable ? "true" : "false", string.Join(",", listeners.ToArray()));
                return;
            }

            var toggle = go.GetComponent<Toggle>();
            if (toggle != null)
            {
                sb.AppendFormat(",\"interaction\":{{\"type\":\"Toggle\",\"isOn\":{0},\"interactable\":{1}}}",
                    toggle.isOn ? "true" : "false", toggle.interactable ? "true" : "false");
                return;
            }

            var slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    ",\"interaction\":{{\"type\":\"Slider\",\"value\":{0},\"minValue\":{1},\"maxValue\":{2}}}",
                    slider.value, slider.minValue, slider.maxValue);
                return;
            }

            var input = go.GetComponent<InputField>();
            if (input != null)
            {
                sb.AppendFormat(",\"interaction\":{{\"type\":\"InputField\",\"text\":\"{0}\",\"placeholder\":\"{1}\"}}",
                    Esc(input.text), input.placeholder != null ? Esc(input.placeholder.GetComponent<Text>()?.text ?? "") : "");
                return;
            }
        }

        private static List<string> ExtractListeners(UnityEventBase evt)
        {
            var result = new List<string>();
            int count = evt.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                string target = evt.GetPersistentTarget(i)?.name ?? "null";
                string method = evt.GetPersistentMethodName(i) ?? "null";
                result.Add(string.Format("{{\"target\":\"{0}\",\"method\":\"{1}\"}}", Esc(target), Esc(method)));
            }
            return result;
        }

        private static void SerializeTMP(GameObject go, System.Text.StringBuilder sb)
        {
            // Use reflection to check for TMP_Text without requiring TMPro assembly reference
            var comp = go.GetComponent<Component>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var typeName = c.GetType().FullName;
                if (typeName != null && typeName.Contains("TMPro.TMP_Text") || typeName != null && typeName.Contains("TMPro.TextMeshProUGUI"))
                {
                    try
                    {
                        var textProp = c.GetType().GetProperty("text");
                        var fontSizeProp = c.GetType().GetProperty("fontSize");
                        var colorProp = c.GetType().GetProperty("color");
                        string content = textProp?.GetValue(c)?.ToString() ?? "";
                        float fontSize = fontSizeProp != null ? (float)fontSizeProp.GetValue(c) : 0;
                        Color color = colorProp != null ? (Color)colorProp.GetValue(c) : Color.white;
                        sb.AppendFormat(",\"tmpText\":{{\"content\":\"{0}\",\"fontSize\":{1},\"color\":\"#{2}\"}}",
                            Esc(content), fontSize, ColorUtility.ToHtmlStringRGBA(color));
                    }
                    catch { }
                    break;
                }
            }
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
