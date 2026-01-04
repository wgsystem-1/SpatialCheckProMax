using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    /// <summary>
    /// 속성별 도로경계선 단절 검사 전략
    /// - 동일 속성값(road_se 등)을 가진 도로경계선이 단절된 경우 검출
    /// </summary>
    public class LineDisconnectionWithAttributeStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "LineDisconnectionWithAttribute";

        public LineDisconnectionWithAttributeStrategy(ILogger logger) : base(logger)
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
            var layer = getLayer(config.MainTableId);
            if (layer == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {TableId}", CaseType, config.MainTableId);
                return Task.CompletedTask;
            }

            var tolerance = config.Tolerance ?? 0.5;
            var fieldFilter = config.FieldFilter ?? string.Empty;

            if (string.IsNullOrWhiteSpace(fieldFilter))
            {
                _logger.LogWarning("FieldFilter가 지정되지 않아 속성 검사를 수행할 수 없습니다: RuleId={RuleId}", config.RuleId);
                return Task.CompletedTask;
            }

            // FieldFilter 파싱: "road_se;exclude_road_types=RDS010,RDS011" 형식
            var fieldParts = fieldFilter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var attributeFieldName = fieldParts.First().Trim();
            var excludedRoadTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in fieldParts.Skip(1))
            {
                if (part.StartsWith("exclude_road_types=", StringComparison.OrdinalIgnoreCase))
                {
                    var codeList = part.Substring("exclude_road_types=".Length);
                    foreach (var code in codeList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        excludedRoadTypes.Add(code);
                    }
                }
            }

            _logger.LogInformation("LineDisconnectionWithAttribute 검사 시작: 속성필드={Field}, 제외코드={Codes}",
                attributeFieldName, excludedRoadTypes.Count > 0 ? string.Join(",", excludedRoadTypes) : "(없음)");
            var startTime = DateTime.Now;

            // 속성별로 그룹화하여 검사
            var segmentsByAttribute = new Dictionary<string, List<(long Oid, double StartX, double StartY, double EndX, double EndY)>>();

            layer.ResetReading();
            var totalFeatures = layer.GetFeatureCount(1);
            var processedCount = 0;
            var skippedCount = 0;
            var maxIterations = totalFeatures > 0 ? Math.Max(10000, (int)(totalFeatures * 2.0)) : int.MaxValue;
            var useDynamicCounting = totalFeatures == 0;

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
                    var rawValue = feature.GetFieldAsString(attributeFieldName);
                    var attrValue = string.IsNullOrWhiteSpace(rawValue) ? "UNKNOWN" : rawValue.Trim();

                    if (excludedRoadTypes.Contains(attrValue))
                    {
                        skippedCount++;
                        continue;
                    }

                    if (!segmentsByAttribute.ContainsKey(attrValue))
                    {
                        segmentsByAttribute[attrValue] = new List<(long, double, double, double, double)>();
                    }

                    ExtractLineEndpoints(geom, oid, segmentsByAttribute[attrValue]);
                }
            }

            _logger.LogInformation("LineDisconnectionWithAttribute 피처 수집 완료: 전체={Total}, 검사대상={Filtered}, 제외={Skipped}",
                processedCount, processedCount - skippedCount, skippedCount);

            // 각 속성값별로 단절 검사
            var totalErrors = 0;
            foreach (var (attrValue, segments) in segmentsByAttribute)
            {
                token.ThrowIfCancellationRequested();

                var endpointIndex = new Dictionary<string, List<EndpointInfo>>();
                double gridSize = Math.Max(tolerance, 1.0);

                foreach (var (oid, sx, sy, ex, ey) in segments)
                {
                    AddEndpointToIndex(endpointIndex, sx, sy, oid, true, gridSize);
                    AddEndpointToIndex(endpointIndex, ex, ey, oid, false, gridSize);
                }

                foreach (var (oid, sx, sy, ex, ey) in segments)
                {
                    var startCandidates = SearchEndpointsNearby(endpointIndex, sx, sy, tolerance);
                    var endCandidates = SearchEndpointsNearby(endpointIndex, ex, ey, tolerance);

                    bool startConnected = startCandidates.Any(c => c.Oid != oid &&
                        Distance(sx, sy, c.X, c.Y) <= tolerance);
                    bool endConnected = endCandidates.Any(c => c.Oid != oid &&
                        Distance(ex, ey, c.X, c.Y) <= tolerance);

                    if (!startConnected && !endConnected)
                    {
                        totalErrors++;
                        var geom = GetGeometryByOID(layer, oid);
                        AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_022",
                            $"동일 {attributeFieldName}({attrValue})를 가진 도로경계선이 단절되었습니다",
                            config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), $"{fieldFilter}={attrValue}", geom, config.MainTableName,
                            config.RelatedTableId, config.RelatedTableName);
                        geom?.Dispose();
                    }
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("도로경계선 단절 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초",
                processedCount, totalErrors, elapsed);

            RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processedCount, processedCount, completed: true);

            return Task.CompletedTask;
        }
    }
}
