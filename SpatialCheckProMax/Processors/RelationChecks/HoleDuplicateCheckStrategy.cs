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
    /// 홀 겹침 객체 검사 전략
    /// - 홀과 동일한 객체가 존재하는지 검사
    /// </summary>
    public class HoleDuplicateCheckStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "HoleDuplicateCheck";

        public HoleDuplicateCheckStrategy(ILogger logger) : base(logger)
        {
        }

        public override Task ExecuteAsync(
            DataSource ds,
            Func<string, Layer?> getLayer,
            ValidationResult result,
            RelationCheckConfig config,
            Action<RelationValidationProgressEventArgs> onProgress,
            CancellationToken token)
        {
            var tolerance = config.Tolerance ?? 0.0;

            // 모든 폴리곤 레이어에서 홀 추출
            var allHoles = new List<(Geometry Hole, string SourceTable, long SourceOid, int HoleIndex)>();
            var polygonTables = new[] { "tn_buld", "tn_arrfc", "tn_rodway_bndry", "tn_river_bndry", "tn_fmlnd_bndry" };

            _logger.LogInformation("홀 추출 시작");

            foreach (var tableId in polygonTables)
            {
                var layer = getLayer(tableId);
                if (layer == null) continue;

                layer.ResetReading();
                var featureCount = layer.GetFeatureCount(1);
                var processedCount = 0;
                var maxIterations = featureCount > 0 ? Math.Max(10000, (int)(featureCount * 2.0)) : int.MaxValue;
                var useDynamicCounting = featureCount == 0;

                Feature? feature;
                while ((feature = layer.GetNextFeature()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    processedCount++;

                    if (processedCount > maxIterations && !useDynamicCounting)
                    {
                        _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                        break;
                    }

                    using (feature)
                    {
                        var geom = feature.GetGeometryRef();
                        if (geom == null || geom.IsEmpty()) continue;

                        var oid = feature.GetFID();
                        var geomType = geom.GetGeometryType();
                        var flatType = (wkbGeometryType)((int)geomType & 0xFF);

                        try
                        {
                            if (flatType == wkbGeometryType.wkbPolygon)
                            {
                                ExtractHolesFromPolygon(geom, tableId, oid, allHoles);
                            }
                            else if (flatType == wkbGeometryType.wkbMultiPolygon)
                            {
                                var polyCount = geom.GetGeometryCount();
                                for (int p = 0; p < polyCount; p++)
                                {
                                    using var poly = geom.GetGeometryRef(p);
                                    if (poly == null) continue;
                                    ExtractHolesFromPolygon(poly, tableId, oid, allHoles);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "홀 추출 중 오류: OID={OID}, Table={Table}", oid, tableId);
                        }
                    }
                }
            }

            _logger.LogInformation("홀 추출 완료: {Count}개", allHoles.Count);

            try
            {
                // 모든 객체와 홀 비교
                var totalProcessed = 0;
                var totalErrors = 0;

                foreach (var tableId in polygonTables)
                {
                    var layer = getLayer(tableId);
                    if (layer == null) continue;

                    layer.ResetReading();
                    var featureCount = layer.GetFeatureCount(1);
                    var processedCount = 0;
                    var maxIterations = featureCount > 0 ? Math.Max(10000, (int)(featureCount * 2.0)) : int.MaxValue;
                    var useDynamicCounting = featureCount == 0;

                    Feature? feature;
                    while ((feature = layer.GetNextFeature()) != null)
                    {
                        token.ThrowIfCancellationRequested();
                        processedCount++;
                        totalProcessed++;

                        if (processedCount > maxIterations && !useDynamicCounting)
                        {
                            _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                            break;
                        }

                        using (feature)
                        {
                            var geom = feature.GetGeometryRef();
                            if (geom == null || geom.IsEmpty()) continue;

                            var oid = feature.GetFID();
                            var geomEnv = new OgrEnvelope();
                            geom.GetEnvelope(geomEnv);

                            foreach (var (holeGeom, sourceTable, sourceOid, holeIdx) in allHoles)
                            {
                                try
                                {
                                    var holeEnv = new OgrEnvelope();
                                    holeGeom.GetEnvelope(holeEnv);

                                    // Envelope가 겹치지 않으면 스킵
                                    if (geomEnv.MaxX < holeEnv.MinX || geomEnv.MinX > holeEnv.MaxX ||
                                        geomEnv.MaxY < holeEnv.MinY || geomEnv.MinY > holeEnv.MaxY)
                                    {
                                        continue;
                                    }

                                    bool isEqual = false;
                                    try
                                    {
                                        isEqual = geom.Equals(holeGeom) ||
                                                  (geom.Within(holeGeom) && holeGeom.Within(geom) &&
                                                   Math.Abs(geom.GetArea() - holeGeom.GetArea()) < tolerance * tolerance);
                                    }
                                    catch
                                    {
                                        continue;
                                    }

                                    if (isEqual)
                                    {
                                        totalErrors++;
                                        AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_040",
                                            $"홀과 동일한 객체가 존재합니다 (홀 출처: {sourceTable} OID={sourceOid}, 홀 인덱스={holeIdx})",
                                            tableId, oid.ToString(CultureInfo.InvariantCulture),
                                            $"홀 출처={sourceTable}:{sourceOid}", geom, tableId,
                                            sourceTable, string.Empty);
                                        break;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "홀 중복 검사 중 오류: OID={OID}, Table={Table}", oid, tableId);
                                }
                            }
                        }
                    }
                }

                _logger.LogInformation("홀겹침 객체 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개", totalProcessed, totalErrors);
                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, totalProcessed, totalProcessed, completed: true);
            }
            finally
            {
                foreach (var (holeGeom, _, _, _) in allHoles)
                {
                    holeGeom?.Dispose();
                }
            }

            return Task.CompletedTask;
        }

        private void ExtractHolesFromPolygon(Geometry poly, string tableId, long oid, List<(Geometry Hole, string SourceTable, long SourceOid, int HoleIndex)> holes)
        {
            var ringCount = poly.GetGeometryCount();
            for (int i = 1; i < ringCount; i++)
            {
                using var ring = poly.GetGeometryRef(i);
                if (ring != null && !ring.IsEmpty())
                {
                    using var holePoly = new Geometry(wkbGeometryType.wkbPolygon);
                    using var holeRing = ring.Clone();
                    holePoly.AddGeometry(holeRing);
                    holes.Add((holePoly.Clone(), tableId, oid, i));
                }
            }
        }
    }
}
