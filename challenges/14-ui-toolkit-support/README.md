# Challenge 14: UI Toolkit Support (UXML & USS)

## Overview
Build tools for parsing and generating UI Toolkit files — UXML (structure) and USS (styles). Supports modern Unity UI approach alongside traditional UGUI.

## Brief Reference
Section 5.3 (UI Toolkit) — "Parse UXML files, parse USS files, generate UXML/USS programmatically, bind UI to data."

## Problem Statement
UI Toolkit uses XML-like UXML for structure and CSS-like USS for styling — fundamentally different from UGUI. An agent needs tooling for both systems to work with any project. UXML/USS are text-based and parseable, but structured tooling ensures reliability and token efficiency.

## Success Criteria
1. `UnityBridge.UIToolkitTools.ParseUXML(string filePath)` returns JSON hierarchy with attributes
2. `UnityBridge.UIToolkitTools.ParseUSS(string filePath)` returns JSON selectors/properties
3. `UnityBridge.UIToolkitTools.GenerateUXML(string jsonSpec)` creates UXML file
4. `UnityBridge.UIToolkitTools.GenerateUSS(string jsonSpec)` creates USS file
5. `UnityBridge.UIToolkitTools.FindUIToolkitUsage()` inventories UXML/USS files
6. UXML parsing: element types, names, classes, inline styles, template refs, binding-path
7. USS parsing: selectors, property declarations, --custom-property variables
8. Generated UXML is valid and loadable
9. Generated USS is properly formatted
10. Handles data binding syntax

## Expected Development Work
### New Files
- `Unity-Bridge/Editor/Tools/UIToolkitTools.cs` — XML parsing (System.Xml.Linq) for UXML, custom CSS-subset parser for USS, file generation, asset discovery.

### UXML Output
```json
{
  "filePath": "Assets/UI/Menu.uxml",
  "rootElement": {"type": "UXML", "children": [
    {"type": "VisualElement", "name": "root", "classes": ["container"], "children": [
      {"type": "Label", "name": "title", "text": "Menu"},
      {"type": "Button", "name": "play-btn", "text": "Play", "classes": ["primary-btn"]}
    ]}
  ]}
}
```

### USS Output
```json
{
  "rules": [{"selector": ".container", "declarations": [{"property": "flex-grow", "value": "1"}]}],
  "variables": [{"name": "--primary-color", "value": "#1A73E8"}]
}
```

## Testing Protocol
1. `bash .agent/tools/unity_bridge.sh compile` — Confirm
2. FindUIToolkitUsage — Check for existing UXML/USS
3. GenerateUXML + GenerateUSS with test specs
4. Parse generated files — verify round-trip consistency
5. Verify generated UXML is valid XML

## Dependencies
- Challenge 01 (Execute Endpoint)
- Challenge 09 (Asset Inventory — discovering files)
