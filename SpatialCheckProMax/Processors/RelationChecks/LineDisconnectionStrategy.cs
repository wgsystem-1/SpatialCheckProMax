using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    /// <summary>
    /// 도로중심선 단절 검사 전략
    /// - 연결되지 않은 끝점 검사 (양쪽 끝점이 모두 연결되지 않은 경우)
    /// </summary>
    public class LineDisconnectionStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "LineDisconnection";

        public LineDisconnectionStrategy(ILogger logger) : base(logger)
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

            // NOT IN 필터 파싱 - 메모리 필터링용 (GDAL FileGDB 드라이버가 NOT IN을 제대로 지원하지 않음)
            var excludedRoadTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(fieldFilter))
            {
                var notInMatch = Regex.Match(fieldFilter, @"(?i)road_se\s+NOT\s+IN\s*\(([^)]+)\)");
                if (notInMatch.Success)
                {
                    var codeList = notInMatch.Groups[1].Value;
                    foreach (var code in codeList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        excludedRoadTypes.Add(code.Trim('\'', '"'));
                    }
                    _logger.LogInformation("LineDisconnection 메모리 필터링 활성화: 제외 도로구분={Codes}", string.Join(",", excludedRoadTypes));
                }
            }

            using var _attrFilter = ApplyAttributeFilterIfMatch(layer, fieldFilter);

            _logger.LogInformation("도로중심선 단절 검사 시작: 허용오차={Tolerance}m", tolerance);
            var startTime = DateTime.Now;

            layer.ResetReading();
            var totalFeatures = layer.GetFeatureCount(1);
            var processedCount = 0;
            var skippedCount = 0;
            var maxIterations = totalFeatures > 0 ? Math.Max(10000, (int)(totalFeatures * 2.0)) : int.MaxValue;
            var useDynamicCounting = totalFeatures == 0;

            var endpointIndex = new Dictionary<string, List<EndpointInfo>>();
            var allSegments = new List<LineSegmentInfo>();
            double gridSize = Math.Max(tolerance, 1.0);

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
                    if (excludedRoadTypes.Count > 0)
                    {
                        var roadSe = GetFieldValueSafe(feature, "road_se") ?? string.Empty;
                        if (excludedRoadTypes.Contains(roadSe.Trim()))
                        {
                            skippedCount++;
                            continue;
                        }
                    }

                    var geom = feature.GetGeometryRef();
                    if (geom == null || geom.IsEmpty()) continue;

                    var oid = feature.GetFID();
                    ExtractLineSegments(geom, oid, allSegments, endpointIndex, gridSize);
                }
            }

            _logger.LogInformation("LineDisconnection 피처 수집 완료: 전체={Total}, 검사대상={Filtered}, 제외={Skipped}",
                processedCount, processedCount - skippedCount, skippedCount);

            try
            {
                var disconnectedEndpoints = new HashSet<long>();
                foreach (var segment in allSegments)
                {
                    token.ThrowIfCancellationRequested();

                    var startCandidates = SearchEndpointsNearby(endpointIndex, segment.StartX, segment.StartY, tolerance);
                    var endCandidates = SearchEndpointsNearby(endpointIndex, segment.EndX, segment.EndY, tolerance);

                    bool startConnected = startCandidates.Any(c => c.Oid != segment.Oid &&
                        Distance(segment.StartX, segment.StartY, c.X, c.Y) <= tolerance);
                    bool endConnected = endCandidates.Any(c => c.Oid != segment.Oid &&
                        Distance(segment.EndX, segment.EndY, c.X, c.Y) <= tolerance);

                    if (!startConnected && !endConnected)
                    {
                        disconnectedEndpoints.Add(segment.Oid);
                    }
                }

                foreach (var oid in disconnectedEndpoints)
                {
                    var geom = GetGeometryByOID(layer, oid);
                    AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_027",
                        "도로중심선이 중간에 단절되었습니다",
                        config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), string.Empty, geom, config.MainTableName,
                        config.RelatedTableId, config.RelatedTableName);
                    geom?.Dispose();
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("도로중심선 단절 검사 완료: 처리 {ProcessedCount}개, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초",
                    processedCount, disconnectedEndpoints.Count, elapsed);

                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processedCount, processedCount, completed: true);
            }
            finally
            {
                foreach (var seg in allSegments)
                {
                    seg.Geom?.Dispose();
                }
            }

            return Task.CompletedTask;
        }
    }
}
