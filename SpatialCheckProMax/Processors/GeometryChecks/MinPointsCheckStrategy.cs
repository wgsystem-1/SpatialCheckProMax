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
    /// 최소 정점 검사 전략
    /// </summary>
    public class MinPointsCheckStrategy : BaseGeometryCheckStrategy
    {
        public MinPointsCheckStrategy(ILogger<MinPointsCheckStrategy> logger) : base(logger)
        {
        }

        public override string CheckType => "MinPoints";

        public override bool IsEnabled(GeometryCheckConfig config)
        {
            return config.ShouldCheckMinPoints;
        }

        public override async Task<List<ValidationError>> ExecuteAsync(
            Layer layer,
            GeometryCheckConfig config,
            GeometryCheckContext context,
            CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();
            var ringClosureTolerance = context.Criteria.RingClosureTolerance;

            await Task.Run(() =>
            {
                _logger.LogInformation("최소 정점 검사 시작: {TableId}", config.TableId);

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

                        Geometry? workingGeometry = null;
                        try
                        {
                            workingGeometry = CloneAndLinearize(geometryRef);
                            if (workingGeometry == null || workingGeometry.IsEmpty()) continue;

                            workingGeometry.FlattenTo2D();

                            var minVertexCheck = EvaluateMinimumVertexRequirement(workingGeometry, ringClosureTolerance);
                            if (!minVertexCheck.IsValid)
                            {
                                var detail = string.IsNullOrWhiteSpace(minVertexCheck.Detail)
                                    ? string.Empty
                                    : $" ({minVertexCheck.Detail})";

                                var (x, y) = GeometryCoordinateExtractor.GetFirstVertex(workingGeometry);
                                workingGeometry.ExportToWkt(out string wkt);

                                errors.Add(CreateErrorWithMetadata(
                                    "LOG_TOP_GEO_008",
                                    $"정점 수가 부족합니다: {minVertexCheck.ObservedVertices}개 (최소: {minVertexCheck.RequiredVertices}개){detail}",
                                    config.TableId,
                                    config.TableName,
                                    fid,
                                    x,
                                    y,
                                    new Dictionary<string, string>
                                    {
                                        ["PolygonDebug"] = BuildPolygonDebugInfo(workingGeometry, minVertexCheck, ringClosureTolerance),
                                        ["X"] = x.ToString(),
                                        ["Y"] = y.ToString(),
                                        ["ObservedVertices"] = minVertexCheck.ObservedVertices.ToString(),
                                        ["RequiredVertices"] = minVertexCheck.RequiredVertices.ToString(),
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

                _logger.LogInformation("최소 정점 검사 완료: {TableId}, 오류 {Count}개",
                    config.TableId, errors.Count);
            }, cancellationToken);

            return errors;
        }
    }
}
