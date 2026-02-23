# Prefab-First Asset Rules

All game objects and UI must be authored as prefabs, not constructed at runtime via code. This makes the game inspectable in the Editor without entering Play Mode.

## Rules

- **Prefabs for everything visible** — All game objects and UI must be authored as prefabs.
- **Visual properties in the prefab** — Colors, fonts, positions, anchors, materials belong in the prefab Inspector, not in C# code.
- **Serialized field values in Inspector** — ScriptableObject references, configuration values, and component wiring are set in the prefab, not via reflection.
- **Never use `[RuntimeInitializeOnLoadMethod]`** — All game objects must be present in the scene at edit time. No bootstrap scripts, no runtime instantiation of prefabs.
- **Prefab instances go in the scene** — Place prefab instances directly in the scene via project-specific setup tools (see PROJECT.md) or manual Editor placement, not via runtime code.
- **Materials as .mat assets** — Materials are created as assets, never via `Shader.Find()` + `new Material()` at runtime.
- **Use TMPro directly** — `using TMPro;` and `TextMeshProUGUI` directly. Never access TMP via reflection.
- **Single Canvas rule** — There must only ever be one ScreenSpace canvas in the scene. All screen-space UI lives as child objects under that single canvas. When adding new UI, create a logical grouping GameObject as a child of the existing canvas — never add a second canvas.

## Anti-Patterns (Never Do These in Game Code)

- `new GameObject()` + `AddComponent<T>()` — Use a prefab instead
- Creating a second ScreenSpace Canvas — Add UI as children of the existing canvas instead
- `ScriptableObject.CreateInstance<T>()` at runtime — Create `.asset` files at editor time
- `GetField(BindingFlags.NonPublic)` / reflection to set serialized fields — Set in Inspector
- `Shader.Find()` + `new Material(shader)` — Create `.mat` assets
- `FindType("TMPro.TextMeshProUGUI")` — Use `using TMPro;` directly
