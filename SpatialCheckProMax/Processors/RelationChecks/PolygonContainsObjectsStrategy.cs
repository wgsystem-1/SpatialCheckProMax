using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    /// <summary>
    /// 폴리곤 내부 객체 포함 검사 전략
    /// - 경지경계 내부에 건물, 도로시설 등이 포함되거나 겹치는지 검사
    /// </summary>
    public class PolygonContainsObjectsStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PolygonContainsObjects";

        public PolygonContainsObjectsStrategy(ILogger logger) : base(logger)
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
            var boundaryLayer = getLayer(config.MainTableId);
            if (boundaryLayer == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {MainTable}", CaseType, config.MainTableId);
                return Task.CompletedTask;
            }

            // 성능을 위해 주요 테이블만 검사
            var targetTables = new Dictionary<string, string>
            {
                { "tn_buld", "건물" },
                { "tn_arrfc", "면형도로시설" },
                { "tn_rodway_bndry", "도로경계면" },
                { "tn_rodway_ctln", "도로중심선" }
            };

            var targetLayers = new Dictionary<string, (Layer? Layer, string DisplayName)>();
            foreach (var (tableId, displayName) in targetTables)
            {
                targetLayers[tableId] = (getLayer(tableId), displayName);
            }

            boundaryLayer.ResetReading();
            var boundaryCount = boundaryLayer.GetFeatureCount(1);
            var processedBoundaries = 0;
            var maxIterations = boundaryCount > 0 ? Math.Max(10000, (int)(boundaryCount * 2.0)) : int.MaxValue;
            var useDynamicCounting = boundaryCount == 0;

            _logger.LogInformation("경지경계 내부객체 검사 시작: 경지경계 {BoundaryCount}개, 대상 테이블 {TableCount}개",
                boundaryCount, targetTables.Count);

            var startTime = DateTime.Now;
            var totalErrors = 0;
            var checkedPairs = new HashSet<string>();

            Feature? boundaryFeature;
            while ((boundaryFeature = boundaryLayer.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedBoundaries++;

                if (processedBoundaries > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                if (processedBoundaries % 50 == 0 || processedBoundaries == boundaryCount)
                {
                    RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processedBoundaries, boundaryCount);
                }

                using (boundaryFeature)
                {
                    var boundaryGeom = boundaryFeature.GetGeometryRef();
                    if (boundaryGeom == null || boundaryGeom.IsEmpty()) continue;

                    var boundaryOid = boundaryFeature.GetFID();

                    foreach (var (tableId, (targetLayer, tableDisplayName)) in targetLayers)
                    {
                        if (targetLayer == null) continue;

                        try
                        {
                            targetLayer.SetSpatialFilter(boundaryGeom);

                            Feature? targetFeature;
                            while ((targetFeature = targetLayer.GetNextFeature()) != null)
                            {
                                token.ThrowIfCancellationRequested();

                                using (targetFeature)
                                {
                                    var targetGeom = targetFeature.GetGeometryRef();
                                    if (targetGeom == null || targetGeom.IsEmpty()) continue;

                                    var targetOid = targetFeature.GetFID();
                                    var pairKey = $"{tableId}_{targetOid}_{boundaryOid}";

                                    if (checkedPairs.Contains(pairKey)) continue;
                                    checkedPairs.Add(pairKey);

                                    try
                                    {
                                        bool isInside = false;
                                        bool isOverlap = false;

                                        try
                                        {
                                            isInside = targetGeom.Within(boundaryGeom) || boundaryGeom.Contains(targetGeom);

                                            if (!isInside)
                                            {
                                                isOverlap = targetGeom.Overlaps(boundaryGeom) || boundaryGeom.Overlaps(targetGeom);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogWarning(ex, "포함/겹침 관계 검사 중 오류: OID={OID}, Table={Table}", targetOid, tableId);
                                            continue;
                                        }

                                        if (isInside || isOverlap)
                                        {
                                            totalErrors++;

                                            Geometry? errorGeom = targetGeom;
                                            if (isOverlap)
                                            {
                                                try
                                                {
                                                    var intersection = targetGeom.Intersection(boundaryGeom);
                                                    if (intersection != null && !intersection.IsEmpty())
                                                    {
                                                        errorGeom = intersection;
                                                    }
                                                }
                                                catch { }
                                            }

                                            AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_016",
                                                $"{tableDisplayName} 객체가 경지경계 내부에 포함되거나 겹칩니다",
                                                tableId, targetOid.ToString(CultureInfo.InvariantCulture),
                                                isInside ? "포함" : "겹침", errorGeom, tableDisplayName,
                                                config.MainTableId, config.MainTableName);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "경지경계 내부객체 검사 중 오류: OID={OID}, Table={Table}", targetOid, tableId);
                                    }
                                }
                            }

                            targetLayer.SetSpatialFilter(null);
                            targetLayer.ResetReading();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "경지경계 내부객체 검사 중 테이블 오류: Table={Table}", tableId);
                            try
                            {
                                targetLayer?.SetSpatialFilter(null);
                                targetLayer?.ResetReading();
                            }
                            catch { }
                        }
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("경지경계 내부객체 검사 완료: 경지경계 {BoundaryCount}개 처리, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초",
                processedBoundaries, totalErrors, elapsed);
            RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processedBoundaries, processedBoundaries, completed: true);

            return Task.CompletedTask;
        }
    }
}
