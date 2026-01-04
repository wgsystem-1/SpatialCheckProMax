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
    /// 연결된 선분 속성값 일치 검사 전략
    /// - 연결된 선분(등고선 등)의 속성값(높이 등)이 일치해야 함
    /// </summary>
    public class ConnectedLinesSameAttributeStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "ConnectedLinesSameAttribute";

        public ConnectedLinesSameAttributeStrategy(ILogger logger) : base(logger)
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

            var tolerance = config.Tolerance ?? 0.5;
            var attributeFieldName = config.FieldFilter ?? string.Empty;

            if (string.IsNullOrWhiteSpace(attributeFieldName))
            {
                _logger.LogWarning("속성 필드명이 지정되지 않았습니다. FieldFilter에 필드명을 지정하세요.");
                return Task.CompletedTask;
            }

            _logger.LogInformation("연결된 선분 속성값 일치 검사 시작: 레이어={Layer}, 속성필드={Field}, 허용오차={Tolerance}m",
                config.MainTableId, attributeFieldName, tolerance);
            var startTime = DateTime.Now;

            // 1단계: 모든 선분과 끝점 정보 수집 (속성값 포함)
            line.ResetReading();
            var allSegments = new List<LineSegmentInfo>();
            var endpointIndex = new Dictionary<string, List<EndpointInfo>>();
            var attributeValues = new Dictionary<long, double?>();

            Feature? f;
            int fieldIndex = -1;
            while ((f = line.GetNextFeature()) != null)
            {
                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null) continue;

                    var oid = f.GetFID();
                    var lineString = g.GetGeometryType() == wkbGeometryType.wkbLineString ? g : g.GetGeometryRef(0);
                    if (lineString == null || lineString.GetPointCount() < 2) continue;

                    // 속성값 읽기 (첫 번째 피처에서 필드 인덱스 확인)
                    if (fieldIndex < 0)
                    {
                        var defn = line.GetLayerDefn();
                        fieldIndex = defn.GetFieldIndex(attributeFieldName);
                        if (fieldIndex < 0)
                        {
                            _logger.LogError("속성 필드를 찾을 수 없습니다: {Field} (레이어: {Layer})", attributeFieldName, config.MainTableId);
                            return Task.CompletedTask;
                        }
                    }

                    double? attrValue = null;
                    if (fieldIndex >= 0)
                    {
                        var fieldDefn = f.GetFieldDefnRef(fieldIndex);
                        if (fieldDefn != null)
                        {
                            var fieldType = fieldDefn.GetFieldType();
                            if (fieldType == FieldType.OFTReal || fieldType == FieldType.OFTInteger || fieldType == FieldType.OFTInteger64)
                            {
                                attrValue = f.GetFieldAsDouble(fieldIndex);
                            }
                            else
                            {
                                var strValue = f.GetFieldAsString(fieldIndex);
                                if (!string.IsNullOrWhiteSpace(strValue) && double.TryParse(strValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue))
                                {
                                    attrValue = parsedValue;
                                }
                            }
                        }
                    }

                    attributeValues[oid] = attrValue;

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

            _logger.LogInformation("선분 수집 완료: {Count}개, 끝점 인덱스 그리드 수: {GridCount}",
                allSegments.Count, endpointIndex.Count);

            // 2단계: 연결된 선분끼리 속성값 비교
            var total = allSegments.Count;
            var idx = 0;
            var checkedPairs = new HashSet<string>();

            try
            {
                foreach (var segment in allSegments)
                {
                    token.ThrowIfCancellationRequested();
                    idx++;
                    if (idx % 50 == 0 || idx == total)
                    {
                        RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, idx, total);
                    }

                    var oid = segment.Oid;
                    var currentAttrValue = attributeValues.GetValueOrDefault(oid);

                    if (!currentAttrValue.HasValue) continue;

                    var sx = segment.StartX;
                    var sy = segment.StartY;
                    var ex = segment.EndX;
                    var ey = segment.EndY;

                    var startCandidates = SearchEndpointsNearby(endpointIndex, sx, sy, tolerance);
                    var endCandidates = SearchEndpointsNearby(endpointIndex, ex, ey, tolerance);

                    // 시작점에 연결된 선분 확인
                    CheckConnectedAttributes(segment, currentAttrValue.Value, startCandidates, sx, sy,
                        tolerance, checkedPairs, attributeValues, result, config);

                    // 끝점에 연결된 선분 확인
                    CheckConnectedAttributes(segment, currentAttrValue.Value, endCandidates, ex, ey,
                        tolerance, checkedPairs, attributeValues, result, config);
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("연결된 선분 속성값 일치 검사 완료: {Count}개 선분, 소요시간: {Elapsed:F2}초",
                    total, elapsed);

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

        private void CheckConnectedAttributes(
            LineSegmentInfo segment,
            double currentAttrValue,
            List<EndpointInfo> candidates,
            double x, double y,
            double tolerance,
            HashSet<string> checkedPairs,
            Dictionary<long, double?> attributeValues,
            ValidationResult result,
            RelationCheckConfig config)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Oid == segment.Oid) continue;

                var dist = Distance(x, y, candidate.X, candidate.Y);
                if (dist <= tolerance)
                {
                    var pairKey = segment.Oid < candidate.Oid
                        ? $"{segment.Oid}_{candidate.Oid}"
                        : $"{candidate.Oid}_{segment.Oid}";

                    if (checkedPairs.Contains(pairKey)) continue;
                    checkedPairs.Add(pairKey);

                    var connectedAttrValue = attributeValues.GetValueOrDefault(candidate.Oid);
                    if (connectedAttrValue.HasValue)
                    {
                        var diff = Math.Abs(currentAttrValue - connectedAttrValue.Value);
                        if (diff > 0.01)
                        {
                            var oidStr = segment.Oid.ToString(CultureInfo.InvariantCulture);
                            var connectedOidStr = candidate.Oid.ToString(CultureInfo.InvariantCulture);
                            AddDetailedError(result, config.RuleId ?? "LOG_CNC_REL_002",
                                $"연결된 등고선의 높이값이 일치하지 않음: {currentAttrValue:F2}m vs {connectedAttrValue.Value:F2}m (차이: {diff:F2}m)",
                                config.MainTableId, oidStr, $"연결된 피처: {connectedOidStr}", segment.Geom, config.MainTableName,
                                config.RelatedTableId, config.RelatedTableName);
                        }
                    }
                }
            }
        }
    }
}
