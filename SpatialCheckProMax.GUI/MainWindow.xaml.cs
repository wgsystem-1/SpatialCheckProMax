#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Enums;
using SpatialCheckProMax.GUI.Services;
using SpatialCheckProMax.Constants;
using System.Runtime.Versioning;
using SpatialCheckProMax.Models.Config;

using SpatialCheckProMax.Services;
using WinForms = System.Windows.Forms;
using SpatialCheckProMax.GUI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using SpatialCheckProMax.Services.RemainingTime;

namespace SpatialCheckProMax.GUI
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ILogger<MainWindow> _logger;
        private readonly IValidationOrchestrator _validationOrchestrator;
        private readonly StageSummaryCollectionViewModel _stageSummaryCollectionViewModel;
        private readonly ValidationSettingsViewModel _validationSettingsViewModel;

        private string? _selectedFilePath;
        private List<string> _selectedFilePaths = new List<string>();
        private SpatialCheckProMax.Models.ValidationResult? _currentValidationResult;
        private bool _isValidationRunning;
        private System.Threading.CancellationTokenSource? _validationCancellationTokenSource;
        private Views.CompactValidationProgressView? _currentProgressView;
        private Views.ValidationSettingsView? _validationSettingsView;

        // Config paths (기본값)
        private string _tableConfigPath = "Configs/TableConfig.json";
        private string _schemaConfigPath = "Configs/SchemaConfig.json";
        private string _geometryConfigPath = "Configs/GeometryConfig.json";
        private string _relationConfigPath = "Configs/RelationConfig.json";
        private string _attributeConfigPath = "Configs/AttributeConfig.json";

        private DispatcherTimer? _timer;

        public MainWindow(
            ILogger<MainWindow> logger,
            IValidationOrchestrator validationOrchestrator,
            StageSummaryCollectionViewModel stageSummaryViewModel,
            MainViewModel mainViewModel,
            ValidationSettingsViewModel validationSettingsViewModel)
        {
            InitializeComponent();
            _logger = logger;
            _validationOrchestrator = validationOrchestrator;
            _stageSummaryCollectionViewModel = stageSummaryViewModel;
            _validationSettingsViewModel = validationSettingsViewModel;

            DataContext = mainViewModel;

            // ValidationOrchestrator 이벤트 구독
            SubscribeToOrchestratorEvents();

            InitializeValidationSettingsView();
            InitializeDefaultConfigPaths();
        }

        /// <summary>
        /// ValidationOrchestratorOptions 생성 (ViewModel에서 설정을 가져와 변환)
        /// </summary>
        private ValidationOrchestratorOptions CreateValidationOptions()
        {
            return new ValidationOrchestratorOptions
            {
                TableConfigPath = !string.IsNullOrEmpty(_validationSettingsViewModel.TableConfigPath)
                    ? _validationSettingsViewModel.TableConfigPath : _tableConfigPath,
                SchemaConfigPath = !string.IsNullOrEmpty(_validationSettingsViewModel.SchemaConfigPath)
                    ? _validationSettingsViewModel.SchemaConfigPath : _schemaConfigPath,
                GeometryConfigPath = !string.IsNullOrEmpty(_validationSettingsViewModel.GeometryConfigPath)
                    ? _validationSettingsViewModel.GeometryConfigPath : _geometryConfigPath,
                RelationConfigPath = !string.IsNullOrEmpty(_validationSettingsViewModel.RelationConfigPath)
                    ? _validationSettingsViewModel.RelationConfigPath : _relationConfigPath,
                AttributeConfigPath = !string.IsNullOrEmpty(_validationSettingsViewModel.AttributeConfigPath)
                    ? _validationSettingsViewModel.AttributeConfigPath : _attributeConfigPath,
                CodelistPath = _validationSettingsViewModel.CodelistPath,
                SelectedStage1Items = _validationSettingsViewModel.SelectedStage1Items?
                    .Select(c => c.TableId).ToList(),
                SelectedStage2Items = _validationSettingsViewModel.SelectedStage2Items?
                    .Select(c => c.TableId).ToList(),
                SelectedStage3Items = _validationSettingsViewModel.SelectedStage3Items?
                    .Select(c => c.TableId).ToList(),
                SelectedStage4Items = _validationSettingsViewModel.SelectedStage4Items?
                    .Select(c => c.TableId).ToList(),
                SelectedStage5Items = _validationSettingsViewModel.SelectedStage5Items?
                    .Select(c => c.RuleId).ToList()
            };
        }

        /// <summary>
        /// ValidationOrchestrator 이벤트 구독
        /// </summary>
        private void SubscribeToOrchestratorEvents()
        {
            _validationOrchestrator.ProgressUpdated += OnOrchestratorProgressUpdated;
            _validationOrchestrator.FileCompleted += OnOrchestratorFileCompleted;
            _validationOrchestrator.ValidationCompleted += OnOrchestratorValidationCompleted;
        }

        /// <summary>
        /// ValidationOrchestrator 이벤트 구독 해제
        /// </summary>
        private void UnsubscribeFromOrchestratorEvents()
        {
            _validationOrchestrator.ProgressUpdated -= OnOrchestratorProgressUpdated;
            _validationOrchestrator.FileCompleted -= OnOrchestratorFileCompleted;
            _validationOrchestrator.ValidationCompleted -= OnOrchestratorValidationCompleted;
        }

        /// <summary>
        /// ValidationOrchestrator 진행률 업데이트 이벤트 핸들러
        /// </summary>
        private void OnOrchestratorProgressUpdated(object? sender, ValidationProgressEventArgs args)
        {
            Dispatcher.Invoke(() =>
            {
                _stageSummaryCollectionViewModel.ApplyProgress(args);

                if (args.IsStageCompleted)
                {
                    if (args.IsStageSkipped)
                    {
                        _stageSummaryCollectionViewModel.ForceStageStatus(args.CurrentStage, StageStatus.Skipped, args.StatusMessage);
                    }
                    else
                    {
                        var finalStatus = args.IsStageSuccessful ? StageStatus.Completed : StageStatus.Failed;
                        _stageSummaryCollectionViewModel.ForceStageStatus(args.CurrentStage, finalStatus, args.StatusMessage);
                    }

                    var nextStageNumber = args.CurrentStage + 1;
                    var nextStage = _stageSummaryCollectionViewModel.GetStage(nextStageNumber);
                    if (nextStage != null && nextStage.Status == StageStatus.NotStarted)
                    {
                        _stageSummaryCollectionViewModel.ForceStageStatus(nextStageNumber, StageStatus.Pending, "대기 중");
                    }

                    if (args.PartialResult != null)
                    {
                        _currentValidationResult = args.PartialResult;
                        try
                        {
                            _currentProgressView?.UpdatePartialResults(_currentValidationResult);
                        }
                        catch { }
                    }
                }

                _currentProgressView?.UpdateProgress(args.OverallProgress, args.StatusMessage);
                _currentProgressView?.UpdateCurrentStage(args.StageName, args.CurrentStage);

                try { _currentProgressView?.UpdateStageProgress(args.CurrentStage, args.StageProgress); } catch { }

                try
                {
                    if (args.ProcessedUnits >= 0 && args.TotalUnits >= 0)
                    {
                        _currentProgressView?.UpdateUnits(args.CurrentStage, args.ProcessedUnits, args.TotalUnits);
                    }
                }
                catch { }
            }, DispatcherPriority.Render);
        }

        /// <summary>
        /// ValidationOrchestrator 파일 완료 이벤트 핸들러
        /// </summary>
        private void OnOrchestratorFileCompleted(object? sender, FileCompletedEventArgs args)
        {
            Dispatcher.Invoke(() =>
            {
                _currentValidationResult = args.Result;
                _currentProgressView?.MarkFileCompleted(args.FileIndex, args.IsValid, args.ErrorCount, args.WarningCount);
            });
        }

        /// <summary>
        /// ValidationOrchestrator 검수 완료 이벤트 핸들러
        /// </summary>
        private void OnOrchestratorValidationCompleted(object? sender, ValidationCompletedEventArgs args)
        {
            Dispatcher.Invoke(() =>
            {
                if (args.IsCancelled)
                {
                    UpdateStatus("검수가 취소되었습니다.");
                }
                else if (args.IsSuccess)
                {
                    UpdateStatus($"검수 완료 - 성공: {args.SuccessCount}개, 실패: {args.FailCount}개");
                }
                else if (!string.IsNullOrEmpty(args.ErrorMessage))
                {
                    UpdateStatus($"검수 실패: {args.ErrorMessage}");
                }
            });
        }

        private void UpdateStatus(string message)
        {
            try
            {
                _logger?.LogInformation(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"상태 업데이트 실패: {ex.Message}");
            }
        }

        private void UpdateNavigationButtons(string currentView)
        {
            // Navigation logic placeholder
            // This method updates the state of navigation buttons based on the current view
        }

        /// <summary>
        /// ValidationSettingsView 초기화 및 이벤트 구독
        /// </summary>
        private void InitializeValidationSettingsView()
        {
            try
            {
                // ValidationSettingsView 찾기
                // 주의: ValidationSettingsView는 필요할 때만 생성되므로 초기화 시점에는 null일 수 있음
                _validationSettingsView = FindValidationSettingsView();
                
                if (_validationSettingsView != null)
                {
                    // 성능 설정 변경 이벤트 구독
                    // _validationSettingsView.PerformanceSettingsChanged += OnPerformanceSettingsChanged;
                    _logger?.LogInformation("ValidationSettingsView 이벤트 구독 완료");
                }
                // else: ValidationSettingsView는 필요할 때만 생성되므로 초기화 시점에 없어도 정상
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ValidationSettingsView 초기화 실패");
            }
        }

        /// <summary>
        /// ValidationSettingsView 인스턴스 찾기 (필요시에만 생성)
        /// </summary>
        private Views.ValidationSettingsView? FindValidationSettingsView()
        {
            try
            {
                // 설정 화면은 필요할 때만 생성하고 표시하지 않음
                // 실제 표시는 ValidationSettings_Click에서 처리
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ValidationSettingsView 찾기 실패");
                return null;
            }
        }

        /// <summary>
        /// 로드 완료 후 한 번만 ValidationSettingsView의 DataContext를 설정합니다
        /// </summary>
        private void MainWindow_SetSettingsDataContextOnceOnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_validationSettingsView == null)
                {
                    _validationSettingsView = FindValidationSettingsView();
                }
                if (_validationSettingsView != null && _validationSettingsViewModel != null)
                {
                    _validationSettingsView.DataContext = _validationSettingsViewModel;
                }
            }
            catch { }
            finally
            {
                // 1회성 처리 후 핸들러 제거
                this.Loaded -= MainWindow_SetSettingsDataContextOnceOnLoaded;
            }
        }

        /// <summary>
        /// 시각적 트리에서 특정 타입의 자식 요소 찾기
        /// </summary>
        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        private void InitializeDefaultConfigPaths()
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var configDirectory = Path.Combine(appDirectory, "Config");
            
            _tableConfigPath = Path.Combine(configDirectory, "1_table_check.csv");
            _schemaConfigPath = Path.Combine(configDirectory, "2_schema_check.csv");
            _geometryConfigPath = Path.Combine(configDirectory, "3_geometry_check.csv");
            _attributeConfigPath = Path.Combine(configDirectory, "4_attribute_check.csv");
            _relationConfigPath = Path.Combine(configDirectory, "5_relation_check.csv");
        }

        #region 네비게이션 메서드

        /// <summary>
        /// 파일 선택 화면으로 이동하고 폴더 선택 대화상자를 엽니다.
        /// </summary>
        private void NavigateToFileSelection(object sender, RoutedEventArgs e)
        {
            // 먼저 파일 선택 화면 UI를 구성하고 표시합니다.
            ShowFileSelectionView();
            
            // 이어서 폴더 선택 대화상자를 즉시 엽니다.
            // 사용자가 폴더를 선택하면 UI가 자동으로 새로고침됩니다.
            ShowFolderBrowserAndRefresh();
        }

        /// <summary>
        /// 검수 진행 화면으로 이동합니다
        /// </summary>
        private async void NavigateToValidation(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFilePath))
            {
                MessageBox.Show("먼저 검수할 파일을 선택해주세요.", "파일 미선택", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                NavigateToFileSelection(sender, e);
                return;
            }

			// 메인 컨텐츠 영역에 진행 화면 표시

            // CompactValidationProgressView 생성 및 설정
            var progressView = new Views.CompactValidationProgressView
            {
                DataContext = _stageSummaryCollectionViewModel
            };
            progressView.ValidationStopRequested += (s, args) => StopValidation();
            
            // 예측 모델 적용
            try
            {
                await ApplyPredictedTimesToProgressView(progressView);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "예측 시간 적용 실패");
            }
            
            MainContentContainer.Content = progressView;
            UpdateNavigationButtons("Validation");
            
            // 검수 시작: 단일/배치 분기
            if (_selectedFilePaths != null && _selectedFilePaths.Count > 0)
            {
                _ = StartBatchValidationAsync(progressView);
            }
            else
            {
                _ = StartValidationAsync(progressView);
            }
        }

        /// <summary>
        /// 예측 시간을 진행 뷰에 적용합니다
        /// </summary>
        private async Task ApplyPredictedTimesToProgressView(Views.CompactValidationProgressView progressView)
        {
            try
            {
                int tableCount = 0;
                long featureCount = 0;
                int schemaFieldCount = 20;
                int geometryCheckCount = 5;
                int relationRuleCount = 2;
                int attributeColumnCount = 10;

                // 실제 데이터 통계 수집 (비동기)
                await Task.Run(() =>
                {
                    try
                    {
                        var targetPath = _selectedFilePath;
                        if (!string.IsNullOrEmpty(targetPath) && Directory.Exists(targetPath))
                        {
                            using var ds = OSGeo.OGR.Ogr.Open(targetPath, 0);
                            if (ds != null)
                            {
                                tableCount = ds.GetLayerCount();
                                for (int i = 0; i < tableCount; i++)
                                {
                                    using var layer = ds.GetLayerByIndex(i);
                                    if (layer != null)
                                    {
                                        featureCount += layer.GetFeatureCount(1);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "GDB 통계 수집 실패, 기본값 사용");
                    }
                });

                // 예측 시스템이 0 값을 처리할 수 있으므로 폴백 불필요 (최소 시간 보장 내장)
                var predictor = new Models.ValidationTimePredictor(Microsoft.Extensions.Logging.Abstractions.NullLogger<Models.ValidationTimePredictor>.Instance);
                var predictedTimes = predictor.PredictStageTimes(
                        tableCount, (int)featureCount, schemaFieldCount,
                        geometryCheckCount, relationRuleCount, attributeColumnCount);

                
                // 예측 시간 로그
                _logger?.LogInformation("예측 시간 계산 완료:");
                foreach (var kvp in predictedTimes)
                {
                    _logger?.LogInformation("  {Stage}단계: {Time:F1}초", kvp.Key, kvp.Value);
                }
                
                var metadata = new Dictionary<string, string>
                {
                    { "SchemaFieldCount", schemaFieldCount.ToString() },
                    { "GeometryCheckCount", geometryCheckCount.ToString() },
                    { "RelationRuleCount", relationRuleCount.ToString() }
                };

                foreach (var definition in SpatialCheckProMax.GUI.Constants.StageDefinitions.All)
                {
                    metadata[$"StageId_{definition.StageNumber}"] = definition.StageId;
                    metadata[$"StageName_{definition.StageNumber}"] = definition.StageName;
                }

                long fileSizeBytes = 0;
                if (!string.IsNullOrEmpty(_selectedFilePath) && Directory.Exists(_selectedFilePath))
                {
                    try
                    {
                        fileSizeBytes = new DirectoryInfo(_selectedFilePath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                    }
                    catch { }
                }

                var context = new SpatialCheckProMax.Services.RemainingTime.Models.ValidationRunContext
                {
                    TargetFilePath = _selectedFilePath,
                    FileSizeBytes = fileSizeBytes,
                    FeatureCount = featureCount,
                    LayerCount = tableCount,
                    Metadata = metadata
                };

                _stageSummaryCollectionViewModel.InitializeEta(predictedTimes, context);
                
                _logger?.LogInformation("예측 시간 적용 완료 - 테이블: {Tables}개, 피처: {Features}개", 
                    tableCount, featureCount);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "예측 시간 계산 중 오류");
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// 지도 뷰어 화면으로 이동합니다
        /// </summary>
        private async void NavigateToMapView(object sender, RoutedEventArgs e)
        {
            // 지도 기능 제거: 안내 메시지로 대체
            UpdateStatus("지도 기능이 비활성화되었습니다");
            MessageBox.Show("지도 뷰어 기능이 비활성화되었습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 검수 결과 화면으로 이동합니다
        /// </summary>
		private void NavigateToResults(object sender, RoutedEventArgs e)
        {
			// 결과가 없는 경우 안내 후 종료
			if (_currentValidationResult == null)
			{
				MessageBox.Show("아직 결과가 존재하지 않습니다.", "검수 결과 없음", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}

			var resultView = new Views.ValidationResultView();
			// 현재 검수 결과 설정
			resultView.SetValidationResult(_currentValidationResult);

			// 메인 컨텐츠 영역에 결과 화면 표시
			MainContentContainer.Content = resultView;
			UpdateNavigationButtons("Results");
        }

        /// <summary>
        /// 보고서 화면으로 이동합니다
        /// </summary>
        private void NavigateToReports(object sender, RoutedEventArgs e)
        {
            // 보고서 뷰로 이동
            var reportView = new Views.ReportView();
            if (_currentValidationResult != null)
            {
                reportView.SetValidationResult(_currentValidationResult);
            }
            MainContentContainer.Content = reportView;
            UpdateNavigationButtons("Reports");
        }

        /// <summary>
        /// SHP 변환 대화상자를 표시합니다 (스마트 분할 지원)
        /// </summary>
        private void NavigateToShpConvert(object sender, RoutedEventArgs e)
        {
            try
            {
                var smartShpConvertDialog = new Views.SmartShpConvertDialog
                {
                    Owner = this
                };
                smartShpConvertDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SHP 변환 대화상자 표시 오류");
                MessageBox.Show($"SHP 변환 대화상자 표시 오류: {ex.Message}", "오류", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 좌표 변환 대화상자를 표시합니다
        /// </summary>
        private void NavigateToCoordTransform(object sender, RoutedEventArgs e)
        {
            try
            {
                var coordTransformDialog = new Views.CoordinateTransformDialog
                {
                    Owner = this
                };
                coordTransformDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "좌표 변환 대화상자 표시 오류");
                MessageBox.Show($"좌표 변환 대화상자 표시 오류: {ex.Message}", "오류", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 파일 선택 화면을 표시합니다.
        /// </summary>
        private void ShowFileSelectionView()
        {
            var fileSelectionView = new Views.WelcomeView();
            MainContentContainer.Content = fileSelectionView;
            UpdateNavigationButtons("FileSelection");
        }

        /// <summary>
        /// 폴더 선택 대화상자를 열고, 선택 결과에 따라 UI를 새로고침합니다.
        /// </summary>
        private void ShowFolderBrowserAndRefresh()
        {
            // File Geodatabase 폴더 선택
            string? selectedPath = null;

            using (var folderDialog = new WinForms.FolderBrowserDialog())
            {
                folderDialog.Description = "File Geodatabase(.gdb) 또는 이를 포함한 상위 폴더를 선택하세요";
                folderDialog.ShowNewFolderButton = false;

                if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    var selectedFolder = folderDialog.SelectedPath;

                    // 미리보기 다이얼로그 표시
                    var previewDialog = new Views.FolderSelectionPreviewDialog(selectedFolder)
                    {
                        Owner = this
                    };

                    if (previewDialog.ShowDialog() == true && previewDialog.IsContinue)
                    {
                        // 사용자가 계속하기를 선택한 경우
                        selectedPath = selectedFolder;
                        _selectedFilePaths = previewDialog.SelectedGdbPaths;

                        if (_selectedFilePaths.Count == 0)
                        {
                            MessageBox.Show(
                                "검수 대상 FileGDB가 없습니다.",
                                "대상 없음",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        _logger?.LogInformation("폴더 선택 확정 - 대상: {Count}개 FileGDB", _selectedFilePaths.Count);
                    }
                    else
                    {
                        // 사용자가 취소한 경우
                        _logger?.LogInformation("사용자가 폴더 선택을 취소했습니다");
                        return;
                    }
                }
            }

            // 선택된 경로가 있으면 처리
            if (!string.IsNullOrEmpty(selectedPath))
            {
                _selectedFilePath = selectedPath;
                var fileName = Path.GetFileName(selectedPath);
                
                // UI 업데이트
                UpdateStatus($"선택된 폴더: {fileName}");
                
                // 파일 선택 화면 갱신 (선택된 파일 정보 표시)
                ShowFileSelectionView();
            }
        }

        /// <summary>
        /// 검수를 시작합니다
        /// </summary>
        private async Task StartValidationAsync(Views.CompactValidationProgressView progressView)
        {
            if (_isValidationRunning)
            {
                MessageBox.Show("이미 검수가 진행 중입니다.", "검수 진행 중",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _currentProgressView = progressView;
            _validationCancellationTokenSource = new System.Threading.CancellationTokenSource();
            _isValidationRunning = true;

            try
            {
                UpdateStatus("검수를 시작합니다...");

                // 검수 시작 시간 초기화
                progressView.ResetStartTime();
                var startTime = DateTime.Now;
                progressView.InitializeBatchFiles(new[] { _selectedFilePath! });
                var singleFileName = Path.GetFileName(_selectedFilePath!);
                progressView.UpdateCurrentFile(1, 1, singleFileName, _selectedFilePath);

                // 소요시간 업데이트 타이머
                var progressTimer = new DispatcherTimer(DispatcherPriority.Normal);
                progressTimer.Interval = TimeSpan.FromSeconds(1);
                progressTimer.Tick += (s, e) =>
                {
                    var elapsed = DateTime.Now - startTime;
                    Dispatcher.BeginInvoke(new Action(() => progressView.UpdateElapsedTime(elapsed)), DispatcherPriority.Normal);
                };
                progressTimer.Start();

                // ValidationOrchestrator 옵션 생성
                var options = CreateValidationOptions();
                var token = _validationCancellationTokenSource.Token;

                // ValidationOrchestrator를 통해 검수 실행
                _currentValidationResult = await _validationOrchestrator.StartValidationAsync(
                    _selectedFilePath!,
                    options,
                    token);

                progressView.UpdateProgress(100, "검수 완료");
                progressTimer.Stop();

                try
                {
                    var targetPath = _selectedFilePath ?? string.Empty;
                    var (pdfPath, htmlPath) = GenerateReportsForTarget(_currentValidationResult, targetPath);
                    ShowCompletionDialog(_currentValidationResult, pdfPath, htmlPath);
                }
                catch (Exception rex)
                {
                    MessageBox.Show($"보고서 자동 생성 중 오류: {rex.Message}", "보고서 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                NavigateToResults(this, new RoutedEventArgs());
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("검수가 취소되었습니다.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"검수 실패: {ex.Message}");
                MessageBox.Show($"검수 중 오류가 발생했습니다:\n{ex.Message}", "검수 오류",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isValidationRunning = false;
                _validationCancellationTokenSource?.Dispose();
                _validationCancellationTokenSource = null;
                _currentProgressView = null;
            }
        }

        /// <summary>
        /// 검수를 중지합니다
        /// </summary>
        private void StopValidation()
        {
            if (!_isValidationRunning)
            {
                return;
            }

            var result = MessageBox.Show(
                "진행 중인 검수를 중단하시겠습니까?",
                "검수 중단",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _validationCancellationTokenSource?.Cancel();
                _validationOrchestrator.StopValidation();
                UpdateStatus("검수를 중단하는 중...");
            }
        }

        /// <summary>
        /// 배치 검수를 시작합니다 (.gdb 여러 개 순차 처리)
        /// </summary>
        private async Task StartBatchValidationAsync(Views.CompactValidationProgressView progressView)
        {
            if (_isValidationRunning)
            {
                MessageBox.Show("이미 검수가 진행 중입니다.", "검수 진행 중",
                              MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var targets = (_selectedFilePaths ?? new List<string>()).ToList();
            if (targets.Count == 0 && !string.IsNullOrWhiteSpace(_selectedFilePath))
            {
                targets.Add(_selectedFilePath!);
            }
            if (targets.Count == 0)
            {
                MessageBox.Show("검수 대상이 없습니다.", "대상 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Dispatcher.Invoke(() => progressView.InitializeBatchFiles(targets), DispatcherPriority.Normal);

            _currentProgressView = progressView;
            _validationCancellationTokenSource = new System.Threading.CancellationTokenSource();
            _isValidationRunning = true;

            // 파일별 진행률 추적
            int currentFileIndex = 0;
            int totalFiles = targets.Count;

            // 파일 완료 이벤트 핸들러 (보고서 생성용)
            EventHandler<FileCompletedEventArgs>? fileCompletedHandler = null;
            fileCompletedHandler = (sender, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    currentFileIndex = args.FileIndex;
                    _stageSummaryCollectionViewModel.Reset();
                    progressView.UpdateCurrentFile(args.FileIndex, args.TotalFiles,
                        System.IO.Path.GetFileName(args.FilePath), args.FilePath);

                    // 파일별 보고서 생성
                    if (args.Result != null)
                    {
                        try
                        {
                            var (pdfPath, htmlPath) = GenerateReportsForTarget(args.Result, args.FilePath);
                        }
                        catch (Exception rex)
                        {
                            _logger?.LogWarning(rex, "보고서 생성 중 오류가 발생했습니다.");
                        }
                    }
                });
            };

            // 진행률 업데이트 핸들러 (배치 진행률 계산)
            EventHandler<ValidationProgressEventArgs>? progressHandler = null;
            progressHandler = (sender, args) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    double currentFileProgress = args.OverallProgress;
                    var completed = Math.Max(0, currentFileIndex - 1);
                    double batchPct = ((completed * 100.0) + currentFileProgress) / Math.Max(1, totalFiles);

                    var currentFileName = currentFileIndex > 0 && currentFileIndex <= targets.Count
                        ? System.IO.Path.GetFileName(targets[currentFileIndex - 1])
                        : "";
                    var status = !string.IsNullOrEmpty(currentFileName)
                        ? $"[{currentFileIndex}/{totalFiles}] {currentFileName} - {args.StageName} - {args.StatusMessage}"
                        : $"[{Math.Max(1, currentFileIndex)}/{totalFiles}] {args.StageName} - {args.StatusMessage}";
                    progressView.UpdateProgress(batchPct, status);
                }, DispatcherPriority.Normal);
            };

            _validationOrchestrator.FileCompleted += fileCompletedHandler;
            _validationOrchestrator.ProgressUpdated += progressHandler;

            try
            {
                UpdateStatus("배치 검수를 시작합니다...");

                var startTime = DateTime.Now;
                var progressTimer = new DispatcherTimer(DispatcherPriority.Render);
                progressTimer.Interval = TimeSpan.FromMilliseconds(100);
                progressTimer.Tick += (s, e) =>
                {
                    var elapsed = DateTime.Now - startTime;
                    Dispatcher.Invoke(() => progressView.UpdateElapsedTime(elapsed), DispatcherPriority.Render);
                };
                progressTimer.Start();

                // ValidationOrchestrator 옵션 생성
                var options = CreateValidationOptions();
                var token = _validationCancellationTokenSource.Token;

                // ValidationOrchestrator를 통해 배치 검수 실행
                var batchResults = await _validationOrchestrator.StartBatchValidationAsync(
                    targets,
                    options,
                    token);

                if (!token.IsCancellationRequested)
                {
                    progressView.UpdateProgress(100, "배치 검수 완료");
                    UpdateStatus("배치 검수 완료");

                    // 결과 화면으로 이동하되, 배치 전체 결과를 전달하여 파일 선택이 가능하도록 설정
                    try
                    {
                        var resultView = new Views.ValidationResultView();
                        resultView.SetBatchResults(batchResults);
                        MainContentContainer.Content = resultView;
                        UpdateNavigationButtons("Results");
                    }
                    catch
                    {
                        NavigateToResults(this, new RoutedEventArgs());
                    }
                }
                else
                {
                    UpdateStatus("배치 검사가 취소되었습니다.");
                }

                progressTimer.Stop();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("배치 검수가 취소되었습니다.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"배치 검수 실패: {ex.Message}");
                MessageBox.Show($"배치 검수 중 오류가 발생했습니다:\n{ex.Message}", "검수 오류",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 이벤트 구독 해제
                if (fileCompletedHandler != null)
                    _validationOrchestrator.FileCompleted -= fileCompletedHandler;
                if (progressHandler != null)
                    _validationOrchestrator.ProgressUpdated -= progressHandler;

                _isValidationRunning = false;
                _validationCancellationTokenSource?.Dispose();
                _validationCancellationTokenSource = null;
                _currentProgressView = null;
            }
        }

        /// <summary>
        /// 검수 설정 클릭 이벤트 핸들러
        /// </summary>
        private void ValidationSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 설정 화면을 메인 컨텐츠에 표시
                var settingsView = new Views.ValidationSettingsView();
                MainContentContainer.Content = settingsView;
                UpdateNavigationButtons("Settings");
                
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "검수 설정 화면 표시 오류");
                MessageBox.Show($"검수 설정 화면 표시 오류: {ex.Message}", "오류", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 설정창에서 OK로 나온 결과를 적용하는 헬퍼 (탭 통합용)
        /// </summary>
        /// <param name="settingsWindow">설정 창 인스턴스</param>
        public void ApplyValidationSettingsFromWindow(ValidationSettingsWindow settingsWindow)
        {
            _validationSettingsViewModel.TableConfigPath = settingsWindow.TableConfigPath;
            _validationSettingsViewModel.SchemaConfigPath = settingsWindow.SchemaConfigPath;
            _validationSettingsViewModel.GeometryConfigPath = settingsWindow.GeometryConfigPath;
            _validationSettingsViewModel.RelationConfigPath = settingsWindow.RelationConfigPath;
            _validationSettingsViewModel.AttributeConfigPath = settingsWindow.AttributeConfigPath;
            _validationSettingsViewModel.GeometryCriteriaPath = settingsWindow.GeometryCriteriaPath;

            if (!string.IsNullOrWhiteSpace(settingsWindow.TargetPath))
            {
                _selectedFilePath = settingsWindow.TargetPath;
            }

            _validationSettingsViewModel.EnableStage1 = settingsWindow.EnableStage1;
            _validationSettingsViewModel.EnableStage2 = settingsWindow.EnableStage2;
            _validationSettingsViewModel.EnableStage3 = settingsWindow.EnableStage3;
            _validationSettingsViewModel.EnableStage4 = settingsWindow.EnableStage4;
            _validationSettingsViewModel.EnableStage5 = settingsWindow.EnableStage5;

            _validationSettingsViewModel.SelectedStage1Items = settingsWindow.SelectedStage1Items;
            _validationSettingsViewModel.SelectedStage2Items = settingsWindow.SelectedStage2Items;
            _validationSettingsViewModel.SelectedStage3Items = settingsWindow.SelectedStage3Items;
            _validationSettingsViewModel.SelectedStage4Items = settingsWindow.SelectedStage4Items;
            _validationSettingsViewModel.SelectedStage5Items = settingsWindow.SelectedStage5Items;
        }

        /// <summary>
        /// 지도 뷰로 전환합니다
        /// </summary>
        public void SwitchToMapView()
        {
            // 지도 기능 제거: 동작 없음
            UpdateStatus("지도 기능이 비활성화되었습니다");
        }

        /// <summary>
        /// 지도에서 피처로 줌합니다
        /// </summary>
        /// <param name="tableId">테이블 ID</param>
        /// <param name="objectId">객체 ID</param>
        public void ZoomToFeatureInMap(string tableId, string objectId)
        {
            // 지도 기능 제거: 동작 없음
            UpdateStatus("지도 기능이 비활성화되었습니다");
        }

        /// <summary>
        /// 지도 이동 요청 이벤트 핸들러 - ValidationResultView에서 오류 위치로 지도 이동
        /// </summary>
        private async void OnMapNavigationRequested(object? sender, Views.MapNavigationEventArgs e)
        {
            // 지도 기능 제거: 이벤트 무시
            UpdateStatus("지도 기능 비활성화: 지도 이동 요청이 무시되었습니다");
            await Task.CompletedTask;
        }

        /// <summary>
        /// GDB 파일 분석 디버깅 메서드 (비활성화됨)
        /// </summary>
        private void DebugGdbAnalysis_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("해당 기능은 비활성화되었습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region 보고서 생성 헬퍼 메서드

        /// <summary>
        /// 보고서 파일 경로를 준비합니다
        /// </summary>
        private (string PdfPath, string HtmlPath) PrepareReportPaths(string targetPath)
        {
            var baseDir = Directory.Exists(targetPath) 
                ? targetPath 
                : Path.GetDirectoryName(targetPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var parentDir = Directory.GetParent(baseDir)?.FullName ?? baseDir;

            // 검수 파일명(또는 .gdb 폴더명) 추출
            string rawName;
            if (Directory.Exists(targetPath))
            {
                rawName = Path.GetFileName(targetPath);
                if (rawName.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                    rawName = Path.GetFileNameWithoutExtension(rawName);
            }
            else
            {
                rawName = Path.GetFileNameWithoutExtension(targetPath);
            }

            // 파일명에 사용할 수 없는 문자 치환
            var invalid = Path.GetInvalidFileNameChars();
            var nameToSanitize = (rawName ?? "검수결과").Trim();
            var sanitized = new string(nameToSanitize.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();

            var timestamp = DateTime.Now.ToString(DateTimeFormats.FileTimestamp);
            var baseName = $"{sanitized}_{timestamp}";
            var pdfPath = Path.Combine(parentDir, baseName + ".pdf");
            var htmlPath = Path.Combine(parentDir, baseName + ".html");

            return (pdfPath, htmlPath);
        }

        /// <summary>
        /// 검수 결과에 대한 보고서를 생성합니다
        /// </summary>
        private (string PdfPath, string HtmlPath) GenerateReportsForTarget(SpatialCheckProMax.Models.ValidationResult result, string targetPath)
        {
            var (pdfPath, htmlPath) = PrepareReportPaths(targetPath);

            // PDF 생성
            try
            {
                var app = Application.Current as App;
                var pdfService = app?.GetService<PdfReportService>();
                if (pdfService != null)
                {
                    pdfService.GeneratePdfReport(result, pdfPath);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "PDF 보고서 생성 실패: {PdfPath}", pdfPath);
            }

            // HTML 생성
            try
            {
                var html = new GUI.Views.ReportView();
                html.SetValidationResult(result);
                var htmlContent = html.GenerateHtmlReport();
                File.WriteAllText(htmlPath, htmlContent, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "HTML 보고서 생성 실패: {HtmlPath}", htmlPath);
            }

            return (pdfPath, htmlPath);
        }

        /// <summary>
        /// 검수 완료 다이얼로그를 표시합니다
        /// </summary>
        private void ShowCompletionDialog(SpatialCheckProMax.Models.ValidationResult result, string pdfPath, string htmlPath)
        {
            try
            {
                var dlg = new GUI.Views.ModernMessageDialog
                {
                    Owner = this
                };
                dlg.Configure("검수 완료", "검수가 완료되었습니다!", pdfPath, htmlPath);
                
                // 결과 상태에 따라 스타일 적용
                var status = result.ErrorCount == 0 
                    ? "success" 
                    : (result.WarningCount > 0 ? "partial" : "partial");
                dlg.ApplyStyle("light", status);
                dlg.ShowDialog();
            }
            catch
            {
                // 폴백: 기존 메시지 박스
                MessageBox.Show(
                    $"검수가 완료되었습니다!\n\nPDF: {pdfPath}\nHTML: {htmlPath}", 
                    "검수 완료", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
            }
        }

        #endregion

        /// <summary>
        /// 메인 윈도우 종료 이벤트 핸들러
        /// </summary>
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _logger?.LogInformation("애플리케이션 종료 중...");

                // 타이머 정지
                _timer?.Stop();

                // 검수 진행 중이면 취소
                if (_isValidationRunning)
                {
                    _validationCancellationTokenSource?.Cancel();
                    _validationOrchestrator.StopValidation();
                    _logger?.LogInformation("진행 중인 검수를 취소했습니다.");
                }

                // ValidationOrchestrator 이벤트 구독 해제
                UnsubscribeFromOrchestratorEvents();

                // 리소스 정리
                _validationCancellationTokenSource?.Dispose();

                _logger?.LogInformation("애플리케이션 종료 완료");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "애플리케이션 종료 중 오류 발생");
            }
        }
    }
}

