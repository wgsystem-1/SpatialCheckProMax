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
    /// 폴리곤 내 선형 객체 누락 검사 전략
    /// - 경계면(폴리곤) 내부에 중심선(선)이 최소 하나 이상 존재해야 함
    /// </summary>
    public class PolygonMissingLineStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PolygonMissingLine";

        public PolygonMissingLineStrategy(ILogger logger) : base(logger)
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
            // 메인: 도로경계면, 관련: 도로중심선
            var boundary = getLayer(config.MainTableId);
            var centerline = getLayer(config.RelatedTableId);

            if (boundary == null || centerline == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            // FieldFilter는 MainTable(경계면)에 적용
            using var filterRestore = ApplyAttributeFilterIfMatch(boundary, config.FieldFilter);

            _logger.LogInformation("폴리곤 내 선형 누락 검사 시작: MainTable={MainTable}, RelatedTable={RelatedTable}",
                config.MainTableId, config.RelatedTableId);
            var startTime = DateTime.Now;

            boundary.ResetReading();
            var total = boundary.GetFeatureCount(1);
            _logger.LogInformation("검수 대상 피처 수: {Count}개", total);

            Feature? bf;
            var processed = 0;

            while ((bf = boundary.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();

                using (bf)
                {
                    processed++;
                    if (processed % 50 == 0 || processed == total)
                    {
                        RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processed, total);
                    }

                    var bg = bf.GetGeometryRef();
                    if (bg == null || bg.IsEmpty()) continue;

                    // SetSpatialFilter를 사용하여 공간 인덱스 활용 (GDAL 내부 최적화)
                    centerline.SetSpatialFilter(bg);

                    var hasAny = centerline.GetNextFeature() != null;
                    centerline.ResetReading();
                    centerline.SetSpatialFilter(null);

                    if (!hasAny)
                    {
                        var oid = bf.GetFID().ToString(CultureInfo.InvariantCulture);
                        AddDetailedError(result, config.RuleId ?? "COM_OMS_REL_001",
                            $"{config.MainTableName}에 {config.RelatedTableName}이(가) 누락됨",
                            config.MainTableId, oid, "", bg, config.MainTableName,
                            config.RelatedTableId, config.RelatedTableName);
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("폴리곤 내 선형 누락 검사 완료: {Count}개 피처, 소요시간: {Elapsed:F2}초",
                total, elapsed);

            RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);

            return Task.CompletedTask;
        }
    }
}
