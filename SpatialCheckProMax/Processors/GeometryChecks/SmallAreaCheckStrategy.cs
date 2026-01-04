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
    /// 작은 면적 검사 전략 (폴리곤의 최소 면적)
    /// </summary>
    public class SmallAreaCheckStrategy : BaseGeometryCheckStrategy
    {
        public SmallAreaCheckStrategy(ILogger<SmallAreaCheckStrategy> logger) : base(logger)
        {
        }

        public override string CheckType => "SmallArea";

        public override bool IsEnabled(GeometryCheckConfig config)
        {
            return config.ShouldCheckSmallArea;
        }

        public override async Task<List<ValidationError>> ExecuteAsync(
            Layer layer,
            GeometryCheckConfig config,
            GeometryCheckContext context,
            CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();
            var minPolygonArea = context.Criteria.MinPolygonArea;

            await Task.Run(() =>
            {
                _logger.LogInformation("작은 면적 검사 시작: {TableId}, 최소 면적: {MinArea}㎡",
                    config.TableId, minPolygonArea);

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

                            workingGeometry.FlattenTo2D();

                            var area = workingGeometry.GetArea();
                            if (area > 0 && area < minPolygonArea)
                            {
                                var (centerX, centerY) = GeometryCoordinateExtractor.GetFirstVertex(workingGeometry);
                                workingGeometry.ExportToWkt(out string wkt);

                                errors.Add(CreateErrorWithMetadata(
                                    "LOG_TOP_GEO_006",
                                    $"면적이 너무 작습니다: {area:F2}㎡ (최소: {minPolygonArea}㎡)",
                                    config.TableId,
                                    config.TableName,
                                    fid,
                                    centerX,
                                    centerY,
                                    new Dictionary<string, string>
                                    {
                                        ["X"] = centerX.ToString(),
                                        ["Y"] = centerY.ToString(),
                                        ["Area"] = area.ToString("F2"),
                                        ["MinArea"] = minPolygonArea.ToString("F2"),
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

                _logger.LogInformation("작은 면적 검사 완료: {TableId}, 오류 {Count}개",
                    config.TableId, errors.Count);
            }, cancellationToken);

            return errors;
        }
    }
}
