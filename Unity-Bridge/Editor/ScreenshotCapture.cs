using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace UnityBridge
{
    /// <summary>
    /// Static helper for capturing screenshots from the Game view.
    /// All methods must be called on the main thread (via QueueUnityAction).
    /// Uses direct camera RenderTexture capture for reliability on Windows.
    /// </summary>
    public static class ScreenshotCapture
    {
        [Serializable]
        public class CaptureResult
        {
            public bool success;
            public string base64;
            public string filePath;
            public int width;
            public int height;
            public string error;
        }

        /// <summary>
        /// Find the best camera to capture from.
        /// Prefers Camera.main, falls back to any active camera.
        /// </summary>
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

        /// <summary>
        /// Capture using direct camera render to RenderTexture.
        /// This is more reliable than CaptureScreenshotAsTexture in the editor
        /// because it doesn't depend on the Game View rendering pipeline timing.
        /// </summary>
        private static CaptureResult CaptureViaCamera(int superSize)
        {
            var cam = FindCaptureCamera();
            if (cam == null)
                return new CaptureResult { success = false, error = "No active camera found in scene." };

            int w = cam.pixelWidth * superSize;
            int h = cam.pixelHeight * superSize;

            if (w <= 0 || h <= 0)
            {
                // Fallback to a reasonable default size
                w = 1920 * superSize;
                h = 1080 * superSize;
            }

            RenderTexture rt = null;
            RenderTexture prevCamTarget = null;
            RenderTexture prevActive = null;
            Texture2D tex = null;

            try
            {
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 1;
                rt.Create();

                prevCamTarget = cam.targetTexture;
                prevActive = RenderTexture.active;

                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();

                cam.targetTexture = prevCamTarget;
                RenderTexture.active = prevActive;

                byte[] pngBytes = tex.EncodeToPNG();
                if (pngBytes == null || pngBytes.Length == 0)
                    return new CaptureResult { success = false, error = "EncodeToPNG returned empty data." };

                string base64 = Convert.ToBase64String(pngBytes);

                return new CaptureResult
                {
                    success = true,
                    base64 = base64,
                    width = w,
                    height = h
                };
            }
            catch (Exception e)
            {
                // Restore camera state on error
                if (cam != null) cam.targetTexture = prevCamTarget;
                if (prevActive != null) RenderTexture.active = prevActive;
                return new CaptureResult { success = false, error = e.Message };
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

        public static CaptureResult CaptureAsBase64(int superSize = 1)
        {
            return CaptureViaCamera(superSize);
        }

        public static CaptureResult CaptureToFile(string path, int superSize = 1)
        {
            var result = CaptureViaCamera(superSize);
            if (!result.success)
                return result;

            try
            {
                byte[] pngBytes = Convert.FromBase64String(result.base64);

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(path, pngBytes);

                return new CaptureResult
                {
                    success = true,
                    filePath = path,
                    width = result.width,
                    height = result.height
                };
            }
            catch (Exception e)
            {
                return new CaptureResult { success = false, error = $"File write error: {e.Message}" };
            }
        }
    }
}
