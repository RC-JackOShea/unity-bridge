using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.Editor.Setup
{
    /// <summary>
    /// Editor tool that creates all game prefabs, materials, and ScriptableObject assets.
    /// Run once via: execute Game.Editor.Setup.SandboxPrefabSetup.CreateAllPrefabs
    /// </summary>
    public static class SandboxPrefabSetup
    {
        private const string PrefabDir = "Assets/Resources/Prefabs";
        private const string SODir = "Assets/Resources/ScriptableObjects";
        private const string MatDir = "Assets/Materials";

        public static string CreateAllPrefabs()
        {
            try
            {
                EnsureDir(PrefabDir);
                EnsureDir(SODir);
                EnsureDir(MatDir);

                // 1. Create material
                var goldMat = CreateGoldMaterial();

                // 2. Create ScoreManager ScriptableObject asset
                var scoreManagerAsset = CreateScoreManagerAsset();

                // 3. Create prefabs
                CreateEventSystemPrefab();
                CreateTestPlayerPrefab();
                CreateScoreTriggerZonePrefab(goldMat, scoreManagerAsset);
                CreateUICanvasPrefab(scoreManagerAsset);
                CreateTabSystemPrefab();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                return "{\"success\":true,\"message\":\"All prefabs, materials, and SO assets created\"}";
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string ValidatePrefabs()
        {
            string[] expectedPrefabs = {
                PrefabDir + "/EventSystem.prefab",
                PrefabDir + "/TestPlayer.prefab",
                PrefabDir + "/ScoreTriggerZone.prefab",
                PrefabDir + "/UICanvas.prefab",
                PrefabDir + "/TabSystem.prefab"
            };
            string[] expectedAssets = {
                SODir + "/ScoreManager.asset",
                MatDir + "/GoldSphere.mat"
            };

            var missing = new System.Collections.Generic.List<string>();

            foreach (var p in expectedPrefabs)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(p) == null)
                    missing.Add(p);
            foreach (var a in expectedAssets)
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(a) == null)
                    missing.Add(a);

            if (missing.Count > 0)
                return "{\"success\":false,\"missing\":[\"" + string.Join("\",\"", missing.ToArray()) + "\"]}";

            return "{\"success\":true,\"message\":\"All prefabs and assets present\"}";
        }

        // ----------------------------------------------------------------
        // Material
        // ----------------------------------------------------------------

        private static Material CreateGoldMaterial()
        {
            string path = MatDir + "/GoldSphere.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            // Try URP Lit first, fall back to Standard
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.name = "GoldSphere";

            // URP uses _BaseColor, Standard uses _Color
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(1f, 0.84f, 0f));
            else
                mat.color = new Color(1f, 0.84f, 0f);

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ----------------------------------------------------------------
        // ScriptableObject
        // ----------------------------------------------------------------

        private static ScriptableObject CreateScoreManagerAsset()
        {
            string path = SODir + "/ScoreManager.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (existing != null) return existing;

            var smType = FindGameType("Game.Score.ScoreManager");
            if (smType == null)
                return null;

            var asset = ScriptableObject.CreateInstance(smType);
            asset.name = "ScoreManager";
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ----------------------------------------------------------------
        // Prefabs
        // ----------------------------------------------------------------

        private static void CreateEventSystemPrefab()
        {
            string path = PrefabDir + "/EventSystem.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            SavePrefab(go, path);
        }

        private static void CreateTestPlayerPrefab()
        {
            string path = PrefabDir + "/TestPlayer.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "TestPlayer";

            // Tag must exist — if not, will throw
            go.tag = "Player";

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            SavePrefab(go, path);
        }

        private static void CreateScoreTriggerZonePrefab(Material goldMat, ScriptableObject scoreManagerAsset)
        {
            string path = PrefabDir + "/ScoreTriggerZone.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            var go = new GameObject("ScoreTriggerZone");

            // Box collider trigger on root
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = Vector3.one * 1.5f;

            // ScoreTrigger component via reflection (Assembly-CSharp type)
            var triggerType = FindGameType("Game.Score.ScoreTrigger");
            if (triggerType != null)
            {
                var trigger = go.AddComponent(triggerType);
                // Use SerializedObject to set private serialized fields
                var so = new SerializedObject(trigger);
                SetSerializedRef(so, "scoreManager", scoreManagerAsset);
                SetSerializedInt(so, "pointValue", 1);
                SetSerializedInt(so, "maxUses", 10);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Child sphere visual
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = Vector3.one * 0.8f;

            // Remove sphere collider on the visual
            var sphereCol = visual.GetComponent<Collider>();
            if (sphereCol != null) UnityEngine.Object.DestroyImmediate(sphereCol);

            // Apply gold material
            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null && goldMat != null)
                renderer.sharedMaterial = goldMat;

            SavePrefab(go, path);
        }

        private static void CreateUICanvasPrefab(ScriptableObject scoreManagerAsset)
        {
            string path = PrefabDir + "/UICanvas.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            // Single shared canvas for all screen-space UI
            var canvasGo = new GameObject("UICanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // --- ScoreGroup (HUD elements) ---
            var scoreGroup = new GameObject("ScoreGroup");
            scoreGroup.transform.SetParent(canvasGo.transform, false);
            var sgRt = scoreGroup.AddComponent<RectTransform>();
            sgRt.anchorMin = Vector2.zero;
            sgRt.anchorMax = Vector2.one;
            sgRt.offsetMin = Vector2.zero;
            sgRt.offsetMax = Vector2.zero;

            var scoreGo = new GameObject("ScoreDisplay");
            scoreGo.transform.SetParent(scoreGroup.transform, false);

            var rt = scoreGo.GetComponent<RectTransform>();
            if (rt == null) rt = scoreGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.7f, 0.9f);
            rt.anchorMax = new Vector2(0.98f, 0.98f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = scoreGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "Score: 0";
            tmp.fontSize = 36;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopRight;

            var displayType = FindGameType("Game.UI.ScoreDisplay");
            if (displayType != null)
            {
                var display = scoreGo.AddComponent(displayType);
                var so = new SerializedObject(display);
                SetSerializedRef(so, "scoreManager", scoreManagerAsset);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // --- PauseGroup (pause menu elements) ---
            var pauseGroup = new GameObject("PauseGroup");
            pauseGroup.transform.SetParent(canvasGo.transform, false);
            var pgRt = pauseGroup.AddComponent<RectTransform>();
            pgRt.anchorMin = Vector2.zero;
            pgRt.anchorMax = Vector2.one;
            pgRt.offsetMin = Vector2.zero;
            pgRt.offsetMax = Vector2.zero;

            // Overlay (semi-transparent black background)
            var overlay = CreateUIChild(pauseGroup.transform, "Overlay",
                Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.6f));

            // Menu panel
            var panel = CreateUIChild(overlay.transform, "MenuPanel",
                new Vector2(0.3f, 0.25f), new Vector2(0.7f, 0.75f),
                new Color(0.176f, 0.176f, 0.176f, 1f));

            // Title
            CreateTMPChild(panel.transform, "TitleText", "PAUSED", 48,
                new Vector2(0.1f, 0.75f), new Vector2(0.9f, 0.95f),
                TextAlignmentOptions.Center);

            // Buttons
            var resumeBtn = CreateButtonChild(panel.transform, "ResumeButton", "Resume",
                new Color(0.298f, 0.686f, 0.314f),
                new Vector2(0.2f, 0.55f), new Vector2(0.8f, 0.7f));

            var settingsBtn = CreateButtonChild(panel.transform, "SettingsButton", "Settings",
                new Color(0.129f, 0.588f, 0.953f),
                new Vector2(0.2f, 0.35f), new Vector2(0.8f, 0.5f));

            var quitBtn = CreateButtonChild(panel.transform, "QuitButton", "Quit",
                new Color(0.957f, 0.263f, 0.212f),
                new Vector2(0.2f, 0.15f), new Vector2(0.8f, 0.3f));

            // PauseMenuController on the PauseGroup
            var controllerType = FindGameType("Game.UI.PauseMenuController");
            if (controllerType != null)
            {
                var controller = pauseGroup.AddComponent(controllerType);
                var so = new SerializedObject(controller);
                SetSerializedRef(so, "menuPanel", overlay);
                so.ApplyModifiedPropertiesWithoutUndo();

                WireButton(resumeBtn, controller, "OnResumeClicked");
                WireButton(settingsBtn, controller, "OnSettingsClicked");
                WireButton(quitBtn, controller, "OnQuitClicked");
            }

            // Pause overlay starts hidden
            overlay.SetActive(false);

            SavePrefab(canvasGo, path);
        }

        private static void CreateTabSystemPrefab()
        {
            string path = PrefabDir + "/TabSystem.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;

            var root = new GameObject("TabSystem");
            var rootRt = root.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // Add TabController component (wire fields later)
            var tabControllerType = FindGameType("Game.UI.TabController");
            Component tabController = null;
            if (tabControllerType != null)
                tabController = root.AddComponent(tabControllerType);

            // --- TabBar (top 10% strip) ---
            var tabBar = new GameObject("TabBar");
            tabBar.transform.SetParent(root.transform, false);
            var tabBarRt = tabBar.AddComponent<RectTransform>();
            tabBarRt.anchorMin = new Vector2(0, 0.9f);
            tabBarRt.anchorMax = new Vector2(1, 1);
            tabBarRt.offsetMin = Vector2.zero;
            tabBarRt.offsetMax = Vector2.zero;
            var hlg = tabBar.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 2;

            // Create 5 tab buttons
            var buttons = new Button[5];
            for (int i = 0; i < 5; i++)
            {
                var tab = new GameObject("Tab" + (i + 1));
                tab.transform.SetParent(tabBar.transform, false);
                var img = tab.AddComponent<Image>();
                img.color = i == 0
                    ? new Color(0.129f, 0.588f, 0.953f)
                    : new Color(0.25f, 0.25f, 0.25f);
                buttons[i] = tab.AddComponent<Button>();

                CreateTMPChild(tab.transform, "Label", "Tab " + (i + 1), 20,
                    Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
            }

            // --- ContentArea (bottom 90%) ---
            var contentArea = new GameObject("ContentArea");
            contentArea.transform.SetParent(root.transform, false);
            var contentRt = contentArea.AddComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = new Vector2(1, 0.9f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            Color[] panelColors = {
                new Color(0.2f, 0.3f, 0.4f),
                new Color(0.3f, 0.2f, 0.3f),
                new Color(0.2f, 0.4f, 0.2f),
                new Color(0.4f, 0.3f, 0.2f),
                new Color(0.3f, 0.2f, 0.2f)
            };

            var panels = new GameObject[5];
            for (int i = 0; i < 5; i++)
            {
                panels[i] = CreateUIChild(contentArea.transform, "Panel" + (i + 1),
                    Vector2.zero, Vector2.one, panelColors[i]);
                CreateTMPChild(panels[i].transform, "ContentText",
                    "Panel " + (i + 1) + " Content", 36,
                    Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
                panels[i].SetActive(i == 0);
            }

            // Wire TabController serialized fields
            if (tabController != null)
            {
                var so = new SerializedObject(tabController);

                var btnProp = so.FindProperty("tabButtons");
                if (btnProp != null)
                {
                    btnProp.arraySize = 5;
                    for (int i = 0; i < 5; i++)
                        btnProp.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
                }

                var panelProp = so.FindProperty("tabPanels");
                if (panelProp != null)
                {
                    panelProp.arraySize = 5;
                    for (int i = 0; i < 5; i++)
                        panelProp.GetArrayElementAtIndex(i).objectReferenceValue = panels[i];
                }

                so.ApplyModifiedPropertiesWithoutUndo();

                // Wire persistent onClick listeners
                var selectTab = tabControllerType.GetMethod("SelectTab",
                    BindingFlags.Public | BindingFlags.Instance);
                if (selectTab != null)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var action = (UnityEngine.Events.UnityAction<int>)
                            Delegate.CreateDelegate(
                                typeof(UnityEngine.Events.UnityAction<int>),
                                tabController, selectTab);
                        UnityEditor.Events.UnityEventTools.AddIntPersistentListener(
                            buttons[i].onClick, action, i);
                    }
                }
            }

            SavePrefab(root, path);
        }

        // ----------------------------------------------------------------
        // UI Helpers
        // ----------------------------------------------------------------

        private static GameObject CreateUIChild(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        private static void CreateTMPChild(Transform parent, string name, string text,
            int fontSize, Vector2 anchorMin, Vector2 anchorMax, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = alignment;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static GameObject CreateButtonChild(Transform parent, string name, string label,
            Color bgColor, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            go.AddComponent<Button>();

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Button label
            CreateTMPChild(go.transform, name + "Text", label, 24,
                Vector2.zero, Vector2.one, TextAlignmentOptions.Center);

            return go;
        }

        private static void WireButton(GameObject buttonGo, Component target, string methodName)
        {
            var btn = buttonGo.GetComponent<Button>();
            if (btn == null || target == null) return;

            var method = target.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return;

            // Use UnityEditor.Events to add a persistent listener
            UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(
                btn.onClick,
                (UnityEngine.Events.UnityAction)Delegate.CreateDelegate(
                    typeof(UnityEngine.Events.UnityAction), target, method));
        }

        // ----------------------------------------------------------------
        // Utility
        // ----------------------------------------------------------------

        private static void SavePrefab(GameObject go, string path)
        {
            EnsureDir(System.IO.Path.GetDirectoryName(path).Replace("\\", "/"));
            PrefabUtility.SaveAsPrefabAsset(go, path, out bool success);
            UnityEngine.Object.DestroyImmediate(go);
            if (!success)
                throw new Exception("Failed to save prefab: " + path);
        }

        private static void EnsureDir(string dir)
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Split('/');
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

        private static Type FindGameType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }

        private static void SetSerializedRef(SerializedObject so, string fieldName, UnityEngine.Object value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null)
                prop.objectReferenceValue = value;
        }

        private static void SetSerializedInt(SerializedObject so, string fieldName, int value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null)
                prop.intValue = value;
        }

        private static string Esc(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}
