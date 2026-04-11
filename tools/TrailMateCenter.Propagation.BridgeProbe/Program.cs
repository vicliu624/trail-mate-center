using Microsoft.Extensions.Logging.Abstractions;
using TrailMateCenter.Services;

return await RunAsync();

static async Task<int> RunAsync()
{
    var timeoutSeconds = ResolveInt("TRAILMATE_BRIDGE_PROBE_TIMEOUT_SECONDS", 90, min: 15, max: 600);
    var attachRetries = ResolveInt("TRAILMATE_BRIDGE_PROBE_ATTACH_RETRIES", 25, min: 1, max: 120);
    var viewportId = ResolveString("TRAILMATE_BRIDGE_PROBE_VIEWPORT_ID", "propagation-main-slot");
    var holdMs = ResolveInt("TRAILMATE_BRIDGE_PROBE_HOLD_MS", 5000, min: 0, max: 120000);

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    using var bridge = new UnityProcessPropagationBridge(NullLogger<UnityProcessPropagationBridge>.Instance);

    var telemetryCount = 0;
    var layerStateCount = 0;
    var diagnosticCount = 0;
    var cameraEventCount = 0;
    var mapPointCount = 0;
    var profileCount = 0;

    bridge.BridgeStateChanged += (_, e) =>
        Console.WriteLine($"[probe] BridgeState attached={e.IsAttached} viewport={e.ViewportId} msg={e.Message}");
    bridge.TelemetryUpdated += (_, e) =>
    {
        Interlocked.Increment(ref telemetryCount);
        Console.WriteLine($"[probe] Telemetry type={e.EventType} connected={e.IsConnected} attached={e.IsAttached} msg={e.Message}");
    };
    bridge.LayerStateChanged += (_, e) =>
    {
        Interlocked.Increment(ref layerStateCount);
        Console.WriteLine($"[probe] LayerState layer={e.LayerId} state={e.State} progress={e.ProgressPercent}");
    };
    bridge.DiagnosticSnapshotReceived += (_, e) =>
    {
        Interlocked.Increment(ref diagnosticCount);
        Console.WriteLine($"[probe] Diagnostic fps={e.Fps:F1} frame95={e.FrameTimeP95Ms:F1}ms");
    };
    bridge.CameraStateChanged += (_, e) =>
    {
        Interlocked.Increment(ref cameraEventCount);
        Console.WriteLine($"[probe] CameraChanged x={e.CameraState.X:F1} y={e.CameraState.Y:F1} z={e.CameraState.Z:F1}");
    };
    bridge.MapPointSelected += (_, e) =>
    {
        Interlocked.Increment(ref mapPointCount);
        Console.WriteLine($"[probe] MapPoint x={e.X:F1} y={e.Y:F1} node={e.NodeId}");
    };
    bridge.ProfileLineChanged += (_, e) =>
    {
        Interlocked.Increment(ref profileCount);
        Console.WriteLine($"[probe] ProfileLine ({e.StartX:F1},{e.StartY:F1}) -> ({e.EndX:F1},{e.EndY:F1})");
    };

    await AttachWithRetryAsync(bridge, viewportId, attachRetries, timeoutCts.Token);

    var runId = $"probe_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
    var rasterUri = ResolveRasterUri();
    Console.WriteLine($"[probe] runId={runId}");
    Console.WriteLine($"[probe] rasterUri={rasterUri}");

    var requestAck = await bridge.PushSimulationRequestAsync(runId, BuildRequest(runId), timeoutCts.Token);
    Console.WriteLine($"[probe] Ack push_request detail={requestAck.Detail}");

    var resultAck = await bridge.PushSimulationResultAsync(BuildResult(runId, rasterUri), timeoutCts.Token);
    Console.WriteLine($"[probe] Ack push_result detail={resultAck.Detail}");

    var layerAck = await bridge.SetLayerPresentationAsync(
        new PropagationUnityLayerPresentation
        {
            LayerIds = new[] { "coverage_mean", "interference" },
            LayerVisibility = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["coverage_mean"] = true,
                ["interference"] = true,
            },
            LayerOpacity = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["coverage_mean"] = 0.8,
                ["interference"] = 0.45,
            },
            LayerOrder = new[] { "coverage_mean", "interference" },
        },
        runId,
        timeoutCts.Token);
    Console.WriteLine($"[probe] Ack set_active_layer detail={layerAck.Detail}");

    var cameraAck = await bridge.SetCameraStateAsync(
        new PropagationUnityCameraState
        {
            X = 1200,
            Y = 1600,
            Z = 950,
            Pitch = 24,
            Yaw = 38,
            Roll = 0,
            Fov = 52,
        },
        runId,
        timeoutCts.Token);
    Console.WriteLine($"[probe] Ack set_camera_state detail={cameraAck.Detail}");

    if (holdMs > 0)
    {
        await Task.Delay(holdMs, timeoutCts.Token);
    }

    await bridge.DisconnectAsync(CancellationToken.None);

    Console.WriteLine("[probe] Summary");
    Console.WriteLine($"[probe] telemetry={telemetryCount}, layerState={layerStateCount}, diagnostic={diagnosticCount}, cameraEvents={cameraEventCount}, mapPoints={mapPointCount}, profiles={profileCount}");

    return 0;
}

static async Task AttachWithRetryAsync(
    IPropagationUnityBridge bridge,
    string viewportId,
    int retries,
    CancellationToken cancellationToken)
{
    Exception? last = null;
    for (var attempt = 1; attempt <= retries; attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var perAttemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            perAttemptCts.CancelAfter(TimeSpan.FromSeconds(3));
            Console.WriteLine($"[probe] attach attempt {attempt}/{retries} ...");
            await bridge.AttachViewportAsync(viewportId, perAttemptCts.Token);
            Console.WriteLine("[probe] attach success");
            return;
        }
        catch (Exception ex)
        {
            last = ex;
            Console.WriteLine($"[probe] attach failed: {ex.GetType().Name}: {ex.Message}");
            await Task.Delay(1000, cancellationToken);
        }
    }

    throw new InvalidOperationException("Unable to attach viewport after retries.", last);
}

static string ResolveRasterUri()
{
    var explicitRaster = Environment.GetEnvironmentVariable("TRAILMATE_BRIDGE_PROBE_RASTER_URI");
    if (!string.IsNullOrWhiteSpace(explicitRaster))
        return explicitRaster.Trim();

    var localRaster = Environment.GetEnvironmentVariable("TRAILMATE_BRIDGE_PROBE_RASTER_PATH");
    if (!string.IsNullOrWhiteSpace(localRaster) && File.Exists(localRaster))
        return new Uri(Path.GetFullPath(localRaster)).AbsoluteUri;

    var defaultRasterPath = Path.GetFullPath(Path.Combine("src", "TrailMateCenter.App", "Assets", "noise.png"));
    if (!File.Exists(defaultRasterPath))
        throw new FileNotFoundException("Bridge probe raster not found.", defaultRasterPath);

    return new Uri(defaultRasterPath).AbsoluteUri;
}

static string ResolveString(string key, string defaultValue)
{
    var raw = Environment.GetEnvironmentVariable(key);
    return string.IsNullOrWhiteSpace(raw) ? defaultValue : raw.Trim();
}

static int ResolveInt(string key, int defaultValue, int min, int max)
{
    var raw = Environment.GetEnvironmentVariable(key);
    if (!int.TryParse(raw, out var parsed))
        parsed = defaultValue;
    return Math.Clamp(parsed, min, max);
}

static PropagationSimulationRequest BuildRequest(string runId)
{
    return new PropagationSimulationRequest
    {
        RequestId = runId,
        Mode = PropagationSimulationMode.CoverageMap,
        FrequencyMHz = 915,
        TxPowerDbm = 20,
        UplinkSpreadingFactor = "SF10",
        DownlinkSpreadingFactor = "SF10",
        EnvironmentLossDb = 6,
        VegetationAlphaSparse = 0.3,
        VegetationAlphaDense = 0.9,
        ShadowSigmaDb = 8,
        ReflectionCoeff = 0.2,
        EnableMonteCarlo = false,
        MonteCarloIterations = 100,
        DemVersion = "dem_probe",
        LandcoverVersion = "lc_probe",
        SurfaceVersion = "surface_probe",
    };
}

static PropagationSimulationResult BuildResult(string runId, string rasterUri)
{
    var now = DateTimeOffset.UtcNow;
    return new PropagationSimulationResult
    {
        RunMeta = new PropagationRunMeta
        {
            RunId = runId,
            Status = PropagationJobState.Completed,
            StartedAtUtc = now.AddSeconds(-8),
            FinishedAtUtc = now,
            DurationMs = 8000,
            ProgressPercent = 100,
            CacheHit = false,
        },
        InputBundle = new PropagationInputBundle
        {
            AoiId = "probe_aoi",
            NodeSetVersion = "probe_nodes",
            ParameterSetVersion = "probe_params",
        },
        ModelOutputs = new PropagationModelOutputs
        {
            MeanCoverageRasterUri = rasterUri,
            Reliability95RasterUri = rasterUri,
            Reliability80RasterUri = rasterUri,
            InterferenceRasterUri = rasterUri,
            CapacityRasterUri = rasterUri,
            RasterLayers = new[]
            {
                BuildRasterLayer("coverage_mean", rasterUri, "dBm", "viridis", -125, -85),
                BuildRasterLayer("interference", rasterUri, "dB", "inferno", -20, 20),
            },
        },
        AnalysisOutputs = new PropagationAnalysisOutputs
        {
            Link = new PropagationLinkOutput
            {
                DownlinkRssiDbm = -98,
                UplinkRssiDbm = -102,
                DownlinkMarginDb = 12,
                UplinkMarginDb = 9,
                LinkFeasible = true,
                MarginGuardrail = "safe",
            },
            Reliability = new PropagationReliabilityOutput
            {
                P95 = 0.91,
                P80 = 0.97,
                ConfidenceNote = "probe",
            },
            LossBreakdown = new PropagationLossBreakdownOutput
            {
                FsplDb = 118,
                DiffractionDb = 7,
                VegetationDb = 5,
                ReflectionDb = 1.2,
                ShadowDb = 4,
            },
            CoverageProbability = new PropagationCoverageProbabilityOutput
            {
                AreaP95Km2 = 5.2,
                AreaP80Km2 = 7.1,
            },
            Network = new PropagationNetworkOutput
            {
                SinrDb = 8.6,
                ConflictRate = 0.18,
                MaxCapacityNodes = 240,
            },
            Profile = new PropagationProfileOutput
            {
                DistanceKm = 8.3,
                FresnelRadiusM = 6.1,
                MarginDb = 9,
                MainObstacle = new PropagationMainObstacle
                {
                    Label = "ridge_01",
                    V = 0.4,
                    LdDb = 6.8,
                },
            },
            Optimization = new PropagationOptimizationOutput
            {
                TopPlans = new[]
                {
                    new PropagationRelayPlan
                    {
                        PlanId = "plan_probe",
                        Score = 88.5,
                        CoverageGain = 2.2,
                        ReliabilityGain = 0.11,
                        BlindAreaPenalty = 0.6,
                        InterferencePenalty = 0.4,
                    },
                },
            },
            Uncertainty = new PropagationUncertaintyOutput
            {
                CiLower = 0.86,
                CiUpper = 0.94,
                StabilityIndex = 0.81,
            },
            Calibration = new PropagationCalibrationOutput
            {
                MaeBefore = 11.2,
                MaeAfter = 8.4,
                MaeDelta = -2.8,
                RmseBefore = 14.6,
                RmseAfter = 10.5,
                RmseDelta = -4.1,
                CalibrationRunId = "cal_probe",
            },
        },
        Provenance = new PropagationProvenance
        {
            DatasetBundle = new PropagationDatasetBundle
            {
                DemVersion = "dem_probe",
                LandcoverVersion = "lc_probe",
                SurfaceVersion = "surface_probe",
            },
            ModelVersion = "probe_model",
            GitCommit = "probe_commit",
            ParameterHash = "probe_hash",
        },
        QualityFlags = new PropagationQualityFlags
        {
            AssumptionFlags = new[] { "probe_assumption" },
            ValidityWarnings = Array.Empty<string>(),
        },
        SceneGeometry = new PropagationSceneGeometry
        {
            RelayCandidates = new[]
            {
                new PropagationScenePoint
                {
                    Id = "rc1",
                    Label = "candidate",
                    X = 1000,
                    Z = 1400,
                    Y = 320,
                    Score = 0.72,
                },
            },
            RelayRecommendations = new[]
            {
                new PropagationScenePoint
                {
                    Id = "rr1",
                    Label = "recommended",
                    X = 1450,
                    Z = 1720,
                    Y = 380,
                    Score = 0.89,
                },
            },
            ProfileObstacles = new[]
            {
                new PropagationScenePoint
                {
                    Id = "po1",
                    Label = "obstacle",
                    X = 1220,
                    Z = 1510,
                    Y = 402,
                    Score = null,
                },
            },
            ProfileLines = new[]
            {
                new PropagationScenePolyline
                {
                    Id = "pl1",
                    Points = new[]
                    {
                        new PropagationScenePolylinePoint { X = 900, Z = 1200, Y = 280 },
                        new PropagationScenePolylinePoint { X = 1250, Z = 1500, Y = 410 },
                        new PropagationScenePolylinePoint { X = 1650, Z = 1900, Y = 330 },
                    },
                },
            },
        },
    };
}

static PropagationRasterLayerMetadata BuildRasterLayer(
    string layerId,
    string rasterUri,
    string unit,
    string palette,
    double minValue,
    double maxValue)
{
    return new PropagationRasterLayerMetadata
    {
        LayerId = layerId,
        RasterUri = rasterUri,
        Bounds = new PropagationRasterBounds
        {
            MinX = 0,
            MinZ = 0,
            MaxX = 3000,
            MaxZ = 3000,
        },
        Crs = "LOCAL_TERRAIN_M",
        MinValue = minValue,
        MaxValue = maxValue,
        NoDataValue = -9999,
        ValueScale = maxValue - minValue,
        ValueOffset = minValue,
        ClassBreaks = new[]
        {
            minValue,
            minValue + (maxValue - minValue) * 0.25,
            minValue + (maxValue - minValue) * 0.5,
            minValue + (maxValue - minValue) * 0.75,
            maxValue,
        },
        Unit = unit,
        Palette = palette,
    };
}
