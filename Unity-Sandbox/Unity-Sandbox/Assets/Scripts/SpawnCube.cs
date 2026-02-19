using UnityEngine;

/// <summary>
/// Spawns a red cube at the origin when Play Mode starts.
/// Uses RuntimeInitializeOnLoadMethod so no GameObject attachment is needed.
/// </summary>
public static class SpawnCube
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnSceneLoaded()
    {
        // Create a cube primitive
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "TestCube";
        cube.transform.position = Vector3.zero;
        cube.transform.localScale = Vector3.one * 2f;

        // Apply a bright red URP-compatible material
        var renderer = cube.GetComponent<Renderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard"); // fallback

        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", Color.red);
            mat.color = Color.red;
            renderer.material = mat;
        }

        Debug.Log("[SpawnCube] Red cube created at origin.");
    }
}
