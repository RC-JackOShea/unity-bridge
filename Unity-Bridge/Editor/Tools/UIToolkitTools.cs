using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Tools for parsing and generating UI Toolkit UXML and USS files.
    /// </summary>
    public static class UIToolkitTools
    {
        public static string ParseUXML(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "{\"error\":\"filePath is required\"}";

            string fullPath = ResolveAssetPath(filePath);
            if (!File.Exists(fullPath))
                return "{\"error\":\"File not found: " + Esc(filePath) + "\"}";

            try
            {
                var doc = new XmlDocument();
                doc.Load(fullPath);
                string rootJson = SerializeXmlNode(doc.DocumentElement);
                return string.Format("{{\"filePath\":\"{0}\",\"rootElement\":{1}}}", Esc(filePath), rootJson);
            }
            catch (Exception e)
            {
                return "{\"error\":\"UXML parse error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string ParseUSS(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "{\"error\":\"filePath is required\"}";

            string fullPath = ResolveAssetPath(filePath);
            if (!File.Exists(fullPath))
                return "{\"error\":\"File not found: " + Esc(filePath) + "\"}";

            try
            {
                string content = File.ReadAllText(fullPath);
                var rules = new List<string>();
                var variables = new List<string>();

                // Remove comments
                content = Regex.Replace(content, @"/\*.*?\*/", "", RegexOptions.Singleline);
                content = Regex.Replace(content, @"//[^\n]*", "");

                // Parse CSS-like rules
                var ruleRegex = new Regex(@"([^{]+)\{([^}]*)\}", RegexOptions.Singleline);
                foreach (Match m in ruleRegex.Matches(content))
                {
                    string selector = m.Groups[1].Value.Trim();
                    string body = m.Groups[2].Value.Trim();

                    var declarations = new List<string>();
                    var declParts = body.Split(';');
                    foreach (var part in declParts)
                    {
                        string trimmed = part.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        int colon = trimmed.IndexOf(':');
                        if (colon <= 0) continue;
                        string prop = trimmed.Substring(0, colon).Trim();
                        string val = trimmed.Substring(colon + 1).Trim();

                        declarations.Add(string.Format("{{\"property\":\"{0}\",\"value\":\"{1}\"}}", Esc(prop), Esc(val)));

                        if (prop.StartsWith("--"))
                            variables.Add(string.Format("{{\"name\":\"{0}\",\"value\":\"{1}\"}}", Esc(prop), Esc(val)));
                    }

                    rules.Add(string.Format("{{\"selector\":\"{0}\",\"declarations\":[{1}]}}",
                        Esc(selector), string.Join(",", declarations.ToArray())));
                }

                return string.Format("{{\"filePath\":\"{0}\",\"rules\":[{1}],\"variables\":[{2}]}}",
                    Esc(filePath), string.Join(",", rules.ToArray()), string.Join(",", variables.ToArray()));
            }
            catch (Exception e)
            {
                return "{\"error\":\"USS parse error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string GenerateUXML(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"error\":\"jsonSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonSpec);
                string outputPath = spec.GetString("outputPath");
                if (string.IsNullOrEmpty(outputPath))
                    return "{\"error\":\"outputPath is required\"}";

                var root = spec.Get("root");
                if (root == null)
                    return "{\"error\":\"root element is required\"}";

                var sb = new StringBuilder();
                sb.AppendLine("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:uie=\"UnityEditor.UIElements\">");
                GenerateUXMLElement(root, sb, 1);
                sb.AppendLine("</ui:UXML>");

                string fullPath = ResolveAssetPath(outputPath);
                string dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, sb.ToString());
                AssetDatabase.Refresh();

                return string.Format("{{\"success\":true,\"path\":\"{0}\",\"sizeBytes\":{1}}}",
                    Esc(outputPath), sb.Length);
            }
            catch (Exception e)
            {
                return "{\"error\":\"UXML generation error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string GenerateUSS(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"error\":\"jsonSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonSpec);
                string outputPath = spec.GetString("outputPath");
                if (string.IsNullOrEmpty(outputPath))
                    return "{\"error\":\"outputPath is required\"}";

                var rules = spec.GetArray("rules");
                if (rules == null)
                    return "{\"error\":\"rules array is required\"}";

                var sb = new StringBuilder();
                foreach (var rule in rules)
                {
                    string selector = rule.GetString("selector");
                    var declarations = rule.GetArray("declarations");
                    sb.AppendLine(selector + " {");
                    if (declarations != null)
                    {
                        foreach (var decl in declarations)
                        {
                            string prop = decl.GetString("property");
                            string val = decl.GetString("value");
                            sb.AppendLine("    " + prop + ": " + val + ";");
                        }
                    }
                    sb.AppendLine("}");
                    sb.AppendLine();
                }

                string fullPath = ResolveAssetPath(outputPath);
                string dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, sb.ToString());
                AssetDatabase.Refresh();

                return string.Format("{{\"success\":true,\"path\":\"{0}\",\"rulesWritten\":{1}}}",
                    Esc(outputPath), rules.Count);
            }
            catch (Exception e)
            {
                return "{\"error\":\"USS generation error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string FindUIToolkitUsage()
        {
            var uxmlFiles = new List<string>();
            var ussFiles = new List<string>();

            var uxmlGuids = AssetDatabase.FindAssets("t:VisualTreeAsset");
            foreach (var guid in uxmlGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/"))
                    uxmlFiles.Add("\"" + Esc(path) + "\"");
            }

            var ussGuids = AssetDatabase.FindAssets("t:StyleSheet");
            foreach (var guid in ussGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/"))
                    ussFiles.Add("\"" + Esc(path) + "\"");
            }

            return string.Format(
                "{{\"uxmlFiles\":[{0}],\"ussFiles\":[{1}],\"uxmlCount\":{2},\"ussCount\":{3}}}",
                string.Join(",", uxmlFiles.ToArray()), string.Join(",", ussFiles.ToArray()),
                uxmlFiles.Count, ussFiles.Count);
        }

        private static string SerializeXmlNode(XmlNode node)
        {
            if (node == null) return "null";

            var sb = new StringBuilder();
            sb.Append("{\"type\":\"").Append(Esc(node.LocalName)).Append("\"");

            // Attributes
            if (node.Attributes != null && node.Attributes.Count > 0)
            {
                var attrs = new List<string>();
                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (attr.Name.StartsWith("xmlns")) continue;
                    string attrName = attr.LocalName;
                    if (attrName == "name")
                        sb.Append(",\"name\":\"").Append(Esc(attr.Value)).Append("\"");
                    else if (attrName == "class")
                    {
                        var classes = attr.Value.Split(' ');
                        var clsArr = new List<string>();
                        foreach (var c in classes)
                            if (!string.IsNullOrEmpty(c.Trim())) clsArr.Add("\"" + Esc(c.Trim()) + "\"");
                        sb.Append(",\"classes\":[").Append(string.Join(",", clsArr.ToArray())).Append("]");
                    }
                    else if (attrName == "text")
                        sb.Append(",\"text\":\"").Append(Esc(attr.Value)).Append("\"");
                    else if (attrName == "binding-path")
                        sb.Append(",\"bindingPath\":\"").Append(Esc(attr.Value)).Append("\"");
                    else if (attrName == "style")
                        sb.Append(",\"inlineStyle\":\"").Append(Esc(attr.Value)).Append("\"");
                    else
                        attrs.Add(string.Format("{{\"name\":\"{0}\",\"value\":\"{1}\"}}", Esc(attrName), Esc(attr.Value)));
                }
                if (attrs.Count > 0)
                    sb.Append(",\"attributes\":[").Append(string.Join(",", attrs.ToArray())).Append("]");
            }

            // Children
            if (node.HasChildNodes)
            {
                var children = new List<string>();
                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element)
                        children.Add(SerializeXmlNode(child));
                }
                if (children.Count > 0)
                    sb.Append(",\"children\":[").Append(string.Join(",", children.ToArray())).Append("]");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static void GenerateUXMLElement(SimpleJson.JsonNode node, StringBuilder sb, int indent)
        {
            string type = node.GetString("type") ?? "VisualElement";
            string prefix = type.Contains(":") ? "" : "ui:";
            string padding = new string(' ', indent * 4);

            sb.Append(padding).Append("<").Append(prefix).Append(type);

            string name = node.GetString("name");
            if (!string.IsNullOrEmpty(name)) sb.Append(" name=\"").Append(EscXml(name)).Append("\"");

            var classes = node.GetArray("classes");
            if (classes != null && classes.Count > 0)
            {
                var clsNames = new List<string>();
                foreach (var c in classes) clsNames.Add(c.AsString());
                sb.Append(" class=\"").Append(EscXml(string.Join(" ", clsNames.ToArray()))).Append("\"");
            }

            string text = node.GetString("text");
            if (!string.IsNullOrEmpty(text)) sb.Append(" text=\"").Append(EscXml(text)).Append("\"");

            string style = node.GetString("style");
            if (!string.IsNullOrEmpty(style)) sb.Append(" style=\"").Append(EscXml(style)).Append("\"");

            string bindingPath = node.GetString("bindingPath");
            if (!string.IsNullOrEmpty(bindingPath)) sb.Append(" binding-path=\"").Append(EscXml(bindingPath)).Append("\"");

            var attrs = node.GetArray("attributes");
            if (attrs != null)
            {
                foreach (var attr in attrs)
                {
                    string aName = attr.GetString("name");
                    string aVal = attr.GetString("value");
                    if (!string.IsNullOrEmpty(aName))
                        sb.Append(" ").Append(aName).Append("=\"").Append(EscXml(aVal ?? "")).Append("\"");
                }
            }

            var children = node.GetArray("children");
            if (children != null && children.Count > 0)
            {
                sb.AppendLine(">");
                foreach (var child in children)
                    GenerateUXMLElement(child, sb, indent + 1);
                sb.Append(padding).AppendLine("</" + prefix + type + ">");
            }
            else
            {
                sb.AppendLine(" />");
            }
        }

        private static string ResolveAssetPath(string assetPath)
        {
            if (assetPath.StartsWith("Assets/"))
                return Path.Combine(Application.dataPath.Replace("/Assets", ""), assetPath.Replace("/", "\\"));
            return assetPath;
        }

        private static string EscXml(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
