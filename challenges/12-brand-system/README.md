# Challenge 12: Brand System & Style Consistency

## Overview
Build a brand/style system accepting a brand specification (colors, fonts, spacing) and applying it consistently across all UI. Includes brand extraction from existing scenes and token-based styling for the UIBuilder.

## Brief Reference
Section 5.2 — "Apply consistent branding: Accept a 'brand spec' (primary/secondary/accent colours, font family, font sizes H1/H2/body, corner radius, spacing scale)." Section 12 Example — "learns existing UI uses TextMeshPro, primary colour #1A73E8, font Roboto."

## Problem Statement
Without a brand system, every UI element needs explicit color/font/spacing values, leading to inconsistency. A brand system provides a single source of truth referenced symbolically ("@primaryColor") rather than literally ("#1A73E8"). It also extracts brand specs from existing UI automatically.

## Success Criteria
1. `UnityBridge.BrandSystem.SetBrandSpec(string jsonSpec)` stores brand spec
2. `UnityBridge.BrandSystem.GetBrandSpec()` returns current spec
3. `UnityBridge.BrandSystem.ExtractBrandFromScene(string scenePath)` infers brand from existing UI (common colors, fonts, spacing)
4. Spec includes: primaryColor, secondaryColor, accentColor, backgroundColor, textPrimaryColor, fontFamily, fontSizes (h1/h2/body/caption), spacing (sm/md/lg), cornerRadius, buttonHeight
5. `UnityBridge.BrandSystem.ApplyBrand(string scenePath)` re-applies brand to all UI in scene
6. UIBuilder (Challenge 11) can reference tokens: `"color": "@primaryColor"`, `"fontSize": "@h1"`
7. Brand persisted as JSON file at `Assets/Config/brand-spec.json`
8. Extraction identifies most-used colors/fonts via frequency analysis
9. Color contrast validation (WCAG AA < 4.5:1 warning)
10. Returns comparison report when applying brand

## Expected Development Work
### New Files
- `Unity-Bridge/Editor/Tools/BrandSystem.cs` — Brand management, extraction (color/font clustering), application, contrast validation
- `Unity-Bridge/Editor/Tools/BrandSpec.cs` — Data class or JSON serialization model

### Brand Spec JSON
```json
{
  "colors": {"primary": "#1A73E8", "secondary": "#5F6368", "accent": "#EA4335", "background": "#1A1A2E", "textPrimary": "#FFFFFF"},
  "typography": {"fontFamily": "Roboto", "h1": 48, "h2": 32, "body": 18, "caption": 14},
  "spacing": {"sm": 8, "md": 16, "lg": 24},
  "components": {"cornerRadius": 8, "buttonHeight": 48}
}
```

## Testing Protocol
1. `bash .agent/tools/unity_bridge.sh compile` — Confirm
2. SetBrandSpec with test spec — verify stored
3. GetBrandSpec — verify matches
4. If UI exists: ExtractBrandFromScene — verify extracted values
5. Create UI with brand tokens via UIBuilder — verify colors match
6. ApplyBrand on non-conforming UI — verify updates

## Dependencies
- Challenge 01 (Execute Endpoint)
- Challenge 10 (UI Inventory)
- Challenge 11 (UI Builder — token integration)
