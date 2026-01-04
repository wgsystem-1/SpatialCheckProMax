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
    /// 선형 객체 급격한 꺾임 검사 전략
    /// - 등고선/도로중심선 등 선형 객체에서 급격한 각도 변화 탐지
    /// - ContourSharpBend, RoadSharpBend 등 CaseType 지원
    /// </summary>
    public class SharpBendCheckStrategy : BaseRelationCheckStrategy
    {
        private readonly string _caseType;

        public override string CaseType => _caseType;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="logger">로거</param>
        /// <param name="caseType">CaseType (ContourSharpBend 또는 RoadSharpBend)</param>
        public SharpBendCheckStrategy(ILogger logger, string caseType = "ContourSharpBend") : base(logger)
        {
            _caseType = caseType;
        }

        public override Task ExecuteAsync(
            DataSource ds,
            Func<string, Layer?> getLayer,
            ValidationResult result,
            RelationCheckConfig config,
            Action<RelationValidationProgressEventArgs> onProgress,
            CancellationToken token)
        {
            var line = getLayer(config.MainTableId);
            if (line == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {TableId}", CaseType, config.MainTableId);
                return Task.CompletedTask;
            }

            using var filterRestore = ApplyAttributeFilterIfMatch(line, config.FieldFilter);

            // 각도 임계값 (기본값: 등고선 90도, 도로 6도)
            double defaultAngle = CaseType.Contains("Contour") ? 90.0 : 6.0;
            double angleThreshold = config.Tolerance ?? defaultAngle;

            var objectTypeName = CaseType.Contains("Contour") ? "등고선" : "도로중심선";
            _logger.LogInformation("{ObjectType} 꺾임 검사 시작: 레이어={Layer}, 각도임계값={Threshold}도",
                objectTypeName, config.MainTableId, angleThreshold);
            var startTime = DateTime.Now;

            line.ResetReading();
            var total = line.GetFeatureCount(1);
            var idx = 0;

            Feature? f;
            while ((f = line.GetNextFeature()) != null)
            {
                using (f)
                {
                    token.ThrowIfCancellationRequested();
                    idx++;
                    if (idx % 50 == 0 || idx == total)
                    {
                        RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, idx, total);
                    }

                    var g = f.GetGeometryRef();
                    if (g == null) continue;

                    var oid = f.GetFID();
                    var oidStr = oid.ToString(CultureInfo.InvariantCulture);

                    // 멀티라인스트링 처리
                    int geometryCount = g.GetGeometryType() == wkbGeometryType.wkbMultiLineString ? g.GetGeometryCount() : 1;

                    for (int geomIdx = 0; geomIdx < geometryCount; geomIdx++)
                    {
                        var lineString = geometryCount > 1 ? g.GetGeometryRef(geomIdx) : g;
                        if (lineString == null || lineString.GetPointCount() < 3) continue;

                        var pointCount = lineString.GetPointCount();

                        // 연속된 3개 점으로 각도 계산
                        for (int i = 1; i < pointCount - 1; i++)
                        {
                            var x0 = lineString.GetX(i - 1);
                            var y0 = lineString.GetY(i - 1);
                            var x1 = lineString.GetX(i);
                            var y1 = lineString.GetY(i);
                            var x2 = lineString.GetX(i + 1);
                            var y2 = lineString.GetY(i + 1);

                            // 벡터 계산
                            var v1x = x1 - x0;
                            var v1y = y1 - y0;
                            var v2x = x2 - x1;
                            var v2y = y2 - y1;

                            // 벡터 길이
                            var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                            var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

                            if (len1 < 1e-10 || len2 < 1e-10) continue;

                            // 내적 계산
                            var dot = v1x * v2x + v1y * v2y;
                            var cosAngle = dot / (len1 * len2);

                            // 각도 계산 (0~180도)
                            var angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosAngle))) * 180.0 / Math.PI;

                            // 임계값 미만이면 오류
                            if (angle < angleThreshold)
                            {
                                var errorCode = CaseType.Contains("Contour") ? "LOG_TOP_GEO_014" : "LOG_TOP_GEO_015";
                                AddDetailedError(result, config.RuleId ?? errorCode,
                                    $"{objectTypeName}이(가) {angle:F1}도로 꺾임 (임계값: {angleThreshold}도 미만)",
                                    config.MainTableId, oidStr, $"정점 {i}에서 꺾임", g, config.MainTableName,
                                    config.RelatedTableId, config.RelatedTableName);
                                break; // 한 피처당 하나의 오류만 보고
                            }
                        }
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("{ObjectType} 꺾임 검사 완료: {Count}개 피처, 소요시간: {Elapsed:F2}초",
                objectTypeName, total, elapsed);

            RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);

            return Task.CompletedTask;
        }
    }
}
