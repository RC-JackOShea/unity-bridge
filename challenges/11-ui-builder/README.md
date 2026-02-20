# Challenge 11: UI Builder

## Overview
Build UIBuilder (Brief Tool #11) — accepts JSON UI specification, creates corresponding UI hierarchy in a scene with layout groups, anchoring, colors, fonts, and event bindings. The agent's primary tool for programmatic UI creation.

## Brief Reference
Section 5.2 (UI Creation) — "Create UI elements programmatically: Canvas, Panel, Button, Text (TextMeshPro), Image, Layout Groups. Layout intelligence: proper anchoring, responsive layout groups, correct pivot points."

## Problem Statement
UI creation is the most GUI-dependent Unity task. The agent needs a specification-driven builder that creates complete, properly-configured UI hierarchies from JSON — handling element creation, correct anchoring, layout group configuration, and visual properties.

## Success Criteria
1. `UnityBridge.UIBuilder.BuildUI(string jsonSpec)` creates UI hierarchy in current scene
2. `UnityBridge.UIBuilder.BuildUIPrefab(string jsonSpec)` creates UI as prefab asset
3. Supports: Canvas, Panel, Button, TextMeshPro, Image, RawImage, Toggle, Slider, InputField, Dropdown, ScrollView
4. Sets RectTransform: anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta
5. Configures layout groups: Horizontal/Vertical/Grid with spacing, padding, alignment
6. Sets visual properties: Image color, Text content/font/size/color/alignment
7. Configures Button ColorBlock: normal, highlighted, pressed, selected colors
8. Adds CanvasGroup, ContentSizeFitter, LayoutElement where specified
9. Creates EventSystem with InputSystemUIInputModule if not present
10. Returns JSON result with created hierarchy summary
11. TextMeshPro preferred, falls back to legacy Text
12. CanvasScaler configured for responsive scaling

## Expected Development Work
### New Files
- `Unity-Bridge/Editor/Tools/UIBuilder.cs` — Recursive JSON-to-UI builder. Per element: create GO, add RectTransform, set anchors, add visual component, set properties, add layout/interaction components, recurse children. Handles Canvas creation with GraphicRaycaster and CanvasScaler.

### JSON Spec Format
```json
{
  "outputMode": "scene",
  "canvas": {
    "name": "MenuCanvas", "renderMode": "ScreenSpaceOverlay",
    "scaler": {"uiScaleMode": "ScaleWithScreenSize", "referenceResolution": {"x": 1920, "y": 1080}},
    "children": [{
      "name": "Panel", "type": "Panel",
      "rectTransform": {"anchorMin": {"x":0.3,"y":0.2}, "anchorMax": {"x":0.7,"y":0.8}},
      "image": {"color": "#16213ECC"},
      "layout": {"type": "VerticalLayoutGroup", "spacing": 20, "padding": {"left":40,"right":40,"top":40,"bottom":40}},
      "children": [
        {"name": "Title", "type": "TextMeshPro", "text": {"content": "Menu", "fontSize": 48, "color": "#FFFFFF"}},
        {"name": "PlayBtn", "type": "Button", "button": {"colors": {"normal": "#0F3460", "highlighted": "#1A73E8"}},
         "children": [{"name": "Text", "type": "TextMeshPro", "text": {"content": "Play", "fontSize": 24, "color": "#FFFFFF"}}]}
      ]
    }]
  }
}
```

## Testing Protocol
1. `bash .agent/tools/unity_bridge.sh compile` — Confirm
2. Execute BuildUI with a simple spec (canvas + button)
3. `bash .agent/tools/unity_bridge.sh execute UnityBridge.UIInventoryTool.GetUIInventory '["Assets/Scenes/SampleScene.unity"]'` — Verify created UI
4. `bash .agent/tools/unity_bridge.sh play enter` — Enter play mode
5. `bash .agent/tools/unity_bridge.sh screenshot C:/temp/ui_test.png` — Verify visible
6. `bash .agent/tools/unity_bridge.sh input tap 960 540` — Test interactivity
7. `bash .agent/tools/unity_bridge.sh play exit`
8. Test BuildUIPrefab — verify prefab created

## Dependencies
- Challenge 01 (Execute Endpoint)
- Challenge 10 (UI Inventory — for verification)
- Challenge 05 (Prefab Creator — for prefab saving patterns)
