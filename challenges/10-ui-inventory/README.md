# Challenge 10: UI Inventory & Hierarchy Mapper

## Overview
Build UIInventoryTool (Brief Tool #10) — scans all canvases in a scene, outputs complete UI tree with visual properties, layout settings, and interaction bindings as JSON.

## Brief Reference
Section 5.1 (UI Introspection) — All capabilities: canvas render modes, UI hierarchy mapping, visual properties (colors/fonts/sprites), layout groups (Horizontal/Vertical/Grid with spacing/padding), interactive elements (Button/Toggle/Slider/InputField/Dropdown with event listeners).

## Problem Statement
UI is the most visually-sensitive part of Unity development. Before creating or modifying UI, the agent must understand current structure: canvas configuration, element nesting, layout groups, colors, fonts, and event bindings. Without this, new UI will clash with existing designs.

## Success Criteria
1. `UnityBridge.UIInventoryTool.GetUIInventory(string scenePath)` returns complete UI structure
2. Canvas entries include: renderMode, sortOrder, referenceCamera, hasGraphicRaycaster
3. Nested tree with parent-child relationships
4. RectTransform properties: anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta
5. Visual properties: Image color/sprite/type, Text/TMP content/font/fontSize/color/alignment
6. Layout groups: type, spacing, padding, childAlignment, childForceExpand
7. Interactive elements: Button onClick listeners, Toggle/Slider/InputField bindings
8. Event listeners: target object name, method name, argument type
9. ContentSizeFitter and LayoutElement where present
10. Works with UGUI and identifies UI Toolkit usage

## Expected Development Work
### New Files
- `Unity-Bridge/Editor/Tools/UIInventoryTool.cs` — Opens scene, finds Canvas components, recursively extracts: RectTransform, Image, Text/TMP_Text, Button/Toggle/Slider, LayoutGroup, CanvasGroup, ContentSizeFitter. Uses `UnityEventBase.GetPersistentEventCount()` for listener extraction.

### JSON Output
```json
{
  "scenePath": "Assets/Scenes/MainMenu.unity",
  "canvases": [{
    "name": "HUDCanvas", "renderMode": "ScreenSpaceOverlay", "sortOrder": 0,
    "children": [{
      "name": "Panel", "rectTransform": {"anchorMin": {"x":0,"y":0}, "anchorMax": {"x":1,"y":1}},
      "image": {"color": "#FFFFFF80"}, "layoutGroup": {"type": "Vertical", "spacing": 10},
      "children": [{
        "name": "StartButton", "interaction": {"type": "Button", "listeners": [{"target": "GameManager", "method": "StartGame"}]}
      }]
    }]
  }]
}
```

## Testing Protocol
1. `bash .agent/tools/unity_bridge.sh compile` — Confirm
2. `bash .agent/tools/unity_bridge.sh execute UnityBridge.UIInventoryTool.GetUIInventory '["Assets/Scenes/SampleScene.unity"]'` — Read output
3. If no UI exists, create a Canvas with Button first, compile, then re-run
4. Verify Canvas render mode, RectTransform values, interactive elements
5. Test with layout groups if available

## Dependencies
- Challenge 01 (Execute Endpoint)
- Challenge 02 (Scene Inventory — scene opening patterns)
