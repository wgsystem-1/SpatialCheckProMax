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
    /// 중심선 속성 불일치 검사 전략 (하이브리드 방식)
    /// - Phase 1: 교차로 감지 (연결된 선분 개수)
    /// - Phase 2: 각도 기반 연속성 판단
    /// </summary>
    public class CenterlineAttributeMismatchStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "CenterlineAttributeMismatch";

        private readonly int _defaultIntersectionThreshold;
        private readonly double _defaultAngleThreshold;

        public CenterlineAttributeMismatchStrategy(ILogger logger, int intersectionThreshold = 3, double angleThreshold = 30.0)
            : base(logger)
        {
            _defaultIntersectionThreshold = intersectionThreshold;
            _defaultAngleThreshold = angleThreshold;
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

            var tolerance = config.Tolerance ?? 0.5;
            var fieldFilter = config.FieldFilter ?? string.Empty;

            // FieldFilter 파싱: "field1|field2|field3;intersection_threshold=3;angle_threshold=30"
            if (string.IsNullOrWhiteSpace(fieldFilter))
            {
                _logger.LogWarning("속성 필드명이 지정되지 않았습니다. FieldFilter에 필드명을 파이프(|)로 구분하여 지정하세요.");
                return Task.CompletedTask;
            }

            var parts = fieldFilter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var attributeFieldsPart = parts[0];

            var intersectionThreshold = _defaultIntersectionThreshold;
            var angleThreshold = _defaultAngleThreshold;
            var excludedRoadTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 파라미터 파싱
            for (int i = 1; i < parts.Length; i++)
            {
                var param = parts[i].Trim();
                if (param.StartsWith("intersection_threshold=", StringComparison.OrdinalIgnoreCase))
                {
                    var valueStr = param.Substring("intersection_threshold=".Length).Trim();
                    if (int.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    {
                        intersectionThreshold = value;
                    }
                }
                else if (param.StartsWith("angle_threshold=", StringComparison.OrdinalIgnoreCase))
                {
                    var valueStr = param.Substring("angle_threshold=".Length).Trim();
                    if (double.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    {
                        angleThreshold = value;
                    }
                }
                else if (param.StartsWith("exclude_road_types=", StringComparison.OrdinalIgnoreCase))
                {
                    var valueStr = param.Substring("exclude_road_types=".Length);
                    var codes = valueStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var code in codes)
                    {
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            excludedRoadTypes.Add(code);
                        }
                    }
                }
            }

            var attributeFields = attributeFieldsPart.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (attributeFields.Length == 0)
            {
                _logger.LogWarning("속성 필드명이 올바르게 지정되지 않았습니다: {FieldFilter}", fieldFilter);
                return Task.CompletedTask;
            }

            _logger.LogInformation("중심선 속성 불일치 검사 시작 (하이브리드 방식): 레이어={Layer}, 속성필드={Fields}, 허용오차={Tolerance}m, 교차로임계값={IntersectionThreshold}개, 각도임계값={AngleThreshold}도",
                config.MainTableId, string.Join(", ", attributeFields), tolerance, intersectionThreshold, angleThreshold);
            var startTime = DateTime.Now;

            // 1단계: 모든 선분과 끝점 정보 수집
            line.ResetReading();
            var allSegments = new List<LineSegmentInfo>();
            var endpointIndex = new Dictionary<string, List<EndpointInfo>>();
            var attributeValues = new Dictionary<long, Dictionary<string, string?>>();
            var excludedSegmentOids = new HashSet<long>();

            Feature? f;
            var fieldIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var roadTypeFieldName = attributeFields.FirstOrDefault(field =>
                string.Equals(field, "road_se", StringComparison.OrdinalIgnoreCase));

            if (excludedRoadTypes.Count > 0 && roadTypeFieldName == null)
            {
                _logger.LogWarning("exclude_road_types 파라미터가 지정되었으나 FieldFilter에 road_se 필드가 포함되지 않았습니다: {FieldFilter}", fieldFilter);
            }

            var featureCount = line.GetFeatureCount(1);
            var processedCount = 0;
            var maxIterations = featureCount > 0 ? Math.Max(10000, (int)(featureCount * 2.0)) : int.MaxValue;
            var useDynamicCounting = featureCount == 0;

            while ((f = line.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;

                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null) continue;

                    var oid = f.GetFID();
                    var lineString = g.GetGeometryType() == wkbGeometryType.wkbLineString ? g : g.GetGeometryRef(0);
                    if (lineString == null || lineString.GetPointCount() < 2) continue;

                    // 필드 인덱스 확인 (첫 번째 피처에서)
                    if (fieldIndices.Count == 0)
                    {
                        var defn = line.GetLayerDefn();
                        foreach (var fieldName in attributeFields)
                        {
                            var fieldIdx = defn.GetFieldIndex(fieldName);
                            if (fieldIdx >= 0)
                            {
                                fieldIndices[fieldName] = fieldIdx;
                            }
                            else
                            {
                                _logger.LogWarning("속성 필드를 찾을 수 없습니다: {Field} (레이어: {Layer})", fieldName, config.MainTableId);
                            }
                        }
                        if (fieldIndices.Count == 0)
                        {
                            _logger.LogError("모든 속성 필드를 찾을 수 없습니다: {Fields}", string.Join(", ", attributeFields));
                            return Task.CompletedTask;
                        }
                    }

                    // 속성값 읽기
                    var attrDict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in fieldIndices)
                    {
                        var fieldName = kvp.Key;
                        var fieldIdx = kvp.Value;
                        var strValue = f.GetFieldAsString(fieldIdx);
                        attrDict[fieldName] = string.IsNullOrWhiteSpace(strValue) ? null : strValue.Trim();
                    }
                    attributeValues[oid] = attrDict;

                    if (roadTypeFieldName != null &&
                        excludedRoadTypes.Count > 0 &&
                        attrDict.TryGetValue(roadTypeFieldName, out var roadTypeValue) &&
                        !string.IsNullOrWhiteSpace(roadTypeValue) &&
                        excludedRoadTypes.Contains(roadTypeValue))
                    {
                        excludedSegmentOids.Add(oid);
                        _logger.LogDebug("제외 도로구분으로 중심선 속성불일치 검사 생략: OID={Oid}, 코드={Code}", oid, roadTypeValue);
                    }

                    var pCount = lineString.GetPointCount();
                    var sx = lineString.GetX(0);
                    var sy = lineString.GetY(0);
                    var ex = lineString.GetX(pCount - 1);
                    var ey = lineString.GetY(pCount - 1);

                    var segmentInfo = new LineSegmentInfo
                    {
                        Oid = oid,
                        Geom = g.Clone(),
                        StartX = sx,
                        StartY = sy,
                        EndX = ex,
                        EndY = ey
                    };
                    allSegments.Add(segmentInfo);

                    AddEndpointToIndex(endpointIndex, sx, sy, oid, true, tolerance);
                    AddEndpointToIndex(endpointIndex, ex, ey, oid, false, tolerance);
                }
            }

            _logger.LogInformation("선분 수집 완료: {Count}개, 끝점 인덱스 그리드 수: {GridCount}", allSegments.Count, endpointIndex.Count);

            try
            {
                // 2단계: 연결된 선분끼리 속성값 비교 (하이브리드 방식)
                var total = allSegments.Count;
                var idx = 0;
                var checkedPairs = new HashSet<string>();
                var intersectionExcludedCount = 0;
                var angleExcludedCount = 0;
                var checkedCount = 0;

                foreach (var segment in allSegments)
                {
                    token.ThrowIfCancellationRequested();
                    idx++;

                    if (excludedSegmentOids.Contains(segment.Oid))
                    {
                        continue;
                    }

                    if (idx % 50 == 0 || idx == total)
                    {
                        RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, idx, total);
                    }

                    var oid = segment.Oid;
                    var currentAttrs = attributeValues.GetValueOrDefault(oid);
                    if (currentAttrs == null || currentAttrs.Count == 0) continue;

                    var sx = segment.StartX;
                    var sy = segment.StartY;
                    var ex = segment.EndX;
                    var ey = segment.EndY;

                    var startCandidates = SearchEndpointsNearby(endpointIndex, sx, sy, tolerance);
                    var endCandidates = SearchEndpointsNearby(endpointIndex, ex, ey, tolerance);

                    var currentVectorX = ex - sx;
                    var currentVectorY = ey - sy;

                    // 시작점에 연결된 선분 확인
                    foreach (var candidate in startCandidates)
                    {
                        if (candidate.Oid == oid) continue;
                        if (excludedSegmentOids.Contains(candidate.Oid)) continue;

                        var dist = Distance(sx, sy, candidate.X, candidate.Y);
                        if (dist <= tolerance)
                        {
                            var pairKey = oid < candidate.Oid ? $"{oid}_{candidate.Oid}" : $"{candidate.Oid}_{oid}";
                            if (checkedPairs.Contains(pairKey)) continue;
                            checkedPairs.Add(pairKey);

                            // Phase 1: 교차로 감지
                            var startConnectionCount = startCandidates.Count(c => c.Oid != oid && Distance(sx, sy, c.X, c.Y) <= tolerance);
                            if (startConnectionCount >= intersectionThreshold)
                            {
                                intersectionExcludedCount++;
                                continue;
                            }

                            // Phase 2: 각도 기반 연속성 판단
                            var connectedSegment = allSegments.FirstOrDefault(s => s.Oid == candidate.Oid);
                            if (connectedSegment != null)
                            {
                                double connectedVectorX, connectedVectorY;
                                if (candidate.IsStart)
                                {
                                    connectedVectorX = connectedSegment.EndX - connectedSegment.StartX;
                                    connectedVectorY = connectedSegment.EndY - connectedSegment.StartY;
                                }
                                else
                                {
                                    connectedVectorX = connectedSegment.StartX - connectedSegment.EndX;
                                    connectedVectorY = connectedSegment.StartY - connectedSegment.EndY;
                                }

                                var angleDiff = CalculateAngleDifference(currentVectorX, currentVectorY, connectedVectorX, connectedVectorY);
                                if (angleDiff > angleThreshold)
                                {
                                    angleExcludedCount++;
                                    continue;
                                }
                            }

                            // 속성 불일치 검사 수행
                            checkedCount++;
                            CheckAttributeMismatch(result, config, attributeFields, currentAttrs, oid, candidate.Oid, segment.Geom, attributeValues);
                        }
                    }

                    // 끝점에 연결된 선분 확인
                    foreach (var candidate in endCandidates)
                    {
                        if (candidate.Oid == oid) continue;
                        if (excludedSegmentOids.Contains(candidate.Oid)) continue;

                        var dist = Distance(ex, ey, candidate.X, candidate.Y);
                        if (dist <= tolerance)
                        {
                            var pairKey = oid < candidate.Oid ? $"{oid}_{candidate.Oid}" : $"{candidate.Oid}_{oid}";
                            if (checkedPairs.Contains(pairKey)) continue;
                            checkedPairs.Add(pairKey);

                            // Phase 1: 교차로 감지
                            var endConnectionCount = endCandidates.Count(c => c.Oid != oid && Distance(ex, ey, c.X, c.Y) <= tolerance);
                            if (endConnectionCount >= intersectionThreshold)
                            {
                                intersectionExcludedCount++;
                                continue;
                            }

                            // Phase 2: 각도 기반 연속성 판단
                            var connectedSegment = allSegments.FirstOrDefault(s => s.Oid == candidate.Oid);
                            if (connectedSegment != null)
                            {
                                double connectedVectorX, connectedVectorY;
                                if (candidate.IsStart)
                                {
                                    connectedVectorX = connectedSegment.EndX - connectedSegment.StartX;
                                    connectedVectorY = connectedSegment.EndY - connectedSegment.StartY;
                                }
                                else
                                {
                                    connectedVectorX = connectedSegment.StartX - connectedSegment.EndX;
                                    connectedVectorY = connectedSegment.StartY - connectedSegment.EndY;
                                }

                                var angleDiff = CalculateAngleDifference(currentVectorX, currentVectorY, connectedVectorX, connectedVectorY);
                                if (angleDiff > angleThreshold)
                                {
                                    angleExcludedCount++;
                                    continue;
                                }
                            }

                            // 속성 불일치 검사 수행
                            checkedCount++;
                            CheckAttributeMismatch(result, config, attributeFields, currentAttrs, oid, candidate.Oid, segment.Geom, attributeValues);
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("중심선 속성 불일치 검사 완료: {Count}개 선분, 소요시간: {Elapsed:F2}초, 교차로 제외: {IntersectionExcluded}개, 각도 제외: {AngleExcluded}개, 실제 검사: {Checked}개",
                    total, elapsed, intersectionExcludedCount, angleExcludedCount, checkedCount);

                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);
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

        private void CheckAttributeMismatch(
            ValidationResult result,
            RelationCheckConfig config,
            string[] attributeFields,
            Dictionary<string, string?> currentAttrs,
            long oid,
            long connectedOid,
            Geometry? geom,
            Dictionary<long, Dictionary<string, string?>> attributeValues)
        {
            var connectedAttrs = attributeValues.GetValueOrDefault(connectedOid);
            if (connectedAttrs == null) return;

            var mismatchedFields = new List<string>();
            foreach (var fieldName in attributeFields)
            {
                var currentValue = currentAttrs.GetValueOrDefault(fieldName);
                var connectedValue = connectedAttrs.GetValueOrDefault(fieldName);

                if (currentValue != connectedValue)
                {
                    mismatchedFields.Add(fieldName);
                }
            }

            if (mismatchedFields.Count > 0)
            {
                var oidStr = oid.ToString(CultureInfo.InvariantCulture);
                var connectedOidStr = connectedOid.ToString(CultureInfo.InvariantCulture);
                var mismatchDetails = string.Join(", ", mismatchedFields.Select(f =>
                    $"{f}: {currentAttrs.GetValueOrDefault(f) ?? "NULL"} vs {connectedAttrs.GetValueOrDefault(f) ?? "NULL"}"));

                AddDetailedError(result, config.RuleId ?? "LOG_CNC_REL_001",
                    $"연결된 중심선의 속성값이 불일치함: {mismatchDetails}",
                    config.MainTableId, oidStr, $"연결된 피처: {connectedOidStr}", geom, config.MainTableName,
                    config.RelatedTableId, config.RelatedTableName);
            }
        }
    }
}
