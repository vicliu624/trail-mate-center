using System;
using System.Collections.Concurrent;
using UnityEngine;
namespace TrailMateCenter.Unity.Core
{
public sealed class MainThreadDispatcher : MonoBehaviour
{
    private static readonly ConcurrentQueue<Action> Queue = new();
    private static MainThreadDispatcher? _instance;

    public static void Ensure()
    {
        if (_instance != null)
            return;
        var go = new GameObject("MainThreadDispatcher");
        _instance = go.AddComponent<MainThreadDispatcher>();
        DontDestroyOnLoad(go);
    }

    public static void Post(Action action)
    {
        if (action == null)
            return;
        Queue.Enqueue(action);
    }

    private void Update()
    {
        while (Queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Dispatcher] action failed: {ex.Message}");
            }
        }
    }
}
}

