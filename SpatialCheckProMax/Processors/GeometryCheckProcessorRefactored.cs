using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Processors.GeometryChecks;
using SpatialCheckProMax.Services;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Collections.Concurrent;
using ConfigPerformanceSettings = SpatialCheckProMax.Models.Config.PerformanceSettings;

namespace SpatialCheckProMax.Processors
{
    /// <summary>
    /// 지오메트리 검수 프로세서 (Strategy 패턴 적용 리팩토링 버전)
    /// ISO 19107 표준 준수 및 최적화된 알고리즘 적용
    /// </summary>
    public class GeometryCheckProcessorRefactored : IGeometryCheckProcessor
    {
        private readonly ILogger<GeometryCheckProcessorRefactored> _logger;
        private readonly SpatialIndexService? _spatialIndexService;
        private readonly HighPerformanceGeometryValidator? _highPerfValidator;
        private readonly GeometryCriteria _criteria;
        private readonly IFeatureFilterService _featureFilterService;
        private readonly IEnumerable<IGeometryCheckStrategy> _strategies;

        // 공간 인덱스 캐싱
        private readonly ConcurrentDictionary<string, object> _spatialIndexCache = new();

        public GeometryCheckProcessorRefactored(
            ILogger<GeometryCheckProcessorRefactored> logger,
            IEnumerable<IGeometryCheckStrategy> strategies,
            SpatialIndexService? spatialIndexService = null,
            HighPerformanceGeometryValidator? highPerfValidator = null,
            GeometryCriteria? criteria = null,
            IFeatureFilterService? featureFilterService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
            _spatialIndexService = spatialIndexService;
            _highPerfValidator = highPerfValidator;
            _criteria = criteria ?? GeometryCriteria.CreateDefault();
            _featureFilterService = featureFilterService ?? new FeatureFilterService(
                logger as ILogger<FeatureFilterService> ?? new LoggerFactory().CreateLogger<FeatureFilterService>(),
                new ConfigPerformanceSettings());
        }

        /// <summary>
        /// 마지막 실행에서 스킵된 피처 수
        /// </summary>
        public int LastSkippedFeatureCount { get; private set; }

        /// <summary>
        /// 공간 인덱스 캐시 정리
        /// </summary>
        public void ClearSpatialIndexCache()
        {
            _spatialIndexCache.Clear();
            _logger.LogDebug("공간 인덱스 캐시 정리 완료");

            if (_highPerfValidator != null)
            {
                _highPerfValidator.ClearSpatialIndexCache();
            }
        }

        /// <summary>
        /// 특정 파일의 공간 인덱스 캐시 정리
        /// </summary>
        public void ClearSpatialIndexCacheForFile(string filePath)
        {
            _spatialIndexCache.Clear();
            _logger.LogDebug("공간 인덱스 캐시 정리 완료 (파일별)");

            if (_highPerfValidator != null)
            {
                _highPerfValidator.RemoveSpatialIndexCacheForFile(filePath);
            }
        }

        /// <summary>
        /// 전체 지오메트리 검수 (Strategy 패턴 적용)
        /// </summary>
        public async Task<ValidationResult> ProcessAsync(
            string filePath,
            GeometryCheckConfig config,
            CancellationToken cancellationToken = default,
            string? streamingOutputPath = null)
        {
            var result = new ValidationResult { IsValid = true };
            IStreamingErrorWriter? errorWriter = null;

            try
            {
                _logger.LogInformation("지오메트리 검수 시작 (Strategy 패턴): {TableId} ({TableName}), 스트리밍 모드: {StreamingEnabled}",
                    config.TableId, config.TableName, streamingOutputPath != null);
                var startTime = DateTime.Now;

                // HighPerformanceGeometryValidator에 현재 파일 경로 설정
                if (_highPerfValidator != null)
                {
                    _highPerfValidator.SetCurrentFilePath(filePath);
                }

                using var ds = Ogr.Open(filePath, 0);
                if (ds == null)
                {
                    return new ValidationResult { IsValid = false, Message = $"파일을 열 수 없습니다: {filePath}" };
                }

                var layer = ds.GetLayerByName(config.TableId);
                if (layer == null)
                {
                    _logger.LogWarning("레이어를 찾을 수 없습니다: {TableId}", config.TableId);
                    return result;
                }

                // 필터 적용 전 총 피처 수
                var totalFeatureCountBeforeFilter = layer.GetFeatureCount(1);

                LastSkippedFeatureCount = 0;
                var filterOutcome = _featureFilterService.ApplyObjectChangeFilter(
                    layer,
                    "Geometry",
                    config.TableId);
                if (filterOutcome.Applied)
                {
                    LastSkippedFeatureCount = filterOutcome.ExcludedCount;
                }
                result.SkippedCount = LastSkippedFeatureCount;

                var featureCount = layer.GetFeatureCount(1);
                _logger.LogInformation("검수 대상 피처: {Count}개 (필터 전: {BeforeFilter}, 제외: {Excluded})",
                    featureCount, totalFeatureCountBeforeFilter, LastSkippedFeatureCount);

                // 스트리밍 모드 활성화
                if (!string.IsNullOrEmpty(streamingOutputPath))
                {
                    errorWriter = new StreamingErrorWriter(streamingOutputPath,
                        _logger as ILogger<StreamingErrorWriter> ??
                        new LoggerFactory().CreateLogger<StreamingErrorWriter>());
                }

                // 검사 컨텍스트 생성
                var context = new GeometryCheckContext
                {
                    FilePath = filePath,
                    Criteria = _criteria,
                    FeatureFilterService = _featureFilterService,
                    HighPerfValidator = _highPerfValidator,
                    StreamingErrorWriter = errorWriter
                };

                // 활성화된 Strategy 실행
                var enabledStrategies = _strategies.Where(s => s.IsEnabled(config)).ToList();
                _logger.LogInformation("활성화된 검사 전략: {Count}개 ({Strategies})",
                    enabledStrategies.Count,
                    string.Join(", ", enabledStrategies.Select(s => s.CheckType)));

                foreach (var strategy in enabledStrategies)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _logger.LogDebug("검사 전략 실행: {CheckType}", strategy.CheckType);

                    // 레이어 리셋 (각 전략이 처음부터 읽을 수 있도록)
                    layer.ResetReading();

                    var strategyErrors = await strategy.ExecuteAsync(layer, config, context, cancellationToken);

                    if (errorWriter != null)
                    {
                        await errorWriter.WriteErrorsAsync(strategyErrors);
                    }
                    else
                    {
                        result.Errors.AddRange(strategyErrors);
                    }
                    result.ErrorCount += strategyErrors.Count;
                }

                // 스트리밍 완료 및 통계 업데이트
                if (errorWriter != null)
                {
                    var finalStats = await errorWriter.FinalizeAsync();
                    result.ErrorCount = finalStats.TotalErrorCount;
                    result.WarningCount = finalStats.TotalWarningCount;
                    result.Message = $"오류가 파일에 기록되었습니다: {streamingOutputPath}";

                    _logger.LogInformation("스트리밍 모드 통계 - 오류: {ErrorCount}개, 경고: {WarningCount}개, 출력: {OutputPath}",
                        finalStats.TotalErrorCount, finalStats.TotalWarningCount, streamingOutputPath);
                }

                result.IsValid = result.ErrorCount == 0;
                var elapsed = (DateTime.Now - startTime).TotalSeconds;

                _logger.LogInformation("지오메트리 검수 완료 (Strategy 패턴): {TableId}, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초",
                    config.TableId, result.ErrorCount, elapsed);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 검수 실패: {TableId}", config.TableId);
                return new ValidationResult { IsValid = false, Message = $"검수 중 오류 발생: {ex.Message}" };
            }
            finally
            {
                errorWriter?.Dispose();

                if (_highPerfValidator != null)
                {
                    _highPerfValidator.SetCurrentFilePath(null);
                }
            }
        }

        public async Task<ValidationResult> CheckDuplicateGeometriesAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("중복 지오메트리 검수 시작: {TableId}", config.TableId);

            var newConfig = new GeometryCheckConfig
            {
                TableId = config.TableId,
                TableName = config.TableName,
                GeometryType = config.GeometryType,
                CheckDuplicate = "Y"
            };
            return await ProcessAsync(filePath, newConfig, cancellationToken);
        }

        public async Task<ValidationResult> CheckOverlappingGeometriesAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("겹치는 지오메트리 검수 시작: {TableId}", config.TableId);

            var newConfig = new GeometryCheckConfig
            {
                TableId = config.TableId,
                TableName = config.TableName,
                GeometryType = config.GeometryType,
                CheckOverlap = "Y"
            };
            return await ProcessAsync(filePath, newConfig, cancellationToken);
        }

        public async Task<ValidationResult> CheckTwistedGeometriesAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("뒤틀린 지오메트리 검수 시작: {TableId}", config.TableId);

            var newConfig = new GeometryCheckConfig
            {
                TableId = config.TableId,
                TableName = config.TableName,
                GeometryType = config.GeometryType,
                CheckSelfIntersection = "Y"
            };
            return await ProcessAsync(filePath, newConfig, cancellationToken);
        }

        public async Task<ValidationResult> CheckSliverPolygonsAsync(string filePath, GeometryCheckConfig config, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("슬리버 폴리곤 검수 시작: {TableId}", config.TableId);

            var newConfig = new GeometryCheckConfig
            {
                TableId = config.TableId,
                TableName = config.TableName,
                GeometryType = config.GeometryType,
                CheckSliver = "Y"
            };
            return await ProcessAsync(filePath, newConfig, cancellationToken);
        }
    }
}
