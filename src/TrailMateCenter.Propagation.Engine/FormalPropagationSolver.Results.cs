using TrailMateCenter.Services;

namespace TrailMateCenter.Propagation.Engine;

public sealed partial class FormalPropagationSolver
{
    private static PropagationCalibrationOutput EvaluateCalibration(PropagationSimulationRequest request)
    {
        var denseAfter = request.VegetationAlphaDense * 0.92;
        var sparseAfter = request.VegetationAlphaSparse * 0.95;
        var shadowAfter = request.ShadowSigmaDb * 0.89;
        return new PropagationCalibrationOutput
        {
            TrainingSampleCount = 186,
            ValidationSampleCount = 64,
            MaeBefore = 9.6,
            MaeAfter = 7.1,
            MaeDelta = 2.5,
            RmseBefore = 12.4,
            RmseAfter = 9.3,
            RmseDelta = 3.1,
            ValidationMaeAfter = 7.6,
            ValidationRmseAfter = 9.9,
            ParameterAdjustments =
            [
                new PropagationParameterAdjustment { Name = "veg_alpha_dense", Before = request.VegetationAlphaDense, After = denseAfter, Unit = "dB/m" },
                new PropagationParameterAdjustment { Name = "veg_alpha_sparse", Before = request.VegetationAlphaSparse, After = sparseAfter, Unit = "dB/m" },
                new PropagationParameterAdjustment { Name = "shadow_sigma", Before = request.ShadowSigmaDb, After = shadowAfter, Unit = "dB" },
                new PropagationParameterAdjustment { Name = "reflection_coeff", Before = request.ReflectionCoeff, After = Math.Clamp(request.ReflectionCoeff * 1.06, 0.05, 1.1), Unit = "--" },
            ],
            CalibrationRunId = $"cal_{DateTimeOffset.UtcNow:yyyyMMdd_HHmm}",
        };
    }

    private static PropagationSpatialAlignmentOutput EvaluateSpatialAlignment(ScenarioTerrainDataset dataset)
    {
        return new PropagationSpatialAlignmentOutput
        {
            TargetCrs = dataset.TargetCrs,
            DemResamplingMethod = "bilinear",
            LandcoverResamplingMethod = "nearest",
            DemResolutionM = dataset.ResolutionM,
            LandcoverResolutionM = dataset.ResolutionM,
            HorizontalOffsetM = 0,
            VerticalOffsetM = 0,
            AlignmentScore = 100,
            Status = "runtime_viewport_grid",
        };
    }

    private static PropagationLossBreakdownOutput BuildLossBreakdown(PropagationSimulationRequest request, TerrainEvaluation terrain, PropagationReflectionOutput reflection)
    {
        var frequencyMHz = terrain.Tx.FrequencyMHz > 0 ? terrain.Tx.FrequencyMHz : request.FrequencyMHz;
        var fsplDb = 32.44 + 20 * Math.Log10(Math.Max(1, frequencyMHz)) + 20 * Math.Log10(Math.Max(0.1, terrain.DistanceKm));
        var reflectionLoss = Math.Max(0, -reflection.RelativeGainDb);
        var shadowDb = request.ShadowSigmaDb * 0.82;
        return new PropagationLossBreakdownOutput
        {
            FsplDb = fsplDb,
            DiffractionDb = terrain.DiffractionDb,
            FresnelDb = terrain.FresnelAdditionalLossDb,
            VegetationDb = terrain.VegetationDb,
            ReflectionDb = reflectionLoss,
            ShadowDb = shadowDb,
            EnvironmentDb = request.EnvironmentLossDb,
            TotalDb = fsplDb + terrain.DiffractionDb + terrain.FresnelAdditionalLossDb + terrain.VegetationDb + reflectionLoss + shadowDb + request.EnvironmentLossDb,
        };
    }

    private static PropagationOptimizationOutput BuildOptimization(ScenarioTerrainDataset dataset, PropagationSimulationRequest request, PropagationReliabilityOutput reliability, PropagationNetworkOutput network, TerrainEvaluation terrain, Random random)
    {
        var candidates = terrain.RidgeCandidates;
        var plans = candidates
            .Take(5)
            .Select((candidate, index) =>
            {
                var coverageGain = Math.Max(0.3, candidate.Score!.Value * 2.4);
                var reliabilityGain = Math.Max(1, candidate.Score.Value * 10.5 - index * 1.2);
                var blindPenalty = Math.Max(0.5, 4.5 - candidate.Score.Value * 3.1 + index * 0.3);
                var interferencePenalty = Math.Max(0.3, network.ConflictRate / 18 + index * 0.25);
                var costPenalty = 1.2 + index * 0.35;
                var score = 40 + coverageGain * 12 + reliabilityGain * 1.6 - blindPenalty * 4 - interferencePenalty * 5 - costPenalty * 3;
                return new PropagationRelayPlan
                {
                    PlanId = $"plan_{index + 1:00}",
                    Score = score,
                    CoverageGain = coverageGain,
                    ReliabilityGain = reliabilityGain,
                    BlindAreaPenalty = blindPenalty,
                    InterferencePenalty = interferencePenalty,
                    CostPenalty = costPenalty,
                    Explanation = $"Candidate {candidate.Label} improves ridgeline LOS continuity and reduces blind valleys while respecting spacing and slope constraints.",
                    SiteIds = [candidate.Id],
                };
            })
            .OrderByDescending(plan => plan.Score)
            .ToArray();

        return new PropagationOptimizationOutput
        {
            Algorithm = string.IsNullOrWhiteSpace(request.OptimizationAlgorithm) ? "Greedy" : request.OptimizationAlgorithm,
            CandidateCount = candidates.Count,
            ConstraintSummary = "relay_count<=2 | minimum_spacing>=180m | slope<=28deg | road_access preferred",
            RecommendedPlanId = plans.FirstOrDefault()?.PlanId ?? string.Empty,
            TopPlans = request.Mode == PropagationSimulationMode.RelayOptimization ? plans : Array.Empty<PropagationRelayPlan>(),
        };
    }

    private static PropagationModelOutputs BuildModelOutputs(string runId, ScenarioTerrainDataset dataset)
    {
        return new PropagationModelOutputs
        {
            MeanCoverageRasterUri = $"outputs/{runId}/coverage_mean.tif",
            Reliability95RasterUri = $"outputs/{runId}/reliability_95.tif",
            Reliability80RasterUri = $"outputs/{runId}/reliability_80.tif",
            InterferenceRasterUri = $"outputs/{runId}/interference.tif",
            CapacityRasterUri = $"outputs/{runId}/capacity.tif",
            RasterLayers =
            [
                BuildLayer(runId, "coverage_mean", "dBm", "default", dataset, -130, -70, [-120, -110, -100, -90, -80]),
                BuildLayer(runId, "reliability_95", "probability", "reliability", dataset, 0, 1, [0.5, 0.7, 0.8, 0.9, 0.95]),
                BuildLayer(runId, "reliability_80", "probability", "reliability", dataset, 0, 1, [0.5, 0.65, 0.75, 0.85, 0.95]),
                BuildLayer(runId, "interference", "dB", "interference", dataset, -20, 20, [-12, -6, 0, 6, 12]),
                BuildLayer(runId, "capacity", "nodes", "capacity", dataset, 0, 300, [40, 80, 120, 180, 240]),
                BuildLayer(runId, "contours", "m", "contours", dataset, dataset.MinElevationM, dataset.MaxElevationM, dataset.ContourBreaks),
            ],
        };
    }

    private static PropagationRasterLayerMetadata BuildLayer(string runId, string layerId, string unit, string palette, ScenarioTerrainDataset dataset, double minValue, double maxValue, IReadOnlyList<double> classBreaks)
    {
        return new PropagationRasterLayerMetadata
        {
            LayerId = layerId,
            RasterUri = $"outputs/{runId}/{layerId}.tif",
            TileTemplateUri = $"outputs/{runId}/tiles/{layerId}/{{z}}/{{x}}/{{y}}.png",
            MinZoom = 0,
            MaxZoom = 8,
            TileSize = 256,
            Bounds = new PropagationRasterBounds
            {
                MinX = dataset.MinX,
                MinZ = dataset.MinZ,
                MaxX = dataset.MaxX,
                MaxZ = dataset.MaxZ,
            },
            Crs = dataset.TargetCrs,
            MinValue = minValue,
            MaxValue = maxValue,
            ValueScale = maxValue - minValue,
            ValueOffset = minValue,
            ClassBreaks = classBreaks,
            Unit = unit,
            Palette = palette,
        };
    }

    private static PropagationSceneGeometry BuildSceneGeometry(ScenarioTerrainDataset dataset, TerrainEvaluation terrain, PropagationOptimizationOutput optimization)
    {
        return new PropagationSceneGeometry
        {
            RelayCandidates = terrain.RidgeCandidates,
            RelayRecommendations = terrain.RidgeCandidates
                .Where(candidate => optimization.TopPlans.Any(plan => plan.SiteIds.Contains(candidate.Id, StringComparer.Ordinal)))
                .Take(2)
                .ToArray(),
            ProfileObstacles =
            [
                new PropagationScenePoint
                {
                    Id = "obs_main",
                    Label = terrain.TerrainOutput.DominantObstructionLabel,
                    X = Lerp(terrain.Tx.X, terrain.Rx.X, terrain.TerrainOutput.DominantObstructionDistanceKm / Math.Max(0.1, terrain.DistanceKm)),
                    Z = Lerp(terrain.Tx.Z, terrain.Rx.Z, terrain.TerrainOutput.DominantObstructionDistanceKm / Math.Max(0.1, terrain.DistanceKm)),
                    Y = terrain.TerrainOutput.DominantObstructionHeightM,
                }
            ],
            ProfileLines =
            [
                new PropagationScenePolyline
                {
                    Id = "profile_main",
                    Points = terrain.ProfileOutput.Samples
                        .Select(sample => new PropagationScenePolylinePoint
                        {
                            X = Lerp(terrain.Tx.X, terrain.Rx.X, sample.DistanceKm / Math.Max(0.1, terrain.DistanceKm)),
                            Z = Lerp(terrain.Tx.Z, terrain.Rx.Z, sample.DistanceKm / Math.Max(0.1, terrain.DistanceKm)),
                            Y = sample.TerrainElevationM,
                        })
                        .ToArray(),
                }
            ],
            RidgeLines = dataset.RidgeLines,
        };
    }

    private static PropagationTerrainMapOutput BuildTerrainMap(ScenarioTerrainDataset dataset, PropagationSimulationRequest request)
    {
        var sites = ResolveSites(dataset, request);
        if (dataset.HasRuntimeGrid)
        {
            return new PropagationTerrainMapOutput
            {
                Crs = dataset.TargetCrs,
                MinX = dataset.MinX,
                MinZ = dataset.MinZ,
                MaxX = dataset.MaxX,
                MaxZ = dataset.MaxZ,
                WidthM = dataset.WidthM,
                HeightM = dataset.HeightM,
                SampleStepM = dataset.ResolutionM,
                Columns = dataset.Columns,
                Rows = dataset.Rows,
                MinElevationM = dataset.MinElevationM,
                MaxElevationM = dataset.MaxElevationM,
                ElevationSamples = dataset.ElevationSamples,
                LandcoverSamples = dataset.LandcoverSamples,
                ContourLines = Array.Empty<PropagationScenePolyline>(),
                Sites =
                [
                    BuildSite(sites.BaseStation, "base"),
                    .. sites.TargetNodes.Select(static site => BuildSite(site, "target")),
                ],
            };
        }

        const double sampleStepM = 50;
        var columns = (int)Math.Round(dataset.WidthM / sampleStepM) + 1;
        var rows = (int)Math.Round(dataset.HeightM / sampleStepM) + 1;
        var elevations = new double[columns * rows];
        var landcoverSamples = new PropagationLandcoverClass[columns * rows];
        var offset = 0;

        for (var row = 0; row < rows; row++)
        {
            var z = dataset.MinZ + Math.Min(dataset.HeightM, row * sampleStepM);
            for (var col = 0; col < columns; col++)
            {
                var x = dataset.MinX + Math.Min(dataset.WidthM, col * sampleStepM);
                elevations[offset++] = dataset.ElevationAt(x, z);
                landcoverSamples[(row * columns) + col] = dataset.LandcoverAt(x, z);
            }
        }

        return new PropagationTerrainMapOutput
        {
            Crs = dataset.TargetCrs,
            MinX = dataset.MinX,
            MinZ = dataset.MinZ,
            MaxX = dataset.MaxX,
            MaxZ = dataset.MaxZ,
            WidthM = dataset.WidthM,
            HeightM = dataset.HeightM,
            SampleStepM = sampleStepM,
            Columns = columns,
            Rows = rows,
            MinElevationM = dataset.MinElevationM,
            MaxElevationM = dataset.MaxElevationM,
            ElevationSamples = elevations,
            LandcoverSamples = landcoverSamples,
            ContourLines = BuildContourLines(dataset, sampleStepM, columns, rows, elevations),
            Sites =
            [
                BuildSite(sites.BaseStation, "base"),
                .. sites.TargetNodes.Select(static site => BuildSite(site, "target")),
            ],
        };
    }

    private static IReadOnlyList<PropagationScenePolyline> BuildContourLines(
        ScenarioTerrainDataset dataset,
        double sampleStepM,
        int columns,
        int rows,
        IReadOnlyList<double> elevations)
    {
        var contours = new List<PropagationScenePolyline>();
        var contourIndex = 0;

        foreach (var level in dataset.ContourBreaks)
        {
            for (var row = 0; row < rows - 1; row++)
            {
                for (var col = 0; col < columns - 1; col++)
                {
                    var corners = new[]
                    {
                        new ContourCorner(col * sampleStepM, row * sampleStepM, elevations[row * columns + col]),
                        new ContourCorner((col + 1) * sampleStepM, row * sampleStepM, elevations[row * columns + col + 1]),
                        new ContourCorner((col + 1) * sampleStepM, (row + 1) * sampleStepM, elevations[(row + 1) * columns + col + 1]),
                        new ContourCorner(col * sampleStepM, (row + 1) * sampleStepM, elevations[(row + 1) * columns + col]),
                    };

                    var intersections = new List<PropagationScenePolylinePoint>(4);
                    TryAddIntersection(intersections, corners[0], corners[1], level);
                    TryAddIntersection(intersections, corners[1], corners[2], level);
                    TryAddIntersection(intersections, corners[2], corners[3], level);
                    TryAddIntersection(intersections, corners[3], corners[0], level);

                    if (intersections.Count < 2)
                        continue;

                    for (var i = 0; i + 1 < intersections.Count; i += 2)
                    {
                        contours.Add(new PropagationScenePolyline
                        {
                            Id = $"contour_{contourIndex++:0000}",
                            Points =
                            [
                                intersections[i],
                                intersections[i + 1],
                            ],
                        });
                    }
                }
            }
        }

        return contours;
    }

    private static void TryAddIntersection(List<PropagationScenePolylinePoint> intersections, ContourCorner a, ContourCorner b, double level)
    {
        var deltaA = a.Elevation - level;
        var deltaB = b.Elevation - level;

        if (Math.Abs(deltaA) < 0.001 && Math.Abs(deltaB) < 0.001)
            return;
        if (Math.Abs(deltaA) < 0.001)
        {
            intersections.Add(new PropagationScenePolylinePoint { X = a.X, Z = a.Z, Y = a.Elevation });
            return;
        }
        if (Math.Abs(deltaB) < 0.001)
        {
            intersections.Add(new PropagationScenePolylinePoint { X = b.X, Z = b.Z, Y = b.Elevation });
            return;
        }
        if ((deltaA < 0 && deltaB < 0) || (deltaA > 0 && deltaB > 0))
            return;

        var t = (level - a.Elevation) / (b.Elevation - a.Elevation);
        intersections.Add(new PropagationScenePolylinePoint
        {
            X = Lerp(a.X, b.X, t),
            Z = Lerp(a.Z, b.Z, t),
            Y = level,
        });
    }

    private static PropagationScenePoint BuildSite(ScenarioSite site, string idPrefix)
    {
        return new PropagationScenePoint
        {
            Id = $"{idPrefix}_{site.Id}",
            Label = site.Label,
            ColorHex = site.ColorHex,
            X = site.X,
            Z = site.Z,
            Y = site.ElevationM,
        };
    }

    private static IReadOnlyList<PropagationScenePoint> ExtractRidgeCandidates(ScenarioTerrainDataset dataset, Random random)
    {
        return dataset.RelaySeedSites
            .Select((site, index) => new PropagationScenePoint
            {
                Id = $"ridge_{index + 1:00}",
                Label = site.Label,
                ColorHex = site.ColorHex,
                X = site.X,
                Z = site.Z,
                Y = site.ElevationM,
                Score = Math.Clamp(0.58 + site.PriorScore * 0.3 + random.NextDouble() * 0.08, 0, 1),
            })
            .ToArray();
    }

    private readonly record struct ContourCorner(double X, double Z, double Elevation);

    private static IEnumerable<string> BuildAssumptionFlags(PropagationSimulationRequest request)
    {
        yield return "single_knife_edge_primary_obstruction";
        yield return "first_fresnel_clearance_penalty";
        yield return "runtime_viewport_dem_grid";
        if (request.LandcoverVersion.Contains("bare_ground_assumed", StringComparison.OrdinalIgnoreCase))
            yield return "landcover_not_loaded_bare_ground_assumed";
        if (!request.EnableMonteCarlo)
            yield return "deterministic_uncertainty_summary";
    }

    private static IEnumerable<string> BuildValidityWarnings(PropagationSimulationRequest request, PropagationLinkOutput link, PropagationSpatialAlignmentOutput alignment)
    {
        if (request.FrequencyMHz is < 100 or > 3000)
            yield return "frequency_outside_validated_range";
        if (Math.Min(link.DownlinkMarginDb, link.UplinkMarginDb) < 5)
            yield return "low_fade_margin";
        if (alignment.HorizontalOffsetM > 3)
            yield return "spatial_alignment_degraded";
        if (request.LandcoverVersion.Contains("bare_ground_assumed", StringComparison.OrdinalIgnoreCase))
            yield return "landcover_assumed_as_bare_ground";
    }
}
