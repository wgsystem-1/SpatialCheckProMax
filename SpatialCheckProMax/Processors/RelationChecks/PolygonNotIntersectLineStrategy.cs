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
    /// 폴리곤과 선형 교차 금지 검사 전략
    /// - 메인 폴리곤(건물)이 관련 선(철도중심선 등)과 교차하면 안 됨
    /// </summary>
    public class PolygonNotIntersectLineStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PolygonNotIntersectLine";

        public PolygonNotIntersectLineStrategy(ILogger logger) : base(logger)
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
            var buld = getLayer(config.MainTableId);
            var line = getLayer(config.RelatedTableId);

            if (buld == null || line == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            using var _fl = ApplyAttributeFilterIfMatch(line, config.FieldFilter);

            _logger.LogInformation("폴리곤-선형 교차 금지 검사 시작: MainTable={MainTable}, RelatedTable={RelatedTable}",
                config.MainTableId, config.RelatedTableId);
            var startTime = DateTime.Now;

            // 선형 레이어를 Union하여 단일 지오메트리로 만들어 성능 개선
            var lineUnion = BuildUnionGeometry(line);
            if (lineUnion == null || lineUnion.IsEmpty())
            {
                _logger.LogInformation("선형 레이어에 피처가 없습니다: {RelatedTable}", config.RelatedTableId);
                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, 0, 0, completed: true);
                return Task.CompletedTask;
            }

            // Union 지오메트리 유효성 보장
            try
            {
                lineUnion = lineUnion.MakeValid(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Union 지오메트리 유효성 보정 실패, 원본 사용");
            }

            try
            {
                buld.ResetReading();
                var total = buld.GetFeatureCount(1);
                _logger.LogInformation("검수 대상 피처 수: {Count}개", total);

                Feature? bf;
                var processed = 0;

                // Union 지오메트리의 Envelope 미리 계산
                var unionEnvelope = new OgrEnvelope();
                lineUnion.GetEnvelope(unionEnvelope);

                while ((bf = buld.GetNextFeature()) != null)
                {
                    token.ThrowIfCancellationRequested();

                    using (bf)
                    {
                        processed++;
                        if (processed % 50 == 0 || processed == total)
                        {
                            RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processed, total);
                        }

                        var pg = bf.GetGeometryRef();
                        if (pg == null || pg.IsEmpty()) continue;

                        // Envelope 기반 사전 필터링 (빠른 제외)
                        var envelope = new OgrEnvelope();
                        pg.GetEnvelope(envelope);

                        // Envelope이 겹치지 않으면 교차 불가능
                        if (envelope.MaxX < unionEnvelope.MinX || envelope.MinX > unionEnvelope.MaxX ||
                            envelope.MaxY < unionEnvelope.MinY || envelope.MinY > unionEnvelope.MaxY)
                        {
                            continue;
                        }

                        try
                        {
                            using var inter = pg.Intersection(lineUnion);
                            if (inter != null && !inter.IsEmpty())
                            {
                                var oid = bf.GetFID().ToString(CultureInfo.InvariantCulture);

                                var geomType = inter.GetGeometryType();
                                var isCollection = geomType == wkbGeometryType.wkbGeometryCollection ||
                                                   geomType == wkbGeometryType.wkbMultiLineString ||
                                                   geomType == wkbGeometryType.wkbMultiPoint;

                                if (isCollection)
                                {
                                    int count = inter.GetGeometryCount();
                                    for (int i = 0; i < count; i++)
                                    {
                                        using var subGeom = inter.GetGeometryRef(i)?.Clone();
                                        if (subGeom != null && !subGeom.IsEmpty())
                                        {
                                            AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_004",
                                                $"{config.MainTableName}(이)가 {config.RelatedTableName}(을)를 침범함 (부분 {i + 1})",
                                                config.MainTableId, oid, $"교차 부분 {i + 1}/{count}", subGeom, config.MainTableName,
                                                config.RelatedTableId, config.RelatedTableName);
                                        }
                                    }
                                }
                                else
                                {
                                    AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_004",
                                        $"{config.MainTableName}(이)가 {config.RelatedTableName}(을)를 침범함",
                                        config.MainTableId, oid, string.Empty, inter, config.MainTableName,
                                        config.RelatedTableId, config.RelatedTableName);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "교차 검사 중 오류 발생: OID={Oid}", bf.GetFID());
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("폴리곤-선형 교차 금지 검사 완료: {Count}개 피처, 소요시간: {Elapsed:F2}초",
                    total, elapsed);

                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);
            }
            finally
            {
                lineUnion?.Dispose();
            }

            return Task.CompletedTask;
        }
    }
}
