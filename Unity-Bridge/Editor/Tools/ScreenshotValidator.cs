using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Multi-resolution screenshot capture and pixel-level comparison.
    /// Captures at multiple resolutions by adjusting camera RenderTexture size.
    /// Provides pixel comparison for visual regression testing.
    /// Note: ScreenSpaceOverlay canvases are NOT captured (camera-based rendering).
    /// </summary>
    public static class ScreenshotValidator
    {
        public static string CaptureMultiResolution(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"success\":false,\"error\":\"jsonSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonSpec);
                string outputDir = spec.GetString("outputDirectory") ?? "C:/temp/screenshots";
                var resolutions = spec.Get("resolutions");
                int delayFrames = 3;
                var delayNode = spec.Get("delayFrames");
                if (delayNode != null) delayFrames = (int)delayNode.AsFloat();

                if (resolutions == null || resolutions.arr == null || resolutions.arr.Count == 0)
                    return "{\"success\":false,\"error\":\"resolutions array is required\"}";

                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                var cam = FindCaptureCamera();
                if (cam == null)
                    return "{\"success\":false,\"error\":\"No active camera found in scene\"}";

                var captures = new List<string>();

                foreach (var resNode in resolutions.arr)
                {
                    int width = (int)resNode.Get("width").AsFloat();
                    int height = (int)resNode.Get("height").AsFloat();
                    string label = resNode.GetString("label") ?? (width + "x" + height);

                    if (width <= 0 || height <= 0)
                    {
                        captures.Add(string.Format(
                            "{{\"label\":\"{0}\",\"width\":{1},\"height\":{2},\"success\":false,\"error\":\"Invalid resolution\"}}",
                            Esc(label), width, height));
                        continue;
                    }

                    string filename = "screenshot_" + width + "x" + height + ".png";
                    string filePath = Path.Combine(outputDir, filename).Replace("\\", "/");

                    try
                    {
                        byte[] pngBytes = CaptureAtResolution(cam, width, height);
                        File.WriteAllBytes(filePath, pngBytes);
                        long fileSize = new FileInfo(filePath).Length;

                        captures.Add(string.Format(CultureInfo.InvariantCulture,
                            "{{\"label\":\"{0}\",\"width\":{1},\"height\":{2},\"path\":\"{3}\",\"fileSize\":{4},\"success\":true}}",
                            Esc(label), width, height, Esc(filePath), fileSize));
                    }
                    catch (Exception e)
                    {
                        captures.Add(string.Format(
                            "{{\"label\":\"{0}\",\"width\":{1},\"height\":{2},\"success\":false,\"error\":\"{3}\"}}",
                            Esc(label), width, height, Esc(e.Message)));
                    }
                }

                return string.Format("{{\"success\":true,\"outputDirectory\":\"{0}\",\"captures\":[{1}],\"totalCaptures\":{2}}}",
                    Esc(outputDir), string.Join(",", captures.ToArray()), captures.Count);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string CompareScreenshots(string baselinePath, string currentPath)
        {
            if (string.IsNullOrEmpty(baselinePath))
                return "{\"success\":false,\"error\":\"baselinePath is required\"}";
            if (string.IsNullOrEmpty(currentPath))
                return "{\"success\":false,\"error\":\"currentPath is required\"}";

            try
            {
                if (!File.Exists(baselinePath))
                    return "{\"success\":false,\"error\":\"Baseline file not found: " + Esc(baselinePath) + "\"}";
                if (!File.Exists(currentPath))
                    return "{\"success\":false,\"error\":\"Current file not found: " + Esc(currentPath) + "\"}";

                byte[] baselineBytes = File.ReadAllBytes(baselinePath);
                byte[] currentBytes = File.ReadAllBytes(currentPath);

                var baselineTex = new Texture2D(2, 2);
                var currentTex = new Texture2D(2, 2);

                try
                {
                    if (!baselineTex.LoadImage(baselineBytes))
                        return "{\"success\":false,\"error\":\"Failed to load baseline image\"}";
                    if (!currentTex.LoadImage(currentBytes))
                        return "{\"success\":false,\"error\":\"Failed to load current image\"}";

                    if (baselineTex.width != currentTex.width || baselineTex.height != currentTex.height)
                    {
                        return string.Format(
                            "{{\"success\":true,\"match\":false,\"differencePercent\":100.0,\"reason\":\"Resolution mismatch: baseline={0}x{1}, current={2}x{3}\"}}",
                            baselineTex.width, baselineTex.height, currentTex.width, currentTex.height);
                    }

                    Color[] baselinePixels = baselineTex.GetPixels();
                    Color[] currentPixels = currentTex.GetPixels();
                    int totalPixels = baselinePixels.Length;
                    int diffPixels = 0;
                    float threshold = 0.01f;

                    for (int i = 0; i < totalPixels; i++)
                    {
                        float dr = Mathf.Abs(baselinePixels[i].r - currentPixels[i].r);
                        float dg = Mathf.Abs(baselinePixels[i].g - currentPixels[i].g);
                        float db = Mathf.Abs(baselinePixels[i].b - currentPixels[i].b);
                        if (dr > threshold || dg > threshold || db > threshold)
                            diffPixels++;
                    }

                    double diffPercent = (double)diffPixels / totalPixels * 100.0;
                    bool pass = diffPercent < 0.1;

                    return string.Format(CultureInfo.InvariantCulture,
                        "{{\"success\":true,\"match\":{0},\"differencePercent\":{1:F4},\"differentPixels\":{2},\"totalPixels\":{3},\"threshold\":{4},\"resolution\":{{\"width\":{5},\"height\":{6}}}}}",
                        pass ? "true" : "false", diffPercent, diffPixels, totalPixels, threshold,
                        baselineTex.width, baselineTex.height);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baselineTex);
                    UnityEngine.Object.DestroyImmediate(currentTex);
                }
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        private static byte[] CaptureAtResolution(Camera cam, int width, int height)
        {
            RenderTexture rt = null;
            RenderTexture prevCamTarget = null;
            RenderTexture prevActive = null;
            Texture2D tex = null;

            try
            {
                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 1;
                rt.Create();

                prevCamTarget = cam.targetTexture;
                prevActive = RenderTexture.active;

                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                cam.targetTexture = prevCamTarget;
                RenderTexture.active = prevActive;

                byte[] pngBytes = tex.EncodeToPNG();
                if (pngBytes == null || pngBytes.Length == 0)
                    throw new Exception("EncodeToPNG returned empty data");

                return pngBytes;
            }
            catch
            {
                if (cam != null) cam.targetTexture = prevCamTarget;
                RenderTexture.active = prevActive;
                throw;
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
        }

        private static Camera FindCaptureCamera()
        {
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled)
                return main;

            foreach (var cam in Camera.allCameras)
            {
                if (cam.isActiveAndEnabled)
                    return cam;
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
