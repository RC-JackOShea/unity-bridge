using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Rule-based visual validation for screenshot images. Checks for non-blank content,
    /// expected dimensions, minimum color diversity, pixel-level baseline comparison,
    /// and WCAG color contrast. All Texture2D objects are destroyed after use.
    /// </summary>
    public static class VisualValidator
    {
        public static string ValidateScreenshot(string screenshotPath, string rulesJson)
        {
            if (string.IsNullOrEmpty(screenshotPath))
                return "{\"success\":false,\"error\":\"screenshotPath is required\"}";

            if (!File.Exists(screenshotPath))
                return "{\"success\":false,\"error\":\"File not found: " + Esc(screenshotPath) + "\"}";

            Texture2D tex = null;
            try
            {
                byte[] data = File.ReadAllBytes(screenshotPath);
                tex = new Texture2D(2, 2);
                if (!tex.LoadImage(data))
                    return "{\"success\":false,\"error\":\"Failed to load image: " + Esc(screenshotPath) + "\"}";

                // Parse rules
                SimpleJson.JsonNode rules = null;
                if (!string.IsNullOrEmpty(rulesJson))
                    rules = SimpleJson.Parse(rulesJson);

                var ruleResults = new List<string>();
                bool allPassed = true;

                // notBlank rule
                bool runNotBlank = true;
                int minColors = 10;
                if (rules != null)
                {
                    var nbRule = rules.Get("notBlank");
                    if (nbRule != null)
                    {
                        var enabledNode = nbRule.Get("enabled");
                        if (enabledNode != null) runNotBlank = enabledNode.AsBool();
                        var minNode = nbRule.Get("minColors");
                        if (minNode != null) minColors = (int)minNode.AsFloat();
                    }
                }

                if (runNotBlank)
                {
                    var pixels32 = tex.GetPixels32();
                    var uniqueColors = new HashSet<int>();
                    foreach (var p in pixels32)
                    {
                        int colorKey = (p.r << 16) | (p.g << 8) | p.b;
                        uniqueColors.Add(colorKey);
                        if (uniqueColors.Count > minColors) break;
                    }
                    bool nbPassed = uniqueColors.Count >= minColors;
                    if (!nbPassed) allPassed = false;
                    ruleResults.Add(string.Format(
                        "{{\"rule\":\"notBlank\",\"passed\":{0},\"uniqueColors\":{1},\"minRequired\":{2}}}",
                        nbPassed ? "true" : "false", uniqueColors.Count, minColors));
                }

                // expectedDimensions rule
                if (rules != null)
                {
                    var dimRule = rules.Get("expectedDimensions");
                    if (dimRule != null)
                    {
                        int expectedW = (int)(dimRule.Get("width")?.AsFloat() ?? 0);
                        int expectedH = (int)(dimRule.Get("height")?.AsFloat() ?? 0);
                        if (expectedW > 0 && expectedH > 0)
                        {
                            bool dimPassed = tex.width == expectedW && tex.height == expectedH;
                            if (!dimPassed) allPassed = false;
                            ruleResults.Add(string.Format(
                                "{{\"rule\":\"expectedDimensions\",\"passed\":{0},\"actual\":{{\"width\":{1},\"height\":{2}}},\"expected\":{{\"width\":{3},\"height\":{4}}}}}",
                                dimPassed ? "true" : "false", tex.width, tex.height, expectedW, expectedH));
                        }
                    }
                }

                // minColorDiversity rule
                if (rules != null)
                {
                    var divRule = rules.Get("minColorDiversity");
                    if (divRule != null)
                    {
                        int minDiversity = (int)(divRule.Get("minColors")?.AsFloat() ?? 50);
                        var pixels32b = tex.GetPixels32();
                        var uniqueAll = new HashSet<int>();
                        foreach (var p in pixels32b)
                        {
                            int colorKey = (p.r << 16) | (p.g << 8) | p.b;
                            uniqueAll.Add(colorKey);
                        }
                        bool divPassed = uniqueAll.Count >= minDiversity;
                        if (!divPassed) allPassed = false;
                        ruleResults.Add(string.Format(
                            "{{\"rule\":\"minColorDiversity\",\"passed\":{0},\"uniqueColors\":{1},\"minRequired\":{2}}}",
                            divPassed ? "true" : "false", uniqueAll.Count, minDiversity));
                    }
                }

                return string.Format(
                    "{{\"success\":true,\"screenshotPath\":\"{0}\",\"width\":{1},\"height\":{2},\"allPassed\":{3},\"rules\":[{4}]}}",
                    Esc(screenshotPath), tex.width, tex.height, allPassed ? "true" : "false",
                    string.Join(",", ruleResults.ToArray()));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        public static string CompareWithBaseline(string currentPath, string baselinePath, float threshold)
        {
            if (string.IsNullOrEmpty(currentPath))
                return "{\"success\":false,\"error\":\"currentPath is required\"}";
            if (string.IsNullOrEmpty(baselinePath))
                return "{\"success\":false,\"error\":\"baselinePath is required\"}";
            if (!File.Exists(currentPath))
                return "{\"success\":false,\"error\":\"Current file not found: " + Esc(currentPath) + "\"}";
            if (!File.Exists(baselinePath))
                return "{\"success\":false,\"error\":\"Baseline file not found: " + Esc(baselinePath) + "\"}";

            if (threshold <= 0) threshold = 1.0f;

            Texture2D currentTex = null, baselineTex = null, diffTex = null;
            try
            {
                currentTex = new Texture2D(2, 2);
                baselineTex = new Texture2D(2, 2);

                if (!currentTex.LoadImage(File.ReadAllBytes(currentPath)))
                    return "{\"success\":false,\"error\":\"Failed to load current image\"}";
                if (!baselineTex.LoadImage(File.ReadAllBytes(baselinePath)))
                    return "{\"success\":false,\"error\":\"Failed to load baseline image\"}";

                if (currentTex.width != baselineTex.width || currentTex.height != baselineTex.height)
                {
                    return string.Format(
                        "{{\"success\":true,\"passed\":false,\"differencePercent\":100.0,\"reason\":\"Resolution mismatch: current={0}x{1}, baseline={2}x{3}\"}}",
                        currentTex.width, currentTex.height, baselineTex.width, baselineTex.height);
                }

                Color[] currentPixels = currentTex.GetPixels();
                Color[] baselinePixels = baselineTex.GetPixels();
                int total = currentPixels.Length;
                int diffCount = 0;
                float channelTolerance = 2f / 255f;

                // Create diff image
                diffTex = new Texture2D(currentTex.width, currentTex.height, TextureFormat.RGB24, false);
                Color[] diffPixels = new Color[total];

                for (int i = 0; i < total; i++)
                {
                    float dr = Mathf.Abs(currentPixels[i].r - baselinePixels[i].r);
                    float dg = Mathf.Abs(currentPixels[i].g - baselinePixels[i].g);
                    float db = Mathf.Abs(currentPixels[i].b - baselinePixels[i].b);

                    if (dr > channelTolerance || dg > channelTolerance || db > channelTolerance)
                    {
                        diffCount++;
                        diffPixels[i] = Color.magenta; // Highlight changed pixels
                    }
                    else
                    {
                        diffPixels[i] = currentPixels[i] * 0.3f; // Darken unchanged
                        diffPixels[i].a = 1;
                    }
                }

                double diffPercent = (double)diffCount / total * 100.0;
                bool passed = diffPercent <= threshold;

                // Save diff image
                string diffPath = Path.Combine(Path.GetDirectoryName(currentPath),
                    Path.GetFileNameWithoutExtension(currentPath) + "_diff.png");
                diffTex.SetPixels(diffPixels);
                diffTex.Apply();
                byte[] diffBytes = diffTex.EncodeToPNG();
                File.WriteAllBytes(diffPath, diffBytes);

                return string.Format(CultureInfo.InvariantCulture,
                    "{{\"success\":true,\"passed\":{0},\"differencePercent\":{1:F4},\"differentPixels\":{2},\"totalPixels\":{3},\"threshold\":{4},\"diffImagePath\":\"{5}\"}}",
                    passed ? "true" : "false", diffPercent, diffCount, total, threshold, Esc(diffPath.Replace("\\", "/")));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
            finally
            {
                if (currentTex != null) UnityEngine.Object.DestroyImmediate(currentTex);
                if (baselineTex != null) UnityEngine.Object.DestroyImmediate(baselineTex);
                if (diffTex != null) UnityEngine.Object.DestroyImmediate(diffTex);
            }
        }

        public static string GenerateValidationReport(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
                return "{\"success\":false,\"error\":\"directoryPath is required\"}";
            if (!Directory.Exists(directoryPath))
                return "{\"success\":false,\"error\":\"Directory not found: " + Esc(directoryPath) + "\"}";

            try
            {
                var pngFiles = Directory.GetFiles(directoryPath, "*.png");
                var fileResults = new List<string>();
                int totalPassed = 0, totalFailed = 0;

                foreach (var file in pngFiles)
                {
                    // Skip diff images
                    if (file.Contains("_diff.png")) continue;

                    string result = ValidateScreenshot(file, null);
                    bool passed = result.Contains("\"allPassed\":true");
                    if (passed) totalPassed++;
                    else totalFailed++;

                    string fileName = Path.GetFileName(file);
                    fileResults.Add(string.Format("{{\"file\":\"{0}\",\"passed\":{1}}}", Esc(fileName), passed ? "true" : "false"));
                }

                return string.Format(
                    "{{\"success\":true,\"directory\":\"{0}\",\"files\":[{1}],\"summary\":{{\"total\":{2},\"passed\":{3},\"failed\":{4}}}}}",
                    Esc(directoryPath), string.Join(",", fileResults.ToArray()),
                    totalPassed + totalFailed, totalPassed, totalFailed);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string CheckColorContrast(string screenshotPath, string regionJson)
        {
            if (string.IsNullOrEmpty(screenshotPath))
                return "{\"success\":false,\"error\":\"screenshotPath is required\"}";
            if (!File.Exists(screenshotPath))
                return "{\"success\":false,\"error\":\"File not found: " + Esc(screenshotPath) + "\"}";

            Texture2D tex = null;
            try
            {
                tex = new Texture2D(2, 2);
                if (!tex.LoadImage(File.ReadAllBytes(screenshotPath)))
                    return "{\"success\":false,\"error\":\"Failed to load image\"}";

                // Parse region
                int rx = 0, ry = 0, rw = tex.width, rh = tex.height;
                if (!string.IsNullOrEmpty(regionJson))
                {
                    var region = SimpleJson.Parse(regionJson);
                    rx = (int)(region.Get("x")?.AsFloat() ?? 0);
                    ry = (int)(region.Get("y")?.AsFloat() ?? 0);
                    rw = (int)(region.Get("width")?.AsFloat() ?? tex.width);
                    rh = (int)(region.Get("height")?.AsFloat() ?? tex.height);
                }

                // Clamp region
                rx = Mathf.Clamp(rx, 0, tex.width - 1);
                ry = Mathf.Clamp(ry, 0, tex.height - 1);
                rw = Mathf.Clamp(rw, 1, tex.width - rx);
                rh = Mathf.Clamp(rh, 1, tex.height - ry);

                // Get pixels in region and find dominant foreground/background
                var colorCounts = new Dictionary<int, int>();
                Color[] allPixels = tex.GetPixels(rx, ry, rw, rh);

                foreach (var p in allPixels)
                {
                    // Quantize to reduce noise (round to nearest 16)
                    int r = ((int)(p.r * 255) / 16) * 16;
                    int g = ((int)(p.g * 255) / 16) * 16;
                    int b = ((int)(p.b * 255) / 16) * 16;
                    int key = (r << 16) | (g << 8) | b;

                    if (!colorCounts.ContainsKey(key)) colorCounts[key] = 0;
                    colorCounts[key]++;
                }

                // Find top 2 colors
                int topKey1 = 0, topCount1 = 0;
                int topKey2 = 0, topCount2 = 0;
                foreach (var kv in colorCounts)
                {
                    if (kv.Value > topCount1)
                    {
                        topKey2 = topKey1; topCount2 = topCount1;
                        topKey1 = kv.Key; topCount1 = kv.Value;
                    }
                    else if (kv.Value > topCount2)
                    {
                        topKey2 = kv.Key; topCount2 = kv.Value;
                    }
                }

                Color bg = new Color(((topKey1 >> 16) & 0xFF) / 255f, ((topKey1 >> 8) & 0xFF) / 255f, (topKey1 & 0xFF) / 255f);
                Color fg = new Color(((topKey2 >> 16) & 0xFF) / 255f, ((topKey2 >> 8) & 0xFF) / 255f, (topKey2 & 0xFF) / 255f);

                double l1 = RelativeLuminance(bg);
                double l2 = RelativeLuminance(fg);
                double lighter = Math.Max(l1, l2);
                double darker = Math.Min(l1, l2);
                double ratio = (lighter + 0.05) / (darker + 0.05);

                bool passAA = ratio >= 4.5;
                bool passAAA = ratio >= 7.0;

                string bgHex = "#" + ColorUtility.ToHtmlStringRGB(bg);
                string fgHex = "#" + ColorUtility.ToHtmlStringRGB(fg);

                return string.Format(CultureInfo.InvariantCulture,
                    "{{\"success\":true,\"contrastRatio\":{0:F2},\"wcagAA\":{1},\"wcagAAA\":{2},\"backgroundColor\":\"{3}\",\"foregroundColor\":\"{4}\",\"region\":{{\"x\":{5},\"y\":{6},\"width\":{7},\"height\":{8}}}}}",
                    ratio, passAA ? "true" : "false", passAAA ? "true" : "false",
                    bgHex, fgHex, rx, ry, rw, rh);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static double RelativeLuminance(Color c)
        {
            double r = c.r <= 0.03928 ? c.r / 12.92 : Math.Pow((c.r + 0.055) / 1.055, 2.4);
            double g = c.g <= 0.03928 ? c.g / 12.92 : Math.Pow((c.g + 0.055) / 1.055, 2.4);
            double b = c.b <= 0.03928 ? c.b / 12.92 : Math.Pow((c.b + 0.055) / 1.055, 2.4);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
