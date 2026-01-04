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
    /// 면형도로시설 버텍스 일치 검사 전략
    /// - 면형도로시설의 버텍스가 도로경계면의 버텍스와 일치해야 함
    /// </summary>
    public class PolygonNotWithinPolygonStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PolygonNotWithinPolygon";

        public PolygonNotWithinPolygonStrategy(ILogger logger) : base(logger)
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
            var arrfc = getLayer(config.MainTableId);
            var boundary = getLayer(config.RelatedTableId);

            if (arrfc == null || boundary == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            var tolerance = config.Tolerance ?? 0.0;
            var fieldFilter = config.FieldFilter ?? string.Empty;

            // SQL 스타일 필터 파싱
            var (isNotIn, filterValues) = ParseSqlStyleFilter(fieldFilter, "pg_rdfc_se");

            using var _attrFilterRestore = ApplyAttributeFilterIfMatch(arrfc, fieldFilter);

            _logger.LogInformation("면형도로시설 버텍스 일치 검사 시작: 허용오차={Tolerance}m", tolerance);
            var startTime = DateTime.Now;

            var boundaryUnion = BuildUnionGeometry(boundary);
            if (boundaryUnion == null)
            {
                _logger.LogWarning("경계면 Union 생성 실패");
                return Task.CompletedTask;
            }

            try { boundaryUnion = boundaryUnion.MakeValid(Array.Empty<string>()); } catch { }

            try
            {
                arrfc.ResetReading();
                var totalFeatures = arrfc.GetFeatureCount(1);
                _logger.LogInformation("면형도로시설 필터 적용: 피처수={Count}, 필터={Filter}",
                    totalFeatures, fieldFilter);

                Feature? pf;
                var processedCount = 0;
                var skippedCount = 0;

                while ((pf = arrfc.GetNextFeature()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    processedCount++;

                    if (processedCount % 50 == 0 || processedCount == totalFeatures)
                    {
                        RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processedCount, totalFeatures);
                    }

                    using (pf)
                    {
                        var code = pf.GetFieldAsString("PG_RDFC_SE") ?? string.Empty;

                        if (!ShouldIncludeByFilter(code, isNotIn, filterValues))
                        {
                            skippedCount++;
                            continue;
                        }

                        var pg = pf.GetGeometryRef();
                        if (pg == null) continue;

                        var oid = pf.GetFID().ToString(CultureInfo.InvariantCulture);

                        bool verticesMatch = false;
                        try
                        {
                            verticesMatch = ArePolygonVerticesAlignedWithBoundary(pg, boundaryUnion, tolerance);

                            if (verticesMatch)
                            {
                                using var inter = pg.GetBoundary()?.Intersection(boundaryUnion.GetBoundary());
                                if (inter == null || inter.IsEmpty() || inter.Length() <= 0)
                                {
                                    verticesMatch = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "면형도로시설 버텍스 일치 검사 중 오류: OID={OID}", oid);
                            verticesMatch = false;
                        }

                        if (!verticesMatch)
                        {
                            AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_025",
                                "면형도로시설의 버텍스가 도로경계면의 버텍스와 일치하지 않습니다",
                                config.MainTableId, oid,
                                $"PG_RDFC_SE={code}, 허용오차={tolerance}m", pg, config.MainTableName,
                                config.RelatedTableId, config.RelatedTableName);
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("면형도로시설 버텍스 일치 검수 완료: 처리 {ProcessedCount}개, 제외 {SkippedCount}개, 소요시간: {Elapsed:F2}초",
                    processedCount, skippedCount, elapsed);

                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processedCount, processedCount, completed: true);
            }
            finally
            {
                boundaryUnion?.Dispose();
            }

            return Task.CompletedTask;
        }

        private bool ArePolygonVerticesAlignedWithBoundary(Geometry polygon, Geometry boundary, double tolerance)
        {
            if (polygon == null || boundary == null) return false;

            try
            {
                using var boundaryLines = boundary.GetBoundary();
                if (boundaryLines == null) return false;

                int parts = polygon.GetGeometryType() == wkbGeometryType.wkbMultiPolygon
                    ? polygon.GetGeometryCount()
                    : 1;

                for (int p = 0; p < parts; p++)
                {
                    Geometry? polyPart = polygon.GetGeometryType() == wkbGeometryType.wkbMultiPolygon
                        ? polygon.GetGeometryRef(p)
                        : polygon;

                    if (polyPart == null || polyPart.GetGeometryType() != wkbGeometryType.wkbPolygon)
                        continue;

                    var exterior = polyPart.GetGeometryRef(0);
                    if (exterior == null) continue;

                    int vertexCount = exterior.GetPointCount();
                    for (int i = 0; i < vertexCount; i++)
                    {
                        double x = exterior.GetX(i);
                        double y = exterior.GetY(i);
                        using var pt = new Geometry(wkbGeometryType.wkbPoint);
                        pt.AddPoint(x, y, 0);

                        double dist = pt.Distance(boundaryLines);
                        if (dist > tolerance)
                        {
                            _logger.LogDebug("버텍스 불일치: ({X},{Y}), 경계선까지 거리={Dist:F6} > Tol={Tol}", x, y, dist, tolerance);
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "면형도로시설 버텍스 일치 검사 중 오류 발생");
                return false;
            }
        }
    }
}
