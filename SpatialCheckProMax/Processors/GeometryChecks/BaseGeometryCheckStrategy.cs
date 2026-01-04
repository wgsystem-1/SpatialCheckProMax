using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Models.Enums;
using SpatialCheckProMax.Utils;

namespace SpatialCheckProMax.Processors.GeometryChecks
{
    /// <summary>
    /// 지오메트리 검사 전략의 기반 클래스
    /// </summary>
    public abstract class BaseGeometryCheckStrategy : IGeometryCheckStrategy
    {
        protected readonly ILogger _logger;

        protected BaseGeometryCheckStrategy(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public abstract string CheckType { get; }

        public abstract Task<List<ValidationError>> ExecuteAsync(
            Layer layer,
            GeometryCheckConfig config,
            GeometryCheckContext context,
            CancellationToken cancellationToken);

        public abstract bool IsEnabled(GeometryCheckConfig config);

        #region Helper Methods

        /// <summary>
        /// ValidationError 생성 헬퍼
        /// </summary>
        protected ValidationError CreateError(
            string errorCode,
            string message,
            string tableId,
            string? tableName,
            long featureId,
            Geometry? geometry = null,
            double? x = null,
            double? y = null)
        {
            double errorX = x ?? 0;
            double errorY = y ?? 0;

            if (!x.HasValue || !y.HasValue)
            {
                if (geometry != null)
                {
                    (errorX, errorY) = GeometryCoordinateExtractor.GetFirstVertex(geometry);
                }
            }

            return new ValidationError
            {
                ErrorCode = errorCode,
                Message = message,
                TableId = tableId,
                TableName = ResolveTableName(tableId, tableName),
                FeatureId = featureId.ToString(),
                Severity = ErrorSeverity.Error,
                X = errorX,
                Y = errorY,
                GeometryWKT = QcError.CreatePointWKT(errorX, errorY)
            };
        }

        /// <summary>
        /// ValidationError 생성 헬퍼 (메타데이터 포함)
        /// </summary>
        protected ValidationError CreateErrorWithMetadata(
            string errorCode,
            string message,
            string tableId,
            string? tableName,
            long featureId,
            double x,
            double y,
            Dictionary<string, string> metadata)
        {
            var error = new ValidationError
            {
                ErrorCode = errorCode,
                Message = message,
                TableId = tableId,
                TableName = ResolveTableName(tableId, tableName),
                FeatureId = featureId.ToString(),
                Severity = ErrorSeverity.Error,
                X = x,
                Y = y,
                GeometryWKT = QcError.CreatePointWKT(x, y)
            };

            foreach (var kvp in metadata)
            {
                error.Metadata[kvp.Key] = kvp.Value;
            }

            return error;
        }

        /// <summary>
        /// 테이블 이름 결정
        /// </summary>
        protected static string ResolveTableName(string tableId, string? tableName) =>
            string.IsNullOrWhiteSpace(tableName) ? tableId : tableName;

        /// <summary>
        /// 지오메트리가 선형 타입인지 확인
        /// </summary>
        protected static bool GeometryRepresentsLine(Geometry geometry)
        {
            var type = WkbFlatten(geometry.GetGeometryType());
            return type == wkbGeometryType.wkbLineString || type == wkbGeometryType.wkbMultiLineString;
        }

        /// <summary>
        /// 지오메트리가 폴리곤 타입인지 확인
        /// </summary>
        protected static bool GeometryRepresentsPolygon(Geometry geometry)
        {
            var type = WkbFlatten(geometry.GetGeometryType());
            return type == wkbGeometryType.wkbPolygon || type == wkbGeometryType.wkbMultiPolygon;
        }

        /// <summary>
        /// 지오메트리가 포인트 타입인지 확인
        /// </summary>
        protected static bool GeometryRepresentsPoint(Geometry geometry)
        {
            var type = WkbFlatten(geometry.GetGeometryType());
            return type == wkbGeometryType.wkbPoint || type == wkbGeometryType.wkbMultiPoint;
        }

        /// <summary>
        /// GDAL wkb 타입에서 상위 플래그 제거
        /// </summary>
        protected static wkbGeometryType WkbFlatten(wkbGeometryType type)
        {
            return (wkbGeometryType)((int)type & 0xFF);
        }

        /// <summary>
        /// 지오메트리 타입이 선형인지 확인 (문자열 기반)
        /// </summary>
        protected static bool GeometryTypeIsLine(string geometryType)
        {
            return geometryType.Contains("LINE", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 지오메트리 타입이 폴리곤인지 확인 (문자열 기반)
        /// </summary>
        protected static bool GeometryTypeIsPolygon(string geometryType)
        {
            return geometryType.Contains("POLYGON", StringComparison.OrdinalIgnoreCase);
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
        /// 면적 계산 시 타입 가드
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
        /// 링이 폐합되었는지 확인
        /// </summary>
        protected static bool RingIsClosed(Geometry ring, double tolerance)
        {
            try
            {
                var pointCount = ring.GetPointCount();
                if (pointCount < 2) return false;

                var first = new double[3];
                var last = new double[3];
                ring.GetPoint(0, first);
                ring.GetPoint(pointCount - 1, last);
                return ArePointsClose(first, last, tolerance);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 두 점이 tolerance 범위 내에 있는지 확인
        /// </summary>
        protected static bool ArePointsClose(double[] p1, double[] p2, double tolerance)
        {
            var dx = p1[0] - p2[0];
            var dy = p1[1] - p2[1];
            var distanceSquared = (dx * dx) + (dy * dy);
            return distanceSquared <= tolerance * tolerance;
        }

        /// <summary>
        /// 링의 고유 정점 수 계산 (tolerance 범위 내 중복 제거)
        /// </summary>
        protected static int GetUniquePointCount(Geometry ring, double tolerance)
        {
            var scaledTolerance = 1.0 / tolerance;
            var unique = new HashSet<(long X, long Y)>();
            var coordinate = new double[3];

            for (var i = 0; i < ring.GetPointCount(); i++)
            {
                ring.GetPoint(i, coordinate);
                var key = ((long)Math.Round(coordinate[0] * scaledTolerance), (long)Math.Round(coordinate[1] * scaledTolerance));
                unique.Add(key);
            }

            return unique.Count;
        }

        /// <summary>
        /// 점에서 선분까지의 거리
        /// </summary>
        protected static double PointToSegmentDistance(double px, double py, double x1, double y1, double x2, double y2)
        {
            var vx = x2 - x1;
            var vy = y2 - y1;
            var lenSq = vx * vx + vy * vy;

            if (lenSq == 0) return Distance(px, py, x1, y1);

            var t = Math.Max(0, Math.Min(1, ((px - x1) * vx + (py - y1) * vy) / lenSq));
            var projX = x1 + t * vx;
            var projY = y1 + t * vy;
            return Distance(px, py, projX, projY);
        }

        /// <summary>
        /// 지오메트리 복제 및 선형화 (곡선 처리)
        /// </summary>
        protected static Geometry? CloneAndLinearize(Geometry geometry)
        {
            try
            {
                var clone = geometry.Clone();
                var linearized = clone?.GetLinearGeometry(0, Array.Empty<string>());
                return linearized ?? clone;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Minimum Vertex Check

        /// <summary>
        /// 최소 정점 판정 결과
        /// </summary>
        protected readonly record struct MinVertexCheckResult(bool IsValid, int ObservedVertices, int RequiredVertices, string Detail)
        {
            public static MinVertexCheckResult Valid(int observed = 0, int required = 0) => new(true, observed, required, string.Empty);
            public static MinVertexCheckResult Invalid(int observed, int required, string detail) => new(false, observed, required, detail);
        }

        /// <summary>
        /// 최소 정점 조건 평가
        /// </summary>
        protected MinVertexCheckResult EvaluateMinimumVertexRequirement(Geometry geometry, double ringClosureTolerance)
        {
            var flattenedType = WkbFlatten(geometry.GetGeometryType());
            return flattenedType switch
            {
                wkbGeometryType.wkbPoint => CheckPointMinimumVertices(geometry),
                wkbGeometryType.wkbMultiPoint => CheckMultiPointMinimumVertices(geometry),
                wkbGeometryType.wkbLineString => CheckLineStringMinimumVertices(geometry),
                wkbGeometryType.wkbMultiLineString => CheckMultiLineStringMinimumVertices(geometry),
                wkbGeometryType.wkbPolygon => CheckPolygonMinimumVertices(geometry, ringClosureTolerance),
                wkbGeometryType.wkbMultiPolygon => CheckMultiPolygonMinimumVertices(geometry, ringClosureTolerance),
                _ => MinVertexCheckResult.Valid()
            };
        }

        private static MinVertexCheckResult CheckPointMinimumVertices(Geometry geometry)
        {
            var pointCount = geometry.GetPointCount();
            return pointCount >= 1
                ? MinVertexCheckResult.Valid(pointCount, 1)
                : MinVertexCheckResult.Invalid(pointCount, 1, "포인트 정점 부족");
        }

        private static MinVertexCheckResult CheckMultiPointMinimumVertices(Geometry geometry)
        {
            var geometryCount = geometry.GetGeometryCount();
            var totalPoints = 0;
            for (var i = 0; i < geometryCount; i++)
            {
                using var component = geometry.GetGeometryRef(i)?.Clone();
                if (component != null)
                {
                    totalPoints += component.GetPointCount();
                }
            }

            return totalPoints >= 1
                ? MinVertexCheckResult.Valid(totalPoints, 1)
                : MinVertexCheckResult.Invalid(totalPoints, 1, "멀티포인트 정점 부족");
        }

        private static MinVertexCheckResult CheckLineStringMinimumVertices(Geometry geometry)
        {
            var pointCount = geometry.GetPointCount();
            return pointCount >= 2
                ? MinVertexCheckResult.Valid(pointCount, 2)
                : MinVertexCheckResult.Invalid(pointCount, 2, "라인 정점 부족");
        }

        private MinVertexCheckResult CheckMultiLineStringMinimumVertices(Geometry geometry)
        {
            var geometryCount = geometry.GetGeometryCount();
            var aggregatedPoints = 0;
            for (var i = 0; i < geometryCount; i++)
            {
                using var component = geometry.GetGeometryRef(i)?.Clone();
                if (component == null) continue;

                var componentCheck = CheckLineStringMinimumVertices(component);
                aggregatedPoints += componentCheck.ObservedVertices;
                if (!componentCheck.IsValid)
                {
                    return MinVertexCheckResult.Invalid(componentCheck.ObservedVertices, componentCheck.RequiredVertices, $"라인 {i} 정점 부족");
                }
            }

            return aggregatedPoints >= 2
                ? MinVertexCheckResult.Valid(aggregatedPoints, 2)
                : MinVertexCheckResult.Invalid(aggregatedPoints, 2, "멀티라인 전체 정점 부족");
        }

        private MinVertexCheckResult CheckPolygonMinimumVertices(Geometry geometry, double ringClosureTolerance)
        {
            var ringCount = geometry.GetGeometryCount();
            if (ringCount == 0)
            {
                return MinVertexCheckResult.Invalid(0, 3, "폴리곤 링 없음");
            }

            var totalPoints = 0;
            for (var i = 0; i < ringCount; i++)
            {
                using var ring = geometry.GetGeometryRef(i)?.Clone();
                if (ring == null) continue;

                ring.FlattenTo2D();

                if (!RingIsClosed(ring, ringClosureTolerance))
                {
                    return MinVertexCheckResult.Invalid(ring.GetPointCount(), 3, $"링 {i}가 폐합되지 않았습니다");
                }

                var pointCount = GetUniquePointCount(ring, ringClosureTolerance);
                totalPoints += pointCount;

                if (pointCount < 3)
                {
                    return MinVertexCheckResult.Invalid(pointCount, 3, $"링 {i} 정점 부족");
                }
            }

            return MinVertexCheckResult.Valid(totalPoints, 3);
        }

        private MinVertexCheckResult CheckMultiPolygonMinimumVertices(Geometry geometry, double ringClosureTolerance)
        {
            var geometryCount = geometry.GetGeometryCount();
            var totalPoints = 0;
            for (var i = 0; i < geometryCount; i++)
            {
                using var polygon = geometry.GetGeometryRef(i)?.Clone();
                if (polygon == null) continue;

                polygon.FlattenTo2D();
                var polygonCheck = CheckPolygonMinimumVertices(polygon, ringClosureTolerance);
                totalPoints += polygonCheck.ObservedVertices;
                if (!polygonCheck.IsValid)
                {
                    return MinVertexCheckResult.Invalid(polygonCheck.ObservedVertices, polygonCheck.RequiredVertices, $"폴리곤 {i} 오류: {polygonCheck.Detail}");
                }
            }

            return totalPoints >= 3
                ? MinVertexCheckResult.Valid(totalPoints, 3)
                : MinVertexCheckResult.Invalid(totalPoints, 3, "멀티폴리곤 전체 정점 부족");
        }

        /// <summary>
        /// 폴리곤 디버그 정보 생성
        /// </summary>
        protected string BuildPolygonDebugInfo(Geometry geometry, MinVertexCheckResult result, double ringClosureTolerance)
        {
            try
            {
                if (!GeometryRepresentsPolygon(geometry))
                {
                    return string.Empty;
                }

                var info = new System.Text.StringBuilder();
                info.AppendLine($"링 개수: {geometry.GetGeometryCount()}");

                for (var i = 0; i < geometry.GetGeometryCount(); i++)
                {
                    try
                    {
                        using var ring = geometry.GetGeometryRef(i)?.Clone();
                        if (ring == null)
                        {
                            info.AppendLine($" - 링 {i}: NULL");
                            continue;
                        }

                        ring.FlattenTo2D();
                        var uniqueCount = GetUniquePointCount(ring, ringClosureTolerance);
                        var isClosed = RingIsClosed(ring, ringClosureTolerance);
                        info.AppendLine($" - 링 {i}: 고유 정점 {uniqueCount}개, 폐합 {(isClosed ? "Y" : "N")}");
                    }
                    catch (Exception ex)
                    {
                        info.AppendLine($" - 링 {i}: 오류 ({ex.Message})");
                    }
                }

                info.AppendLine($"관측 정점: {result.ObservedVertices}, 요구 정점: {result.RequiredVertices}");
                return info.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BuildPolygonDebugInfo 실패");
                return $"디버그 정보 생성 실패: {ex.Message}";
            }
        }

        #endregion
    }
}
