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
    /// 등고선 교차 검사 전략
    /// - 등고선이 다른 등고선과 교차(점이 아닌 선/면으로 교차)하는지 검사
    /// </summary>
    public class ContourIntersectionStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "ContourIntersection";

        public ContourIntersectionStrategy(ILogger logger) : base(logger)
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
            var line = getLayer(config.MainTableId);
            if (line == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {TableId}", CaseType, config.MainTableId);
                return Task.CompletedTask;
            }

            using var filterRestore = ApplyAttributeFilterIfMatch(line, config.FieldFilter);

            _logger.LogInformation("등고선 교차 검사 시작: 레이어={Layer}", config.MainTableId);
            var startTime = DateTime.Now;

            // 모든 등고선 피처 수집
            line.ResetReading();
            var allLines = new List<(long Oid, Geometry Geom)>();

            Feature? f;
            while ((f = line.GetNextFeature()) != null)
            {
                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null) continue;

                    var oid = f.GetFID();
                    allLines.Add((oid, g.Clone()));
                }
            }

            _logger.LogInformation("등고선 수집 완료: {Count}개", allLines.Count);

            // 각 등고선이 다른 등고선과 교차하는지 확인
            var total = allLines.Count;
            var idx = 0;
            var checkedPairs = new HashSet<string>(); // 중복 검사 방지

            for (int i = 0; i < allLines.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                idx++;
                if (idx % 50 == 0 || idx == total)
                {
                    RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, idx, total);
                }

                var (oid1, geom1) = allLines[i];

                for (int j = i + 1; j < allLines.Count; j++)
                {
                    var (oid2, geom2) = allLines[j];

                    var pairKey = oid1 < oid2 ? $"{oid1}_{oid2}" : $"{oid2}_{oid1}";
                    if (checkedPairs.Contains(pairKey)) continue;
                    checkedPairs.Add(pairKey);

                    try
                    {
                        // 교차 여부 확인
                        if (geom1.Intersects(geom2))
                        {
                            // 실제로 교차하는지 확인 (단순히 겹치는 것이 아닌)
                            using var intersection = geom1.Intersection(geom2);
                            if (intersection != null && !intersection.IsEmpty())
                            {
                                var intersectionType = intersection.GetGeometryType();
                                // 점이 아닌 교차(선 또는 면)인 경우만 오류
                                if (intersectionType != wkbGeometryType.wkbPoint &&
                                    intersectionType != wkbGeometryType.wkbMultiPoint)
                                {
                                    var oid1Str = oid1.ToString(CultureInfo.InvariantCulture);
                                    var oid2Str = oid2.ToString(CultureInfo.InvariantCulture);
                                    AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_029",
                                        $"등고선이 다른 등고선과 교차함: 피처 {oid1Str}와 {oid2Str}",
                                        config.MainTableId, oid1Str, $"교차 피처: {oid2Str}", intersection, config.MainTableName,
                                        config.RelatedTableId, config.RelatedTableName);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "등고선 교차 검사 중 오류: OID={Oid1}, {Oid2}", oid1, oid2);
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("등고선 교차 검사 완료: {Count}개 등고선, 소요시간: {Elapsed:F2}초",
                total, elapsed);

            RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);

            // 메모리 정리
            foreach (var (_, geom) in allLines)
            {
                geom?.Dispose();
            }

            return Task.CompletedTask;
        }
    }
}
