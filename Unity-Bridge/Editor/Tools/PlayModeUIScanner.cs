using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace UnityBridge
{
    /// <summary>
    /// Runtime UI discovery during Play Mode. Scans all canvases, computes screen pixel
    /// positions from RectTransform world corners, and returns structured hierarchy with
    /// interaction state, text content, and computed alpha.
    /// </summary>
    public static class PlayModeUIScanner
    {
        private const int MAX_DEPTH = 50;

        public static string ScanUI()
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"PlayModeUIScanner requires Play Mode\"}";

            try
            {
                var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
                var canvasEntries = new List<string>();
                var interactables = new List<string>();

                foreach (var canvas in canvases)
                {
                    // Skip nested sub-canvases — only process root canvases
                    if (canvas.transform.parent != null)
                    {
                        var parentCanvas = canvas.transform.parent.GetComponentInParent<Canvas>();
                        if (parentCanvas != null) continue;
                    }

                    canvasEntries.Add(SerializeCanvas(canvas, interactables));
                }

                return string.Format(CultureInfo.InvariantCulture,
                    "{{\"success\":true,\"screenWidth\":{0},\"screenHeight\":{1},\"canvases\":[{2}],\"interactableElements\":[{3}]}}",
                    Screen.width, Screen.height,
                    string.Join(",", canvasEntries.ToArray()),
                    string.Join(",", interactables.ToArray()));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string FindElement(string nameOrPath)
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"PlayModeUIScanner requires Play Mode\"}";

            if (string.IsNullOrEmpty(nameOrPath))
                return "{\"success\":false,\"error\":\"nameOrPath is required\"}";

            try
            {
                var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();

                foreach (var canvas in canvases)
                {
                    if (canvas.transform.parent != null)
                    {
                        var parentCanvas = canvas.transform.parent.GetComponentInParent<Canvas>();
                        if (parentCanvas != null) continue;
                    }

                    var result = FindInHierarchy(canvas.gameObject, nameOrPath, canvas, GetHierarchyPath(canvas.gameObject));
                    if (result != null)
                    {
                        return string.Format("{{\"success\":true,\"element\":{0}}}", result);
                    }
                }

                return "{\"success\":false,\"error\":\"Element not found: " + Esc(nameOrPath) + "\"}";
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string GetInteractables()
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"PlayModeUIScanner requires Play Mode\"}";

            try
            {
                var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
                var interactables = new List<string>();

                foreach (var canvas in canvases)
                {
                    if (canvas.transform.parent != null)
                    {
                        var parentCanvas = canvas.transform.parent.GetComponentInParent<Canvas>();
                        if (parentCanvas != null) continue;
                    }

                    CollectInteractables(canvas.gameObject, canvas, GetHierarchyPath(canvas.gameObject), interactables);
                }

                return string.Format("{{\"success\":true,\"count\":{0},\"elements\":[{1}]}}",
                    interactables.Count, string.Join(",", interactables.ToArray()));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        // --- Internal helpers used by IntegrationTestRunner ---

        internal static GameObject FindElementGameObject(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // Try direct GameObject.Find first
            var go = GameObject.Find(path);
            if (go != null) return go;

            // Walk scene roots for hierarchical paths
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            int slashIdx = path.IndexOf('/');
            string rootName = slashIdx > 0 ? path.Substring(0, slashIdx) : path;
            string childPath = slashIdx > 0 ? path.Substring(slashIdx + 1) : null;

            foreach (var root in roots)
            {
                if (root.name == rootName)
                {
                    if (childPath == null) return root;
                    var child = root.transform.Find(childPath);
                    if (child != null) return child.gameObject;
                }
            }
            return null;
        }

        internal static Vector2 GetScreenCenter(GameObject go, Canvas rootCanvas)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return new Vector2(-1, -1);

            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            Vector2[] screenCorners = new Vector2[4];
            for (int i = 0; i < 4; i++)
                screenCorners[i] = WorldCornerToScreen(corners[i], rootCanvas);

            float minX = screenCorners[0].x, maxX = screenCorners[0].x;
            float minY = screenCorners[0].y, maxY = screenCorners[0].y;
            for (int i = 1; i < 4; i++)
            {
                if (screenCorners[i].x < minX) minX = screenCorners[i].x;
                if (screenCorners[i].x > maxX) maxX = screenCorners[i].x;
                if (screenCorners[i].y < minY) minY = screenCorners[i].y;
                if (screenCorners[i].y > maxY) maxY = screenCorners[i].y;
            }

            return new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
        }

        internal static float GetEffectiveAlpha(GameObject go)
        {
            float alpha = 1f;
            var t = go.transform;
            while (t != null)
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null) alpha *= cg.alpha;
                t = t.parent;
            }
            return alpha;
        }

        internal static Canvas GetRootCanvas(GameObject go)
        {
            Canvas result = null;
            var t = go.transform;
            while (t != null)
            {
                var c = t.GetComponent<Canvas>();
                if (c != null && c.isRootCanvas) result = c;
                t = t.parent;
            }
            return result;
        }

        internal static string GetElementText(GameObject go)
        {
            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null) return tmp.text;

            var legacy = go.GetComponentInChildren<Text>();
            if (legacy != null) return legacy.text;

            return null;
        }

        // --- Private serialization ---

        private static string SerializeCanvas(Canvas canvas, List<string> interactables)
        {
            string renderMode = canvas.renderMode.ToString();
            string path = GetHierarchyPath(canvas.gameObject);

            var children = new List<string>();
            foreach (Transform child in canvas.transform)
                children.Add(SerializeElement(child.gameObject, canvas, path, 0, interactables));

            return string.Format(
                "{{\"name\":\"{0}\",\"renderMode\":\"{1}\",\"sortOrder\":{2},\"elements\":[{3}]}}",
                Esc(canvas.gameObject.name), Esc(renderMode), canvas.sortingOrder,
                string.Join(",", children.ToArray()));
        }

        private static string SerializeElement(GameObject go, Canvas rootCanvas, string parentPath, int depth, List<string> interactables)
        {
            if (depth > MAX_DEPTH) return "{\"name\":\"[MAX_DEPTH]\"}";

            string path = parentPath + "/" + go.name;
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"name\":\"").Append(Esc(go.name)).Append("\"");
            sb.Append(",\"path\":\"").Append(Esc(path)).Append("\"");
            sb.Append(",\"active\":").Append(go.activeInHierarchy ? "true" : "false");

            // Screen rect
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);

                Vector2[] screenCorners = new Vector2[4];
                for (int i = 0; i < 4; i++)
                    screenCorners[i] = WorldCornerToScreen(corners[i], rootCanvas);

                float minX = screenCorners[0].x, maxX = screenCorners[0].x;
                float minY = screenCorners[0].y, maxY = screenCorners[0].y;
                for (int i = 1; i < 4; i++)
                {
                    if (screenCorners[i].x < minX) minX = screenCorners[i].x;
                    if (screenCorners[i].x > maxX) maxX = screenCorners[i].x;
                    if (screenCorners[i].y < minY) minY = screenCorners[i].y;
                    if (screenCorners[i].y > maxY) maxY = screenCorners[i].y;
                }

                float cx = (minX + maxX) / 2f;
                float cy = (minY + maxY) / 2f;
                float w = maxX - minX;
                float h = maxY - minY;

                sb.Append(",\"screenRect\":{\"x\":").Append((int)minX)
                  .Append(",\"y\":").Append((int)minY)
                  .Append(",\"w\":").Append((int)w)
                  .Append(",\"h\":").Append((int)h).Append('}');
                sb.Append(",\"screenCenter\":{\"x\":").Append((int)cx)
                  .Append(",\"y\":").Append((int)cy).Append('}');
            }

            // Text content — TMP first, then legacy
            string textContent = null;
            var tmp = go.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                textContent = tmp.text;
                sb.Append(",\"text\":\"").Append(Esc(textContent)).Append("\"");
            }
            else
            {
                var legacyText = go.GetComponent<Text>();
                if (legacyText != null)
                {
                    textContent = legacyText.text;
                    sb.Append(",\"text\":\"").Append(Esc(textContent)).Append("\"");
                }
            }

            // Interaction type
            string interactionType = null;
            bool interactable = false;

            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                interactionType = "Button";
                interactable = btn.interactable;
                sb.Append(",\"interactionType\":\"Button\"");
                sb.Append(",\"interactable\":").Append(btn.interactable ? "true" : "false");
            }

            var toggle = go.GetComponent<Toggle>();
            if (toggle != null && interactionType == null)
            {
                interactionType = "Toggle";
                interactable = toggle.interactable;
                sb.Append(",\"interactionType\":\"Toggle\"");
                sb.Append(",\"interactable\":").Append(toggle.interactable ? "true" : "false");
                sb.Append(",\"isOn\":").Append(toggle.isOn ? "true" : "false");
            }

            var slider = go.GetComponent<Slider>();
            if (slider != null && interactionType == null)
            {
                interactionType = "Slider";
                interactable = slider.interactable;
                sb.Append(",\"interactionType\":\"Slider\"");
                sb.Append(",\"interactable\":").Append(slider.interactable ? "true" : "false");
                sb.AppendFormat(CultureInfo.InvariantCulture, ",\"value\":{0}", slider.value);
            }

            var inputField = go.GetComponent<InputField>();
            if (inputField != null && interactionType == null)
            {
                interactionType = "InputField";
                interactable = inputField.interactable;
                sb.Append(",\"interactionType\":\"InputField\"");
                sb.Append(",\"interactable\":").Append(inputField.interactable ? "true" : "false");
                sb.Append(",\"inputText\":\"").Append(Esc(inputField.text)).Append("\"");
            }

            // TMP InputField
            var tmpInput = go.GetComponent<TMP_InputField>();
            if (tmpInput != null && interactionType == null)
            {
                interactionType = "InputField";
                interactable = tmpInput.interactable;
                sb.Append(",\"interactionType\":\"InputField\"");
                sb.Append(",\"interactable\":").Append(tmpInput.interactable ? "true" : "false");
                sb.Append(",\"inputText\":\"").Append(Esc(tmpInput.text)).Append("\"");
            }

            var dropdown = go.GetComponent<Dropdown>();
            if (dropdown != null && interactionType == null)
            {
                interactionType = "Dropdown";
                interactable = dropdown.interactable;
                sb.Append(",\"interactionType\":\"Dropdown\"");
                sb.Append(",\"interactable\":").Append(dropdown.interactable ? "true" : "false");
                sb.AppendFormat(",\"value\":{0}", dropdown.value);
                var opts = new List<string>();
                foreach (var opt in dropdown.options) opts.Add("\"" + Esc(opt.text) + "\"");
                sb.Append(",\"options\":[").Append(string.Join(",", opts.ToArray())).Append("]");
            }

            // TMP Dropdown
            var tmpDropdown = go.GetComponent<TMP_Dropdown>();
            if (tmpDropdown != null && interactionType == null)
            {
                interactionType = "Dropdown";
                interactable = tmpDropdown.interactable;
                sb.Append(",\"interactionType\":\"Dropdown\"");
                sb.Append(",\"interactable\":").Append(tmpDropdown.interactable ? "true" : "false");
                sb.AppendFormat(",\"value\":{0}", tmpDropdown.value);
                var opts = new List<string>();
                foreach (var opt in tmpDropdown.options) opts.Add("\"" + Esc(opt.text) + "\"");
                sb.Append(",\"options\":[").Append(string.Join(",", opts.ToArray())).Append("]");
            }

            var scrollRect = go.GetComponent<ScrollRect>();
            if (scrollRect != null && interactionType == null)
            {
                interactionType = "ScrollRect";
                interactable = true;
                sb.Append(",\"interactionType\":\"ScrollRect\"");
            }

            // Raycast target
            var graphic = go.GetComponent<Graphic>();
            if (graphic != null)
                sb.Append(",\"raycastTarget\":").Append(graphic.raycastTarget ? "true" : "false");

            // Effective alpha
            float alpha = GetEffectiveAlpha(go);
            sb.AppendFormat(CultureInfo.InvariantCulture, ",\"alpha\":{0:F2}", alpha);

            // Build interactable summary entry
            if (interactionType != null && interactable && go.activeInHierarchy && alpha > 0f)
            {
                string label = textContent ?? GetElementText(go) ?? "";
                var center = GetScreenCenter(go, rootCanvas);
                interactables.Add("{\"name\":\"" + Esc(go.name) + "\",\"path\":\"" + Esc(path)
                    + "\",\"screenCenter\":{\"x\":" + (int)center.x + ",\"y\":" + (int)center.y
                    + "},\"type\":\"" + Esc(interactionType) + "\",\"text\":\"" + Esc(label) + "\"}");
            }

            // Children
            var childEntries = new List<string>();
            foreach (Transform child in go.transform)
                childEntries.Add(SerializeElement(child.gameObject, rootCanvas, path, depth + 1, interactables));

            if (childEntries.Count > 0)
                sb.Append(",\"children\":[").Append(string.Join(",", childEntries.ToArray())).Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        private static string FindInHierarchy(GameObject go, string nameOrPath, Canvas rootCanvas, string currentPath)
        {
            // Match by full path or by name
            if (currentPath == nameOrPath || go.name == nameOrPath)
            {
                var dummy = new List<string>();
                return SerializeElement(go, rootCanvas, currentPath.Contains("/") ? currentPath.Substring(0, currentPath.LastIndexOf('/')) : "", 0, dummy);
            }

            foreach (Transform child in go.transform)
            {
                string childPath = currentPath + "/" + child.name;
                var result = FindInHierarchy(child.gameObject, nameOrPath, rootCanvas, childPath);
                if (result != null) return result;
            }

            return null;
        }

        private static void CollectInteractables(GameObject go, Canvas rootCanvas, string currentPath, List<string> interactables)
        {
            float alpha = GetEffectiveAlpha(go);
            if (go.activeInHierarchy && alpha > 0f)
            {
                string interactionType = null;
                bool isInteractable = false;

                var btn = go.GetComponent<Button>();
                if (btn != null) { interactionType = "Button"; isInteractable = btn.interactable; }

                var toggle = go.GetComponent<Toggle>();
                if (toggle != null && interactionType == null) { interactionType = "Toggle"; isInteractable = toggle.interactable; }

                var slider = go.GetComponent<Slider>();
                if (slider != null && interactionType == null) { interactionType = "Slider"; isInteractable = slider.interactable; }

                var inputField = go.GetComponent<InputField>();
                if (inputField != null && interactionType == null) { interactionType = "InputField"; isInteractable = inputField.interactable; }

                var tmpInput = go.GetComponent<TMP_InputField>();
                if (tmpInput != null && interactionType == null) { interactionType = "InputField"; isInteractable = tmpInput.interactable; }

                var dropdown = go.GetComponent<Dropdown>();
                if (dropdown != null && interactionType == null) { interactionType = "Dropdown"; isInteractable = dropdown.interactable; }

                var tmpDropdown = go.GetComponent<TMP_Dropdown>();
                if (tmpDropdown != null && interactionType == null) { interactionType = "Dropdown"; isInteractable = tmpDropdown.interactable; }

                if (interactionType != null && isInteractable)
                {
                    string label = GetElementText(go) ?? "";
                    var center = GetScreenCenter(go, rootCanvas);
                    interactables.Add("{\"name\":\"" + Esc(go.name) + "\",\"path\":\"" + Esc(currentPath)
                        + "\",\"screenCenter\":{\"x\":" + (int)center.x + ",\"y\":" + (int)center.y
                        + "},\"type\":\"" + Esc(interactionType) + "\",\"text\":\"" + Esc(label) + "\"}");
                }
            }

            foreach (Transform child in go.transform)
                CollectInteractables(child.gameObject, rootCanvas, currentPath + "/" + child.name, interactables);
        }

        private static Vector2 WorldCornerToScreen(Vector3 worldCorner, Canvas canvas)
        {
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                // World corners ARE screen pixels for overlay canvases
                return new Vector2(worldCorner.x, worldCorner.y);
            }

            // ScreenSpaceCamera or WorldSpace
            Camera cam = canvas.worldCamera;
            if (cam == null) cam = Camera.main;
            if (cam == null) return new Vector2(worldCorner.x, worldCorner.y);

            return RectTransformUtility.WorldToScreenPoint(cam, worldCorner);
        }

        private static string GetHierarchyPath(GameObject go)
        {
            return go.name;
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
