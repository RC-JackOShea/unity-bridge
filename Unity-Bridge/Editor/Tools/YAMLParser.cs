using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Parses Unity YAML (.prefab, .unity) files and returns structured JSON.
    /// Handles Unity's tagged YAML format with !u! class ID tags.
    /// </summary>
    public static class YAMLParser
    {
        private static readonly Dictionary<int, string> ClassIDMap = new Dictionary<int, string>
        {
            {1, "GameObject"}, {2, "Component"}, {4, "Transform"}, {8, "Behaviour"},
            {12, "ParticleAnimator"}, {13, "Input"}, {20, "Camera"}, {21, "Material"},
            {23, "MeshRenderer"}, {25, "Renderer"}, {28, "Texture2D"}, {33, "MeshFilter"},
            {43, "Mesh"}, {48, "Shader"}, {49, "TextAsset"}, {50, "Rigidbody2D"},
            {54, "Rigidbody"}, {56, "Collider"}, {58, "CircleCollider2D"},
            {59, "HingeJoint"}, {60, "PolygonCollider2D"}, {61, "BoxCollider2D"},
            {64, "MeshCollider"}, {65, "BoxCollider"}, {66, "CompositeCollider2D"},
            {68, "EdgeCollider2D"}, {70, "CapsuleCollider2D"}, {72, "ComputeShader"},
            {74, "AnimationClip"}, {78, "AudioClip"}, {82, "AudioSource"},
            {83, "AudioListener"}, {84, "RenderTexture"}, {86, "CustomRenderTexture"},
            {87, "MeshParticleEmitter"}, {89, "Cubemap"}, {91, "AnimatorOverrideController"},
            {95, "Animator"}, {102, "TextMesh"}, {104, "RenderSettings"},
            {108, "Light"}, {111, "Animation"}, {114, "MonoBehaviour"},
            {115, "MonoScript"}, {120, "LineRenderer"}, {124, "Behaviour"},
            {128, "Font"}, {131, "GUITexture"}, {132, "GUIText"},
            {134, "PhysicMaterial"}, {135, "SphereCollider"}, {136, "CapsuleCollider"},
            {137, "SkinnedMeshRenderer"}, {141, "BuildSettings"},
            {142, "AssetBundle"}, {144, "AudioMixer"}, {145, "AudioMixerController"},
            {146, "AudioMixerGroupController"}, {150, "Prefab"}, {152, "EditorSettings"},
            {153, "PresetManager"}, {157, "LightmapSettings"}, {158, "NavMeshSettings"},
            {196, "TilemapCollider2D"}, {198, "ParticleSystem"},
            {199, "ParticleSystemRenderer"}, {200, "ShaderVariantCollection"},
            {205, "LODGroup"}, {206, "BlendTree"}, {207, "Motion"},
            {208, "NavMeshObstacle"}, {210, "SortingGroup"},
            {212, "SpriteRenderer"}, {213, "Sprite"}, {220, "LightProbeGroup"},
            {222, "Canvas"}, {223, "CanvasGroup"}, {224, "RectTransform"},
            {225, "CanvasRenderer"}, {226, "TextMesh"}, {228, "SpriteAtlas"},
            {243, "AudioMixerSnapshot"}, {245, "AudioMixerEffectController"},
            {258, "VideoPlayer"}, {290, "VisualEffectAsset"}, {310, "UnityConnectSettings"},
            {319, "AvatarMask"}, {320, "PlayableDirector"},
            {328, "VideoClip"}, {329, "VideoPlayer"},
            {1001, "PrefabInstance"}, {1002, "EditorExtensionImpl"},
            {1660057539, "SceneVisibilityState"}, {1953259897, "TerrainLayer"},
            {2058629511, "SpriteShape"}
        };

        // Regex for YAML document separator: --- !u!{classID} &{fileID}
        private static readonly Regex DocSeparator = new Regex(@"^---\s*!u!(\d+)\s*&(\d+)", RegexOptions.Compiled);
        // Regex for external references: {fileID: N, guid: HEX, type: N}
        private static readonly Regex ExternalRef = new Regex(@"\{fileID:\s*(\d+),\s*guid:\s*([0-9a-fA-F]+),\s*type:\s*(\d+)\}", RegexOptions.Compiled);
        // Regex for internal references: {fileID: N} (no guid)
        private static readonly Regex InternalRef = new Regex(@"\{fileID:\s*(\d+)\}", RegexOptions.Compiled);

        public static string ParseFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "{\"error\":\"filePath is required\"}";

            string fullPath;
            if (filePath.StartsWith("Assets/"))
                fullPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), filePath.Replace("/", "\\"));
            else
                fullPath = filePath;

            if (!File.Exists(fullPath))
                return "{\"error\":\"File not found: " + Esc(filePath) + "\"}";

            string fileType = filePath.EndsWith(".unity") ? "scene" : filePath.EndsWith(".prefab") ? "prefab" : "unknown";

            try
            {
                var objects = new List<string>();
                var typeCounts = new Dictionary<string, int>();
                int totalExtRefs = 0;

                // Parse the file line by line
                int currentClassID = -1;
                long currentFileID = 0;
                string currentTypeName = "";
                var currentProps = new List<string>();
                var currentIntRefs = new List<string>();
                var currentExtRefs = new List<string>();
                bool isStripped = false;

                using (var reader = new StreamReader(fullPath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // Skip YAML headers
                        if (line.StartsWith("%YAML") || line.StartsWith("%TAG") || line.StartsWith("---") && !line.Contains("!u!"))
                        {
                            if (line.StartsWith("---") && !line.Contains("!u!"))
                            {
                                // Flush current object if any
                                if (currentClassID >= 0)
                                    FlushObject(objects, typeCounts, ref totalExtRefs, currentClassID, currentFileID, currentTypeName, currentProps, currentIntRefs, currentExtRefs, isStripped);
                                currentClassID = -1;
                            }
                            continue;
                        }

                        var match = DocSeparator.Match(line);
                        if (match.Success)
                        {
                            // Flush previous object
                            if (currentClassID >= 0)
                                FlushObject(objects, typeCounts, ref totalExtRefs, currentClassID, currentFileID, currentTypeName, currentProps, currentIntRefs, currentExtRefs, isStripped);

                            currentClassID = int.Parse(match.Groups[1].Value);
                            currentFileID = long.Parse(match.Groups[2].Value);
                            currentTypeName = ClassIDMap.ContainsKey(currentClassID) ? ClassIDMap[currentClassID] : "Unknown_" + currentClassID;
                            currentProps = new List<string>();
                            currentIntRefs = new List<string>();
                            currentExtRefs = new List<string>();
                            isStripped = line.Contains("stripped");
                            continue;
                        }

                        if (currentClassID < 0) continue;

                        // Extract references from the line
                        var extMatches = ExternalRef.Matches(line);
                        foreach (Match em in extMatches)
                        {
                            string guid = em.Groups[2].Value;
                            string refFileID = em.Groups[1].Value;
                            string refType = em.Groups[3].Value;
                            string propName = ExtractPropertyName(line);

                            // Resolve GUID to path
                            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                            currentExtRefs.Add(string.Format(
                                "{{\"property\":\"{0}\",\"fileID\":{1},\"guid\":\"{2}\",\"type\":{3},\"resolvedPath\":\"{4}\"}}",
                                Esc(propName), refFileID, Esc(guid), refType, Esc(assetPath ?? "")));
                        }

                        var intMatches = InternalRef.Matches(line);
                        if (extMatches.Count == 0) // Don't double-count refs that have both patterns
                        {
                            foreach (Match im in intMatches)
                            {
                                long targetID = long.Parse(im.Groups[1].Value);
                                if (targetID == 0) continue; // null reference
                                string propName = ExtractPropertyName(line);
                                currentIntRefs.Add(string.Format(
                                    "{{\"property\":\"{0}\",\"targetFileID\":{1}}}",
                                    Esc(propName), targetID));
                            }
                        }

                        // Extract simple key-value properties (top-level, indent ≤ 2 spaces)
                        if (line.Length > 2 && line[0] == ' ' && line[1] == ' ' && (line.Length < 3 || line[2] != ' '))
                        {
                            int colon = line.IndexOf(':');
                            if (colon > 0)
                            {
                                string key = line.Substring(2, colon - 2).Trim();
                                string val = colon + 1 < line.Length ? line.Substring(colon + 1).Trim() : "";

                                // Skip complex nested values and reference values (handled above)
                                if (!string.IsNullOrEmpty(val) && !val.StartsWith("{") && !val.StartsWith("-"))
                                {
                                    currentProps.Add(string.Format("{{\"key\":\"{0}\",\"value\":\"{1}\"}}", Esc(key), Esc(val)));
                                }
                            }
                        }
                    }

                    // Flush last object
                    if (currentClassID >= 0)
                        FlushObject(objects, typeCounts, ref totalExtRefs, currentClassID, currentFileID, currentTypeName, currentProps, currentIntRefs, currentExtRefs, isStripped);
                }

                // Build byType summary
                var byType = new List<string>();
                foreach (var kv in typeCounts)
                    byType.Add(string.Format("\"{0}\":{1}", Esc(kv.Key), kv.Value));

                return string.Format(
                    "{{\"filePath\":\"{0}\",\"fileType\":\"{1}\",\"objects\":[{2}],\"summary\":{{\"totalObjects\":{3},\"byType\":{{{4}}},\"externalReferences\":{5}}}}}",
                    Esc(filePath), fileType,
                    string.Join(",", objects.ToArray()),
                    objects.Count,
                    string.Join(",", byType.ToArray()),
                    totalExtRefs);
            }
            catch (Exception e)
            {
                return "{\"error\":\"Parse failed: " + Esc(e.Message) + "\"}";
            }
        }

        private static void FlushObject(List<string> objects, Dictionary<string, int> typeCounts, ref int totalExtRefs,
            int classID, long fileID, string typeName, List<string> props, List<string> intRefs, List<string> extRefs, bool isStripped)
        {
            if (!typeCounts.ContainsKey(typeName)) typeCounts[typeName] = 0;
            typeCounts[typeName]++;
            totalExtRefs += extRefs.Count;

            // For MonoBehaviour, extract m_Script GUID from external refs
            string scriptInfo = "null";
            if (classID == 114)
            {
                foreach (var eRef in extRefs)
                {
                    if (eRef.Contains("\"property\":\"m_Script\""))
                    {
                        scriptInfo = eRef;
                        break;
                    }
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("{\"fileID\":").Append(fileID);
            sb.Append(",\"classID\":").Append(classID);
            sb.Append(",\"typeName\":\"").Append(Esc(typeName)).Append("\"");
            if (isStripped) sb.Append(",\"stripped\":true");
            if (classID == 114 && scriptInfo != "null")
                sb.Append(",\"scriptReference\":").Append(scriptInfo);

            // Include name if present in properties
            foreach (var p in props)
            {
                if (p.Contains("\"key\":\"m_Name\""))
                {
                    // Extract name value
                    int vi = p.IndexOf("\"value\":\"");
                    if (vi >= 0)
                    {
                        int start = vi + 9;
                        int end = p.IndexOf("\"", start);
                        if (end > start)
                            sb.Append(",\"name\":\"").Append(p.Substring(start, end - start)).Append("\"");
                    }
                    break;
                }
            }

            sb.Append(",\"properties\":[").Append(string.Join(",", props.ToArray())).Append("]");
            sb.Append(",\"internalReferences\":[").Append(string.Join(",", intRefs.ToArray())).Append("]");
            sb.Append(",\"externalReferences\":[").Append(string.Join(",", extRefs.ToArray())).Append("]");
            sb.Append("}");
            objects.Add(sb.ToString());
        }

        private static string ExtractPropertyName(string line)
        {
            string trimmed = line.TrimStart();
            int colon = trimmed.IndexOf(':');
            if (colon > 0) return trimmed.Substring(0, colon).Trim('-').Trim();
            return "unknown";
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
