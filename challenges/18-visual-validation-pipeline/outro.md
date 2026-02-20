# Challenge 18: Post-Completion Checklist

## Documentation Updates

- [ ] Add `VisualValidator` methods to `CLAUDE.md` or a tools reference document: `ValidateScreenshot`, `CompareWithBaseline`, `GenerateValidationReport`, `CheckColorContrast`
- [ ] Document all supported validation rules: `notBlank`, `expectedDimensions`, `minColorDiversity` -- including their configurable parameters and default values
- [ ] Document the pixel comparison algorithm: per-channel tolerance, diff percentage calculation, diff image generation format
- [ ] Document the WCAG contrast ratio calculation: sRGB linearization formula, relative luminance computation, AA/AAA thresholds for normal and large text
- [ ] Document the JSON input and output formats for every method, including error responses for missing/corrupt files

## Verification Steps

- [ ] Run `ValidateScreenshot` with default rules on a normal game screenshot -- confirm `notBlank` and `minColorDiversity` both pass
- [ ] Run `ValidateScreenshot` with `expectedDimensions` rule on a screenshot of known resolution -- confirm pass with correct dimensions reported
- [ ] Create a solid-color (all black or all white) test image and run `ValidateScreenshot` -- confirm `notBlank` rule fails with details showing 1 unique color
- [ ] Run `CompareWithBaseline` with two identical images -- confirm `diffPercentage` is 0.0 and `passed` is true
- [ ] Run `CompareWithBaseline` with two visually different screenshots -- confirm non-zero `diffPercentage`, verify the diff image exists at the reported path, and visually inspect it for magenta highlights on changed pixels
- [ ] Run `CompareWithBaseline` with images of different dimensions -- confirm structured error about dimension mismatch
- [ ] Run `GenerateValidationReport` on a directory containing both valid and blank screenshots -- confirm per-file results correctly identify failures and summary counts are accurate
- [ ] Run `CheckColorContrast` on a region with black text on white background -- confirm contrast ratio is approximately 21:1 and all WCAG levels pass
- [ ] Run any method with a path to a nonexistent file -- confirm structured error JSON, not an exception
- [ ] Run any method with a path to a corrupt/non-image file -- confirm graceful error handling with descriptive message

## Code Quality

- [ ] Every `Texture2D` created during validation is destroyed with `DestroyImmediate` in a `finally` block -- no texture leaks
- [ ] `GetPixels32` arrays are not held longer than necessary -- local scope only, eligible for GC after method returns
- [ ] For large textures (e.g., 4K screenshots), unique color counting uses sampling or `HashSet` with reasonable memory bounds
- [ ] Pixel comparison loop is efficient -- single pass through both pixel arrays without repeated `GetPixel` calls (use `GetPixels32` bulk read)
- [ ] File existence checks (`File.Exists`) before every `File.ReadAllBytes` call
- [ ] `LoadImage` return value is checked -- false indicates corrupt or unsupported format
- [ ] Non-PNG image formats (JPEG, BMP) are handled gracefully: either supported via `LoadImage` (which handles JPEG) or rejected with a clear error message
- [ ] WCAG luminance calculation uses the correct sRGB linearization formula, not a simplified gamma approximation

## Knowledge Transfer

- [ ] Add code comments clarifying which checks are automated (dimensions, blank detection, pixel comparison, contrast ratio) versus which require the agent's vision model (overlapping elements, text overflow, inconsistent spacing, layout correctness)
- [ ] Document how to add custom validation rules: implement a rule evaluator method, register the rule name in the dispatch table, add parameters to the rules JSON schema
- [ ] Explain the relationship between this tool's `CheckColorContrast` and Challenge 12 (Brand System): the brand system defines expected color pairs and contrast requirements, while this tool performs the actual measurement -- they are designed to work together
- [ ] Note that `CompareWithBaseline` uses a per-channel tolerance (default 2) to handle PNG compression artifacts -- document how to adjust this for stricter or looser comparisons
- [ ] Explain the diff image format: magenta pixels indicate change, darkened pixels indicate no change -- this makes the diff image useful as a standalone visual artifact for the agent's vision model to inspect
