using TrailMateCenter.Unity.Bridge;
using UnityEngine;
namespace TrailMateCenter.Unity.Core
{
public static class AppBootstrap
{
    private static bool _initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        MainThreadDispatcher.Ensure();

        if (Object.FindObjectOfType<BridgeCoordinator>() != null)
            return;

        var root = new GameObject("TrailMateCenter.Unity.App");
        root.AddComponent<BridgeCoordinator>();
        Object.DontDestroyOnLoad(root);
    }
}
}

