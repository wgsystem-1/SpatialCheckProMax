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
    /// 짧은 객체 검사 전략 (선형 객체의 최소 길이)
    /// </summary>
    public class ShortObjectCheckStrategy : BaseGeometryCheckStrategy
    {
        public ShortObjectCheckStrategy(ILogger<ShortObjectCheckStrategy> logger) : base(logger)
        {
        }

        public override string CheckType => "ShortObject";

        public override bool IsEnabled(GeometryCheckConfig config)
        {
            return config.ShouldCheckShortObject;
        }

        public override async Task<List<ValidationError>> ExecuteAsync(
            Layer layer,
            GeometryCheckConfig config,
            GeometryCheckContext context,
            CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();
            var minLineLength = context.Criteria.MinLineLength;

            await Task.Run(() =>
            {
                _logger.LogInformation("짧은 객체 검사 시작: {TableId}, 최소 길이: {MinLength}m",
                    config.TableId, minLineLength);

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

                        // 선형 객체만 검사
                        if (!GeometryRepresentsLine(geometryRef)) continue;

                        Geometry? workingGeometry = null;
                        try
                        {
                            workingGeometry = CloneAndLinearize(geometryRef);
                            if (workingGeometry == null || workingGeometry.IsEmpty()) continue;

                            workingGeometry.FlattenTo2D();

                            var length = workingGeometry.Length();
                            if (length < minLineLength && length > 0)
                            {
                                var (midX, midY) = GeometryCoordinateExtractor.GetFirstVertex(workingGeometry);
                                workingGeometry.ExportToWkt(out string wkt);

                                errors.Add(CreateErrorWithMetadata(
                                    "LOG_TOP_GEO_005",
                                    $"선이 너무 짧습니다: {length:F3}m (최소: {minLineLength}m)",
                                    config.TableId,
                                    config.TableName,
                                    fid,
                                    midX,
                                    midY,
                                    new Dictionary<string, string>
                                    {
                                        ["X"] = midX.ToString(),
                                        ["Y"] = midY.ToString(),
                                        ["Length"] = length.ToString("F3"),
                                        ["MinLength"] = minLineLength.ToString("F3"),
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

                _logger.LogInformation("짧은 객체 검사 완료: {TableId}, 오류 {Count}개",
                    config.TableId, errors.Count);
            }, cancellationToken);

            return errors;
        }
    }
}
