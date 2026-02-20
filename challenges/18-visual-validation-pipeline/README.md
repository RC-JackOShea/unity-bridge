# Challenge 18: Visual Validation Pipeline

## Overview

Build a rule-based visual validation system that analyzes screenshot images for structural correctness: non-blank content, expected dimensions, minimum color diversity, pixel-level baseline comparison, and WCAG color contrast checking. This is the automated validation layer that runs deterministic checks on captured screenshots -- the agent's vision model handles subjective layout assessment, while this tool handles objective, repeatable measurements.

## Brief Reference

Section 6.2 (Quality Assurance Tools) -- "Capture visual output at key moments during gameplay for vision-based validation." Section 5.2 (Visual Validation) -- assess "overlapping elements, text overflow, inconsistent spacing, colour contrast issues, elements outside the visible area." Section 12 (Agentic Workflow) -- "passes to vision model for layout validation."

This challenge creates the structured pipeline that connects screenshot capture to validation logic. The rule-based checks (dimensions, blank detection, color count, pixel comparison) run inside Unity. The subjective checks (overlapping elements, text overflow, spacing) are left to the agent's vision model, which consumes this tool's structured reports alongside the raw screenshots.

## Problem Statement

Screenshots alone are unstructured -- a PNG file tells the agent nothing without visual inspection. The agent's vision model can assess layout quality, but it cannot efficiently perform pixel-level comparisons, compute exact contrast ratios, or detect subtle regressions across hundreds of screenshots. These are mechanical checks that should be automated.

Without this pipeline, the agent must visually inspect every screenshot manually, cannot detect single-pixel regressions between builds, has no quantitative contrast measurements for accessibility compliance, and cannot batch-validate a directory of captures from a multi-resolution test run.

The Visual Validation Pipeline provides deterministic, repeatable checks that complement the agent's subjective visual assessment. It answers "is this screenshot valid and does it match the baseline?" so the agent can focus on "does this layout look correct?"

## Success Criteria

1. `VisualValidator.ValidateScreenshot(string screenshotPath, string rulesJson)` loads a PNG file and runs a configurable set of validation rules against it, returning a per-rule pass/fail report.
2. The `notBlank` rule detects all-black and all-white images by checking that the number of unique colors exceeds a minimum threshold (default: 10).
3. The `expectedDimensions` rule checks that the image width and height match specified values.
4. `VisualValidator.CompareWithBaseline(string currentPath, string baselinePath, float threshold)` performs pixel-level comparison between two images, returning the percentage of pixels that differ and a pass/fail result based on the threshold.
5. `CompareWithBaseline` generates a diff image highlighting changed pixels and saves it to a specified or default path.
6. The threshold-based comparison correctly handles identical images (0% diff, always passes) and completely different images (high diff percentage, fails at any reasonable threshold).
7. `VisualValidator.GenerateValidationReport(string directoryPath)` processes all PNG files in a directory, runs all default rules, and returns a comprehensive report with per-file results and an overall summary.
8. `VisualValidator.CheckColorContrast(string screenshotPath, string regionJson)` analyzes a rectangular region of the image and computes the WCAG luminance contrast ratio between the dominant foreground and background colors.
9. All methods return structured error JSON when given corrupt files, missing files, non-PNG formats, or invalid parameters -- no unhandled exceptions.
10. All `Texture2D` objects created during validation are properly destroyed after use to prevent memory leaks.

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/VisualValidator.cs`** -- Static class in the `UnityBridge` namespace. Must include:

  - `public static string ValidateScreenshot(string screenshotPath, string rulesJson)` -- Loads the image at `screenshotPath` into a `Texture2D`, runs each rule specified in `rulesJson`, and returns results. Rules JSON:
    ```json
    {
      "rules": [
        {"name": "notBlank", "minUniqueColors": 10},
        {"name": "expectedDimensions", "width": 1920, "height": 1080},
        {"name": "minColorDiversity", "minUniqueColors": 50}
      ]
    }
    ```
    If `rulesJson` is null or empty, run a default set: `notBlank`, `minColorDiversity` (threshold 10). Returns JSON:
    ```json
    {
      "success": true,
      "screenshotPath": "C:/temp/screen.png",
      "imageWidth": 1920,
      "imageHeight": 1080,
      "rules": [
        {"name": "notBlank", "passed": true, "details": "Found 1847 unique colors (minimum: 10)"},
        {"name": "expectedDimensions", "passed": true, "details": "Image is 1920x1080, expected 1920x1080"},
        {"name": "minColorDiversity", "passed": true, "details": "Found 1847 unique colors (minimum: 50)"}
      ],
      "summary": {"total": 3, "passed": 3, "failed": 0}
    }
    ```

  - `public static string CompareWithBaseline(string currentPath, string baselinePath, float threshold)` -- Loads both images, compares pixel-by-pixel, computes diff percentage. Threshold is the maximum allowed diff percentage (e.g., 5.0 means 5% of pixels may differ). Generates a diff image where changed pixels are highlighted in magenta and unchanged pixels are darkened. Returns JSON:
    ```json
    {
      "success": true,
      "currentPath": "C:/temp/current.png",
      "baselinePath": "C:/temp/baseline.png",
      "diffPercentage": 2.5,
      "passed": true,
      "threshold": 5.0,
      "totalPixels": 2073600,
      "changedPixels": 51840,
      "diffImagePath": "C:/temp/diff.png"
    }
    ```
    If images have different dimensions:
    ```json
    {
      "success": false,
      "error": "Dimension mismatch: current is 1920x1080, baseline is 1280x720"
    }
    ```

  - `public static string GenerateValidationReport(string directoryPath)` -- Finds all `.png` files in the directory, runs the default rule set on each, and returns a consolidated report. Returns JSON:
    ```json
    {
      "success": true,
      "directory": "C:/temp/screenshots",
      "files": [
        {
          "path": "C:/temp/screenshots/screen_1920x1080.png",
          "rules": [
            {"name": "notBlank", "passed": true, "details": "..."},
            {"name": "minColorDiversity", "passed": true, "details": "..."}
          ],
          "passed": true
        },
        {
          "path": "C:/temp/screenshots/screen_1280x720.png",
          "rules": [
            {"name": "notBlank", "passed": false, "details": "Found 1 unique color (minimum: 10)"}
          ],
          "passed": false
        }
      ],
      "summary": {"totalFiles": 2, "passedFiles": 1, "failedFiles": 1}
    }
    ```

  - `public static string CheckColorContrast(string screenshotPath, string regionJson)` -- Analyzes a rectangular region for WCAG contrast compliance. Region JSON:
    ```json
    {
      "x": 100,
      "y": 200,
      "width": 400,
      "height": 50
    }
    ```
    Extracts pixels from the region, clusters colors into foreground and background groups (the most common color is background, second most common is foreground), computes relative luminance for each, and calculates the WCAG contrast ratio. Returns JSON:
    ```json
    {
      "success": true,
      "region": {"x": 100, "y": 200, "width": 400, "height": 50},
      "foregroundColor": {"r": 33, "g": 33, "b": 33},
      "backgroundColor": {"r": 255, "g": 255, "b": 255},
      "contrastRatio": 12.63,
      "meetsAA": true,
      "meetsAAA": true,
      "meetsAALargeText": true
    }
    ```
    WCAG thresholds: AA normal text >= 4.5:1, AA large text >= 3:1, AAA normal text >= 7:1.

### Key Implementation Details

- **Image loading**: Use `Texture2D.LoadImage(byte[])` from `System.IO.File.ReadAllBytes(path)`. Create the texture with `new Texture2D(2, 2)` and let `LoadImage` resize it. Always wrap in try/finally to `DestroyImmediate` the texture after use.
- **Unique color counting**: For `notBlank` and `minColorDiversity`, get all pixels via `texture.GetPixels32()`, convert each `Color32` to an int key (`(r << 16) | (g << 8) | b`), and count distinct values with a `HashSet<int>`. For large textures, consider sampling (e.g., every Nth pixel) to avoid excessive memory or time.
- **Pixel comparison**: For `CompareWithBaseline`, iterate both pixel arrays simultaneously. A pixel "differs" if any channel delta exceeds a per-channel tolerance (default 2, to account for compression artifacts). Track changed pixel count and generate the diff image by writing magenta for changed pixels and darkened original for unchanged.
- **Diff image output**: Create a new `Texture2D` for the diff, encode via `texture.EncodeToPNG()`, write to disk with `File.WriteAllBytes`. The diff image path defaults to the current image path with `_diff` suffix if not specified.
- **WCAG contrast ratio**: Relative luminance: `L = 0.2126 * R + 0.7152 * G + 0.0722 * B` where each channel is linearized: if `sRGB <= 0.03928` then `linear = sRGB / 12.92` else `linear = ((sRGB + 0.055) / 1.055) ^ 2.4`, with `sRGB = channel / 255.0`. Contrast ratio: `(L1 + 0.05) / (L2 + 0.05)` where L1 is the lighter luminance.
- **Color clustering for contrast**: Simple approach -- histogram all colors in the region, take the two most frequent colors as foreground and background. More robust approach -- k-means with k=2. The simple histogram approach is sufficient for typical UI regions (text on solid background).
- **Memory management**: Every `Texture2D` created must be destroyed with `UnityEngine.Object.DestroyImmediate(texture)` in a finally block. The `GetPixels32` array is managed and will be garbage collected, but avoid holding references longer than needed.
- **Error handling**: `File.Exists` check before loading. Wrap `LoadImage` in try/catch -- if it returns false, the file is corrupt or not a valid image format. Return structured error JSON for all failure cases.

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh health` -- Read `C:/temp/unity_bridge_output.txt`, confirm server is running.
2. Create `Unity-Bridge/Editor/Tools/VisualValidator.cs` with all four methods.
3. `bash .agent/tools/unity_bridge.sh compile` -- Read output, confirm compilation succeeds with no errors.
4. Capture a test screenshot: `bash .agent/tools/unity_bridge.sh play enter` -- Read output. `bash .agent/tools/unity_bridge.sh screenshot C:/temp/test_visual.png` -- Read output. `bash .agent/tools/unity_bridge.sh play exit` -- Read output.
5. `bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.ValidateScreenshot '["C:/temp/test_visual.png", ""]'` -- Read output, verify rules passed (notBlank, minColorDiversity).
6. Test with explicit dimension rule: `bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.ValidateScreenshot '["C:/temp/test_visual.png", "{\"rules\":[{\"name\":\"expectedDimensions\",\"width\":1920,\"height\":1080}]}"]'` -- Read output, verify dimension check result.
7. Test baseline comparison with identical image: `bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.CompareWithBaseline '["C:/temp/test_visual.png", "C:/temp/test_visual.png", 5.0]'` -- Read output, verify `diffPercentage` is 0 and `passed` is true.
8. Capture a second screenshot with a different scene state, then compare against the first: verify non-zero diff percentage and that the diff image is generated at the expected path.
9. Test directory report: place multiple PNGs in `C:/temp/screenshots/`, run `bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.GenerateValidationReport '["C:/temp/screenshots"]'` -- Read output, verify per-file results and summary counts.
10. Test color contrast: `bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.CheckColorContrast '["C:/temp/test_visual.png", "{\"x\":100,\"y\":200,\"width\":400,\"height\":50}"]'` -- Read output, verify contrast ratio and WCAG pass/fail fields.
11. Test missing file: `bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.ValidateScreenshot '["C:/temp/nonexistent.png", ""]'` -- Read output, verify structured error JSON.
12. Test corrupt file: create a file with invalid content at `C:/temp/corrupt.png`, run `ValidateScreenshot` against it, verify graceful error handling.

## Dependencies

- **Challenge 01 (Execute Endpoint)** -- All methods are invoked via `bash .agent/tools/unity_bridge.sh execute UnityBridge.VisualValidator.<Method>`.
- **Challenge 13 (Multi-Resolution Screenshot Validator)** -- Provides the multi-resolution screenshot captures that `GenerateValidationReport` processes. The directory of screenshots from Challenge 13's `CaptureMultiResolution` is a primary input for batch validation.
