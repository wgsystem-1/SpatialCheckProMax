using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Valid;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Utils;

namespace SpatialCheckProMax.Processors.GeometryChecks
{
    /// <summary>
    /// GEOS 유효성 검사 전략 (자기교차, 유효성)
    /// ISO 19107 표준 준수
    /// </summary>
    public class GeosValidityCheckStrategy : BaseGeometryCheckStrategy
    {
        public GeosValidityCheckStrategy(ILogger<GeosValidityCheckStrategy> logger) : base(logger)
        {
        }

        public override string CheckType => "GeosValidity";

        public override bool IsEnabled(GeometryCheckConfig config)
        {
            return config.ShouldCheckSelfIntersection ||
                   config.ShouldCheckSelfOverlap ||
                   config.ShouldCheckPolygonInPolygon;
        }

        public override async Task<List<ValidationError>> ExecuteAsync(
            Layer layer,
            GeometryCheckConfig config,
            GeometryCheckContext context,
            CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();

            await Task.Run(() =>
            {
                _logger.LogInformation("GEOS 유효성 검사 시작: {TableId}", config.TableId);

                layer.ResetReading();
                Feature? feature;
                int processedCount = 0;

                while ((feature = layer.GetNextFeature()) != null)
                {
                    using (feature)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedCount++;

                        var geometry = feature.GetGeometryRef();
                        if (geometry == null || geometry.IsEmpty()) continue;

                        var fid = feature.GetFID();

                        // 피처 필터링
                        if (context.FeatureFilterService?.ShouldSkipFeature(feature, config.TableId, out _) == true)
                        {
                            continue;
                        }

                        // GEOS IsValid() 검사
                        bool isGdalValid;
                        try
                        {
                            isGdalValid = geometry.IsValid();
                        }
                        catch
                        {
                            isGdalValid = false;
                        }

                        if (!isGdalValid)
                        {
                            var error = CreateValidityError(geometry, fid, config);
                            if (error != null)
                            {
                                errors.Add(error);
                            }
                        }

                        // IsSimple() 검사 (자기교차)
                        bool isGdalSimple;
                        try
                        {
                            isGdalSimple = geometry.IsSimple();
                        }
                        catch
                        {
                            isGdalSimple = false;
                        }

                        if (!isGdalSimple)
                        {
                            var error = CreateSelfIntersectionError(geometry, fid, config);
                            if (error != null)
                            {
                                errors.Add(error);
                            }
                        }

                        // 진행률 로깅
                        if (processedCount % 100 == 0)
                        {
                            context.OnProgress?.Invoke(processedCount, 0);
                        }
                    }
                }

                _logger.LogInformation("GEOS 유효성 검사 완료: {TableId}, 오류 {Count}개",
                    config.TableId, errors.Count);
            }, cancellationToken);

            return errors;
        }

        private ValidationError? CreateValidityError(Geometry geometry, long fid, GeometryCheckConfig config)
        {
            try
            {
                geometry.ExportToWkt(out string wkt);
                var reader = new WKTReader();
                var ntsGeom = reader.Read(wkt);
                var validator = new IsValidOp(ntsGeom);
                var validationError = validator.ValidationError;

                double errorX = 0, errorY = 0;
                string errorTypeName = "지오메트리 유효성 오류";

                if (validationError != null)
                {
                    errorTypeName = GeometryCoordinateExtractor.GetKoreanErrorType((int)validationError.ErrorType);
                    (errorX, errorY) = GeometryCoordinateExtractor.GetValidationErrorLocation(ntsGeom, validationError);
                }
                else
                {
                    (errorX, errorY) = GeometryCoordinateExtractor.GetEnvelopeCenter(geometry);
                }

                var error = CreateErrorWithMetadata(
                    "LOG_TOP_GEO_003",
                    validationError != null ? $"{errorTypeName}: {validationError.Message}" : "지오메트리 유효성 오류",
                    config.TableId,
                    config.TableName,
                    fid,
                    errorX,
                    errorY,
                    new Dictionary<string, string>
                    {
                        ["X"] = errorX.ToString(),
                        ["Y"] = errorY.ToString(),
                        ["GeometryWkt"] = wkt,
                        ["ErrorType"] = errorTypeName,
                        ["OriginalGeometryWKT"] = wkt
                    });

                return error;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "유효성 오류 생성 실패: FID={FID}", fid);
                return null;
            }
        }

        private ValidationError? CreateSelfIntersectionError(Geometry geometry, long fid, GeometryCheckConfig config)
        {
            try
            {
                geometry.ExportToWkt(out string wkt);
                var reader = new WKTReader();
                var ntsGeom = reader.Read(wkt);

                double errorX = 0, errorY = 0;

                try
                {
                    var simpleOp = new IsSimpleOp(ntsGeom);
                    var nonSimpleLoc = simpleOp.NonSimpleLocation;
                    if (nonSimpleLoc != null)
                    {
                        errorX = nonSimpleLoc.X;
                        errorY = nonSimpleLoc.Y;
                    }
                    else
                    {
                        (errorX, errorY) = GeometryCoordinateExtractor.GetFirstVertex(geometry);
                    }
                }
                catch
                {
                    (errorX, errorY) = GeometryCoordinateExtractor.GetFirstVertex(geometry);
                }

                return CreateError(
                    "LOG_TOP_GEO_003",
                    "자기 교차 오류 (Self-intersection)",
                    config.TableId,
                    config.TableName,
                    fid,
                    geometry,
                    errorX,
                    errorY);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "자기교차 오류 생성 실패: FID={FID}", fid);
                return null;
            }
        }
    }
}
