using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityBridge
{
    /// <summary>
    /// Brand/style management system. Stores brand specs (colors, fonts, spacing),
    /// extracts brand from existing scenes via frequency analysis, applies brand to
    /// all UI elements, and validates WCAG color contrast. Brand specs are persisted
    /// as JSON at Assets/Config/brand-spec.json.
    /// </summary>
    public static class BrandSystem
    {
        private static SimpleJson.JsonNode currentSpec;
        private static string specPath = "Assets/Config/brand-spec.json";

        public static string SetBrandSpec(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"success\":false,\"error\":\"jsonSpec is required\"}";

            try
            {
                currentSpec = SimpleJson.Parse(jsonSpec);

                // Persist to file
                string fullPath = Path.Combine(Application.dataPath, "..", specPath);
                string dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, jsonSpec);

                return "{\"success\":true,\"message\":\"Brand spec saved\",\"path\":\"" + Esc(specPath) + "\"}";
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string GetBrandSpec()
        {
            try
            {
                // Always read from file for accurate serialization
                string fullPath = Path.Combine(Application.dataPath, "..", specPath);
                if (File.Exists(fullPath))
                {
                    string content = File.ReadAllText(fullPath);
                    currentSpec = SimpleJson.Parse(content);
                    return "{\"success\":true,\"spec\":" + content + "}";
                }

                return "{\"success\":false,\"error\":\"No brand spec set. Use SetBrandSpec to define one.\"}";
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string ExtractBrandFromScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return "{\"success\":false,\"error\":\"scenePath is required\"}";

            try
            {
                // Open scene
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                var colorFreq = new Dictionary<string, int>();
                var fontFreq = new Dictionary<string, int>();
                var fontSizes = new List<float>();
                var spacingValues = new List<float>();
                int elementsAnalyzed = 0;

                var rootObjects = scene.GetRootGameObjects();
                foreach (var root in rootObjects)
                {
                    AnalyzeUIHierarchy(root.transform, colorFreq, fontFreq, fontSizes, spacingValues, ref elementsAnalyzed);
                }

                // Close additive scene
                EditorSceneManager.CloseScene(scene, true);

                // Find most common colors
                var sortedColors = SortByFrequency(colorFreq);
                string primaryColor = sortedColors.Count > 0 ? sortedColors[0] : "#1A73E8";
                string secondaryColor = sortedColors.Count > 1 ? sortedColors[1] : "#5F6368";
                string accentColor = sortedColors.Count > 2 ? sortedColors[2] : "#EA4335";

                // Find most common font
                var sortedFonts = SortByFrequency(fontFreq);
                string fontFamily = sortedFonts.Count > 0 ? sortedFonts[0] : "Arial";

                // Categorize font sizes
                fontSizes.Sort();
                float h1 = 48, h2 = 32, body = 18, caption = 14;
                if (fontSizes.Count >= 4)
                {
                    h1 = fontSizes[fontSizes.Count - 1];
                    h2 = fontSizes[fontSizes.Count / 2];
                    body = fontSizes[fontSizes.Count / 4];
                    caption = fontSizes[0];
                }

                // Check contrast
                var contrastWarnings = new List<string>();
                if (sortedColors.Count >= 2)
                {
                    Color c1, c2;
                    if (ColorUtility.TryParseHtmlString(sortedColors[0], out c1) &&
                        ColorUtility.TryParseHtmlString(sortedColors[1], out c2))
                    {
                        double ratio = CalculateContrastRatio(c1, c2);
                        if (ratio < 4.5)
                            contrastWarnings.Add("Primary/secondary color contrast ratio " +
                                ratio.ToString("F2", CultureInfo.InvariantCulture) +
                                ":1 fails WCAG AA (minimum 4.5:1)");
                    }
                }

                var warnEntries = new List<string>();
                foreach (var w in contrastWarnings) warnEntries.Add("\"" + Esc(w) + "\"");

                return string.Format(CultureInfo.InvariantCulture,
                    "{{\"success\":true,\"elementsAnalyzed\":{0},\"extractedSpec\":{{\"colors\":{{\"primary\":\"{1}\",\"secondary\":\"{2}\",\"accent\":\"{3}\"}},\"typography\":{{\"fontFamily\":\"{4}\",\"h1\":{5},\"h2\":{6},\"body\":{7},\"caption\":{8}}}}},\"contrastWarnings\":[{9}]}}",
                    elementsAnalyzed, Esc(primaryColor), Esc(secondaryColor), Esc(accentColor),
                    Esc(fontFamily), h1, h2, body, caption,
                    string.Join(",", warnEntries.ToArray()));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string ApplyBrand(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return "{\"success\":false,\"error\":\"scenePath is required\"}";

            if (currentSpec == null)
            {
                // Try loading from file
                string fullPath = Path.Combine(Application.dataPath, "..", specPath);
                if (File.Exists(fullPath))
                    currentSpec = SimpleJson.Parse(File.ReadAllText(fullPath));
                else
                    return "{\"success\":false,\"error\":\"No brand spec set\"}";
            }

            try
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                int modified = 0;

                var colors = currentSpec.Get("colors");
                var typography = currentSpec.Get("typography");

                string primaryHex = colors?.GetString("primary") ?? "";
                string secondaryHex = colors?.GetString("secondary") ?? "";
                string fontFamily = typography?.GetString("fontFamily") ?? "";
                float bodySize = typography?.Get("body")?.AsFloat() ?? 0;

                Color primaryColor = Color.white;
                if (!string.IsNullOrEmpty(primaryHex))
                    ColorUtility.TryParseHtmlString(primaryHex, out primaryColor);

                var rootObjects = scene.GetRootGameObjects();
                foreach (var root in rootObjects)
                {
                    modified += ApplyBrandToHierarchy(root.transform, primaryColor, bodySize);
                }

                EditorSceneManager.SaveScene(scene);

                return string.Format("{{\"success\":true,\"scenePath\":\"{0}\",\"elementsModified\":{1}}}",
                    Esc(scenePath), modified);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string ResolveBrandToken(string token)
        {
            if (string.IsNullOrEmpty(token) || !token.StartsWith("@"))
                return token;

            if (currentSpec == null)
            {
                string fullPath = Path.Combine(Application.dataPath, "..", specPath);
                if (File.Exists(fullPath))
                    currentSpec = SimpleJson.Parse(File.ReadAllText(fullPath));
                else
                    return token;
            }

            string key = token.Substring(1);

            // Check colors
            var colors = currentSpec.Get("colors");
            if (colors != null)
            {
                string colorVal = colors.GetString(key);
                if (!string.IsNullOrEmpty(colorVal))
                    return colorVal;
            }

            // Check typography
            var typo = currentSpec.Get("typography");
            if (typo != null)
            {
                var val = typo.Get(key);
                if (val != null) return val.AsString();
            }

            // Check spacing
            var spacing = currentSpec.Get("spacing");
            if (spacing != null)
            {
                var val = spacing.Get(key);
                if (val != null) return val.AsString();
            }

            // Check components
            var components = currentSpec.Get("components");
            if (components != null)
            {
                var val = components.Get(key);
                if (val != null) return val.AsString();
            }

            return token;
        }

        private static void AnalyzeUIHierarchy(Transform t, Dictionary<string, int> colorFreq,
            Dictionary<string, int> fontFreq, List<float> fontSizes, List<float> spacingValues, ref int count)
        {
            // Analyze Image colors
            var img = t.GetComponent<Image>();
            if (img != null && img.color.a > 0.1f)
            {
                string hex = "#" + ColorUtility.ToHtmlStringRGB(img.color);
                if (!colorFreq.ContainsKey(hex)) colorFreq[hex] = 0;
                colorFreq[hex]++;
                count++;
            }

            // Analyze Text components
            var text = t.GetComponent<Text>();
            if (text != null)
            {
                if (text.font != null)
                {
                    string fontName = text.font.name;
                    if (!fontFreq.ContainsKey(fontName)) fontFreq[fontName] = 0;
                    fontFreq[fontName]++;
                }
                fontSizes.Add(text.fontSize);
                string textHex = "#" + ColorUtility.ToHtmlStringRGB(text.color);
                if (!colorFreq.ContainsKey(textHex)) colorFreq[textHex] = 0;
                colorFreq[textHex]++;
                count++;
            }

            // Analyze TMP via reflection
            Type tmpType = FindType("TMPro.TextMeshProUGUI");
            if (tmpType != null)
            {
                var tmpComp = t.GetComponent(tmpType);
                if (tmpComp != null)
                {
                    var fsProp = tmpType.GetProperty("fontSize");
                    if (fsProp != null) fontSizes.Add((float)fsProp.GetValue(tmpComp));

                    var fontProp = tmpType.GetProperty("font");
                    if (fontProp != null)
                    {
                        var font = fontProp.GetValue(tmpComp);
                        if (font != null)
                        {
                            string fontName = font.ToString();
                            if (!fontFreq.ContainsKey(fontName)) fontFreq[fontName] = 0;
                            fontFreq[fontName]++;
                        }
                    }

                    var colorProp = tmpType.GetProperty("color");
                    if (colorProp != null)
                    {
                        Color c = (Color)colorProp.GetValue(tmpComp);
                        string hex = "#" + ColorUtility.ToHtmlStringRGB(c);
                        if (!colorFreq.ContainsKey(hex)) colorFreq[hex] = 0;
                        colorFreq[hex]++;
                    }
                    count++;
                }
            }

            // Layout group spacing
            var vlg = t.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) spacingValues.Add(vlg.spacing);
            var hlg = t.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) spacingValues.Add(hlg.spacing);

            // Recurse children
            for (int i = 0; i < t.childCount; i++)
                AnalyzeUIHierarchy(t.GetChild(i), colorFreq, fontFreq, fontSizes, spacingValues, ref count);
        }

        private static int ApplyBrandToHierarchy(Transform t, Color primaryColor, float bodySize)
        {
            int modified = 0;

            // Apply primary color to Button images
            var btn = t.GetComponent<Button>();
            if (btn != null)
            {
                var img = t.GetComponent<Image>();
                if (img != null)
                {
                    img.color = primaryColor;
                    modified++;
                }
            }

            // Apply body font size to Text
            if (bodySize > 0)
            {
                var text = t.GetComponent<Text>();
                if (text != null)
                {
                    text.fontSize = (int)bodySize;
                    modified++;
                }

                Type tmpType = FindType("TMPro.TextMeshProUGUI");
                if (tmpType != null)
                {
                    var tmpComp = t.GetComponent(tmpType);
                    if (tmpComp != null)
                    {
                        var fsProp = tmpType.GetProperty("fontSize");
                        if (fsProp != null)
                        {
                            fsProp.SetValue(tmpComp, bodySize);
                            modified++;
                        }
                    }
                }
            }

            for (int i = 0; i < t.childCount; i++)
                modified += ApplyBrandToHierarchy(t.GetChild(i), primaryColor, bodySize);

            return modified;
        }

        private static double CalculateContrastRatio(Color c1, Color c2)
        {
            double l1 = RelativeLuminance(c1);
            double l2 = RelativeLuminance(c2);
            double lighter = Math.Max(l1, l2);
            double darker = Math.Min(l1, l2);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double RelativeLuminance(Color c)
        {
            double r = c.r <= 0.03928 ? c.r / 12.92 : Math.Pow((c.r + 0.055) / 1.055, 2.4);
            double g = c.g <= 0.03928 ? c.g / 12.92 : Math.Pow((c.g + 0.055) / 1.055, 2.4);
            double b = c.b <= 0.03928 ? c.b / 12.92 : Math.Pow((c.b + 0.055) / 1.055, 2.4);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static List<string> SortByFrequency(Dictionary<string, int> freq)
        {
            var list = new List<KeyValuePair<string, int>>(freq);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var result = new List<string>();
            foreach (var kv in list) result.Add(kv.Key);
            return result;
        }

        private static string SerializeNode(SimpleJson.JsonNode node)
        {
            if (node == null) return "null";
            if (node.str != null) return "\"" + Esc(node.str) + "\"";
            if (node.arr != null)
            {
                var items = new List<string>();
                foreach (var item in node.arr) items.Add(SerializeNode(item));
                return "[" + string.Join(",", items.ToArray()) + "]";
            }
            if (node.obj != null)
            {
                var entries = new List<string>();
                foreach (var kv in node.obj)
                    entries.Add("\"" + Esc(kv.Key) + "\":" + SerializeNode(kv.Value));
                return "{" + string.Join(",", entries.ToArray()) + "}";
            }
            return "null";
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
