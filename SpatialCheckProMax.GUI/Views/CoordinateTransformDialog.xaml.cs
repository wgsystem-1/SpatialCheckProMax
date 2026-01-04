#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SpatialCheckProMax.GUI.Services;
using WinForms = System.Windows.Forms;

namespace SpatialCheckProMax.GUI.Views
{
    /// <summary>
    /// 좌표 변환 대화상자
    /// </summary>
    public partial class CoordinateTransformDialog : Window
    {
        private readonly CoordinateTransformService _transformService;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isTransforming;
        private int? _detectedEpsg;

        /// <summary>
        /// 생성자
        /// </summary>
        public CoordinateTransformDialog()
        {
            InitializeComponent();
            _transformService = new CoordinateTransformService();
        }

        /// <summary>
        /// 입력 파일/폴더 찾아보기 버튼 클릭
        /// </summary>
        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new WinForms.FolderBrowserDialog
            {
                Description = "FileGDB(.gdb) 폴더 또는 Shapefile이 있는 폴더를 선택하세요",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                var selectedPath = folderDialog.SelectedPath;
                
                // FileGDB 또는 Shapefile 폴더 확인
                bool isGdb = selectedPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase);
                bool hasShp = Directory.GetFiles(selectedPath, "*.shp", SearchOption.TopDirectoryOnly).Length > 0;
                
                if (!isGdb && !hasShp)
                {
                    // .gdb 폴더 또는 .shp 파일 선택 안내
                    MessageBox.Show(
                        "FileGDB(.gdb) 폴더 또는 Shapefile(.shp)이 있는 폴더를 선택해주세요.",
                        "잘못된 선택",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                SourcePathTextBox.Text = selectedPath;
                
                // 좌표계 자동 감지 및 레이어 정보 로드
                DetectSourceCrs(selectedPath);
                LoadLayerInfo(selectedPath);
                SetDefaultOutputPath(selectedPath);
                UpdateTransformButtonState();
            }
        }

        /// <summary>
        /// 소스 좌표계를 자동 감지합니다
        /// </summary>
        private void DetectSourceCrs(string sourcePath)
        {
            try
            {
                var crsInfo = _transformService.DetectCrs(sourcePath);
                _detectedEpsg = crsInfo.Epsg;
                
                if (crsInfo.Epsg.HasValue)
                {
                    SourceCrsText.Text = $"EPSG:{crsInfo.Epsg} - {crsInfo.Name}";
                    SourceCrsText.Foreground = System.Windows.Media.Brushes.Black;
                }
                else if (!string.IsNullOrEmpty(crsInfo.Name))
                {
                    SourceCrsText.Text = crsInfo.Name;
                    SourceCrsText.Foreground = System.Windows.Media.Brushes.Black;
                }
                else
                {
                    SourceCrsText.Text = "좌표계 미정의";
                    SourceCrsText.Foreground = System.Windows.Media.Brushes.Red;
                    _detectedEpsg = null;
                }
            }
            catch (Exception ex)
            {
                SourceCrsText.Text = $"감지 실패: {ex.Message}";
                SourceCrsText.Foreground = System.Windows.Media.Brushes.Red;
                _detectedEpsg = null;
            }
        }

        /// <summary>
        /// 레이어 정보를 로드합니다
        /// </summary>
        private void LoadLayerInfo(string sourcePath)
        {
            try
            {
                var layerInfos = _transformService.GetLayerInfos(sourcePath);
                
                if (layerInfos.Count == 0)
                {
                    LayerInfoText.Text = "레이어가 없습니다.";
                    return;
                }

                var infoText = $"총 {layerInfos.Count}개 레이어:\n";
                foreach (var info in layerInfos)
                {
                    infoText += $"  • {info.Name} ({info.GeometryType}, {info.FeatureCount:N0}개 피처)\n";
                }

                LayerInfoText.Text = infoText.TrimEnd('\n');
            }
            catch (Exception ex)
            {
                LayerInfoText.Text = $"레이어 정보 조회 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// 기본 출력 경로를 설정합니다
        /// </summary>
        private void SetDefaultOutputPath(string sourcePath)
        {
            try
            {
                var parentDir = Path.GetDirectoryName(sourcePath);
                if (string.IsNullOrEmpty(parentDir)) return;

                var baseName = Path.GetFileNameWithoutExtension(sourcePath);
                var targetEpsg = GetSelectedTargetEpsg();
                var suffix = OutputFormatGdb.IsChecked == true ? ".gdb" : "_shp";
                
                var outputName = $"{baseName}_EPSG{targetEpsg}{suffix}";
                var outputPath = Path.Combine(parentDir, outputName);
                
                OutputPathTextBox.Text = outputPath;
            }
            catch
            {
                // 경로 생성 실패 시 무시
            }
        }

        /// <summary>
        /// 출력 경로 찾아보기 버튼 클릭
        /// </summary>
        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new WinForms.FolderBrowserDialog
            {
                Description = "출력 폴더를 선택하세요",
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                OutputPathTextBox.Text = folderDialog.SelectedPath;
                UpdateTransformButtonState();
            }
        }

        /// <summary>
        /// 대상 좌표계 선택 변경
        /// </summary>
        private void TargetCrs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 초기화 중에는 무시
            if (SourcePathTextBox == null || TransformButton == null) return;
            
            if (!string.IsNullOrEmpty(SourcePathTextBox.Text))
            {
                SetDefaultOutputPath(SourcePathTextBox.Text);
            }
            UpdateTransformButtonState();
        }

        /// <summary>
        /// 출력 포맷 변경
        /// </summary>
        private void OutputFormat_Changed(object sender, RoutedEventArgs e)
        {
            // 초기화 중에는 무시
            if (SourcePathTextBox == null || OutputFormatGdb == null) return;
            
            if (!string.IsNullOrEmpty(SourcePathTextBox.Text))
            {
                SetDefaultOutputPath(SourcePathTextBox.Text);
            }
        }

        /// <summary>
        /// 선택된 대상 EPSG 코드 반환
        /// </summary>
        private int GetSelectedTargetEpsg()
        {
            if (TargetCrsComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                return int.Parse(tag);
            }
            return 5179; // 기본값
        }

        /// <summary>
        /// 변환 버튼 활성화 상태 업데이트
        /// </summary>
        private void UpdateTransformButtonState()
        {
            // 초기화 중에는 무시
            if (SourcePathTextBox == null || OutputPathTextBox == null || TransformButton == null) return;
            
            bool hasSource = !string.IsNullOrWhiteSpace(SourcePathTextBox.Text);
            bool hasOutput = !string.IsNullOrWhiteSpace(OutputPathTextBox.Text);
            bool hasCrs = _detectedEpsg.HasValue;
            
            TransformButton.IsEnabled = hasSource && hasOutput && hasCrs && !_isTransforming;
        }

        /// <summary>
        /// 변환 실행 버튼 클릭
        /// </summary>
        private async void Transform_Click(object sender, RoutedEventArgs e)
        {
            if (_isTransforming)
            {
                _cancellationTokenSource?.Cancel();
                return;
            }

            var sourcePath = SourcePathTextBox.Text;
            var outputPath = OutputPathTextBox.Text;
            var targetEpsg = GetSelectedTargetEpsg();
            var outputAsGdb = OutputFormatGdb.IsChecked == true;

            // 소스와 대상 좌표계가 같은지 확인
            if (_detectedEpsg == targetEpsg)
            {
                MessageBox.Show(
                    "소스 좌표계와 대상 좌표계가 동일합니다.",
                    "동일 좌표계",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 출력 경로 확인/생성
            if (outputAsGdb)
            {
                // FileGDB 출력 경로는 .gdb로 끝나야 함
                if (!outputPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath += ".gdb";
                    OutputPathTextBox.Text = outputPath;
                }
            }
            else
            {
                // Shapefile 출력 폴더 생성
                if (!Directory.Exists(outputPath))
                {
                    try
                    {
                        Directory.CreateDirectory(outputPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"출력 폴더 생성 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            // 변환 시작
            _isTransforming = true;
            _cancellationTokenSource = new CancellationTokenSource();
            TransformButton.Content = "취소";
            TransformButton.IsEnabled = true;
            StatusText.Text = "변환 준비 중...";
            TransformProgressBar.Value = 0;

            try
            {
                var progress = new Progress<CoordinateTransformProgress>(p =>
                {
                    TransformProgressBar.Value = p.OverallProgress;
                    StatusText.Text = p.StatusMessage;
                });

                var result = await _transformService.TransformAsync(
                    sourcePath,
                    outputPath,
                    _detectedEpsg!.Value,
                    targetEpsg,
                    outputAsGdb,
                    progress,
                    _cancellationTokenSource.Token);

                if (result.Success)
                {
                    TransformProgressBar.Value = 100;
                    StatusText.Text = $"완료: {result.ConvertedCount}개 레이어 변환됨";
                    
                    var openFolder = MessageBox.Show(
                        $"좌표 변환이 완료되었습니다.\n\n성공: {result.ConvertedCount}개\n실패: {result.FailedCount}개\n\n출력 폴더를 여시겠습니까?",
                        "변환 완료",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (openFolder == MessageBoxResult.Yes)
                    {
                        var folderToOpen = outputAsGdb ? Path.GetDirectoryName(outputPath) : outputPath;
                        if (!string.IsNullOrEmpty(folderToOpen))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", folderToOpen);
                        }
                    }
                }
                else
                {
                    StatusText.Text = $"변환 실패: {result.ErrorMessage}";
                    MessageBox.Show(
                        $"변환 중 오류가 발생했습니다.\n\n{result.ErrorMessage}",
                        "변환 오류",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "사용자에 의해 취소됨";
                TransformProgressBar.Value = 0;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"오류: {ex.Message}";
                MessageBox.Show(
                    $"변환 중 예기치 않은 오류가 발생했습니다.\n\n{ex.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isTransforming = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                TransformButton.Content = "변환 실행";
                UpdateTransformButtonState();
            }
        }

        /// <summary>
        /// 닫기 버튼 클릭
        /// </summary>
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_isTransforming)
            {
                var result = MessageBox.Show(
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


