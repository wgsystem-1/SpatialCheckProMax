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
    /// 선 연결성 검사 전략
    /// - 도로중심선 등 선형 객체의 끝점이 근접하지만 연결되지 않은 경우 검출
    /// </summary>
    public class LineConnectivityStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "LineConnectivity";

        public LineConnectivityStrategy(ILogger logger) : base(logger)
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
            var fieldFilter = config.FieldFilter ?? string.Empty;

            using var _attrFilter = ApplyAttributeFilterIfMatch(line, fieldFilter);

            _logger.LogInformation("선 연결성 검사 시작: 허용오차={Tolerance}m", tolerance);
            var startTime = DateTime.Now;

            // 1단계: 모든 선분과 끝점 정보 수집
            line.ResetReading();
            var allSegments = new List<LineSegmentInfo>();
            var endpointIndex = new Dictionary<string, List<EndpointInfo>>();

            Feature? f;
            while ((f = line.GetNextFeature()) != null)
            {
                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null) continue;

                    var oid = f.GetFID();
                    var lineString = g.GetGeometryType() == wkbGeometryType.wkbLineString ? g : g.GetGeometryRef(0);
                    if (lineString == null || lineString.GetPointCount() < 2) continue;

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

            // 2단계: 공간 인덱스를 사용하여 빠른 연결성 검사
            var total = allSegments.Count;
            var idx = 0;

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
                    var sx = segment.StartX;
                    var sy = segment.StartY;
                    var ex = segment.EndX;
                    var ey = segment.EndY;

                    var startCandidates = SearchEndpointsNearby(endpointIndex, sx, sy, tolerance);
                    var endCandidates = SearchEndpointsNearby(endpointIndex, ex, ey, tolerance);

                    bool startConnected = startCandidates.Any(c => c.Oid != oid &&
                        Distance(sx, sy, c.X, c.Y) <= tolerance);

                    bool endConnected = endCandidates.Any(c => c.Oid != oid &&
                        Distance(ex, ey, c.X, c.Y) <= tolerance);

                    bool startNearAnyLine = false;
                    bool endNearAnyLine = false;

                    using var startPt = new Geometry(wkbGeometryType.wkbPoint);
                    startPt.AddPoint(sx, sy, 0);
                    using var endPt = new Geometry(wkbGeometryType.wkbPoint);
                    endPt.AddPoint(ex, ey, 0);

                    var nearbySegments = GetNearbySegments(allSegments, sx, sy, ex, ey, tolerance * 5);
                    foreach (var nearby in nearbySegments)
                    {
                        if (nearby.Oid == oid) continue;

                        if (!startNearAnyLine && startPt.Distance(nearby.Geom) <= tolerance)
                            startNearAnyLine = true;
                        if (!endNearAnyLine && endPt.Distance(nearby.Geom) <= tolerance)
                            endNearAnyLine = true;

                        if (startNearAnyLine && endNearAnyLine) break;
                    }

                    if ((startNearAnyLine && !startConnected) || (endNearAnyLine && !endConnected))
                    {
                        var length = Math.Abs(segment.Geom.Length());
                        if (length <= tolerance)
                        {
                            AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_028",
                                $"도로중심선 끝점이 {tolerance}m 이내 타 선과 근접하나 스냅되지 않음(엔더숏)",
                                config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), "", segment.Geom, config.MainTableName,
                                config.RelatedTableId, config.RelatedTableName);
                        }
                        else
                        {
                            string which = (startNearAnyLine && !startConnected) && (endNearAnyLine && !endConnected)
                                ? "양쪽"
                                : ((startNearAnyLine && !startConnected) ? "시작점" : "끝점");
                            AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_028",
                                $"도로중심선 {which}이(가) {tolerance}m 이내 타 선과 근접하나 연결되지 않음",
                                config.MainTableId, oid.ToString(CultureInfo.InvariantCulture), "", segment.Geom, config.MainTableName,
                                config.RelatedTableId, config.RelatedTableName);
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("선 연결성 검사 완료: {Count}개 선분, 소요시간: {Elapsed:F2}초",
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
    }
}
