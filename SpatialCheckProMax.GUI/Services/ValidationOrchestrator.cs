#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Services;
using SpatialCheckProMax.GUI.Models;

namespace SpatialCheckProMax.GUI.Services
{
    /// <summary>
    /// 검수 오케스트레이션 서비스 구현
    /// MainWindow에서 검수 비즈니스 로직을 분리하여 MVVM 패턴 준수
    /// </summary>
    public class ValidationOrchestrator : IValidationOrchestrator
    {
        private readonly ILogger<ValidationOrchestrator> _logger;
        private readonly SimpleValidationService _validationService;
        private readonly IDataSourcePool _dataSourcePool;
        private readonly ValidationTimePredictor _timePredictor;

        private CancellationTokenSource? _cancellationTokenSource;
        private ValidationResult? _currentResult;
        private bool _isRunning;

        /// <summary>
        /// 검수 진행 중 여부
        /// </summary>
        public bool IsValidationRunning => _isRunning;

        /// <summary>
        /// 현재 검수 결과
        /// </summary>
        public ValidationResult? CurrentResult => _currentResult;

        /// <summary>
        /// 진행률 업데이트 이벤트
        /// </summary>
        public event EventHandler<ValidationProgressEventArgs>? ProgressUpdated;

        /// <summary>
        /// 파일 완료 이벤트
        /// </summary>
        public event EventHandler<FileCompletedEventArgs>? FileCompleted;

        /// <summary>
        /// 검수 완료 이벤트
        /// </summary>
        public event EventHandler<ValidationCompletedEventArgs>? ValidationCompleted;

        public ValidationOrchestrator(
            ILogger<ValidationOrchestrator> logger,
            SimpleValidationService validationService,
            IDataSourcePool dataSourcePool,
            ValidationTimePredictor? timePredictor = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _dataSourcePool = dataSourcePool ?? throw new ArgumentNullException(nameof(dataSourcePool));
            _timePredictor = timePredictor ?? new ValidationTimePredictor(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ValidationTimePredictor>.Instance);

            // ValidationService의 진행률 이벤트를 전달
            _validationService.ProgressUpdated += OnValidationServiceProgressUpdated;
        }

        /// <summary>
        /// 단일 파일 검수 시작
        /// </summary>
        public async Task<ValidationResult> StartValidationAsync(
            string filePath,
            ValidationOrchestratorOptions options,
            CancellationToken cancellationToken = default)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("검수가 이미 진행 중입니다.");
            }

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isRunning = true;
            _currentResult = null;

            var startTime = DateTime.Now;

            try
            {
                _logger.LogInformation("단일 파일 검수 시작: {FilePath}", filePath);

                // 선택된 항목 설정
                ApplySelectedItems(options);

                // 검수 실행
                _currentResult = await _validationService.ValidateAsync(
                    filePath,
                    options.TableConfigPath,
                    options.SchemaConfigPath,
                    options.GeometryConfigPath,
                    options.RelationConfigPath,
                    options.AttributeConfigPath,
                    options.CodelistPath,
                    _cancellationTokenSource.Token);

                // 파일 완료 이벤트 발생
                FileCompleted?.Invoke(this, new FileCompletedEventArgs
                {
                    FileIndex = 1,
                    TotalFiles = 1,
                    FilePath = filePath,
                    IsValid = _currentResult.IsValid,
                    ErrorCount = _currentResult.ErrorCount,
                    WarningCount = _currentResult.WarningCount,
                    Result = _currentResult
                });

                // 실행 데이터 저장 시도
                await SaveValidationRunDataAsync(_currentResult);

                // 검수 완료 이벤트 발생
                ValidationCompleted?.Invoke(this, new ValidationCompletedEventArgs
                {
                    IsBatch = false,
                    IsCancelled = false,
                    IsSuccess = _currentResult.IsValid,
                    SuccessCount = _currentResult.IsValid ? 1 : 0,
                    FailCount = _currentResult.IsValid ? 0 : 1,
                    TotalErrors = _currentResult.ErrorCount,
                    TotalWarnings = _currentResult.WarningCount,
                    Results = new List<ValidationResult> { _currentResult },
                    ElapsedTime = DateTime.Now - startTime
                });

                _logger.LogInformation("단일 파일 검수 완료: {FilePath}, 유효: {IsValid}, 오류: {ErrorCount}",
                    filePath, _currentResult.IsValid, _currentResult.ErrorCount);

                return _currentResult;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("검수가 취소되었습니다: {FilePath}", filePath);

                ValidationCompleted?.Invoke(this, new ValidationCompletedEventArgs
                {
                    IsBatch = false,
                    IsCancelled = true,
                    IsSuccess = false,
                    ElapsedTime = DateTime.Now - startTime
                });

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 중 오류 발생: {FilePath}", filePath);

                ValidationCompleted?.Invoke(this, new ValidationCompletedEventArgs
                {
                    IsBatch = false,
                    IsCancelled = false,
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    ElapsedTime = DateTime.Now - startTime
                });

                throw;
            }
            finally
            {
                _isRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// 배치 검수 시작 (여러 파일)
        /// </summary>
        public async Task<List<ValidationResult>> StartBatchValidationAsync(
            IList<string> filePaths,
            ValidationOrchestratorOptions options,
            CancellationToken cancellationToken = default)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("검수가 이미 진행 중입니다.");
            }

            if (filePaths == null || filePaths.Count == 0)
            {
                throw new ArgumentException("검수 대상 파일이 없습니다.", nameof(filePaths));
            }

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isRunning = true;
            _currentResult = null;

            var startTime = DateTime.Now;
            var results = new List<ValidationResult>();
            int successCount = 0;
            int failCount = 0;
            int totalErrors = 0;
            int totalWarnings = 0;

            try
            {
                _logger.LogInformation("배치 검수 시작: {FileCount}개 파일", filePaths.Count);

                // 선택된 항목 설정
                ApplySelectedItems(options);

                for (int i = 0; i < filePaths.Count; i++)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    var filePath = filePaths[i];
                    var fileName = System.IO.Path.GetFileName(filePath);

                    _logger.LogInformation("배치 검수 진행: [{Index}/{Total}] {FileName}",
                        i + 1, filePaths.Count, fileName);

                    try
                    {
                        var result = await _validationService.ValidateAsync(
                            filePath,
                            options.TableConfigPath,
                            options.SchemaConfigPath,
                            options.GeometryConfigPath,
                            options.RelationConfigPath,
                            options.AttributeConfigPath,
                            options.CodelistPath,
                            _cancellationTokenSource.Token);

                        _currentResult = result;
                        results.Add(result);

                        if (result.IsValid)
                            successCount++;
                        else
                            failCount++;

                        totalErrors += result.ErrorCount;
                        totalWarnings += result.WarningCount;

                        // 파일 완료 이벤트 발생
                        FileCompleted?.Invoke(this, new FileCompletedEventArgs
                        {
                            FileIndex = i + 1,
                            TotalFiles = filePaths.Count,
                            FilePath = filePath,
                            IsValid = result.IsValid,
                            ErrorCount = result.ErrorCount,
                            WarningCount = result.WarningCount,
                            Result = result
                        });

                        // 파일별 캐시 정리
                        CleanupFileResources(filePath);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "파일 검수 중 오류: {FilePath}", filePath);
                        failCount++;

                        // 오류가 발생해도 다음 파일 계속 처리
                        FileCompleted?.Invoke(this, new FileCompletedEventArgs
                        {
                            FileIndex = i + 1,
                            TotalFiles = filePaths.Count,
                            FilePath = filePath,
                            IsValid = false,
                            ErrorCount = 1
                        });
                    }
                }

                var isCancelled = _cancellationTokenSource.Token.IsCancellationRequested;

                // 검수 완료 이벤트 발생
                ValidationCompleted?.Invoke(this, new ValidationCompletedEventArgs
                {
                    IsBatch = true,
                    IsCancelled = isCancelled,
                    IsSuccess = failCount == 0 && !isCancelled,
                    SuccessCount = successCount,
                    FailCount = failCount,
                    TotalErrors = totalErrors,
                    TotalWarnings = totalWarnings,
                    Results = results,
                    ElapsedTime = DateTime.Now - startTime
                });

                _logger.LogInformation("배치 검수 완료: 성공 {SuccessCount}개, 실패 {FailCount}개, 총 오류 {TotalErrors}개",
                    successCount, failCount, totalErrors);

                return results;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("배치 검수가 취소되었습니다.");

                ValidationCompleted?.Invoke(this, new ValidationCompletedEventArgs
                {
                    IsBatch = true,
                    IsCancelled = true,
                    IsSuccess = false,
                    SuccessCount = successCount,
                    FailCount = failCount,
                    TotalErrors = totalErrors,
                    TotalWarnings = totalWarnings,
                    Results = results,
                    ElapsedTime = DateTime.Now - startTime
                });

                throw;
            }
            finally
            {
                // 남은 리소스 정리
                foreach (var filePath in filePaths)
                {
                    CleanupFileResources(filePath);
                }

                _isRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// 검수 중지
        /// </summary>
        public void StopValidation()
        {
            if (!_isRunning)
            {
                return;
            }

            _logger.LogInformation("검수 중지 요청");
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// 예측 시간 계산
        /// </summary>
        public async Task<Dictionary<int, TimeSpan>> CalculatePredictedTimesAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                var result = new Dictionary<int, TimeSpan>();

                try
                {
                    // 기본 메트릭 값 사용 (실제로는 파일 분석 필요)
                    var predictions = _timePredictor.PredictStageTimes(
                        tableCount: 52,
                        totalFeatureCount: 10000,
                        schemaFieldCount: 400,
                        geometryCheckItemCount: 232,
                        relationRuleCount: 100,
                        attributeColumnCount: 0);

                    result[1] = TimeSpan.FromSeconds(predictions.GetValueOrDefault(1, 1));
                    result[2] = TimeSpan.FromSeconds(predictions.GetValueOrDefault(2, 5));
                    result[3] = TimeSpan.FromSeconds(predictions.GetValueOrDefault(3, 10));
                    result[4] = TimeSpan.FromSeconds(predictions.GetValueOrDefault(4, 30));
                    result[5] = TimeSpan.FromSeconds(predictions.GetValueOrDefault(5, 5));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "예측 시간 계산 실패");

                    // 기본값 반환
                    result[1] = TimeSpan.FromSeconds(1);
                    result[2] = TimeSpan.FromSeconds(5);
                    result[3] = TimeSpan.FromSeconds(10);
                    result[4] = TimeSpan.FromSeconds(30);
                    result[5] = TimeSpan.FromSeconds(5);
                }

                return result;
            });
        }

        /// <summary>
        /// ValidationService의 진행률 이벤트 전달
        /// </summary>
        private void OnValidationServiceProgressUpdated(object? sender, ValidationProgressEventArgs e)
        {
            ProgressUpdated?.Invoke(this, e);
        }

        /// <summary>
        /// 선택된 항목을 ValidationService에 적용
        /// </summary>
        private void ApplySelectedItems(ValidationOrchestratorOptions options)
        {
            _validationService._selectedStage1Items = options.SelectedStage1Items?
                .Select(name => new TableCheckConfig { TableId = name }).ToList();
            _validationService._selectedStage2Items = options.SelectedStage2Items?
                .Select(name => new SchemaCheckConfig { TableId = name }).ToList();
            _validationService._selectedStage3Items = options.SelectedStage3Items?
                .Select(name => new GeometryCheckConfig { TableId = name }).ToList();
            _validationService._selectedStage4Items = options.SelectedStage4Items?
                .Select(name => new AttributeCheckConfig { TableId = name }).ToList();
            _validationService._selectedStage5Items = options.SelectedStage5Items?
                .Select(name => new RelationCheckConfig { RuleId = name }).ToList();
        }

        /// <summary>
        /// 검수 실행 데이터 저장
        /// </summary>
        private async Task SaveValidationRunDataAsync(ValidationResult result)
        {
            try
            {
                var runData = new ValidationTimePredictor.ValidationRunData
                {
                    Timestamp = result.StartedAt,
                    FilePath = result.TargetFile,
                    TableCount = result.TableCheckResult?.TotalTableCount ?? 0,
                    TotalFeatureCount = result.TableCheckResult?.TableResults?.Sum(t => t.FeatureCount ?? 0) ?? 0,
                    SchemaFieldCount = result.SchemaCheckResult?.TotalColumnCount ?? 0,
                    GeometryCheckItemCount = 232,
                    RelationRuleCount = 100,
                    AttributeColumnCount = 0,
                    Stage0Time = 0.2,
                    Stage1Time = 0,
                    Stage2Time = 0,
                    Stage3Time = 0,
                    Stage4Time = 0,
                    Stage5Time = 0,
                    TotalTime = result.ProcessingTime.TotalSeconds
                };

                _timePredictor.SaveRunData(runData);
                _logger.LogDebug("검수 실행 데이터 저장 완료");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "검수 실행 데이터 저장 실패");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 파일별 리소스 정리
        /// </summary>
        private void CleanupFileResources(string filePath)
        {
            try
            {
                _validationService.ClearAllCachesForFile(filePath);
                _dataSourcePool.RemoveDataSource(filePath);
                _logger.LogDebug("파일 리소스 정리 완료: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "파일 리소스 정리 중 오류: {FilePath}", filePath);
            }
        }
    }
}
