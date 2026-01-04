using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Models.Enums;
using SpatialCheckProMax.Services;

namespace SpatialCheckProMax.Processors.GeometryChecks
{
    /// <summary>
    /// 겹침 지오메트리 검사 전략 (공간 인덱스 기반 고성능 검사)
    /// </summary>
    public class OverlapCheckStrategy : BaseGeometryCheckStrategy
    {
        public OverlapCheckStrategy(ILogger<OverlapCheckStrategy> logger) : base(logger)
        {
        }

        public override string CheckType => "Overlap";

        public override bool IsEnabled(GeometryCheckConfig config)
        {
            return config.ShouldCheckOverlap;
        }

        public override async Task<List<ValidationError>> ExecuteAsync(
            Layer layer,
            GeometryCheckConfig config,
            GeometryCheckContext context,
            CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();

            if (context.HighPerfValidator == null)
            {
                _logger.LogWarning("HighPerformanceGeometryValidator가 없어 겹침 검사를 스킵합니다.");
                return errors;
            }

            var overlapTolerance = context.Criteria.OverlapTolerance;
            _logger.LogInformation("겹침 지오메트리 검사 시작: {TableId}, 허용오차: {Tolerance}",
                config.TableId, overlapTolerance);

            try
            {
                var overlapErrors = await context.HighPerfValidator.CheckOverlapsHighPerformanceAsync(
                    layer, overlapTolerance);

                foreach (var errorDetail in overlapErrors)
                {
                    errors.Add(new ValidationError
                    {
                        ErrorCode = "LOG_TOP_GEO_002",
                        Message = errorDetail.DetailMessage ?? errorDetail.ErrorType,
                        TableId = config.TableId,
                        TableName = ResolveTableName(config.TableId, config.TableName),
                        FeatureId = errorDetail.ObjectId,
                        Severity = ErrorSeverity.Error,
                        X = errorDetail.X,
                        Y = errorDetail.Y,
                        GeometryWKT = errorDetail.GeometryWkt ?? QcError.CreatePointWKT(errorDetail.X, errorDetail.Y)
                    });
                }

                _logger.LogInformation("겹침 지오메트리 검사 완료: {TableId}, 오류 {Count}개",
                    config.TableId, errors.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "겹침 지오메트리 검사 중 오류 발생: {TableId}", config.TableId);
            }

            return errors;
        }
    }
}
