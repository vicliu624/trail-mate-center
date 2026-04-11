using System;
using TrailMateCenter.Unity.Bridge;
using TrailMateCenter.Unity.CameraSystem;
using TrailMateCenter.Unity.Diagnostics;
using TrailMateCenter.Unity.Interaction;
using TrailMateCenter.Unity.Rendering;
using TrailMateCenter.Unity.TerrainSystem;
using UnityEngine;
using UnityEngine.UI;
namespace TrailMateCenter.Unity.Core
{
public sealed class SceneContext
{
    public Terrain Terrain { get; set; } = null!;
    public CameraRigController CameraRig { get; set; } = null!;
    public LayerManager LayerManager { get; set; } = null!;
    public DiagnosticReporter Diagnostics { get; set; } = null!;
    public InteractionController Interaction { get; set; } = null!;
    public HudController? Hud { get; set; }
    public SceneGeometryRenderer GeometryRenderer { get; set; } = null!;
}

public static class SceneFactory
{
    public static SceneContext Build(AppConfig config, BridgeCoordinator bridge)
    {
        var root = new GameObject("TrailMateCenter.Unity.Scene");
        UnityEngine.Object.DontDestroyOnLoad(root);

        var lightGo = new GameObject("DirectionalLight");
        lightGo.transform.SetParent(root.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.05f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var terrainBuilderGo = new GameObject("TerrainBuilder");
        terrainBuilderGo.transform.SetParent(root.transform, false);
        var terrainBuilder = terrainBuilderGo.AddComponent<TerrainBuilder>();
        var terrain = terrainBuilder.Build(config.Terrain, root.transform);

        var cameraRigGo = new GameObject("CameraRig");
        cameraRigGo.transform.SetParent(root.transform, false);
        var pivot = cameraRigGo.transform;
        pivot.position = config.Camera.StartPosition;
        pivot.rotation = Quaternion.Euler(
            config.Camera.StartRotation.Pitch,
            config.Camera.StartRotation.Yaw,
            config.Camera.StartRotation.Roll);

        var cameraGo = new GameObject("MainCamera");
        cameraGo.tag = "MainCamera";
        cameraGo.transform.SetParent(pivot, false);
        cameraGo.transform.localPosition = Vector3.zero;
        cameraGo.transform.localRotation = Quaternion.identity;
        var camera = cameraGo.AddComponent<Camera>();
        camera.fieldOfView = config.Camera.Fov;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 20000f;
        cameraGo.AddComponent<AudioListener>();

        var rigController = cameraRigGo.AddComponent<CameraRigController>();
        rigController.Initialize(bridge, camera, pivot);

        var layerGo = new GameObject("LayerManager");
        layerGo.transform.SetParent(root.transform, false);
        var layerManager = layerGo.AddComponent<LayerManager>();

        var diagnosticsGo = new GameObject("Diagnostics");
        diagnosticsGo.transform.SetParent(root.transform, false);
        var diagnostics = diagnosticsGo.AddComponent<DiagnosticReporter>();
        diagnostics.Initialize(bridge, config.Diagnostics);

        layerManager.Initialize(config.Layers, config.GeoReference, config.Performance, terrain, bridge, diagnostics);

        var geometryGo = new GameObject("SceneGeometry");
        geometryGo.transform.SetParent(root.transform, false);
        var geometryRenderer = geometryGo.AddComponent<SceneGeometryRenderer>();
        geometryRenderer.Initialize(terrain);

        var aoiGo = new GameObject("AoiBoundary");
        aoiGo.transform.SetParent(root.transform, false);
        var aoiBoundary = aoiGo.AddComponent<AoiBoundaryRenderer>();
        aoiBoundary.Initialize(config.GeoReference, terrain);

        var canvasGo = new GameObject("HudCanvas");
        canvasGo.transform.SetParent(root.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        HudController? hud = null;
        if (!Application.isBatchMode)
        {
            try
            {
                hud = canvasGo.AddComponent<HudController>();
                hud.Initialize(canvas, layerManager, config.Layers);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Hud] Initialization skipped: {ex.Message}");
                if (hud != null)
                    UnityEngine.Object.Destroy(hud);
                hud = null;
            }
        }

        var interactionGo = new GameObject("Interaction");
        interactionGo.transform.SetParent(root.transform, false);
        var interaction = interactionGo.AddComponent<InteractionController>();
        interaction.Initialize(bridge, camera, terrain, config.Interaction);

        return new SceneContext
        {
            Terrain = terrain,
            CameraRig = rigController,
            LayerManager = layerManager,
            Diagnostics = diagnostics,
            Interaction = interaction,
            Hud = hud,
            GeometryRenderer = geometryRenderer
        };
    }
}
}




