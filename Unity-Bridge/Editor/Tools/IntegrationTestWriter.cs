using System;
using System.Collections.Generic;
using System.IO;

namespace UnityBridge
{
    /// <summary>
    /// Validates and saves integration test definition files (JSON) to disk.
    /// Used by agents to persist discovered test sequences for replay.
    /// </summary>
    public static class IntegrationTestWriter
    {
        private static readonly HashSet<string> ValidActionTypes = new HashSet<string>
        {
            // Original 13 PlayModeInteractor action types
            "enter_play", "exit_play", "wait_frames", "wait_seconds", "wait_condition",
            "input_tap", "input_hold", "input_drag", "input_key",
            "screenshot", "check_state", "check_log", "clear_logs",
            // 7 new integration test action types
            "scan_ui", "tap_element", "assert_active", "assert_text",
            "assert_interactable", "assert_not_visible", "assert_screenshot"
        };

        private static readonly HashSet<string> ActionTypesRequiringPath = new HashSet<string>
        {
            "tap_element", "assert_active", "assert_text",
            "assert_interactable", "assert_not_visible"
        };

        private static readonly HashSet<string> AssertionTypes = new HashSet<string>
        {
            "assert_active", "assert_text", "assert_interactable",
            "assert_not_visible", "assert_screenshot", "check_state", "check_log"
        };

        public static string SaveTest(string jsonDefinition, string filePath)
        {
            if (string.IsNullOrEmpty(jsonDefinition))
                return "{\"success\":false,\"error\":\"jsonDefinition is required\"}";
            if (string.IsNullOrEmpty(filePath))
                return "{\"success\":false,\"error\":\"filePath is required\"}";

            try
            {
                string validation = ValidateTest(jsonDefinition);
                var validationNode = SimpleJson.Parse(validation);
                bool valid = validationNode.Get("valid")?.AsBool() ?? false;

                if (!valid)
                    return "{\"success\":false,\"error\":\"Validation failed\",\"validation\":" + validation + "}";

                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(filePath, jsonDefinition);

                return "{\"success\":true,\"filePath\":\"" + Esc(filePath.Replace("\\", "/")) + "\",\"validation\":" + validation + "}";
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string ValidateTest(string jsonDefinition)
        {
            if (string.IsNullOrEmpty(jsonDefinition))
                return "{\"valid\":false,\"errors\":[\"jsonDefinition is required\"],\"warnings\":[]}";

            var errors = new List<string>();
            var warnings = new List<string>();

            try
            {
                var test = SimpleJson.Parse(jsonDefinition);

                // Check version
                var versionNode = test.Get("version");
                if (versionNode == null)
                    warnings.Add("Missing 'version' field — defaulting to 1");

                // Check name
                string name = test.GetString("name");
                if (string.IsNullOrEmpty(name))
                    errors.Add("'name' field is required");

                // Check actions
                var actions = test.Get("actions");
                if (actions == null || actions.arr == null || actions.arr.Count == 0)
                {
                    errors.Add("'actions' array is required and must not be empty");
                }
                else
                {
                    bool hasAssertion = false;

                    for (int i = 0; i < actions.arr.Count; i++)
                    {
                        var action = actions.arr[i];
                        string actionType = action.GetString("type") ?? "";

                        if (string.IsNullOrEmpty(actionType))
                        {
                            errors.Add("Action " + i + ": missing 'type' field");
                            continue;
                        }

                        if (!ValidActionTypes.Contains(actionType))
                        {
                            errors.Add("Action " + i + ": unknown type '" + actionType + "'");
                            continue;
                        }

                        if (ActionTypesRequiringPath.Contains(actionType))
                        {
                            string path = action.GetString("path");
                            if (string.IsNullOrEmpty(path))
                                errors.Add("Action " + i + " (" + actionType + "): 'path' field is required");
                        }

                        if (actionType == "assert_text")
                        {
                            string expected = action.GetString("expected");
                            if (string.IsNullOrEmpty(expected))
                                errors.Add("Action " + i + " (assert_text): 'expected' field is required");
                        }

                        if (actionType == "assert_screenshot")
                        {
                            string baseline = action.GetString("baseline");
                            if (string.IsNullOrEmpty(baseline))
                                errors.Add("Action " + i + " (assert_screenshot): 'baseline' field is required");
                        }

                        if (AssertionTypes.Contains(actionType))
                            hasAssertion = true;
                    }

                    if (!hasAssertion)
                        warnings.Add("Test has no assertions — it will always pass");
                }

                bool valid = errors.Count == 0;
                var errorStrs = new List<string>();
                foreach (var e in errors) errorStrs.Add("\"" + Esc(e) + "\"");
                var warnStrs = new List<string>();
                foreach (var w in warnings) warnStrs.Add("\"" + Esc(w) + "\"");

                return string.Format("{{\"valid\":{0},\"errors\":[{1}],\"warnings\":[{2}],\"actionCount\":{3}}}",
                    valid ? "true" : "false",
                    string.Join(",", errorStrs.ToArray()),
                    string.Join(",", warnStrs.ToArray()),
                    actions?.arr?.Count ?? 0);
            }
            catch (Exception e)
            {
                return "{\"valid\":false,\"errors\":[\"Failed to parse JSON: " + Esc(e.Message) + "\"],\"warnings\":[]}";
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
