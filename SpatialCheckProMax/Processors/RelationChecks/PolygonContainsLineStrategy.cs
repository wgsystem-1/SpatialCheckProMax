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
    /// 폴리곤 내 선형 포함 검사 전략
    /// - 선형 객체(하천경계 등)가 폴리곤(실폭하천 등) 내에 포함되어야 함
    /// </summary>
    public class PolygonContainsLineStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PolygonContainsLine";

        public PolygonContainsLineStrategy(ILogger logger) : base(logger)
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
            var polygonLayer = getLayer(config.MainTableId);
            var lineLayer = getLayer(config.RelatedTableId);

            if (polygonLayer == null || lineLayer == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            var tolerance = config.Tolerance ?? 0.001;

            _logger.LogInformation("폴리곤 내 선형 포함 검사 시작: MainTable={MainTable}, RelatedTable={RelatedTable}",
                config.MainTableId, config.RelatedTableId);
            var startTime = DateTime.Now;

            // 폴리곤 레이어 Union 생성
            var polygonUnion = BuildUnionGeometry(polygonLayer);
            if (polygonUnion == null || polygonUnion.IsEmpty())
            {
                _logger.LogInformation("폴리곤 레이어에 피처가 없습니다: {MainTable}", config.MainTableId);
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

                // Union 지오메트리의 Envelope 미리 계산
                var polygonEnv = new OgrEnvelope();
                polygonUnion.GetEnvelope(polygonEnv);

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
                        var geom = feature.GetGeometryRef();
                        if (geom == null || geom.IsEmpty()) continue;

                        var oid = feature.GetFID().ToString(CultureInfo.InvariantCulture);

                        try
                        {
                            // Envelope 기반 사전 필터링
                            var env = new OgrEnvelope();
                            geom.GetEnvelope(env);

                            if (env.MaxX < polygonEnv.MinX || env.MinX > polygonEnv.MaxX ||
                                env.MaxY < polygonEnv.MinY || env.MinY > polygonEnv.MaxY)
                            {
                                AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_032",
                                    $"{config.RelatedTableName}이 {config.MainTableName}에 포함되지 않습니다",
                                    config.RelatedTableId, oid, string.Empty, geom, config.RelatedTableName,
                                    config.MainTableId, config.MainTableName);
                                continue;
                            }

                            // 실제 포함 관계 검사 (허용오차 고려)
                            bool isWithin = IsLineWithinPolygonWithTolerance(geom, polygonUnion, tolerance);

                            if (!isWithin)
                            {
                                AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_032",
                                    $"{config.RelatedTableName}이 {config.MainTableName}에 포함되지 않습니다",
                                    config.RelatedTableId, oid, $"허용오차={tolerance}m", geom, config.RelatedTableName,
                                    config.MainTableId, config.MainTableName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "포함 관계 검사 중 오류: OID={OID}", oid);
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("폴리곤 내 선형 포함 검사 완료: {Count}개 피처, 소요시간: {Elapsed:F2}초",
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
