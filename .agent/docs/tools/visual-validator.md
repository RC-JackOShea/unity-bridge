# VisualValidator

Rule-based visual validation for screenshot images. Checks for non-blank content, expected dimensions, color diversity, pixel-level baseline comparison, and WCAG color contrast. All Texture2D objects are cleaned up after use.

## Key Methods

| Method | Description |
|--------|-------------|
| `ValidateScreenshot(screenshotPath, rulesJson)` | Run validation rules against a screenshot PNG |
| `CompareWithBaseline(currentPath, baselinePath, threshold)` | Pixel-level diff between two images |
| `GenerateValidationReport(directoryPath)` | Batch-validate all PNGs in a directory |
| `CheckColorContrast(screenshotPath, regionJson)` | WCAG contrast check on a screenshot region |

## Usage

```bash
bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.ValidateScreenshot '["C:/temp/screen.png", "<rulesJson>"]'
bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.CompareWithBaseline '["C:/temp/current.png", "C:/temp/baseline.png", "1.0"]'
bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.GenerateValidationReport '["C:/temp/screenshots"]'
bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.CheckColorContrast '["C:/temp/screen.png", "{\"x\":100,\"y\":200,\"width\":300,\"height\":50}"]'
```

## Validation Rules

Pass as the second argument to `ValidateScreenshot`. Omit or pass `""` for defaults (notBlank only).

```json
{
  "notBlank": { "enabled": true, "minColors": 10 },
  "expectedDimensions": { "width": 1920, "height": 1080 },
  "minColorDiversity": { "minColors": 50 }
}
```

| Rule | Default | Description |
|------|---------|-------------|
| `notBlank` | enabled, minColors=10 | Rejects solid-color or near-blank images |
| `expectedDimensions` | off | Checks exact width/height match |
| `minColorDiversity` | off | Ensures minimum number of unique colors in the image |

## CompareWithBaseline

Compares two same-resolution PNGs pixel-by-pixel. Generates a `_diff.png` highlighting changed pixels in magenta. The `threshold` is the maximum allowed percentage of different pixels (default 1.0%).

```json
{"success":true,"passed":true,"differencePercent":0.0234,"differentPixels":45,"totalPixels":2073600,"threshold":1.0,"diffImagePath":"C:/temp/current_diff.png"}
```

## CheckColorContrast

Finds the two most dominant colors in a region and computes their WCAG contrast ratio.

```json
{"success":true,"contrastRatio":7.43,"wcagAA":true,"wcagAAA":true,"backgroundColor":"#FFFFFF","foregroundColor":"#333333","region":{"x":0,"y":0,"width":1920,"height":1080}}
```

- `wcagAA`: ratio >= 4.5:1 (normal text)
- `wcagAAA`: ratio >= 7.0:1 (enhanced)

## Examples

```bash
# Basic screenshot validation (not blank)
bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.ValidateScreenshot '["C:/temp/screen.png", ""]'

# Full validation with all rules
bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.ValidateScreenshot '["C:/temp/screen.png", "{\"notBlank\":{\"minColors\":10},\"expectedDimensions\":{\"width\":1920,\"height\":1080},\"minColorDiversity\":{\"minColors\":100}}"]'

# Compare against a baseline with 2% tolerance
bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.CompareWithBaseline '["C:/temp/current.png", "C:/temp/baseline.png", "2.0"]'

# Check contrast in a button region
bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.CheckColorContrast '["C:/temp/screen.png", "{\"x\":50,\"y\":50,\"width\":200,\"height\":60}"]'
```

## Common Pitfalls

- Files must be PNG format. The path must be an absolute filesystem path, not an `Assets/` path.
- `CompareWithBaseline` requires identical resolutions. Mismatched images return `differencePercent: 100.0`.
- The diff image is saved alongside the current image with `_diff.png` suffix. Existing diff files are overwritten.
- `GenerateValidationReport` skips files ending in `_diff.png` to avoid validating diff output.
- Color quantization in `CheckColorContrast` rounds to nearest 16 per channel, so subtle gradients may be grouped together.
