# Code Separation — Game vs Test Harness

Game code and agent test infrastructure must be kept in separate directories so that agent tooling can be cleanly removed without affecting the game.

## Convention

- **Game code** → `Assets/Scripts/<Feature>/` — No bridge dependencies, no test methods, no `BridgeTests` references.
- **Bridge test harnesses** → `Assets/Scripts/BridgeTests/<Feature>Tests.cs` — Agent-callable test methods only.

## Test Harness Rules

- Namespace: `<ProjectNamespace>.BridgeTests` (see PROJECT.md for project namespace)
- Class pattern: `public static class <Feature>Tests`
- Method pattern: `public static string <Name>()` returning JSON
- Each file header: `/// Delete this file to remove AI test infrastructure.`
- **One-way dependency only**: test harnesses may reference game code, but game code must NEVER reference the BridgeTests namespace.
- Callable via: `execute <ProjectNamespace>.BridgeTests.<Class>.<Method>`

## Example

```csharp
/// Delete this file to remove AI test infrastructure.
namespace MyProject.BridgeTests
{
    public static class PlayerTests
    {
        public static string GetPlayerState()
        {
            // Return JSON with current player state
            return "{\"success\":true,\"health\":100,\"position\":{\"x\":0,\"y\":1,\"z\":0}}";
        }

        public static string RunDamageTest()
        {
            // Test damage logic and return results
            return "{\"success\":true,\"expected\":90,\"actual\":90}";
        }
    }
}
```
