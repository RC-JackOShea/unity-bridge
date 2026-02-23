# BrandSystem

Brand spec management, extraction, application, and WCAG contrast validation. Stores brand definitions (colors, typography, spacing) as JSON, extracts brand from existing scenes via frequency analysis, and applies brand styling to all UI elements in a scene.

## Key Methods

| Method | Description |
|--------|-------------|
| `SetBrandSpec(jsonSpec)` | Save a brand spec to `Assets/Config/brand-spec.json` |
| `GetBrandSpec()` | Load and return the current brand spec |
| `ExtractBrandFromScene(scenePath)` | Analyze a scene's UI elements and derive a brand spec |
| `ApplyBrand(scenePath)` | Apply the current brand spec to all UI in a scene |
| `ResolveBrandToken(token)` | Resolve a `@token` reference to its brand spec value |

## Usage

```bash
bash .agent/tools/unity_bridge.sh execute UnityBridge.BrandSystem.SetBrandSpec '<jsonSpec>'
bash .agent/tools/unity_bridge.sh execute UnityBridge.BrandSystem.GetBrandSpec
bash .agent/tools/unity_bridge.sh execute UnityBridge.BrandSystem.ExtractBrandFromScene '["Assets/Scenes/SampleScene.unity"]'
bash .agent/tools/unity_bridge.sh execute UnityBridge.BrandSystem.ApplyBrand '["Assets/Scenes/SampleScene.unity"]'
bash .agent/tools/unity_bridge.sh execute UnityBridge.BrandSystem.ResolveBrandToken '["@primary"]'
```

## Brand Spec Format

```json
{
  "colors": {
    "primary": "#1A73E8",
    "secondary": "#5F6368",
    "accent": "#EA4335",
    "background": "#FFFFFF",
    "surface": "#F5F5F5"
  },
  "typography": {
    "fontFamily": "Roboto",
    "h1": 48,
    "h2": 32,
    "body": 18,
    "caption": 14
  },
  "spacing": {
    "small": 8,
    "medium": 16,
    "large": 32
  },
  "components": {
    "borderRadius": 8,
    "buttonHeight": 48
  }
}
```

## Brand Tokens

`ResolveBrandToken` maps `@key` references to values from the spec. It searches in order: `colors`, `typography`, `spacing`, `components`.

- `@primary` resolves to `"#1A73E8"`
- `@body` resolves to `"18"`
- `@medium` resolves to `"16"`

## ExtractBrandFromScene Response

```json
{
  "success": true,
  "elementsAnalyzed": 24,
  "extractedSpec": {
    "colors": {"primary": "#2196F3", "secondary": "#FFFFFF", "accent": "#FF5722"},
    "typography": {"fontFamily": "Arial", "h1": 48, "h2": 32, "body": 18, "caption": 14}
  },
  "contrastWarnings": ["Primary/secondary color contrast ratio 3.20:1 fails WCAG AA (minimum 4.5:1)"]
}
```

The extraction uses frequency analysis: the most common UI color becomes `primary`, second most becomes `secondary`, etc.

## ApplyBrand Behavior

`ApplyBrand` modifies the scene in place and saves it. Currently applies:
- **Primary color** to all Button `Image` components
- **Body font size** to all `Text` and `TextMeshProUGUI` components

## Examples

```bash
# Define a brand spec
bash .agent/tools/unity_bridge.sh execute UnityBridge.BrandSystem.SetBrandSpec '{"colors":{"primary":"#4CAF50","secondary":"#212121","accent":"#FF9800"},"typography":{"fontFamily":"Roboto","h1":48,"h2":32,"body":20,"caption":14}}'

# Extract brand from an existing scene
bash .agent/tools/unity_bridge.sh execute UnityBridge.BrandSystem.ExtractBrandFromScene '["Assets/Scenes/SampleScene.unity"]'

# Apply saved brand to a scene
bash .agent/tools/unity_bridge.sh execute UnityBridge.BrandSystem.ApplyBrand '["Assets/Scenes/SampleScene.unity"]'

# Resolve a brand token
bash .agent/tools/unity_bridge.sh execute UnityBridge.BrandSystem.ResolveBrandToken '["@primary"]'
# Returns: "#4CAF50"
```

## Common Pitfalls

- `SetBrandSpec` persists to `Assets/Config/brand-spec.json`. This file must be committed if you want the brand to survive across sessions.
- `ApplyBrand` opens the scene in `Single` mode -- any unsaved changes in the currently open scene will be lost. Save first.
- `ExtractBrandFromScene` opens the scene in `Additive` mode and closes it after analysis -- safer for the current scene.
- Brand application is currently limited to button colors and text font sizes. Manual adjustment may be needed for other properties.
- `ResolveBrandToken` returns the token unchanged if no match is found (e.g., `@unknown` returns `"@unknown"`).
