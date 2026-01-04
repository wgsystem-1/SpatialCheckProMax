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
    /// 결함있는 연결 검사 전략
    /// - 중심선 끝점이 다른 중심선에 연결되거나 경계면 내에 있어야 함
    /// </summary>
    public class DefectiveConnectionStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "DefectiveConnection";

        public DefectiveConnectionStrategy(ILogger logger) : base(logger)
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
            var centerlineLayer = getLayer(config.MainTableId);
            var boundaryLayer = getLayer(config.RelatedTableId);

            if (centerlineLayer == null || boundaryLayer == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            var tolerance = config.Tolerance ?? 0.5;
            var fieldFilter = config.FieldFilter ?? string.Empty;

            // NOT IN 필터 파싱
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
                    _logger.LogInformation("DefectiveConnection 메모리 필터링 활성화: 제외 도로구분={Codes}", string.Join(",", excludedRoadTypes));
                }
            }

            using var _attrFilter = ApplyAttributeFilterIfMatch(centerlineLayer, fieldFilter);

            _logger.LogInformation("결함있는 연결 검사 시작: 허용오차={Tolerance}m", tolerance);
            var startTime = DateTime.Now;

            // 경계면 Union 생성
            var boundaryUnion = BuildUnionGeometry(boundaryLayer);
            if (boundaryUnion == null)
            {
                _logger.LogWarning("경계면 Union 생성 실패");
                return Task.CompletedTask;
            }

            try { boundaryUnion = boundaryUnion.MakeValid(Array.Empty<string>()); } catch { }

            // 끝점 인덱스 구축
            var endpointIndex = new Dictionary<string, List<EndpointInfo>>();
            var allSegments = new List<LineSegmentInfo>();
            double gridSize = Math.Max(tolerance, 1.0);

            centerlineLayer.ResetReading();
            var totalFeatures = centerlineLayer.GetFeatureCount(1);
            var processedCount = 0;
            var skippedCount = 0;
            var maxIterations = totalFeatures > 0 ? Math.Max(10000, (int)(totalFeatures * 2.0)) : int.MaxValue;
            var useDynamicCounting = totalFeatures == 0;

            Feature? feature;
            while ((feature = centerlineLayer.GetNextFeature()) != null)
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

            _logger.LogInformation("DefectiveConnection 피처 수집 완료: 전체={Total}, 필터링 후={Filtered}, 제외={Skipped}",
                processedCount, allSegments.Count, skippedCount);

            try
            {
                // 결함 검사
                foreach (var segment in allSegments)
                {
                    token.ThrowIfCancellationRequested();

                    var startCandidates = SearchEndpointsNearby(endpointIndex, segment.StartX, segment.StartY, tolerance);
                    var endCandidates = SearchEndpointsNearby(endpointIndex, segment.EndX, segment.EndY, tolerance);

                    bool startConnected = startCandidates.Any(c => c.Oid != segment.Oid &&
                        Distance(segment.StartX, segment.StartY, c.X, c.Y) <= tolerance);
                    bool endConnected = endCandidates.Any(c => c.Oid != segment.Oid &&
                        Distance(segment.EndX, segment.EndY, c.X, c.Y) <= tolerance);

                    // 끝점에 붙어있지 않으면 바운더리 면형에 붙어있어야 함
                    if (!startConnected)
                    {
                        using var startPt = new Geometry(wkbGeometryType.wkbPoint);
                        startPt.AddPoint(segment.StartX, segment.StartY, 0);
                        bool nearBoundary = false;
                        try
                        {
                            using var buffer = startPt.Buffer(tolerance, 8);
                            nearBoundary = boundaryUnion.Intersects(buffer) || boundaryUnion.Contains(buffer);
                        }
                        catch { }

                        if (!nearBoundary)
                        {
                            var geom = GetGeometryByOID(centerlineLayer, segment.Oid);
                            AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_015",
                                "중심선 시작점이 다른 중심선에 붙어있지 않고 바운더리 면형에도 붙어있지 않습니다",
                                config.MainTableId, segment.Oid.ToString(CultureInfo.InvariantCulture), string.Empty, geom, config.MainTableName,
                                config.RelatedTableId, config.RelatedTableName);
                            geom?.Dispose();
                        }
                    }

                    if (!endConnected)
                    {
                        using var endPt = new Geometry(wkbGeometryType.wkbPoint);
                        endPt.AddPoint(segment.EndX, segment.EndY, 0);
                        bool nearBoundary = false;
                        try
                        {
                            using var buffer = endPt.Buffer(tolerance, 8);
                            nearBoundary = boundaryUnion.Intersects(buffer) || boundaryUnion.Contains(buffer);
                        }
                        catch { }

                        if (!nearBoundary)
                        {
                            var geom = GetGeometryByOID(centerlineLayer, segment.Oid);
                            AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_015",
                                "중심선 끝점이 다른 중심선에 붙어있지 않고 바운더리 면형에도 붙어있지 않습니다",
                                config.MainTableId, segment.Oid.ToString(CultureInfo.InvariantCulture), string.Empty, geom, config.MainTableName,
                                config.RelatedTableId, config.RelatedTableName);
                            geom?.Dispose();
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("결함있는 연결 검사 완료: 처리 {ProcessedCount}개, 소요시간: {Elapsed:F2}초",
                    processedCount, elapsed);

                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processedCount, processedCount, completed: true);
            }
            finally
            {
                boundaryUnion?.Dispose();
                foreach (var seg in allSegments)
                {
                    seg.Geom?.Dispose();
                }
            }

            return Task.CompletedTask;
        }
    }
}
