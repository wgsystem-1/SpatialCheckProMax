using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using SpatialCheckProMax.Services;
using System.Runtime.Versioning;

namespace SpatialCheckProMax.GUI.Views
{
    /// <summary>
    /// 환영 화면
    /// </summary>
    [SupportedOSPlatform("windows7.0")]
    public partial class WelcomeView : UserControl
    {
        private readonly ILogger<WelcomeView>? _logger;
        private readonly ValidationMetricsCollector? _metricsCollector;

        public event EventHandler? QuickStartRequested;

        public WelcomeView()
        {
            InitializeComponent();
            
            // 서비스 가져오기
            var app = Application.Current as App;
            _logger = app?.GetService<ILogger<WelcomeView>>();
            _metricsCollector = app?.GetService<ValidationMetricsCollector>();
            
            LoadLastValidationInfo();
        }

        /// <summary>
        /// 최근 검수 정보 로드
        /// </summary>
        private void LoadLastValidationInfo()
        {
            try
            {
                if (_metricsCollector != null)
                {
                    // 메트릭 데이터에서 최근 검수 정보 가져오기
                    var metricsType = _metricsCollector.GetType();
                    var metricsField = metricsType.GetField("_metrics", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (metricsField != null)
                    {
                        var metrics = metricsField.GetValue(_metricsCollector);
                        if (metrics != null)
                        {
                            var runsProperty = metrics.GetType().GetProperty("Runs");
                            if (runsProperty != null)
                            {
                                var runs = runsProperty.GetValue(metrics) as System.Collections.IEnumerable;
                                if (runs != null)
                                {
                                    var runsList = runs.Cast<object>().ToList();
                                    if (runsList.Any())
                                    {
                                        // 가장 최근 실행 찾기
                                        var lastRun = runsList
                                            .OrderByDescending(r => r.GetType().GetProperty("StartTime")?.GetValue(r))
                                            .FirstOrDefault();
                                            
                                        if (lastRun != null)
                                        {
                                            var startTimeProp = lastRun.GetType().GetProperty("StartTime");
                                            var filePathProp = lastRun.GetType().GetProperty("FilePath");
                                            var isSuccessfulProp = lastRun.GetType().GetProperty("IsSuccessful");
                                            
                                            if (startTimeProp != null && filePathProp != null)
                                            {
                                                var startTime = (DateTime?)startTimeProp.GetValue(lastRun);
                                                var filePath = filePathProp.GetValue(lastRun)?.ToString();
                                                var isSuccessful = (bool?)isSuccessfulProp?.GetValue(lastRun) ?? false;
                                                
                                                if (startTime.HasValue && !string.IsNullOrEmpty(filePath))
                                                {
                                                    var fileName = System.IO.Path.GetFileName(filePath);
                                                    var timeAgo = GetTimeAgoText(startTime.Value);
                                                    var statusIcon = isSuccessful ? "✅" : "❌";
                                                    
                                                    LastValidationText.Text = $"{statusIcon} {fileName} ({timeAgo})";
                                                    return;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    // 데이터가 없는 경우
                    LastValidationText.Text = "정보 없음";
                }
                else
                {
                    LastValidationText.Text = "-";
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "최근 검수 정보 로드 실패");
                LastValidationText.Text = "-";
            }
        }
        
        /// <summary>
        /// 시간 경과를 사람이 읽기 쉬운 텍스트로 변환
        /// </summary>
        private string GetTimeAgoText(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;
            
            if (timeSpan.TotalMinutes < 1)
                return "방금 전";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes}분 전";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours}시간 전";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays}일 전";
            if (timeSpan.TotalDays < 30)
                return $"{(int)(timeSpan.TotalDays / 7)}주 전";
            if (timeSpan.TotalDays < 365)
                return $"{(int)(timeSpan.TotalDays / 30)}개월 전";
                
            return $"{(int)(timeSpan.TotalDays / 365)}년 전";
        }

        /// <summary>
        /// 빠른 시작 버튼 클릭
        /// </summary>
        private void QuickStartButton_Click(object sender, RoutedEventArgs e)
        {
            QuickStartRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}

