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
using SpatialCheckProMax.Models.Enums;
using SpatialCheckProMax.Utils;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    public abstract class BaseRelationCheckStrategy : IRelationCheckStrategy
    {
        protected readonly ILogger _logger;
        private DateTime _lastProgressUpdate = DateTime.MinValue;
        private const int PROGRESS_UPDATE_INTERVAL_MS = 200;

        protected BaseRelationCheckStrategy(ILogger logger)
        {
            _logger = logger;
        }

        public abstract string CaseType { get; }

        public abstract Task ExecuteAsync(
            DataSource ds,
            Func<string, Layer?> getLayer,
            ValidationResult result,
            RelationCheckConfig config,
            Action<RelationValidationProgressEventArgs> onProgress,
            CancellationToken token);

        protected void RaiseProgress(
            Action<RelationValidationProgressEventArgs> onProgress,
            string ruleId, 
            string caseType, 
            long processedLong, 
            long totalLong, 
            bool completed = false, 
            bool successful = true)
        {
            var now = DateTime.Now;
            if (!completed && (now - _lastProgressUpdate).TotalMilliseconds < PROGRESS_UPDATE_INTERVAL_MS)
            {
                return;
            }
            _lastProgressUpdate = now;

            var processed = (int)Math.Min(int.MaxValue, Math.Max(0, processedLong));
            var total = (int)Math.Min(int.MaxValue, Math.Max(0, totalLong));
            var pct = total > 0 ? (int)Math.Min(100, Math.Round(processed * 100.0 / (double)total)) : (completed ? 100 : 0);
            
            var eventArgs = new RelationValidationProgressEventArgs
            {
                CurrentStage = RelationValidationStage.SpatialRelationValidation,
                StageName = string.IsNullOrWhiteSpace(caseType) ? "공간 관계 검수" : caseType,
                OverallProgress = pct,
                StageProgress = completed ? 100 : pct,
                StatusMessage = completed
                    ? $"규칙 {ruleId} 처리 완료 ({processed}/{total})"
                    : $"규칙 {ruleId} 처리 중... {processed}/{total}",
                CurrentRule = ruleId,
                ProcessedRules = processed,
                TotalRules = total,
                IsStageCompleted = completed,
                IsStageSuccessful = successful,
                ErrorCount = 0,
                WarningCount = 0
            };
            
            onProgress?.Invoke(eventArgs);
        }

        protected void AddError(ValidationResult result, string errType, string message, string table = "", string objectId = "", Geometry? geometry = null, string tableDisplayName = "")
        {
            result.IsValid = false;
            result.ErrorCount += 1;
            
            var (x, y) = ExtractCentroid(geometry);
            result.Errors.Add(new ValidationError
            {
                ErrorCode = errType,
                Message = message,
                TableId = string.IsNullOrWhiteSpace(table) ? null : table,
                TableName = !string.IsNullOrWhiteSpace(tableDisplayName) ? tableDisplayName : string.Empty,
                FeatureId = objectId,
                SourceTable = string.IsNullOrWhiteSpace(table) ? null : table,
                SourceObjectId = long.TryParse(objectId, NumberStyles.Any, CultureInfo.InvariantCulture, out var oid) ? oid : null,
                Severity = Models.Enums.ErrorSeverity.Error,
                X = x,
                Y = y,
                GeometryWKT = QcError.CreatePointWKT(x, y)
            });
        }

        protected void AddDetailedError(ValidationResult result, string errType, string message, string table = "", string objectId = "", string additionalInfo = "", Geometry? geometry = null, string tableDisplayName = "")
        {
            AddDetailedError(result, errType, message, table, objectId, additionalInfo, geometry, tableDisplayName, null, null);
        }

        protected void AddDetailedError(ValidationResult result, string errType, string message, string table, string objectId, string additionalInfo, Geometry? geometry, string tableDisplayName, string? relatedTableId, string? relatedTableName)
        {
            result.IsValid = false;
            result.ErrorCount += 1;

            var fullMessage = string.IsNullOrWhiteSpace(additionalInfo) ? message : $"{message} ({additionalInfo})";
            var (x, y) = ExtractCentroid(geometry);

            var error = new ValidationError
            {
                ErrorCode = errType,
                Message = fullMessage,
                TableId = string.IsNullOrWhiteSpace(table) ? null : table,
                TableName = !string.IsNullOrWhiteSpace(tableDisplayName) ? tableDisplayName : string.Empty,
                FeatureId = objectId,
                SourceTable = string.IsNullOrWhiteSpace(table) ? null : table,
                SourceObjectId = long.TryParse(objectId, NumberStyles.Any, CultureInfo.InvariantCulture, out var oid) ? oid : null,
                Severity = Models.Enums.ErrorSeverity.Error,
                X = x,
                Y = y,
                GeometryWKT = QcError.CreatePointWKT(x, y)
            };

            if (!string.IsNullOrWhiteSpace(relatedTableId))
            {
                error.Metadata["RelatedTableId"] = relatedTableId;
            }
            if (!string.IsNullOrWhiteSpace(relatedTableName))
            {
                error.Metadata["RelatedTableName"] = relatedTableName;
            }

            result.Errors.Add(error);
        }

        protected (double X, double Y) ExtractCentroid(Geometry? geometry)
        {
            if (geometry == null)
                return (0, 0);

            try
            {
                var geomType = geometry.GetGeometryType();
                var flatType = (wkbGeometryType)((int)geomType & 0xFF);

                if (flatType == wkbGeometryType.wkbPolygon || flatType == wkbGeometryType.wkbMultiPolygon)
                {
                    return GeometryCoordinateExtractor.GetPolygonInteriorPoint(geometry);
                }

                if (flatType == wkbGeometryType.wkbLineString || flatType == wkbGeometryType.wkbMultiLineString)
                {
                    return GeometryCoordinateExtractor.GetLineStringMidpoint(geometry);
                }

                if (flatType == wkbGeometryType.wkbPoint || flatType == wkbGeometryType.wkbMultiPoint)
                {
                    return GeometryCoordinateExtractor.GetFirstVertex(geometry);
                }

                return GeometryCoordinateExtractor.GetEnvelopeCenter(geometry);
            }
            catch
            {
                return (0, 0);
            }
        }

        protected Geometry? GetGeometryByOID(Layer? layer, long oid)
        {
            if (layer == null)
                return null;

            try
            {
                layer.SetAttributeFilter($"OBJECTID = {oid}");
                layer.ResetReading();
                var feature = layer.GetNextFeature();
                layer.SetAttributeFilter(null);

                if (feature != null)
                {
                    using (feature)
                    {
                        var geometry = feature.GetGeometryRef();
                        return geometry?.Clone();
                    }
                }
            }
            catch
            {
                layer.SetAttributeFilter(null);
            }

            return null;
        }

        protected IDisposable ApplyAttributeFilterIfMatch(Layer layer, string fieldFilter)
        {
            if (string.IsNullOrWhiteSpace(fieldFilter))
            {
                return new ActionOnDispose(() => { });
            }

            try
            {
                layer.SetAttributeFilter(fieldFilter);
                return new ActionOnDispose(() => layer.SetAttributeFilter(null));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AttributeFilter 적용 실패: {Filter}", fieldFilter);
                layer.SetAttributeFilter(null);
                return new ActionOnDispose(() => { });
            }
        }

        protected bool IsLineWithinPolygonWithTolerance(Geometry line, Geometry polygon, double tolerance)
        {
            if (line == null || polygon == null) return false;
            
            try
            {
                var pointCount = line.GetPointCount();
                
                for (int i = 0; i < pointCount; i++)
                {
                    var x = line.GetX(i);
                    var y = line.GetY(i);

                    using var pt = new Geometry(wkbGeometryType.wkbPoint);
                    pt.AddPoint(x, y, 0);

                    var dist = pt.Distance(polygon);
                    
                    if (dist > tolerance)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "선형 객체 허용오차 검사 중 오류 발생");
                return false;
            }
        }

        protected class ActionOnDispose : IDisposable
        {
            private readonly Action _onDispose;
            public ActionOnDispose(Action onDispose) { _onDispose = onDispose; }
            public void Dispose() { _onDispose?.Invoke(); }
        }

        #region Line Connectivity Helpers

        /// <summary>
        /// 선분 정보 구조체
        /// </summary>
        protected class LineSegmentInfo
        {
            public long Oid { get; set; }
            public Geometry Geom { get; set; } = null!;
            public double StartX { get; set; }
            public double StartY { get; set; }
            public double EndX { get; set; }
            public double EndY { get; set; }
        }

        /// <summary>
        /// 끝점 정보 구조체
        /// </summary>
        protected class EndpointInfo
        {
            public long Oid { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public bool IsStart { get; set; }
        }

        /// <summary>
        /// 그리드 기반 공간 인덱스에 끝점 추가
        /// </summary>
        protected void AddEndpointToIndex(Dictionary<string, List<EndpointInfo>> index,
            double x, double y, long oid, bool isStart, double gridSize)
        {
            int gridX = (int)Math.Floor(x / gridSize);
            int gridY = (int)Math.Floor(y / gridSize);
            string key = $"{gridX}_{gridY}";

            if (!index.ContainsKey(key))
            {
                index[key] = new List<EndpointInfo>();
            }

            index[key].Add(new EndpointInfo
            {
                Oid = oid,
                X = x,
                Y = y,
                IsStart = isStart
            });
        }

        /// <summary>
        /// 좌표 주변의 끝점 검색 (공간 인덱스 사용)
        /// </summary>
        protected List<EndpointInfo> SearchEndpointsNearby(Dictionary<string, List<EndpointInfo>> index,
            double x, double y, double searchRadius)
        {
            var results = new List<EndpointInfo>();

            int centerGridX = (int)Math.Floor(x / searchRadius);
            int centerGridY = (int)Math.Floor(y / searchRadius);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    string key = $"{centerGridX + dx}_{centerGridY + dy}";
                    if (index.TryGetValue(key, out var endpoints))
                    {
                        results.AddRange(endpoints);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 바운딩 박스 기반 근처 선분 필터링
        /// </summary>
        protected List<LineSegmentInfo> GetNearbySegments(List<LineSegmentInfo> allSegments,
            double sx, double sy, double ex, double ey, double searchRadius)
        {
            var minX = Math.Min(sx, ex) - searchRadius;
            var maxX = Math.Max(sx, ex) + searchRadius;
            var minY = Math.Min(sy, ey) - searchRadius;
            var maxY = Math.Max(sy, ey) + searchRadius;

            return allSegments.Where(seg =>
            {
                var segMinX = Math.Min(seg.StartX, seg.EndX);
                var segMaxX = Math.Max(seg.StartX, seg.EndX);
                var segMinY = Math.Min(seg.StartY, seg.EndY);
                var segMaxY = Math.Max(seg.StartY, seg.EndY);

                return !(maxX < segMinX || minX > segMaxX || maxY < segMinY || minY > segMaxY);
            }).ToList();
        }

        /// <summary>
        /// 두 점 사이의 유클리드 거리
        /// </summary>
        protected static double Distance(double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 면적 계산 시 타입 가드: 폴리곤/멀티폴리곤에서만 면적 반환, 그 외 0
        /// </summary>
        protected static double GetSurfaceArea(Geometry geometry)
        {
            try
            {
                if (geometry == null || geometry.IsEmpty()) return 0.0;
                var t = geometry.GetGeometryType();
                return t == wkbGeometryType.wkbPolygon || t == wkbGeometryType.wkbMultiPolygon
                    ? geometry.GetArea()
                    : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>
        /// 대소문자 무시하고 필드 인덱스 찾기
        /// </summary>
        protected static int GetFieldIndexIgnoreCase(FeatureDefn defn, string fieldName)
        {
            if (defn == null || string.IsNullOrEmpty(fieldName)) return -1;
            for (int i = 0; i < defn.GetFieldCount(); i++)
            {
                using var fd = defn.GetFieldDefn(i);
                if (fd != null && string.Equals(fd.GetName(), fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 피처에서 필드 값을 안전하게 가져옴
        /// </summary>
        protected static string? GetFieldValueSafe(Feature feature, string fieldName)
        {
            try
            {
                var defn = feature.GetDefnRef();
                for (int i = 0; i < defn.GetFieldCount(); i++)
                {
                    using var fd = defn.GetFieldDefn(i);
                    if (fd.GetName().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        return feature.IsFieldNull(i) ? null : feature.GetFieldAsString(i);
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 선분의 끝점 추출 헬퍼 (튜플 리스트에 추가)
        /// </summary>
        protected void ExtractLineEndpoints(Geometry geom, long oid, List<(long Oid, double StartX, double StartY, double EndX, double EndY)> segments)
        {
            if (geom == null || geom.IsEmpty()) return;

            var geomType = geom.GetGeometryType();
            var flatType = (wkbGeometryType)((int)geomType & 0xFF);

            if (flatType == wkbGeometryType.wkbLineString)
            {
                var pointCount = geom.GetPointCount();
                if (pointCount >= 2)
                {
                    double startX = geom.GetX(0);
                    double startY = geom.GetY(0);
                    double endX = geom.GetX(pointCount - 1);
                    double endY = geom.GetY(pointCount - 1);
                    segments.Add((oid, startX, startY, endX, endY));
                }
            }
            else if (flatType == wkbGeometryType.wkbMultiLineString)
            {
                var geomCount = geom.GetGeometryCount();
                for (int i = 0; i < geomCount; i++)
                {
                    using var line = geom.GetGeometryRef(i);
                    if (line != null && !line.IsEmpty())
                    {
                        ExtractLineEndpoints(line, oid, segments);
                    }
                }
            }
        }

        /// <summary>
        /// 선분 정보 추출 헬퍼 (LineSegmentInfo 리스트에 추가)
        /// </summary>
        protected void ExtractLineSegments(Geometry geom, long oid, List<LineSegmentInfo> segments, Dictionary<string, List<EndpointInfo>> endpointIndex, double gridSize)
        {
            if (geom == null || geom.IsEmpty()) return;

            var geomType = geom.GetGeometryType();
            var flatType = (wkbGeometryType)((int)geomType & 0xFF);

            if (flatType == wkbGeometryType.wkbLineString)
            {
                var pointCount = geom.GetPointCount();
                if (pointCount >= 2)
                {
                    var sx = geom.GetX(0);
                    var sy = geom.GetY(0);
                    var ex = geom.GetX(pointCount - 1);
                    var ey = geom.GetY(pointCount - 1);

                    var segmentInfo = new LineSegmentInfo
                    {
                        Oid = oid,
                        Geom = geom.Clone(),
                        StartX = sx,
                        StartY = sy,
                        EndX = ex,
                        EndY = ey
                    };
                    segments.Add(segmentInfo);

                    AddEndpointToIndex(endpointIndex, sx, sy, oid, true, gridSize);
                    AddEndpointToIndex(endpointIndex, ex, ey, oid, false, gridSize);
                }
            }
            else if (flatType == wkbGeometryType.wkbMultiLineString)
            {
                var geomCount = geom.GetGeometryCount();
                for (int i = 0; i < geomCount; i++)
                {
                    using var line = geom.GetGeometryRef(i);
                    if (line != null && !line.IsEmpty())
                    {
                        ExtractLineSegments(line, oid, segments, endpointIndex, gridSize);
                    }
                }
            }
        }

        /// <summary>
        /// 두 벡터 사이의 각도 차이 계산 (도 단위)
        /// </summary>
        protected static double CalculateAngleDifference(double v1x, double v1y, double v2x, double v2y)
        {
            var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

            if (len1 == 0 || len2 == 0) return 180.0;

            var cosAngle = (v1x * v2x + v1y * v2y) / (len1 * len2);
            cosAngle = Math.Max(-1.0, Math.Min(1.0, cosAngle));

            var angleRad = Math.Acos(cosAngle);
            var angleDeg = angleRad * 180.0 / Math.PI;

            return angleDeg;
        }

        #endregion

        #region SQL Filter Helpers

        /// <summary>
        /// SQL 스타일의 IN/NOT IN 필터를 파싱합니다.
        /// </summary>
        protected (bool isNotIn, HashSet<string> values) ParseSqlStyleFilter(string fieldFilter, string fieldName)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool isNotIn = false;

            if (string.IsNullOrWhiteSpace(fieldFilter)) return (isNotIn, values);

            // NOT IN 패턴 먼저 확인
            var notInPattern = $@"(?i){Regex.Escape(fieldName)}\s+NOT\s+IN\s*\(([^)]+)\)";
            var notInMatch = Regex.Match(fieldFilter, notInPattern);
            if (notInMatch.Success)
            {
                isNotIn = true;
                var codeList = notInMatch.Groups[1].Value;
                foreach (var code in codeList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    values.Add(code.Trim('\'', '"'));
                }
                _logger.LogInformation("SQL 필터 파싱 (NOT IN): 필드={Field}, 제외값={Values}", fieldName, string.Join(",", values));
                return (isNotIn, values);
            }

            // IN 패턴 확인
            var inPattern = $@"(?i){Regex.Escape(fieldName)}\s+IN\s*\(([^)]+)\)";
            var inMatch = Regex.Match(fieldFilter, inPattern);
            if (inMatch.Success)
            {
                isNotIn = false;
                var codeList = inMatch.Groups[1].Value;
                foreach (var code in codeList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    values.Add(code.Trim('\'', '"'));
                }
                _logger.LogInformation("SQL 필터 파싱 (IN): 필드={Field}, 포함값={Values}", fieldName, string.Join(",", values));
                return (isNotIn, values);
            }

            return (isNotIn, values);
        }

        /// <summary>
        /// 피처의 필드 값이 SQL 스타일 필터 조건을 만족하는지 검사합니다.
        /// </summary>
        protected static bool ShouldIncludeByFilter(string? fieldValue, bool isNotIn, HashSet<string> filterValues)
        {
            if (filterValues.Count == 0) return true;

            var value = (fieldValue ?? string.Empty).Trim();

            if (isNotIn)
            {
                return !filterValues.Contains(value);
            }
            else
            {
                return filterValues.Contains(value);
            }
        }

        /// <summary>
        /// 레이어의 모든 지오메트리를 Union하여 반환합니다.
        /// </summary>
        protected Geometry? BuildUnionGeometry(Layer layer)
        {
            layer.ResetReading();
            Geometry? union = null;
            Feature? feature;

            while ((feature = layer.GetNextFeature()) != null)
            {
                using (feature)
                {
                    var geom = feature.GetGeometryRef();
                    if (geom == null || geom.IsEmpty()) continue;

                    if (union == null)
                    {
                        union = geom.Clone();
                    }
                    else
                    {
                        using var prev = union;
                        union = prev.Union(geom);
                    }
                }
            }

            return union;
        }

        #endregion
    }
}

