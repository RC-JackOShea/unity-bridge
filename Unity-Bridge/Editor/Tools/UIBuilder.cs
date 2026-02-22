using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityBridge
{
    /// <summary>
    /// JSON-driven UI hierarchy builder. Creates Canvas, Panel, Button, Text (TMP preferred),
    /// Image, RawImage, Toggle, Slider, InputField, Dropdown, ScrollView from JSON specs.
    /// Handles RectTransform anchoring, layout groups, visual properties, and event systems.
    /// BuildUI creates in the active scene; BuildUIPrefab saves as a prefab asset.
    /// </summary>
    public static class UIBuilder
    {
        public static string BuildUI(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"success\":false,\"error\":\"jsonSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonSpec);
                var summary = new List<string>();
                var root = BuildElement(spec, null, summary);

                if (root == null)
                    return "{\"success\":false,\"error\":\"Failed to build root element\"}";

                // Ensure EventSystem exists
                EnsureEventSystem();

                return string.Format("{{\"success\":true,\"rootName\":\"{0}\",\"hierarchy\":[{1}],\"elementCount\":{2}}}",
                    Esc(root.name), string.Join(",", summary.ToArray()), summary.Count);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string BuildUIPrefab(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"success\":false,\"error\":\"jsonSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonSpec);
                string prefabPath = spec.GetString("prefabPath") ?? "Assets/UI/GeneratedUI.prefab";

                var summary = new List<string>();
                var root = BuildElement(spec, null, summary);

                if (root == null)
                    return "{\"success\":false,\"error\":\"Failed to build root element\"}";

                // Ensure directory exists
                string dir = Path.GetDirectoryName(prefabPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    string fullDir = Path.Combine(Application.dataPath, "..", dir);
                    if (!Directory.Exists(fullDir))
                    {
                        // Create via AssetDatabase
                        string[] parts = dir.Replace("\\", "/").Split('/');
                        string current = parts[0]; // "Assets"
                        for (int i = 1; i < parts.Length; i++)
                        {
                            string next = current + "/" + parts[i];
                            if (!AssetDatabase.IsValidFolder(next))
                                AssetDatabase.CreateFolder(current, parts[i]);
                            current = next;
                        }
                    }
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                UnityEngine.Object.DestroyImmediate(root);

                return string.Format("{{\"success\":true,\"prefabPath\":\"{0}\",\"hierarchy\":[{1}],\"elementCount\":{2}}}",
                    Esc(prefabPath), string.Join(",", summary.ToArray()), summary.Count);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        private static GameObject BuildElement(SimpleJson.JsonNode spec, Transform parent, List<string> summary)
        {
            string type = spec.GetString("type") ?? "Panel";
            string name = spec.GetString("name") ?? type;

            GameObject go;

            switch (type.ToLowerInvariant())
            {
                case "canvas":
                    go = CreateCanvas(spec, name);
                    break;
                case "panel":
                    go = CreatePanel(name, parent);
                    break;
                case "button":
                    go = CreateButton(spec, name, parent);
                    break;
                case "text":
                case "textmeshpro":
                case "tmp":
                    go = CreateText(spec, name, parent);
                    break;
                case "image":
                    go = CreateImage(spec, name, parent);
                    break;
                case "rawimage":
                    go = CreateRawImage(spec, name, parent);
                    break;
                case "toggle":
                    go = CreateToggle(spec, name, parent);
                    break;
                case "slider":
                    go = CreateSlider(spec, name, parent);
                    break;
                case "inputfield":
                    go = CreateInputField(spec, name, parent);
                    break;
                case "dropdown":
                    go = CreateDropdown(spec, name, parent);
                    break;
                case "scrollview":
                    go = CreateScrollView(spec, name, parent);
                    break;
                default:
                    go = new GameObject(name);
                    go.AddComponent<RectTransform>();
                    if (parent != null) go.transform.SetParent(parent, false);
                    break;
            }

            if (go == null) return null;

            // Apply RectTransform settings
            ApplyRectTransform(go, spec);

            // Apply layout components
            ApplyLayoutGroup(go, spec);
            ApplyLayoutElement(go, spec);
            ApplyContentSizeFitter(go, spec);
            ApplyCanvasGroup(go, spec);

            // Apply visual properties
            ApplyImageColor(go, spec);

            summary.Add(string.Format("\"{0}:{1}\"", Esc(name), Esc(type)));

            // Process children
            var children = spec.Get("children");
            if (children != null && children.arr != null)
            {
                foreach (var child in children.arr)
                {
                    BuildElement(child, go.transform, summary);
                }
            }

            return go;
        }

        private static GameObject CreateCanvas(SimpleJson.JsonNode spec, string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            string renderMode = spec.GetString("renderMode") ?? "overlay";
            switch (renderMode.ToLowerInvariant())
            {
                case "camera": canvas.renderMode = RenderMode.ScreenSpaceCamera; break;
                case "worldspace": canvas.renderMode = RenderMode.WorldSpace; break;
            }

            go.AddComponent<GraphicRaycaster>();

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var resNode = spec.Get("referenceResolution");
            if (resNode != null)
            {
                float w = resNode.Get("x")?.AsFloat() ?? 1920;
                float h = resNode.Get("y")?.AsFloat() ?? 1080;
                scaler.referenceResolution = new Vector2(w, h);
            }

            return go;
        }

        private static GameObject CreatePanel(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = new Color(1, 1, 1, 0.1f);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject CreateButton(SimpleJson.JsonNode spec, string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            var btn = go.AddComponent<Button>();
            if (parent != null) go.transform.SetParent(parent, false);

            // Color block
            var colors = spec.Get("colorBlock");
            if (colors != null)
            {
                var cb = btn.colors;
                var normal = colors.Get("normal");
                if (normal != null) cb.normalColor = ParseColor(normal);
                var highlighted = colors.Get("highlighted");
                if (highlighted != null) cb.highlightedColor = ParseColor(highlighted);
                var pressed = colors.Get("pressed");
                if (pressed != null) cb.pressedColor = ParseColor(pressed);
                var selected = colors.Get("selected");
                if (selected != null) cb.selectedColor = ParseColor(selected);
                btn.colors = cb;
            }

            // Button text as child
            string text = spec.GetString("text");
            if (!string.IsNullOrEmpty(text))
            {
                var textGo = new GameObject("Text");
                textGo.transform.SetParent(go.transform, false);
                var rect = textGo.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                SetupText(textGo, text, spec);
            }

            return go;
        }

        private static GameObject CreateText(SimpleJson.JsonNode spec, string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            if (parent != null) go.transform.SetParent(parent, false);

            string content = spec.GetString("text") ?? spec.GetString("content") ?? "";
            SetupText(go, content, spec);
            return go;
        }

        private static GameObject CreateImage(SimpleJson.JsonNode spec, string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            if (parent != null) go.transform.SetParent(parent, false);

            var colorNode = spec.Get("color");
            if (colorNode != null) img.color = ParseColor(colorNode);

            string spritePath = spec.GetString("sprite");
            if (!string.IsNullOrEmpty(spritePath))
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sprite != null) img.sprite = sprite;
            }

            return go;
        }

        private static GameObject CreateRawImage(SimpleJson.JsonNode spec, string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            go.AddComponent<RawImage>();
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject CreateToggle(SimpleJson.JsonNode spec, string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            var toggle = go.AddComponent<Toggle>();
            go.AddComponent<Image>();
            if (parent != null) go.transform.SetParent(parent, false);

            // Checkmark child
            var checkGo = new GameObject("Checkmark");
            checkGo.transform.SetParent(go.transform, false);
            var checkRect = checkGo.AddComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.1f, 0.1f);
            checkRect.anchorMax = new Vector2(0.9f, 0.9f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = new Color(0.2f, 0.8f, 0.2f);
            toggle.graphic = checkImg;

            return go;
        }

        private static GameObject CreateSlider(SimpleJson.JsonNode spec, string name, Transform parent)
        {
            // Use DefaultControls for proper slider setup
            var resources = new DefaultControls.Resources();
            var go = DefaultControls.CreateSlider(resources);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);

            var slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                var minNode = spec.Get("minValue");
                if (minNode != null) slider.minValue = minNode.AsFloat();
                var maxNode = spec.Get("maxValue");
                if (maxNode != null) slider.maxValue = maxNode.AsFloat();
                var valNode = spec.Get("value");
                if (valNode != null) slider.value = valNode.AsFloat();
            }

            return go;
        }

        private static GameObject CreateInputField(SimpleJson.JsonNode spec, string name, Transform parent)
        {
            // Try TMP InputField first
            Type tmpInputType = FindType("TMPro.TMP_InputField");
            if (tmpInputType != null)
            {
                var go = new GameObject(name);
                go.AddComponent<RectTransform>();
                go.AddComponent<Image>();
                var inputField = go.AddComponent(tmpInputType);
                if (parent != null) go.transform.SetParent(parent, false);

                // Text area child
                var textArea = new GameObject("Text Area");
                textArea.transform.SetParent(go.transform, false);
                var taRect = textArea.AddComponent<RectTransform>();
                taRect.anchorMin = Vector2.zero;
                taRect.anchorMax = Vector2.one;
                taRect.offsetMin = new Vector2(10, 6);
                taRect.offsetMax = new Vector2(-10, -7);
                textArea.AddComponent<RectMask2D>();

                // Placeholder
                var placeholder = new GameObject("Placeholder");
                placeholder.transform.SetParent(textArea.transform, false);
                var phRect = placeholder.AddComponent<RectTransform>();
                phRect.anchorMin = Vector2.zero;
                phRect.anchorMax = Vector2.one;
                phRect.offsetMin = Vector2.zero;
                phRect.offsetMax = Vector2.zero;
                SetupText(placeholder, spec.GetString("placeholder") ?? "Enter text...", spec);

                return go;
            }

            // Fallback to legacy InputField
            var resources = new DefaultControls.Resources();
            var goFallback = DefaultControls.CreateInputField(resources);
            goFallback.name = name;
            if (parent != null) goFallback.transform.SetParent(parent, false);
            return goFallback;
        }

        private static GameObject CreateDropdown(SimpleJson.JsonNode spec, string name, Transform parent)
        {
            // Try TMP dropdown first
            Type tmpDropdownType = FindType("TMPro.TMP_Dropdown");
            if (tmpDropdownType != null)
            {
                var go = new GameObject(name);
                go.AddComponent<RectTransform>();
                go.AddComponent<Image>();
                var dropdown = go.AddComponent(tmpDropdownType);
                if (parent != null) go.transform.SetParent(parent, false);

                // Add options via reflection
                var options = spec.Get("options");
                if (options != null && options.arr != null)
                {
                    var optionDataType = FindType("TMPro.TMP_Dropdown+OptionData");
                    if (optionDataType != null)
                    {
                        var optionsList = dropdown.GetType().GetProperty("options");
                        if (optionsList != null)
                        {
                            var list = optionsList.GetValue(dropdown) as System.Collections.IList;
                            if (list != null)
                            {
                                foreach (var opt in options.arr)
                                {
                                    var optData = Activator.CreateInstance(optionDataType, new object[] { opt.AsString() });
                                    list.Add(optData);
                                }
                            }
                        }
                    }
                }

                return go;
            }

            var resources = new DefaultControls.Resources();
            var goFallback = DefaultControls.CreateDropdown(resources);
            goFallback.name = name;
            if (parent != null) goFallback.transform.SetParent(parent, false);
            return goFallback;
        }

        private static GameObject CreateScrollView(SimpleJson.JsonNode spec, string name, Transform parent)
        {
            var resources = new DefaultControls.Resources();
            var go = DefaultControls.CreateScrollView(resources);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static void SetupText(GameObject go, string content, SimpleJson.JsonNode spec)
        {
            // Try TMP first
            Type tmpType = FindType("TMPro.TextMeshProUGUI");
            if (tmpType != null)
            {
                var comp = go.GetComponent(tmpType) ?? go.AddComponent(tmpType);

                // Set text
                var textProp = tmpType.GetProperty("text");
                if (textProp != null) textProp.SetValue(comp, content);

                // Font size
                var fontSizeNode = spec.Get("fontSize");
                if (fontSizeNode != null)
                {
                    var fsProp = tmpType.GetProperty("fontSize");
                    if (fsProp != null) fsProp.SetValue(comp, fontSizeNode.AsFloat());
                }

                // Color
                var textColorNode = spec.Get("textColor") ?? spec.Get("fontColor");
                if (textColorNode != null)
                {
                    var colorProp = tmpType.GetProperty("color");
                    if (colorProp != null) colorProp.SetValue(comp, ParseColor(textColorNode));
                }

                // Alignment via reflection
                var alignNode = spec.Get("alignment");
                if (alignNode != null)
                {
                    var alignType = FindType("TMPro.TextAlignmentOptions");
                    if (alignType != null)
                    {
                        try
                        {
                            object alignVal = Enum.Parse(alignType, alignNode.AsString(), true);
                            var alignProp = tmpType.GetProperty("alignment");
                            if (alignProp != null) alignProp.SetValue(comp, alignVal);
                        }
                        catch { }
                    }
                }

                return;
            }

            // Fallback to legacy Text
            var text = go.GetComponent<Text>() ?? go.AddComponent<Text>();
            text.text = content;

            var fsNode = spec.Get("fontSize");
            if (fsNode != null) text.fontSize = (int)fsNode.AsFloat();

            var tcNode = spec.Get("textColor") ?? spec.Get("fontColor");
            if (tcNode != null) text.color = ParseColor(tcNode);
        }

        private static void ApplyRectTransform(GameObject go, SimpleJson.JsonNode spec)
        {
            var rect = go.GetComponent<RectTransform>();
            if (rect == null) return;

            var anchorMin = spec.Get("anchorMin");
            if (anchorMin != null)
                rect.anchorMin = new Vector2(anchorMin.Get("x")?.AsFloat() ?? 0, anchorMin.Get("y")?.AsFloat() ?? 0);

            var anchorMax = spec.Get("anchorMax");
            if (anchorMax != null)
                rect.anchorMax = new Vector2(anchorMax.Get("x")?.AsFloat() ?? 1, anchorMax.Get("y")?.AsFloat() ?? 1);

            var pivot = spec.Get("pivot");
            if (pivot != null)
                rect.pivot = new Vector2(pivot.Get("x")?.AsFloat() ?? 0.5f, pivot.Get("y")?.AsFloat() ?? 0.5f);

            var anchoredPosition = spec.Get("anchoredPosition");
            if (anchoredPosition != null)
                rect.anchoredPosition = new Vector2(
                    anchoredPosition.Get("x")?.AsFloat() ?? 0,
                    anchoredPosition.Get("y")?.AsFloat() ?? 0);

            var sizeDelta = spec.Get("sizeDelta");
            if (sizeDelta != null)
                rect.sizeDelta = new Vector2(
                    sizeDelta.Get("x")?.AsFloat() ?? 100,
                    sizeDelta.Get("y")?.AsFloat() ?? 100);
        }

        private static void ApplyLayoutGroup(GameObject go, SimpleJson.JsonNode spec)
        {
            var layout = spec.Get("layoutGroup");
            if (layout == null) return;

            string layoutType = layout.GetString("type") ?? "vertical";
            float spacing = layout.Get("spacing")?.AsFloat() ?? 0;

            var padding = layout.Get("padding");
            int padLeft = 0, padRight = 0, padTop = 0, padBottom = 0;
            if (padding != null)
            {
                padLeft = (int)(padding.Get("left")?.AsFloat() ?? 0);
                padRight = (int)(padding.Get("right")?.AsFloat() ?? 0);
                padTop = (int)(padding.Get("top")?.AsFloat() ?? 0);
                padBottom = (int)(padding.Get("bottom")?.AsFloat() ?? 0);
            }

            switch (layoutType.ToLowerInvariant())
            {
                case "horizontal":
                {
                    var hlg = go.AddComponent<HorizontalLayoutGroup>();
                    hlg.spacing = spacing;
                    hlg.padding = new RectOffset(padLeft, padRight, padTop, padBottom);
                    ApplyChildAlignment(hlg, layout);
                    break;
                }
                case "vertical":
                {
                    var vlg = go.AddComponent<VerticalLayoutGroup>();
                    vlg.spacing = spacing;
                    vlg.padding = new RectOffset(padLeft, padRight, padTop, padBottom);
                    ApplyChildAlignment(vlg, layout);
                    break;
                }
                case "grid":
                {
                    var glg = go.AddComponent<GridLayoutGroup>();
                    glg.spacing = new Vector2(spacing, spacing);
                    glg.padding = new RectOffset(padLeft, padRight, padTop, padBottom);
                    var cellSize = layout.Get("cellSize");
                    if (cellSize != null)
                        glg.cellSize = new Vector2(cellSize.Get("x")?.AsFloat() ?? 100, cellSize.Get("y")?.AsFloat() ?? 100);
                    break;
                }
            }
        }

        private static void ApplyChildAlignment(HorizontalOrVerticalLayoutGroup lg, SimpleJson.JsonNode layout)
        {
            string align = layout.GetString("childAlignment");
            if (string.IsNullOrEmpty(align)) return;
            try
            {
                lg.childAlignment = (TextAnchor)Enum.Parse(typeof(TextAnchor), align, true);
            }
            catch { }
        }

        private static void ApplyLayoutElement(GameObject go, SimpleJson.JsonNode spec)
        {
            var le = spec.Get("layoutElement");
            if (le == null) return;

            var comp = go.AddComponent<LayoutElement>();
            var minW = le.Get("minWidth");
            if (minW != null) comp.minWidth = minW.AsFloat();
            var minH = le.Get("minHeight");
            if (minH != null) comp.minHeight = minH.AsFloat();
            var prefW = le.Get("preferredWidth");
            if (prefW != null) comp.preferredWidth = prefW.AsFloat();
            var prefH = le.Get("preferredHeight");
            if (prefH != null) comp.preferredHeight = prefH.AsFloat();
            var flexW = le.Get("flexibleWidth");
            if (flexW != null) comp.flexibleWidth = flexW.AsFloat();
            var flexH = le.Get("flexibleHeight");
            if (flexH != null) comp.flexibleHeight = flexH.AsFloat();
        }

        private static void ApplyContentSizeFitter(GameObject go, SimpleJson.JsonNode spec)
        {
            var csf = spec.Get("contentSizeFitter");
            if (csf == null) return;

            var comp = go.AddComponent<ContentSizeFitter>();
            string hFit = csf.GetString("horizontalFit");
            string vFit = csf.GetString("verticalFit");
            if (!string.IsNullOrEmpty(hFit))
            {
                try { comp.horizontalFit = (ContentSizeFitter.FitMode)Enum.Parse(typeof(ContentSizeFitter.FitMode), hFit, true); }
                catch { }
            }
            if (!string.IsNullOrEmpty(vFit))
            {
                try { comp.verticalFit = (ContentSizeFitter.FitMode)Enum.Parse(typeof(ContentSizeFitter.FitMode), vFit, true); }
                catch { }
            }
        }

        private static void ApplyCanvasGroup(GameObject go, SimpleJson.JsonNode spec)
        {
            var cg = spec.Get("canvasGroup");
            if (cg == null) return;

            var comp = go.AddComponent<CanvasGroup>();
            var alpha = cg.Get("alpha");
            if (alpha != null) comp.alpha = alpha.AsFloat();
            var interactable = cg.Get("interactable");
            if (interactable != null) comp.interactable = interactable.AsBool();
            var blocksRaycasts = cg.Get("blocksRaycasts");
            if (blocksRaycasts != null) comp.blocksRaycasts = blocksRaycasts.AsBool();
        }

        private static void ApplyImageColor(GameObject go, SimpleJson.JsonNode spec)
        {
            var colorNode = spec.Get("color");
            if (colorNode == null) return;

            var img = go.GetComponent<Image>();
            if (img != null)
                img.color = ParseColor(colorNode);
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null) return;

            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();

            // Try InputSystemUIInputModule first (new Input System)
            Type inputModuleType = FindType("UnityEngine.InputSystem.UI.InputSystemUIInputModule");
            if (inputModuleType != null)
            {
                esGo.AddComponent(inputModuleType);
            }
            else
            {
                esGo.AddComponent<StandaloneInputModule>();
            }
        }

        private static Color ParseColor(SimpleJson.JsonNode node)
        {
            // Support "#RRGGBB" or "#RRGGBBAA" hex strings
            string hex = node.AsString();
            if (!string.IsNullOrEmpty(hex) && hex.StartsWith("#"))
            {
                Color color;
                if (ColorUtility.TryParseHtmlString(hex, out color))
                    return color;
            }

            // Support {r, g, b, a} object (0-1 range)
            var r = node.Get("r");
            if (r != null)
            {
                return new Color(
                    r.AsFloat(),
                    node.Get("g")?.AsFloat() ?? 0,
                    node.Get("b")?.AsFloat() ?? 0,
                    node.Get("a")?.AsFloat() ?? 1);
            }

            return Color.white;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
