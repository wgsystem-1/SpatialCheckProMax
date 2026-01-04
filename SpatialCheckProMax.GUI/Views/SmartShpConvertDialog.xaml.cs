#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SpatialCheckProMax.GUI.Services;
using WinForms = System.Windows.Forms;

namespace SpatialCheckProMax.GUI.Views
{
    /// <summary>
    /// 레이어 분석 결과 뷰모델
    /// </summary>
    public class LayerAnalysisViewModel : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private int _splitCount;
        private string _status = "대기";

        public string Name { get; set; } = string.Empty;
        public string GeometryType { get; set; } = string.Empty;
        public long FeatureCount { get; set; }
        public long EstimatedTotalBytes { get; set; }
        public int RecommendedSplitCount { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public int SplitCount
        {
            get => _splitCount;
            set { _splitCount = value; OnPropertyChanged(nameof(SplitCount)); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public string FeatureCountFormatted => FeatureCount.ToString("N0");

        public string EstimatedSizeFormatted
        {
            get
            {
                if (EstimatedTotalBytes >= 1_000_000_000)
                    return $"{EstimatedTotalBytes / 1_000_000_000.0:F1} GB";
                else if (EstimatedTotalBytes >= 1_000_000)
                    return $"{EstimatedTotalBytes / 1_000_000.0:F0} MB";
                else
                    return $"{EstimatedTotalBytes / 1_000.0:F0} KB";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => 
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 스마트 SHP 변환 대화상자
    /// </summary>
    public partial class SmartShpConvertDialog : Window
    {
        private readonly SmartShpConvertService _convertService;
        private readonly ObservableCollection<LayerAnalysisViewModel> _layers;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isConverting;

        public SmartShpConvertDialog()
        {
            InitializeComponent();
            _convertService = new SmartShpConvertService();
            _layers = new ObservableCollection<LayerAnalysisViewModel>();
            LayerDataGrid.ItemsSource = _layers;

            // 레이어 선택 변경 시 요약 정보 업데이트
            _layers.CollectionChanged += (s, e) => UpdateSummary();
        }

        /// <summary>
        /// 소스 FileGDB 찾아보기
        /// </summary>
        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new WinForms.FolderBrowserDialog
            {
                Description = "File Geodatabase(.gdb) 폴더를 선택하세요",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                var selectedPath = folderDialog.SelectedPath;

                if (!selectedPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        this,
                        "선택한 폴더가 File Geodatabase(.gdb)가 아닙니다.",
                        "잘못된 선택",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                SourcePathTextBox.Text = selectedPath;
                SetDefaultOutputPath(selectedPath);
                UpdateButtonStates();

                // 레이어 목록 초기화
                _layers.Clear();
                StatusText.Text = "레이어 분석 버튼을 클릭하세요";
            }
        }

        /// <summary>
        /// 기본 출력 경로 설정
        /// </summary>
        private void SetDefaultOutputPath(string gdbPath)
        {
            try
            {
                var parentDir = Path.GetDirectoryName(gdbPath);
                if (string.IsNullOrEmpty(parentDir)) return;

                var gdbName = Path.GetFileNameWithoutExtension(gdbPath);
                var outputDir = Path.Combine(parentDir, $"{gdbName}_shp");
                OutputPathTextBox.Text = outputDir;
            }
            catch { }
        }

        /// <summary>
        /// 출력 폴더 찾아보기
        /// </summary>
        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new WinForms.FolderBrowserDialog
            {
                Description = "Shapefile을 저장할 폴더를 선택하세요",
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                OutputPathTextBox.Text = folderDialog.SelectedPath;
                UpdateButtonStates();
            }
        }

        /// <summary>
        /// 레이어 분석
        /// </summary>
        private async void Analyze_Click(object sender, RoutedEventArgs e)
        {
            var sourcePath = SourcePathTextBox.Text;
            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            {
                MessageBox.Show(this, "유효한 FileGDB 경로를 선택하세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = "레이어 분석 중...";
            AnalyzeButton.IsEnabled = false;
            _layers.Clear();

            try
            {
                var targetSize = GetTargetFileSize();
                var options = new SmartConvertOptions
                {
                    TargetFileSizeBytes = targetSize
                };

                var analyses = await Task.Run(() => _convertService.AnalyzeLayers(sourcePath, options));

                foreach (var analysis in analyses)
                {
                    var vm = new LayerAnalysisViewModel
                    {
                        Name = analysis.Name,
                        GeometryType = analysis.GeometryType,
                        FeatureCount = analysis.FeatureCount,
                        EstimatedTotalBytes = analysis.EstimatedTotalBytes,
                        RecommendedSplitCount = analysis.RecommendedSplitCount,
                        SplitCount = analysis.RecommendedSplitCount,
                        IsSelected = true,
                        Status = "대기"
                    };

                    // 선택 변경 시 요약 업데이트
                    vm.PropertyChanged += (s, args) =>
                    {
                        if (args.PropertyName == nameof(LayerAnalysisViewModel.IsSelected) ||
                            args.PropertyName == nameof(LayerAnalysisViewModel.SplitCount))
                        {
                            UpdateSummary();
                        }
                    };

                    _layers.Add(vm);
                }

                StatusText.Text = $"{analyses.Count}개 레이어 분석 완료";
                UpdateSummary();
                UpdateButtonStates();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"분석 오류: {ex.Message}";
                MessageBox.Show(this, $"레이어 분석 중 오류가 발생했습니다.\n\n{ex.Message}", "분석 오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 목표 파일 크기 가져오기
        /// </summary>
        private long GetTargetFileSize()
        {
            return TargetSizeComboBox.SelectedIndex switch
            {
                0 => 1_200_000_000L,  // 1.2 GB
                1 => 1_400_000_000L,  // 1.4 GB
                2 => 1_600_000_000L,  // 1.6 GB
                _ => 1_400_000_000L
            };
        }

        /// <summary>
        /// 요약 정보 업데이트
        /// </summary>
        private void UpdateSummary()
        {
            var selectedLayers = _layers.Where(l => l.IsSelected).ToList();
            
            SelectedLayerCountText.Text = $"{selectedLayers.Count}개";
            TotalFeatureCountText.Text = selectedLayers.Sum(l => l.FeatureCount).ToString("N0");
            
            var totalBytes = selectedLayers.Sum(l => l.EstimatedTotalBytes);
            TotalEstimatedSizeText.Text = totalBytes >= 1_000_000_000 
                ? $"{totalBytes / 1_000_000_000.0:F1} GB" 
                : $"{totalBytes / 1_000_000.0:F0} MB";

            var totalFiles = selectedLayers.Sum(l => l.SplitCount);
            TotalFileCountText.Text = $"{totalFiles}개";

            UpdateButtonStates();
        }

        /// <summary>
        /// 버튼 상태 업데이트
        /// </summary>
        private void UpdateButtonStates()
        {
            var hasSource = !string.IsNullOrWhiteSpace(SourcePathTextBox.Text);
            var hasOutput = !string.IsNullOrWhiteSpace(OutputPathTextBox.Text);
            var hasSelectedLayers = _layers.Any(l => l.IsSelected);

            AnalyzeButton.IsEnabled = hasSource && !_isConverting;
            ConvertButton.IsEnabled = hasSource && hasOutput && hasSelectedLayers && !_isConverting;
        }

        /// <summary>
        /// 전체 선택
        /// </summary>
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var layer in _layers)
                layer.IsSelected = true;
        }

        /// <summary>
        /// 전체 해제
        /// </summary>
        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var layer in _layers)
                layer.IsSelected = false;
        }

        /// <summary>
        /// 변환 시작
        /// </summary>
        private async void Convert_Click(object sender, RoutedEventArgs e)
        {
            if (_isConverting)
            {
                _cancellationTokenSource?.Cancel();
                return;
            }

            var sourcePath = SourcePathTextBox.Text;
            var outputPath = OutputPathTextBox.Text;
            var selectedLayers = _layers.Where(l => l.IsSelected).ToList();

            if (selectedLayers.Count == 0)
            {
                MessageBox.Show(this, "변환할 레이어를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 출력 폴더 생성
            if (!Directory.Exists(outputPath))
            {
                try
                {
                    Directory.CreateDirectory(outputPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"출력 폴더 생성 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 변환 시작
            _isConverting = true;
            _cancellationTokenSource = new CancellationTokenSource();
            ConvertButton.Content = "취소";
            ConvertButton.IsEnabled = true;
            AnalyzeButton.IsEnabled = false;
            var startTime = DateTime.Now;  // 시작 시간 기록

            // 상태 초기화
            foreach (var layer in _layers)
                layer.Status = layer.IsSelected ? "대기" : "-";

            try
            {
                var useGridStreaming = UseGridStreamingCheckBox.IsChecked == true;
                var selectedLayerNames = selectedLayers.Select(l => l.Name).ToList();
                System.Diagnostics.Debug.WriteLine($"[UI] 선택된 레이어 수: {selectedLayerNames.Count}, 이름: {string.Join(", ", selectedLayerNames)}");
                
                var options = new SmartConvertOptions
                {
                    TargetFileSizeBytes = GetTargetFileSize(),
                    UseGridStreaming = useGridStreaming,
                    UseSpatialOrdering = !useGridStreaming,  // 그리드 스트리밍 사용 시 Z-order 정렬 비활성화
                    GenerateIndexFile = GenerateIndexCheckBox.IsChecked == true,
                    SelectedLayerNames = selectedLayerNames  // 선택된 레이어만
                };

                var progress = new Progress<SmartConvertProgress>(p =>
                {
                    ConvertProgressBar.Value = p.OverallProgress;
                    StatusText.Text = p.StatusMessage;

                    // 현재 레이어 상태 업데이트
                    if (!string.IsNullOrEmpty(p.CurrentLayer))
                    {
                        var currentLayer = _layers.FirstOrDefault(l => l.Name == p.CurrentLayer);
                        if (currentLayer != null)
                        {
                            // 이미 완료된 레이어는 상태 변경 안함
                            if (currentLayer.Status.StartsWith("완료"))
                                return;

                            if (p.CurrentPhase == "완료")
                            {
                                currentLayer.Status = $"완료 ({p.TotalSplits}파일)";
                            }
                            else if (p.CurrentPhase == "스트리밍 변환" || p.CurrentPhase == "변환")
                            {
                                currentLayer.Status = $"변환 중 ({p.CurrentSplitIndex}/{p.TotalSplits})";
                            }
                            else
                            {
                                currentLayer.Status = p.CurrentPhase;
                            }
                        }
                    }
                });

                var result = await _convertService.ConvertAsync(
                    sourcePath, outputPath, options, progress, _cancellationTokenSource.Token);

                // 결과 반영
                foreach (var layerIndex in result.LayerIndices)
                {
                    var layer = _layers.FirstOrDefault(l => l.Name == layerIndex.LayerName);
                    if (layer != null)
                        layer.Status = $"완료 ({layerIndex.TotalSplits}파일)";
                }

                foreach (var failedLayer in result.FailedLayers)
                {
                    var layerName = failedLayer.Split(':')[0].Trim();
                    var layer = _layers.FirstOrDefault(l => l.Name == layerName);
                    if (layer != null)
                        layer.Status = "실패";
                }

                ConvertProgressBar.Value = 100;
                var elapsed = DateTime.Now - startTime;
                var elapsedStr = elapsed.TotalHours >= 1 
                    ? $"{(int)elapsed.TotalHours}시간 {elapsed.Minutes}분 {elapsed.Seconds}초"
                    : elapsed.TotalMinutes >= 1 
                        ? $"{(int)elapsed.TotalMinutes}분 {elapsed.Seconds}초"
                        : $"{elapsed.Seconds}초";
                
                StatusText.Text = $"완료: {result.ConvertedLayers}개 레이어, {result.TotalFilesCreated}개 파일 생성 ({elapsedStr})";

                var openFolder = MessageBox.Show(
                    this,  // Owner 지정으로 모달 동작
                    $"변환이 완료되었습니다.\n\n" +
                    $"• 변환 레이어: {result.ConvertedLayers}개\n" +
                    $"• 생성 파일: {result.TotalFilesCreated}개\n" +
                    $"• 총 피처: {result.TotalFeaturesConverted:N0}개\n" +
                    $"• 작업 시간: {elapsedStr}\n\n" +
                    $"출력 폴더를 여시겠습니까?",
                    "변환 완료",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (openFolder == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start("explorer.exe", outputPath);
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "사용자에 의해 취소됨";
                ConvertProgressBar.Value = 0;

                foreach (var layer in _layers.Where(l => l.Status.Contains("변환 중")))
                    layer.Status = "취소됨";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"오류: {ex.Message}";
                MessageBox.Show(this, $"변환 중 오류가 발생했습니다.\n\n{ex.Message}", "변환 오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isConverting = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                ConvertButton.Content = "변환 시작";
                UpdateButtonStates();
            }
        }

        /// <summary>
        /// 닫기
        /// </summary>
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_isConverting)
            {
                var result = MessageBox.Show(
                    this,
                    "변환이 진행 중입니다. 취소하고 닫으시겠습니까?",
                    "변환 진행 중",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _cancellationTokenSource?.Cancel();
                    Close();
                }
            }
            else
            {
                Close();
            }
        }
    }
}


