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
    /// 폴리곤 포함 관계 검사 전략
    /// - 메인 폴리곤(교량/터널 등)이 관련 폴리곤(도로경계면 등)에 포함되어야 함
    /// </summary>
    public class PolygonWithinPolygonStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PolygonWithinPolygon";

        public PolygonWithinPolygonStrategy(ILogger logger) : base(logger)
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
            var mainLayer = getLayer(config.MainTableId);
            var relatedLayer = getLayer(config.RelatedTableId);

            if (mainLayer == null || relatedLayer == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            var tolerance = config.Tolerance ?? 0.0;

            using var _attrFilter = ApplyAttributeFilterIfMatch(mainLayer, config.FieldFilter);

            _logger.LogInformation("폴리곤 포함 관계 검사 시작: MainTable={MainTable}, RelatedTable={RelatedTable}",
                config.MainTableId, config.RelatedTableId);
            var startTime = DateTime.Now;

            // 관련 레이어 Union 생성
            var boundaryUnion = BuildUnionGeometry(relatedLayer);
            if (boundaryUnion == null || boundaryUnion.IsEmpty())
            {
                _logger.LogInformation("관련 레이어에 피처가 없습니다: {RelatedTable}", config.RelatedTableId);
                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, 0, 0, completed: true);
                return Task.CompletedTask;
            }

            try
            {
                boundaryUnion = boundaryUnion.MakeValid(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Union 지오메트리 유효성 보정 실패");
            }

            try
            {
                mainLayer.ResetReading();
                var total = mainLayer.GetFeatureCount(1);
                _logger.LogInformation("검수 대상 피처 수: {Count}개", total);

                Feature? feature;
                var processed = 0;

                // Union 지오메트리의 Envelope 미리 계산
                var boundaryEnv = new OgrEnvelope();
                boundaryUnion.GetEnvelope(boundaryEnv);

                while ((feature = mainLayer.GetNextFeature()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    processed++;

                    if (processed % 50 == 0 || processed == total)
                    {
                        RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processed, total);
                    }

                    using (feature)
                    {
                        var geom = feature.GetGeometryRef();
                        if (geom == null || geom.IsEmpty()) continue;

                        var oid = feature.GetFID().ToString(CultureInfo.InvariantCulture);

                        try
                        {
                            // Envelope 기반 사전 필터링
                            var env = new OgrEnvelope();
                            geom.GetEnvelope(env);

                            if (env.MaxX < boundaryEnv.MinX || env.MinX > boundaryEnv.MaxX ||
                                env.MaxY < boundaryEnv.MinY || env.MinY > boundaryEnv.MaxY)
                            {
                                AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_025",
                                    $"{config.MainTableName}이 {config.RelatedTableName}에 포함되지 않습니다",
                                    config.MainTableId, oid, string.Empty, geom, config.MainTableName,
                                    config.RelatedTableId, config.RelatedTableName);
                                continue;
                            }

                            // 실제 포함 관계 검사
                            bool isWithin = false;
                            try
                            {
                                isWithin = geom.Within(boundaryUnion) || boundaryUnion.Contains(geom);

                                // 허용오차 고려
                                if (!isWithin && tolerance > 0)
                                {
                                    using var diff = geom.Difference(boundaryUnion);
                                    if (diff != null && !diff.IsEmpty())
                                    {
                                        var diffArea = Math.Abs(diff.GetArea());
                                        var geomArea = Math.Abs(geom.GetArea());
                                        if (geomArea > 0 && (diffArea <= tolerance * tolerance || diffArea / geomArea < 0.01))
                                        {
                                            isWithin = true;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "포함 관계 검사 중 오류: OID={OID}", oid);
                                isWithin = false;
                            }

                            if (!isWithin)
                            {
                                AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_025",
                                    $"{config.MainTableName}이 {config.RelatedTableName}에 포함되지 않습니다",
                                    config.MainTableId, oid, $"허용오차={tolerance}m", geom, config.MainTableName,
                                    config.RelatedTableId, config.RelatedTableName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "경계불일치 검사 중 오류: OID={OID}", oid);
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("폴리곤 포함 관계 검사 완료: {Count}개 피처, 소요시간: {Elapsed:F2}초",
                    total, elapsed);

                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);
            }
            finally
            {
                boundaryUnion?.Dispose();
            }

            return Task.CompletedTask;
        }
    }
}
