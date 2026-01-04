using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using OgrEnvelope = OSGeo.OGR.Envelope;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    /// <summary>
    /// 표고점 위치 간격 검사 전략
    /// - 표고점 간 최소 거리 검사 (평지/인도/차도별)
    /// </summary>
    public class PointSpacingCheckStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PointSpacingCheck";

        private readonly double _defaultFlatland;
        private readonly double _defaultSidewalk;
        private readonly double _defaultCarriageway;

        public PointSpacingCheckStrategy(ILogger logger, double flatland = 200.0, double sidewalk = 20.0, double carriageway = 30.0)
            : base(logger)
        {
            _defaultFlatland = flatland;
            _defaultSidewalk = sidewalk;
            _defaultCarriageway = carriageway;
        }

        public override Task ExecuteAsync(
            DataSource ds,
            Func<string, Layer?> getLayer,
            ValidationResult result,
            RelationCheckConfig config,
            Action<RelationValidationProgressEventArgs> onProgress,
            CancellationToken token)
        {
            var pointLayer = getLayer(config.MainTableId);
            if (pointLayer == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {TableId}", CaseType, config.MainTableId);
                return Task.CompletedTask;
            }

            var fieldFilter = config.FieldFilter ?? string.Empty;

            // FieldFilter 형식: "scale=5K;flatland=200;road_sidewalk=20;road_carriageway=30"
            var spacingParams = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(fieldFilter))
            {
                var parts = fieldFilter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (kv.Length == 2 && double.TryParse(kv[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    {
                        spacingParams[kv[0]] = value;
                    }
                    else if (kv.Length == 1 && double.TryParse(kv[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var singleValue))
                    {
                        spacingParams["flatland"] = singleValue;
                    }
                }
            }

            var flatlandSpacing = spacingParams.GetValueOrDefault("flatland", _defaultFlatland);
            var roadSidewalkSpacing = spacingParams.GetValueOrDefault("road_sidewalk", _defaultSidewalk);
            var roadCarriagewaySpacing = spacingParams.GetValueOrDefault("road_carriageway", _defaultCarriageway);

            _logger.LogInformation("표고점 위치간격 검사 시작: 레이어={Layer}, 평탄지={Flatland}m, 인도={Sidewalk}m, 차도={Carriageway}m",
                config.MainTableId, flatlandSpacing, roadSidewalkSpacing, roadCarriagewaySpacing);
            var startTime = DateTime.Now;

            // 모든 표고점 수집
            pointLayer.ResetReading();
            var featureCount = pointLayer.GetFeatureCount(1);
            var allPoints = new List<(long Oid, double X, double Y)>();

            var processedCount = 0;
            var maxIterations = featureCount > 0 ? Math.Max(10000, (int)(featureCount * 2.0)) : int.MaxValue;
            var useDynamicCounting = featureCount == 0;

            Feature? f;
            while ((f = pointLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;

                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null || g.IsEmpty()) continue;

                    var oid = f.GetFID();
                    var pointType = g.GetGeometryType();
                    double x = 0, y = 0;

                    if (pointType == wkbGeometryType.wkbPoint)
                    {
                        x = g.GetX(0);
                        y = g.GetY(0);
                    }
                    else if (pointType == wkbGeometryType.wkbMultiPoint)
                    {
                        using var firstPoint = g.GetGeometryRef(0);
                        if (firstPoint != null && !firstPoint.IsEmpty())
                        {
                            x = firstPoint.GetX(0);
                            y = firstPoint.GetY(0);
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    allPoints.Add((oid, x, y));
                }
            }

            _logger.LogInformation("표고점 수집 완료: {Count}개", allPoints.Count);

            // 도로 레이어 미리 로드
            var sidewalkLayer = getLayer("tn_ftpth_bndry");
            var roadLayer = getLayer("tn_rodway_bndry");
            var sidewalkPolygons = new List<Geometry>();
            var roadPolygons = new List<Geometry>();

            if (sidewalkLayer != null)
            {
                sidewalkLayer.ResetReading();
                Feature? sf;
                while ((sf = sidewalkLayer.GetNextFeature()) != null)
                {
                    using (sf)
                    {
                        var geom = sf.GetGeometryRef();
                        if (geom != null && !geom.IsEmpty())
                        {
                            sidewalkPolygons.Add(geom.Clone());
                        }
                    }
                }
            }

            if (roadLayer != null)
            {
                roadLayer.ResetReading();
                Feature? rf;
                while ((rf = roadLayer.GetNextFeature()) != null)
                {
                    using (rf)
                    {
                        var geom = rf.GetGeometryRef();
                        if (geom != null && !geom.IsEmpty())
                        {
                            roadPolygons.Add(geom.Clone());
                        }
                    }
                }
            }

            try
            {
                // 그리드 기반 공간 인덱스
                var gridSize = Math.Max(flatlandSpacing, Math.Max(roadSidewalkSpacing, roadCarriagewaySpacing)) * 2.0;
                var gridIndex = new Dictionary<string, List<(long Oid, double X, double Y)>>();

                foreach (var (oid, x, y) in allPoints)
                {
                    var gridKey = $"{(int)(x / gridSize)}_{(int)(y / gridSize)}";
                    if (!gridIndex.ContainsKey(gridKey))
                    {
                        gridIndex[gridKey] = new List<(long Oid, double X, double Y)>();
                    }
                    gridIndex[gridKey].Add((oid, x, y));
                }

                var total = allPoints.Count;
                var idx = 0;
                var checkedPairs = new HashSet<string>();
                var errorCount = 0;

                foreach (var (oid1, x1, y1) in allPoints)
                {
                    token.ThrowIfCancellationRequested();
                    idx++;
                    if (idx % 100 == 0 || idx == total)
                    {
                        RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, idx, total);
                    }

                    var gridX = (int)(x1 / gridSize);
                    var gridY = (int)(y1 / gridSize);

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            var neighborKey = $"{gridX + dx}_{gridY + dy}";
                            if (!gridIndex.ContainsKey(neighborKey)) continue;

                            foreach (var (oid2, x2, y2) in gridIndex[neighborKey])
                            {
                                if (oid1 >= oid2) continue;

                                var pairKey = oid1 < oid2 ? $"{oid1}_{oid2}" : $"{oid2}_{oid1}";
                                if (checkedPairs.Contains(pairKey)) continue;
                                checkedPairs.Add(pairKey);

                                try
                                {
                                    var distance = Distance(x1, y1, x2, y2);
                                    if (distance <= 0) continue;

                                    double requiredSpacing = flatlandSpacing;
                                    string locationType = "평지";

                                    bool point1OnRoad = IsPointOnRoad(x1, y1, sidewalkPolygons, roadPolygons, out bool point1OnSidewalk);
                                    bool point2OnRoad = IsPointOnRoad(x2, y2, sidewalkPolygons, roadPolygons, out bool point2OnSidewalk);

                                    if (point1OnRoad && point2OnRoad)
                                    {
                                        if (point1OnSidewalk && point2OnSidewalk)
                                        {
                                            requiredSpacing = roadSidewalkSpacing;
                                            locationType = "인도";
                                        }
                                        else if (!point1OnSidewalk && !point2OnSidewalk)
                                        {
                                            requiredSpacing = roadCarriagewaySpacing;
                                            locationType = "차도";
                                        }
                                        else
                                        {
                                            requiredSpacing = Math.Min(roadSidewalkSpacing, roadCarriagewaySpacing);
                                            locationType = "도로(혼합)";
                                        }
                                    }
                                    else if (point1OnRoad || point2OnRoad)
                                    {
                                        requiredSpacing = flatlandSpacing;
                                        locationType = "도로/평지 혼합";
                                    }

                                    if (distance < requiredSpacing)
                                    {
                                        errorCount++;
                                        var oid1Str = oid1.ToString(CultureInfo.InvariantCulture);
                                        var oid2Str = oid2.ToString(CultureInfo.InvariantCulture);

                                        var pointGeom = new Geometry(wkbGeometryType.wkbPoint);
                                        pointGeom.AddPoint((x1 + x2) / 2.0, (y1 + y2) / 2.0, 0);

                                        AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_039",
                                            $"표고점 간 거리가 최소 간격({requiredSpacing}m, {locationType}) 미만: OID {oid1Str} <-> {oid2Str} (거리: {distance:F2}m)",
                                            config.MainTableId, oid1Str, $"인접 표고점: {oid2Str}", pointGeom, config.MainTableName,
                                            config.RelatedTableId, config.RelatedTableName);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "거리 계산 중 오류: OID={Oid1} vs {Oid2}", oid1, oid2);
                                }
                            }
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("표고점 위치간격 검사 완료: 처리 {Count}개, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초",
                    total, errorCount, elapsed);
                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);
            }
            finally
            {
                foreach (var geom in sidewalkPolygons)
                {
                    geom?.Dispose();
                }
                foreach (var geom in roadPolygons)
                {
                    geom?.Dispose();
                }
            }

            return Task.CompletedTask;
        }

        private bool IsPointOnRoad(double x, double y, List<Geometry> sidewalkPolygons, List<Geometry> roadPolygons, out bool isOnSidewalk)
        {
            isOnSidewalk = false;

            try
            {
                using var pointGeom = new Geometry(wkbGeometryType.wkbPoint);
                pointGeom.AddPoint(x, y, 0);

                foreach (var sidewalkPoly in sidewalkPolygons)
                {
                    if (sidewalkPoly != null && !sidewalkPoly.IsEmpty())
                    {
                        var env = new OgrEnvelope();
                        sidewalkPoly.GetEnvelope(env);
                        if (x >= env.MinX && x <= env.MaxX && y >= env.MinY && y <= env.MaxY)
                        {
                            if (pointGeom.Within(sidewalkPoly) || sidewalkPoly.Contains(pointGeom))
                            {
                                isOnSidewalk = true;
                                return true;
                            }
                        }
                    }
                }

                foreach (var roadPoly in roadPolygons)
                {
                    if (roadPoly != null && !roadPoly.IsEmpty())
                    {
                        var env = new OgrEnvelope();
                        roadPoly.GetEnvelope(env);
                        if (x >= env.MinX && x <= env.MaxX && y >= env.MinY && y <= env.MaxY)
                        {
                            if (pointGeom.Within(roadPoly) || roadPoly.Contains(pointGeom))
                            {
                                isOnSidewalk = false;
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 예외 발생 시 평지로 간주
            }

            return false;
        }
    }
}
