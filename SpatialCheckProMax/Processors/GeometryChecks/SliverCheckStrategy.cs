using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Utils;

namespace SpatialCheckProMax.Processors.GeometryChecks
{
    /// <summary>
    /// 슬리버 폴리곤 검사 전략 (면적/형태지수/신장률 기반)
    /// </summary>
    public class SliverCheckStrategy : BaseGeometryCheckStrategy
    {
        public SliverCheckStrategy(ILogger<SliverCheckStrategy> logger) : base(logger)
        {
        }

        public override string CheckType => "Sliver";

        public override bool IsEnabled(GeometryCheckConfig config)
        {
            return config.ShouldCheckSliver && config.GeometryType.Contains("POLYGON");
        }

        public override async Task<List<ValidationError>> ExecuteAsync(
            Layer layer,
            GeometryCheckConfig config,
            GeometryCheckContext context,
            CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();
            var criteria = context.Criteria;

            await Task.Run(() =>
            {
                _logger.LogInformation("슬리버 검사 시작: {TableId}", config.TableId);

                layer.ResetReading();
                Feature? feature;
                int processedCount = 0;

                while ((feature = layer.GetNextFeature()) != null)
                {
                    using (feature)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedCount++;

                        var geometryRef = feature.GetGeometryRef();
                        if (geometryRef == null || geometryRef.IsEmpty()) continue;

                        var fid = feature.GetFID();

                        // 피처 필터링
                        if (context.FeatureFilterService?.ShouldSkipFeature(feature, config.TableId, out _) == true)
                        {
                            continue;
                        }

                        // 폴리곤 객체만 검사
                        if (!GeometryRepresentsPolygon(geometryRef)) continue;

                        Geometry? workingGeometry = null;
                        try
                        {
                            workingGeometry = CloneAndLinearize(geometryRef);
                            if (workingGeometry == null || workingGeometry.IsEmpty()) continue;

                            if (IsSliverPolygon(workingGeometry, criteria, out string sliverMessage))
                            {
                                var (centerX, centerY) = GeometryCoordinateExtractor.GetFirstVertex(workingGeometry);
                                geometryRef.ExportToWkt(out string wkt);

                                errors.Add(CreateErrorWithMetadata(
                                    "LOG_TOP_GEO_004",
                                    sliverMessage,
                                    config.TableId,
                                    config.TableName,
                                    fid,
                                    centerX,
                                    centerY,
                                    new Dictionary<string, string>
                                    {
                                        ["X"] = centerX.ToString(),
                                        ["Y"] = centerY.ToString(),
                                        ["OriginalGeometryWKT"] = wkt
                                    }));
                            }
                        }
                        finally
                        {
                            workingGeometry?.Dispose();
                        }

                        // 진행률 로깅
                        if (processedCount % 100 == 0)
                        {
                            context.OnProgress?.Invoke(processedCount, 0);
                        }
                    }
                }

                _logger.LogInformation("슬리버 검사 완료: {TableId}, 오류 {Count}개",
                    config.TableId, errors.Count);
            }, cancellationToken);

            return errors;
        }

        /// <summary>
        /// 슬리버 폴리곤 판정 (면적/형태지수/신장률 기반)
        /// </summary>
        private bool IsSliverPolygon(Geometry geometry, GeometryCriteria criteria, out string message)
        {
            message = string.Empty;

            try
            {
                var area = GetSurfaceArea(geometry);

                // 면적이 0 또는 음수면 스킵
                if (area <= 0) return false;

                using var boundary = geometry.Boundary();
                if (boundary == null) return false;

                var perimeter = boundary.Length();
                if (perimeter <= 0) return false;

                // 형태 지수 (Shape Index) = 4π × Area / Perimeter²
                // 1(원)에 가까울수록 조밀, 0에 가까울수록 얇고 긺
                var shapeIndex = (4 * Math.PI * area) / (perimeter * perimeter);

                // 신장률 (Elongation) = Perimeter² / (4π × Area)
                var elongation = (perimeter * perimeter) / (4 * Math.PI * area);

                // 슬리버 판정: 모든 조건을 동시에 만족해야 함 (AND 조건)
                if (area < criteria.SliverArea &&
                    shapeIndex < criteria.SliverShapeIndex &&
                    elongation > criteria.SliverElongation)
                {
                    message = $"슬리버 폴리곤: 면적={area:F2}㎡ (< {criteria.SliverArea}), " +
                              $"형태지수={shapeIndex:F3} (< {criteria.SliverShapeIndex}), " +
                              $"신장률={elongation:F1} (> {criteria.SliverElongation})";
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "슬리버 검사 중 오류");
            }

            return false;
        }
    }
}
