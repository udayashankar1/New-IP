using UnityEngine;
using UnityEditor;
using UnityEditor.AI;
using UnityEditor.SceneManagement;

// Handles play-mode exit automatically: if bake is requested while playing,
// it exits play mode and finishes the bake once the editor is back in edit mode.
[InitializeOnLoad]
public static class NavMeshBakeSetup
{
    const string PendingKey = "NavMeshSetup_PendingBake";

    static NavMeshBakeSetup()
    {
        if (!EditorPrefs.GetBool(PendingKey, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        EditorPrefs.DeleteKey(PendingKey);
        EditorApplication.delayCall += RunSetup;
    }

    [MenuItem("Tools/Setup and Bake NavMesh")]
    public static void RequestBake()
    {
        if (Application.isPlaying)
        {
            EditorPrefs.SetBool(PendingKey, true);
            EditorApplication.isPlaying = false;
            Debug.Log("[NavMesh] Stopping play mode — bake will run automatically on re-entry to edit mode.");
        }
        else
        {
            RunSetup();
        }
    }

    static void RunSetup()
    {
        int count = 0;

        // Terrain is the main walkable surface
        var terrain = GameObject.Find("Terrain");
        if (terrain != null) { SetNavStatic(terrain); count++; }
        else Debug.LogWarning("[NavMesh] Terrain object not found.");

        // World hierarchy: market, barriers — everything except the enemy subtree
        var world = GameObject.Find("World");
        if (world != null)
        {
            foreach (Transform t in world.GetComponentsInChildren<Transform>(true))
            {
                if (IsUnder(t, "Enemy and Path")) continue;
                SetNavStatic(t.gameObject);
                count++;
            }
        }
        else Debug.LogWarning("[NavMesh] World object not found.");

        Debug.Log($"[NavMesh] {count} objects marked as NavigationStatic. Baking...");
        NavMeshBuilder.BuildNavMesh();
        Debug.Log("[NavMesh] Bake complete!");

        EditorSceneManager.MarkAllScenesDirty();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[NavMesh] Scene saved.");
    }

    static bool IsUnder(Transform t, string ancestorName)
    {
        Transform cur = t;
        while (cur != null)
        {
            if (cur.name == ancestorName) return true;
            cur = cur.parent;
        }
        return false;
    }

    static void SetNavStatic(GameObject go)
    {
        var flags = GameObjectUtility.GetStaticEditorFlags(go);
        flags |= StaticEditorFlags.NavigationStatic;
        GameObjectUtility.SetStaticEditorFlags(go, flags);
    }
}
