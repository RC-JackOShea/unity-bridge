using System;
using System.IO;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Static helper for capturing screenshots from the Game view.
    /// All methods must be called on the main thread (via QueueUnityAction).
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

        public static CaptureResult CaptureAsBase64(int superSize = 1)
        {
            try
            {
                var texture = ScreenCapture.CaptureScreenshotAsTexture(superSize);
                if (texture == null)
                {
                    return new CaptureResult
                    {
                        success = false,
                        error = "CaptureScreenshotAsTexture returned null. Is Play Mode active?"
                    };
                }

                var pngBytes = texture.EncodeToPNG();
                var result = new CaptureResult
                {
                    success = true,
                    base64 = Convert.ToBase64String(pngBytes),
                    width = texture.width,
                    height = texture.height
                };

                UnityEngine.Object.DestroyImmediate(texture);
                return result;
            }
            catch (Exception e)
            {
                return new CaptureResult
                {
                    success = false,
                    error = e.Message
                };
            }
        }

        public static CaptureResult CaptureToFile(string path, int superSize = 1)
        {
            try
            {
                var texture = ScreenCapture.CaptureScreenshotAsTexture(superSize);
                if (texture == null)
                {
                    return new CaptureResult
                    {
                        success = false,
                        error = "CaptureScreenshotAsTexture returned null. Is Play Mode active?"
                    };
                }

                var pngBytes = texture.EncodeToPNG();

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(path, pngBytes);

                var result = new CaptureResult
                {
                    success = true,
                    filePath = path,
                    width = texture.width,
                    height = texture.height
                };

                UnityEngine.Object.DestroyImmediate(texture);
                return result;
            }
            catch (Exception e)
            {
                return new CaptureResult
                {
                    success = false,
                    error = e.Message
                };
            }
        }
    }
}
