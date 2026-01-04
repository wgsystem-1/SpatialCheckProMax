using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SpatialCheckProMax.Models.Config;

namespace SpatialCheckProMax.GUI.ViewModels
{
    /// <summary>
    /// 검수 설정을 관리하는 ViewModel
    /// MainWindow와 ValidationSettingsView가 공유하여 사용
    /// </summary>
    public class ValidationSettingsViewModel : INotifyPropertyChanged
    {
        #region 성능 설정
        private bool _enableHighPerformanceMode;
        private bool _forceStreamingMode;
        private int _customBatchSize = 1000;
        private int _maxMemoryUsageMB = 512;
        private bool _enablePrefetching;
        private bool _enableParallelStreaming;

        public bool EnableHighPerformanceMode
        {
            get => _enableHighPerformanceMode;
            set
            {
                _enableHighPerformanceMode = value;
                OnPropertyChanged();
            }
        }

        public bool ForceStreamingMode
        {
            get => _forceStreamingMode;
            set
            {
                _forceStreamingMode = value;
                OnPropertyChanged();
            }
        }

        public int CustomBatchSize
        {
            get => _customBatchSize;
            set
            {
                if (value > 0 && value <= 10000)
                {
                    _customBatchSize = value;
                    OnPropertyChanged();
                }
            }
        }

        public int MaxMemoryUsageMB
        {
            get => _maxMemoryUsageMB;
            set
            {
                if (value >= 128 && value <= 4096)
                {
                    _maxMemoryUsageMB = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnablePrefetching
        {
            get => _enablePrefetching;
            set
            {
                _enablePrefetching = value;
                OnPropertyChanged();
            }
        }

        public bool EnableParallelStreaming
        {
            get => _enableParallelStreaming;
            set
            {
                _enableParallelStreaming = value;
                OnPropertyChanged();
            }
        }
        #endregion

        #region 검수 단계별 활성화 플래그
        private bool _enableStage1 = true;
        private bool _enableStage2 = true;
        private bool _enableStage3 = true;
        private bool _enableStage4 = true;
        private bool _enableStage5 = true;

        public bool EnableStage1
        {
            get => _enableStage1;
            set
            {
                _enableStage1 = value;
                OnPropertyChanged();
            }
        }

        public bool EnableStage2
        {
            get => _enableStage2;
            set
            {
                _enableStage2 = value;
                OnPropertyChanged();
            }
        }

        public bool EnableStage3
        {
            get => _enableStage3;
            set
            {
                _enableStage3 = value;
                OnPropertyChanged();
            }
        }

        public bool EnableStage4
        {
            get => _enableStage4;
            set
            {
                _enableStage4 = value;
                OnPropertyChanged();
            }
        }

        public bool EnableStage5
        {
            get => _enableStage5;
            set
            {
                _enableStage5 = value;
                OnPropertyChanged();
            }
        }
        #endregion

        #region 검수 단계별 선택된 항목
        private List<TableCheckConfig>? _selectedStage1Items;
        private List<SchemaCheckConfig>? _selectedStage2Items;
        private List<GeometryCheckConfig>? _selectedStage3Items;
        private List<AttributeCheckConfig>? _selectedStage4Items;
        private List<RelationCheckConfig>? _selectedStage5Items;

        public List<TableCheckConfig>? SelectedStage1Items
        {
            get => _selectedStage1Items;
            set
            {
                _selectedStage1Items = value;
                OnPropertyChanged();
            }
        }

        public List<SchemaCheckConfig>? SelectedStage2Items
        {
            get => _selectedStage2Items;
            set
            {
                _selectedStage2Items = value;
                OnPropertyChanged();
            }
        }

        public List<GeometryCheckConfig>? SelectedStage3Items
        {
            get => _selectedStage3Items;
            set
            {
                _selectedStage3Items = value;
                OnPropertyChanged();
            }
        }

        public List<AttributeCheckConfig>? SelectedStage4Items
        {
            get => _selectedStage4Items;
            set
            {
                _selectedStage4Items = value;
                OnPropertyChanged();
            }
        }

        public List<RelationCheckConfig>? SelectedStage5Items
        {
            get => _selectedStage5Items;
            set
            {
                _selectedStage5Items = value;
                OnPropertyChanged();
            }
        }
        #endregion

        #region Config 파일 경로
        private string? _targetPath;
        private string? _tableConfigPath;
        private string? _schemaConfigPath;
        private string? _geometryConfigPath;
        private string? _attributeConfigPath;
        private string? _relationConfigPath;
        private string? _geometryCriteriaPath;
        private string? _codelistPath;

        public string? TargetPath
        {
            get => _targetPath;
            set
            {
                _targetPath = value;
                OnPropertyChanged();
            }
        }

        public string? TableConfigPath
        {
            get => _tableConfigPath;
            set
            {
                _tableConfigPath = value;
                OnPropertyChanged();
            }
        }

        public string? SchemaConfigPath
        {
            get => _schemaConfigPath;
            set
            {
                _schemaConfigPath = value;
                OnPropertyChanged();
            }
        }

        public string? GeometryConfigPath
        {
            get => _geometryConfigPath;
            set
            {
                _geometryConfigPath = value;
                OnPropertyChanged();
            }
        }

        public string? AttributeConfigPath
        {
            get => _attributeConfigPath;
            set
            {
                _attributeConfigPath = value;
                OnPropertyChanged();
            }
        }

        public string? RelationConfigPath
        {
            get => _relationConfigPath;
            set
            {
                _relationConfigPath = value;
                OnPropertyChanged();
            }
        }

        public string? GeometryCriteriaPath
        {
            get => _geometryCriteriaPath;
            set
            {
                _geometryCriteriaPath = value;
                OnPropertyChanged();
            }
        }

        public string? CodelistPath
        {
            get => _codelistPath;
            set
            {
                _codelistPath = value;
                OnPropertyChanged();
            }
        }
        #endregion

        #region 파일 경로 목록 (배치 검수용)
        private List<string>? _selectedFilePaths;

        public List<string>? SelectedFilePaths
        {
            get => _selectedFilePaths;
            set
            {
                _selectedFilePaths = value;
                OnPropertyChanged();
            }
        }
        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

