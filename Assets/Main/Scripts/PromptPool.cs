using System.Collections.Generic;
using UnityEngine;

public enum PromptType { Q, F }

/// <summary>
/// Marker placed on every pooled prompt so the pool knows which bucket to
/// return it to. Added automatically at instantiate time.
/// </summary>
public class PooledPrompt : MonoBehaviour
{
    public PromptType type;
}

/// <summary>
/// Central object pool for the Q / F interaction prompts.
/// Instead of every enemy carrying permanent child prompt objects, an enemy
/// borrows a prompt only while it needs to show one, then returns it.
///
/// Setup: put this on one GameObject in the scene, assign the Q and F prompt
/// prefabs (each should be a world-space canvas — a BillboardUI is added
/// automatically if the prefab doesn't already have one).
/// </summary>
public class PromptPool : MonoBehaviour
{
    public static PromptPool Instance { get; private set; }

    [Header("Prefabs (world-space canvas prompts)")]
    public GameObject qPromptPrefab;
    public GameObject fPromptPrefab;

    [Header("Pre-warm")]
    [Tooltip("Instances of each prompt created on Awake so the first show has no spawn hitch.")]
    public int prewarmPerType = 2;

    readonly Stack<GameObject> _qPool = new Stack<GameObject>();
    readonly Stack<GameObject> _fPool = new Stack<GameObject>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        for (int i = 0; i < prewarmPerType; i++)
        {
            var q = CreateInstance(PromptType.Q); if (q != null) _qPool.Push(q);
            var f = CreateInstance(PromptType.F); if (f != null) _fPool.Push(f);
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Borrow a prompt, parent it under <paramref name="anchor"/> and place it at <paramref name="localOffset"/>.</summary>
    public GameObject Acquire(PromptType type, Transform anchor, Vector3 localOffset)
    {
        var pool = type == PromptType.Q ? _qPool : _fPool;
        GameObject go = pool.Count > 0 ? pool.Pop() : CreateInstance(type);
        if (go == null) return null;   // prefab not assigned

        go.transform.SetParent(anchor != null ? anchor : transform, worldPositionStays: false);
        go.transform.localPosition = localOffset;
        go.SetActive(true);
        return go;
    }

    /// <summary>Return a prompt to the pool (hidden, reparented under the pool).</summary>
    public void Release(GameObject instance)
    {
        if (instance == null) return;
        instance.SetActive(false);
        instance.transform.SetParent(transform, worldPositionStays: false);

        var tag = instance.GetComponent<PooledPrompt>();
        if (tag != null && tag.type == PromptType.F) _fPool.Push(instance);
        else                                         _qPool.Push(instance);
    }

    GameObject CreateInstance(PromptType type)
    {
        GameObject prefab = type == PromptType.Q ? qPromptPrefab : fPromptPrefab;
        if (prefab == null) return null;

        GameObject go = Instantiate(prefab, transform);
        var tag = go.GetComponent<PooledPrompt>() ?? go.AddComponent<PooledPrompt>();
        tag.type = type;
        if (go.GetComponent<BillboardUI>() == null) go.AddComponent<BillboardUI>();
        go.SetActive(false);
        return go;
    }
}
