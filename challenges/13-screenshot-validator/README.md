# Challenge 13: Multi-Resolution Screenshot Validator

## Overview
Build UIScreenshotValidator (Brief Tool #12) — captures screenshots at multiple resolutions in a single Play mode session and provides pixel-level comparison for visual regression testing.

## Brief Reference
Section 5.2 — "Visual validation: Capture screenshots at multiple resolutions via ScreenCapture.CaptureScreenshot(), assess overlapping elements, text overflow, inconsistent spacing, colour contrast, elements outside visible area."

## Problem Statement
Code-level correctness doesn't guarantee visual correctness. Elements may overlap, text may overflow, colors may clash. The only reliable verification is rendering at target resolutions and examining output. The bridge supports single screenshots; this extends to multi-resolution capture with comparison.

## Success Criteria
1. `UnityBridge.ScreenshotValidator.CaptureMultiResolution(string jsonSpec)` captures at multiple resolutions
2. Supports: 1920x1080, 1280x720, 2560x1440, 1080x1920 (portrait), 2436x1125 (iPhone X)
3. Sets Game view size via GameView reflection before each capture
4. Saves with resolution in filename (e.g., `screenshot_1920x1080.png`)
5. Returns manifest JSON: paths, resolutions, file sizes
6. `UnityBridge.ScreenshotValidator.CompareScreenshots(string baseline, string current)` pixel comparison
7. Returns: difference percentage, pass/fail
8. Deterministic captures (same state produces same output)
9. Documents ScreenSpaceOverlay limitation
10. Clean Play mode exit after all captures

## Expected Development Work
### New Files
- `Unity-Bridge/Editor/Tools/ScreenshotValidator.cs` — Uses GameView reflection for resolution setting. Sequence: enter play, wait stable, for each resolution: set size, wait, capture, save. Comparison via Texture2D.GetPixels().

### Spec Format
```json
{
  "outputDirectory": "C:/temp/screenshots",
  "resolutions": [{"width": 1920, "height": 1080, "label": "1080p"}, {"width": 1280, "height": 720, "label": "720p"}],
  "delayFrames": 3
}
```

## Testing Protocol
1. `bash .agent/tools/unity_bridge.sh compile` — Confirm
2. Execute CaptureMultiResolution with spec
3. Verify screenshots exist at each path with correct resolution
4. Take second set, run CompareScreenshots — verify 0% difference
5. Modify something visible, re-compare — verify difference detected

## Dependencies
- Challenge 01 (Execute Endpoint)
- Existing ScreenshotCapture.cs
