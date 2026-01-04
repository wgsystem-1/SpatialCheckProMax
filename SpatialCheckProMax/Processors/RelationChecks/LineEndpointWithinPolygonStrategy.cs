using System;
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
    /// 선형 끝점 폴리곤 포함 검사 전략
    /// - 선형 객체(중심선)의 시작점/끝점이 폴리곤(경계면) 내부에 포함되어야 함
    /// </summary>
    public class LineEndpointWithinPolygonStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "LineEndpointWithinPolygon";

        public LineEndpointWithinPolygonStrategy(ILogger logger) : base(logger)
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
            var lineLayer = getLayer(config.MainTableId);
            var polygonLayer = getLayer(config.RelatedTableId);

            if (lineLayer == null || polygonLayer == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            var tolerance = config.Tolerance ?? 0.3;

            _logger.LogInformation("선형 끝점 폴리곤 포함 검사 시작: MainTable={MainTable}, RelatedTable={RelatedTable}, 허용오차={Tolerance}m",
                config.MainTableId, config.RelatedTableId, tolerance);
            var startTime = DateTime.Now;

            // 폴리곤 Union 생성
            var polygonUnion = BuildUnionGeometry(polygonLayer);
            if (polygonUnion == null || polygonUnion.IsEmpty())
            {
                _logger.LogInformation("폴리곤 레이어에 피처가 없습니다: {RelatedTable}", config.RelatedTableId);
                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, 0, 0, completed: true);
                return Task.CompletedTask;
            }

            try
            {
                polygonUnion = polygonUnion.MakeValid(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Union 지오메트리 유효성 보정 실패");
            }

            try
            {
                lineLayer.ResetReading();
                var total = lineLayer.GetFeatureCount(1);
                _logger.LogInformation("검수 대상 피처 수: {Count}개", total);

                Feature? feature;
                var processed = 0;

                while ((feature = lineLayer.GetNextFeature()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    processed++;

                    if (processed % 50 == 0 || processed == total)
                    {
                        RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processed, total);
                    }

                    using (feature)
                    {
                        var lineGeom = feature.GetGeometryRef();
                        if (lineGeom == null || lineGeom.IsEmpty()) continue;

                        var oid = feature.GetFID();
                        var pointCount = lineGeom.GetPointCount();
                        if (pointCount < 2) continue;

                        // 시작점과 끝점 추출
                        double startX = lineGeom.GetX(0);
                        double startY = lineGeom.GetY(0);
                        double endX = lineGeom.GetX(pointCount - 1);
                        double endY = lineGeom.GetY(pointCount - 1);

                        using var startPt = new Geometry(wkbGeometryType.wkbPoint);
                        startPt.AddPoint(startX, startY, 0);
                        using var endPt = new Geometry(wkbGeometryType.wkbPoint);
                        endPt.AddPoint(endX, endY, 0);

                        bool startWithin = false;
                        bool endWithin = false;

                        try
                        {
                            startWithin = polygonUnion.Contains(startPt) || startPt.Within(polygonUnion);
                            endWithin = polygonUnion.Contains(endPt) || endPt.Within(polygonUnion);

                            // 허용오차 고려: 버퍼 생성하여 검사
                            if (!startWithin && tolerance > 0)
                            {
                                using var startBuffer = startPt.Buffer(tolerance, 8);
                                startWithin = polygonUnion.Intersects(startBuffer) || polygonUnion.Contains(startBuffer);
                            }

                            if (!endWithin && tolerance > 0)
                            {
                                using var endBuffer = endPt.Buffer(tolerance, 8);
                                endWithin = polygonUnion.Intersects(endBuffer) || polygonUnion.Contains(endBuffer);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "끝점 포함 검사 중 오류: OID={OID}", oid);
                            continue;
                        }

                        if (!startWithin)
                        {
                            AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_032",
                                $"{config.MainTableName} 시작점이 {config.RelatedTableName} 내부에 포함되지 않습니다",
                                config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), $"허용오차={tolerance}m", startPt, config.MainTableName,
                                config.RelatedTableId, config.RelatedTableName);
                        }

                        if (!endWithin)
                        {
                            AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_032",
                                $"{config.MainTableName} 끝점이 {config.RelatedTableName} 내부에 포함되지 않습니다",
                                config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), $"허용오차={tolerance}m", endPt, config.MainTableName,
                                config.RelatedTableId, config.RelatedTableName);
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("선형 끝점 폴리곤 포함 검사 완료: {Count}개 피처, 소요시간: {Elapsed:F2}초",
                    total, elapsed);

                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);
            }
            finally
            {
                polygonUnion?.Dispose();
            }

            return Task.CompletedTask;
        }
    }
}
