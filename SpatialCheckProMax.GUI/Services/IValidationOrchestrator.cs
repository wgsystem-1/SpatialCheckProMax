using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpatialCheckProMax.Models;

namespace SpatialCheckProMax.GUI.Services
{
    /// <summary>
    /// 검수 오케스트레이션 서비스 인터페이스
    /// MainWindow에서 비즈니스 로직을 분리하여 MVVM 패턴 준수
    /// </summary>
    public interface IValidationOrchestrator
    {
        /// <summary>
        /// 검수 진행 중 여부
        /// </summary>
        bool IsValidationRunning { get; }

        /// <summary>
        /// 현재 검수 결과
        /// </summary>
        ValidationResult? CurrentResult { get; }

        /// <summary>
        /// 진행률 업데이트 이벤트
        /// </summary>
        event EventHandler<ValidationProgressEventArgs>? ProgressUpdated;

        /// <summary>
        /// 파일 완료 이벤트
        /// </summary>
        event EventHandler<FileCompletedEventArgs>? FileCompleted;

        /// <summary>
        /// 검수 완료 이벤트
        /// </summary>
        event EventHandler<ValidationCompletedEventArgs>? ValidationCompleted;

        /// <summary>
        /// 단일 파일 검수 시작
        /// </summary>
        Task<ValidationResult> StartValidationAsync(
            string filePath,
            ValidationOrchestratorOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 배치 검수 시작 (여러 파일)
        /// </summary>
        Task<List<ValidationResult>> StartBatchValidationAsync(
            IList<string> filePaths,
            ValidationOrchestratorOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 검수 중지
        /// </summary>
        void StopValidation();

        /// <summary>
        /// 예측 시간 계산
        /// </summary>
        Task<Dictionary<int, TimeSpan>> CalculatePredictedTimesAsync(string filePath);
    }

    /// <summary>
    /// 검수 오케스트레이터 옵션
    /// </summary>
    public class ValidationOrchestratorOptions
    {
        public string TableConfigPath { get; set; } = string.Empty;
        public string SchemaConfigPath { get; set; } = string.Empty;
        public string GeometryConfigPath { get; set; } = string.Empty;
        public string RelationConfigPath { get; set; } = string.Empty;
        public string AttributeConfigPath { get; set; } = string.Empty;
        public string? CodelistPath { get; set; }

        public List<string>? SelectedStage1Items { get; set; }
        public List<string>? SelectedStage2Items { get; set; }
        public List<string>? SelectedStage3Items { get; set; }
        public List<string>? SelectedStage4Items { get; set; }
        public List<string>? SelectedStage5Items { get; set; }
    }

    /// <summary>
    /// 파일 완료 이벤트 인자
    /// </summary>
    public class FileCompletedEventArgs : EventArgs
    {
        public int FileIndex { get; set; }
        public int TotalFiles { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public ValidationResult? Result { get; set; }
    }

    /// <summary>
    /// 검수 완료 이벤트 인자
    /// </summary>
    public class ValidationCompletedEventArgs : EventArgs
    {
        public bool IsBatch { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsSuccess { get; set; }
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public int TotalErrors { get; set; }
        public int TotalWarnings { get; set; }
        public List<ValidationResult> Results { get; set; } = new();
        public TimeSpan ElapsedTime { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
