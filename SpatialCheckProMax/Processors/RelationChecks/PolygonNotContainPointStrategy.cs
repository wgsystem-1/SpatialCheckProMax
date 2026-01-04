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
    /// 폴리곤 내 점 포함 금지 검사 전략
    /// - 특정 폴리곤(금지 구역) 내부에 점이 존재하는지 검사
    /// </summary>
    public class PolygonNotContainPointStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PolygonNotContainPoint";

        public PolygonNotContainPointStrategy(ILogger logger) : base(logger)
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
            // 메인: 금지 폴리곤, 관련: 점 - 폴리곤 내부에 점 존재 금지
            var poly = getLayer(config.MainTableId);
            var pt = getLayer(config.RelatedTableId);

            if (poly == null || pt == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            using var filterRestore = ApplyAttributeFilterIfMatch(pt, config.FieldFilter);

            _logger.LogInformation("폴리곤 내 점 포함 금지 검사 시작: MainTable={MainTable}, RelatedTable={RelatedTable}",
                config.MainTableId, config.RelatedTableId);
            var startTime = DateTime.Now;

            poly.ResetReading();
            var total = poly.GetFeatureCount(1);
            _logger.LogInformation("검수 대상 피처 수: {Count}개", total);

            Feature? pf;
            var processed = 0;

            while ((pf = poly.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();

                using (pf)
                {
                    processed++;
                    if (processed % 50 == 0 || processed == total)
                    {
                        RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processed, total);
                    }

                    var pg = pf.GetGeometryRef();
                    if (pg == null || pg.IsEmpty()) continue;

                    // SetSpatialFilter를 사용하여 공간 인덱스 활용 (GDAL 내부 최적화)
                    pt.SetSpatialFilter(pg);

                    Feature? insidePoint;
                    while ((insidePoint = pt.GetNextFeature()) != null)
                    {
                        using (insidePoint)
                        {
                            var ptGeom = insidePoint.GetGeometryRef();
                            if (ptGeom != null)
                            {
                                var oid = pf.GetFID().ToString(CultureInfo.InvariantCulture);
                                var ptOid = insidePoint.GetFID().ToString(CultureInfo.InvariantCulture);
                                AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_010",
                                    $"{config.MainTableName}(이) {config.RelatedTableName}을 포함함 (포함된 점 OID: {ptOid})",
                                    config.MainTableId, oid, $"포함된 점: {ptOid}", ptGeom, config.MainTableName,
                                    config.RelatedTableId, config.RelatedTableName);
                            }
                        }
                    }

                    pt.ResetReading();
                    pt.SetSpatialFilter(null);
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("폴리곤 내 점 포함 금지 검사 완료: {Count}개 피처, 소요시간: {Elapsed:F2}초",
                total, elapsed);

            RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);

            return Task.CompletedTask;
        }
    }
}
