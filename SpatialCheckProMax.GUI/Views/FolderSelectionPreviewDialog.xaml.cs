using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using SpatialCheckProMax.Services;
using System.Runtime.Versioning;
using WinForms = System.Windows.Forms;

namespace SpatialCheckProMax.GUI.Views
{
    /// <summary>
    /// 폴더 선택 시 검수 대상 미리보기 다이얼로그
    /// </summary>
    [SupportedOSPlatform("windows7.0")]
    public partial class FolderSelectionPreviewDialog : Window
    {
        private readonly ILogger<FolderSelectionPreviewDialog>? _logger;
        private readonly QcErrorsPathManager _pathManager;
        private string _selectedPath = string.Empty;
        private ObservableCollection<QcErrorsPathManager.FileGdbInfo> _targetGdbs = new();

        public bool IsContinue { get; private set; }
        public List<string> SelectedGdbPaths => _targetGdbs.Select(g => g.FullPath).ToList();

        public FolderSelectionPreviewDialog(string selectedPath)
        {
            InitializeComponent();
            
            // 서비스 가져오기
            var app = Application.Current as App;
            _logger = app?.GetService<ILogger<FolderSelectionPreviewDialog>>();
            
            // QcErrorsPathManager는 직접 생성 (또는 DI에서 가져오기)
            var loggerFactory = app?.GetService<ILoggerFactory>();
            var pathManagerLogger = loggerFactory?.CreateLogger<QcErrorsPathManager>();
            _pathManager = new QcErrorsPathManager(pathManagerLogger ?? 
                Microsoft.Extensions.Logging.Abstractions.NullLogger<QcErrorsPathManager>.Instance);
            
            _selectedPath = selectedPath;
            LoadPreview();
        }

        /// <summary>
        /// 미리보기 정보를 로드합니다
        /// </summary>
        private void LoadPreview()
        {
            try
            {
                // 선택한 경로 표시
                SelectedPathText.Text = $"선택한 폴더: {_selectedPath}";
                
                // QC_errors 경로 표시
                var qcErrorsPath = _pathManager.GetQcErrorsDirectory(_selectedPath);
                QcErrorsPathText.Text = qcErrorsPath;
                
                // 단일 FileGDB 선택인지 확인
                if (_pathManager.IsFileGdb(_selectedPath))
                {
                    // 단일 FileGDB를 직접 선택한 경우
                    _targetGdbs = new ObservableCollection<QcErrorsPathManager.FileGdbInfo>
                    {
                        new QcErrorsPathManager.FileGdbInfo
                        {
                            FullPath = _selectedPath,
                            Name = Path.GetFileName(_selectedPath),
                            RelativePath = Path.GetFileName(_selectedPath),
                            SizeInBytes = _pathManager.CalculateDirectorySize(_selectedPath)
                        }
                    };
                }
                else
                {
                    // 폴더를 선택한 경우 - 하위 FileGDB 검색
                    var foundGdbs = _pathManager.FindValidationTargets(_selectedPath);
                    _targetGdbs = new ObservableCollection<QcErrorsPathManager.FileGdbInfo>(foundGdbs);
                }
                
                TargetGdbGrid.ItemsSource = _targetGdbs;
                UpdateTargetCount();
                
                // 제외된 항목 찾기 (폴더 선택시에만)
                if (!_pathManager.IsFileGdb(_selectedPath))
                {
                    var excludedItems = _pathManager.FindExcludedItems(_selectedPath);
                    if (excludedItems.Any())
                    {
                        ExcludedSection.Visibility = Visibility.Visible;
                        ExcludedGdbGrid.ItemsSource = excludedItems;
                        ExcludedCountText.Text = $"({excludedItems.Count}개)";
                    }
                }
                
                UpdateContinueButtonState();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "미리보기 로드 중 오류");
                MessageBox.Show(
                    $"미리보기 로드 중 오류가 발생했습니다:\n{ex.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
            }
        }

        /// <summary>
        /// 검수 대상 개수 및 상태를 업데이트합니다
        /// </summary>
        private void UpdateTargetCount()
        {
            TargetCountText.Text = $"({_targetGdbs.Count}개)";
            
            if (_targetGdbs.Any())
            {
                var totalSize = _targetGdbs.Sum(g => g.SizeInBytes);
                var totalSizeText = FormatFileSize(totalSize);
                _logger?.LogInformation("검수 대상 FileGDB {Count}개, 총 크기: {Size}", 
                    _targetGdbs.Count, totalSizeText);
            }
        }

        /// <summary>
        /// 계속 버튼 활성화 상태를 업데이트합니다
        /// </summary>
        private void UpdateContinueButtonState()
        {
            if (_targetGdbs.Any())
            {
                ContinueButton.IsEnabled = true;
                WarningText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ContinueButton.IsEnabled = false;
                WarningText.Text = "검수 대상 FileGDB가 없습니다.";
                WarningText.Visibility = Visibility.Visible;
                _logger?.LogWarning("검수 대상 FileGDB를 찾을 수 없습니다: {Path}", _selectedPath);
            }
        }

        /// <summary>
        /// FileGDB 추가 버튼 클릭
        /// </summary>
        private void AddGdbButton_Click(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new WinForms.FolderBrowserDialog
            {
                Description = "추가할 File Geodatabase(.gdb) 폴더를 선택하세요",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                var selectedPath = folderDialog.SelectedPath;

                // .gdb 확장자 확인
                if (!selectedPath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "선택한 폴더가 File Geodatabase(.gdb)가 아닙니다.\n.gdb 확장자를 가진 폴더를 선택해주세요.",
                        "잘못된 선택",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // 중복 확인
                if (_targetGdbs.Any(g => g.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(
                        "이미 목록에 추가된 FileGDB입니다.",
                        "중복",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                // 목록에 추가
                var newGdb = new QcErrorsPathManager.FileGdbInfo
                {
                    FullPath = selectedPath,
                    Name = Path.GetFileName(selectedPath),
                    RelativePath = selectedPath, // 직접 추가한 경우 전체 경로 표시
                    SizeInBytes = _pathManager.CalculateDirectorySize(selectedPath)
                };

                _targetGdbs.Add(newGdb);
                UpdateTargetCount();
                UpdateContinueButtonState();

                _logger?.LogInformation("FileGDB 추가됨: {Path}", selectedPath);
            }
        }

        /// <summary>
        /// 선택 제거 버튼 클릭
        /// </summary>
        private void RemoveGdbButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = TargetGdbGrid.SelectedItems.Cast<QcErrorsPathManager.FileGdbInfo>().ToList();
            
            if (!selectedItems.Any())
            {
                MessageBox.Show(
                    "제거할 항목을 선택해주세요.",
                    "선택 필요",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"선택한 {selectedItems.Count}개 항목을 목록에서 제거하시겠습니까?",
                "제거 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var item in selectedItems)
                {
                    _targetGdbs.Remove(item);
                    _logger?.LogInformation("FileGDB 제거됨: {Path}", item.FullPath);
                }

                UpdateTargetCount();
                UpdateContinueButtonState();
                RemoveGdbButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// 검수 대상 그리드 선택 변경 시
        /// </summary>
        private void TargetGdbGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveGdbButton.IsEnabled = TargetGdbGrid.SelectedItems.Count > 0;
        }

        /// <summary>
        /// 파일 크기를 포맷팅합니다
        /// </summary>
        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// 계속 버튼 클릭
        /// </summary>
        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            IsContinue = true;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 취소 버튼 클릭
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsContinue = false;
            DialogResult = false;
            Close();
        }
    }
}

