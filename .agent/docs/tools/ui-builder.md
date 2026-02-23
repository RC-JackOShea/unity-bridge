# UIBuilder

JSON-driven UI hierarchy builder. Creates Canvas, Panel, Button, Text (TMP preferred), Image, RawImage, Toggle, Slider, InputField, Dropdown, and ScrollView elements from a JSON spec. Handles RectTransform anchoring, layout groups, and event systems.

## Key Methods

| Method | Description |
|--------|-------------|
| `BuildUI(jsonSpec)` | Create a UI hierarchy in the active scene |
| `BuildUIPrefab(jsonSpec)` | Create a UI hierarchy and save it as a prefab asset |

## Usage

```bash
bash .agent/tools/unity_bridge.sh execute UnityBridge.UIBuilder.BuildUI '<jsonSpec>'
bash .agent/tools/unity_bridge.sh execute UnityBridge.UIBuilder.BuildUIPrefab '<jsonSpec>'
```

## Element Types

`canvas`, `panel`, `button`, `text` (aliases: `textmeshpro`, `tmp`), `image`, `rawimage`, `toggle`, `slider`, `inputfield`, `dropdown`, `scrollview`.

## JSON Spec Structure

```json
{
  "type": "Canvas",
  "name": "MainCanvas",
  "renderMode": "overlay",
  "referenceResolution": {"x": 1920, "y": 1080},
  "children": [
    {
      "type": "Panel",
      "name": "Header",
      "color": "#2196F3",
      "anchorMin": {"x": 0, "y": 0.9},
      "anchorMax": {"x": 1, "y": 1},
      "layoutGroup": {"type": "horizontal", "spacing": 10, "padding": {"left": 20, "right": 20}},
      "children": [
        {
          "type": "Text",
          "name": "Title",
          "text": "My App",
          "fontSize": 36,
          "textColor": "#FFFFFF",
          "alignment": "Center"
        },
        {
          "type": "Button",
          "name": "MenuBtn",
          "text": "Menu",
          "sizeDelta": {"x": 120, "y": 40},
          "colorBlock": {"normal": "#4CAF50", "pressed": "#388E3C"}
        }
      ]
    }
  ],
  "prefabPath": "Assets/UI/MainCanvas.prefab"
}
```

## RectTransform Properties

All elements support: `anchorMin`, `anchorMax`, `pivot`, `anchoredPosition`, `sizeDelta` -- each as `{"x": ..., "y": ...}`.

## Layout Options

- `layoutGroup`: `{"type": "vertical|horizontal|grid", "spacing": 10, "padding": {...}, "childAlignment": "MiddleCenter", "cellSize": {"x":100,"y":100}}`
- `layoutElement`: `{"minWidth":..., "minHeight":..., "preferredWidth":..., "preferredHeight":..., "flexibleWidth":..., "flexibleHeight":...}`
- `contentSizeFitter`: `{"horizontalFit": "PreferredSize", "verticalFit": "MinSize"}`
- `canvasGroup`: `{"alpha": 1.0, "interactable": true, "blocksRaycasts": true}`

## Colors

Colors accept hex strings (`"#FF5722"`, `"#FF572288"`) or RGBA objects (`{"r":1,"g":0,"b":0,"a":1}`).

## Examples

```bash
# Create a simple HUD in the scene
bash .agent/tools/unity_bridge.sh execute UnityBridge.UIBuilder.BuildUI '{"type":"Canvas","name":"HUD","children":[{"type":"Text","name":"Score","text":"Score: 0","fontSize":24,"textColor":"#FFFFFF","anchorMin":{"x":0,"y":0.95},"anchorMax":{"x":0.3,"y":1}}]}'

# Save a button bar as a prefab
bash .agent/tools/unity_bridge.sh execute UnityBridge.UIBuilder.BuildUIPrefab '{"type":"Canvas","name":"ButtonBar","prefabPath":"Assets/UI/ButtonBar.prefab","children":[{"type":"Button","name":"Play","text":"Play","sizeDelta":{"x":200,"y":60}}]}'
```

## Common Pitfalls

- `BuildUI` places objects in the active scene -- they are lost on scene reload unless saved. Use `BuildUIPrefab` for persistent assets.
- An `EventSystem` is auto-created if missing; it uses `InputSystemUIInputModule` when the new Input System is available.
- TMP is preferred for text. If TextMeshPro is not installed, it falls back to legacy `UnityEngine.UI.Text`.
- `prefabPath` is only used by `BuildUIPrefab` and defaults to `Assets/UI/GeneratedUI.prefab`.
- Canvas `renderMode` values: `"overlay"` (default), `"camera"`, `"worldspace"`.
