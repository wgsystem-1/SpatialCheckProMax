using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SpatialCheckProMax.GUI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ILogger<MainViewModel> _logger;
        private readonly DispatcherTimer _timer;

        [ObservableProperty]
        private string _currentTime = string.Empty;

        [ObservableProperty]
        private string _statusMessage = "준비";

        [ObservableProperty]
        private string? _selectedFilePath;

        [ObservableProperty]
        private string? _currentValidationFileName;

        [ObservableProperty]
        private string? _currentValidationDirectory;

        [ObservableProperty]
        private int _currentFileIndex = 0;

        [ObservableProperty]
        private int _totalFiles = 0;

        [ObservableProperty]
        private List<string>? _selectedFilePaths;

        // 검수 설정 파일 경로들
        [ObservableProperty] private string? _tableConfigPath;
        [ObservableProperty] private string? _schemaConfigPath;
        [ObservableProperty] private string? _geometryConfigPath;
        [ObservableProperty] private string? _relationConfigPath;
        [ObservableProperty] private string? _attributeConfigPath;

        // 사용자 정의 검수 설정 파일 경로들
        [ObservableProperty] private string? _customTableConfigPath;
        [ObservableProperty] private string? _customSchemaConfigPath;
        [ObservableProperty] private string? _customGeometryConfigPath;
        [ObservableProperty] private string? _customRelationConfigPath;
        [ObservableProperty] private string? _customAttributeConfigPath;
        [ObservableProperty] private string? _customGeometryCriteriaPath;
        [ObservableProperty] private string? _customCodelistPath;

        // 단계별 사용 플래그
        [ObservableProperty] private bool _enableStage1 = true;
        [ObservableProperty] private bool _enableStage2 = true;
        [ObservableProperty] private bool _enableStage3 = true;
        [ObservableProperty] private bool _enableStage4 = true;
        [ObservableProperty] private bool _enableStage5 = true;

        // ValidationSettingsViewModel 참조
        public ValidationSettingsViewModel? ValidationSettingsViewModel { get; set; }

        public MainViewModel(ILogger<MainViewModel> logger, ValidationSettingsViewModel validationSettingsViewModel)
        {
            _logger = logger;
            ValidationSettingsViewModel = validationSettingsViewModel;
            
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
            
            UpdateCurrentTime();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            UpdateCurrentTime();
        }

        private void UpdateCurrentTime()
        {
            CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public void UpdateStatus(string message)
        {
            StatusMessage = message;
            _logger.LogInformation(message);
        }
    }
}

