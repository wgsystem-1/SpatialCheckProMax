#nullable enable
using System.Text.Json.Serialization;

namespace SpatialCheckProMax.Api.Models;

#region Request Models

/// <summary>
/// 검수 시작 요청
/// </summary>
public class ValidationRequest
{
    /// <summary>
    /// 검수 대상 FileGDB 경로
    /// </summary>
    public string GdbPath { get; set; } = string.Empty;
    
    /// <summary>
    /// 검수 설정 디렉토리 경로 (null이면 기본 Config 사용)
    /// </summary>
    public string? ConfigDirectory { get; set; }
    
    /// <summary>
    /// 실행할 검수 단계 목록 (null이면 전체 실행)
    /// 1: 테이블 검수, 2: 스키마 검수, 3: 지오메트리 검수, 4: 속성 관계 검수, 5: 공간 관계 검수
    /// </summary>
    public List<int>? Stages { get; set; }
    
    /// <summary>
    /// 1단계 실패 시 중단 여부 (기본: true)
    /// </summary>
    public bool StopOnTableCheckFailure { get; set; } = true;
}

/// <summary>
/// 단일 단계 검수 요청
/// </summary>
public class StageValidationRequest
{
    /// <summary>
    /// 검수 대상 FileGDB 경로
    /// </summary>
    public string GdbPath { get; set; } = string.Empty;
    
    /// <summary>
    /// 검수 설정 디렉토리 경로
    /// </summary>
    public string? ConfigDirectory { get; set; }
}

#endregion

#region Response Models

/// <summary>
/// 검수 작업 상태
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidationJobState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// 검수 시작 응답
/// </summary>
public class ValidationStartResponse
{
    public bool Success { get; set; }
    public string? JobId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public List<int> SelectedStages { get; set; } = new();
}

/// <summary>
/// 검수 작업 상태 응답
/// </summary>
public class ValidationJobStatusResponse
{
    public string JobId { get; set; } = string.Empty;
    public ValidationJobState State { get; set; }
    public double Progress { get; set; }
    public int CurrentStage { get; set; }
    public string CurrentStageName { get; set; } = string.Empty;
    public string CurrentTask { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? ElapsedTime { get; set; }
    public TimeSpan? EstimatedRemainingTime { get; set; }
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// 단계별 진행 상황
    /// </summary>
    public List<StageProgressInfo> StageProgress { get; set; } = new();
}

/// <summary>
/// 단계별 진행 정보
/// </summary>
public class StageProgressInfo
{
    public int StageNumber { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Running, Completed, Failed, Skipped
    public double Progress { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// 검수 오류 정보
/// </summary>
public class ValidationErrorResponse
{
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? LayerName { get; set; }
    public long? FeatureId { get; set; }
    public string? FieldName { get; set; }
    public string? FieldValue { get; set; }
    public double[]? Coordinates { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// 개별 검수 항목 결과
/// </summary>
public class CheckResultResponse
{
    public string CheckId { get; set; } = string.Empty;
    public string CheckName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<ValidationErrorResponse> Errors { get; set; } = new();
    public List<ValidationErrorResponse> Warnings { get; set; } = new();
}

/// <summary>
/// 단계별 검수 결과
/// </summary>
public class StageResultResponse
{
    public int StageNumber { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public List<CheckResultResponse> CheckResults { get; set; } = new();
}

/// <summary>
/// 전체 검수 결과 응답
/// </summary>
public class ValidationResultResponse
{
    public string JobId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string TargetFile { get; set; } = string.Empty;
    public int TotalErrors { get; set; }
    public int TotalWarnings { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// 1단계: 테이블 검수 결과
    /// </summary>
    public StageResultResponse? TableCheck { get; set; }
    
    /// <summary>
    /// 2단계: 스키마 검수 결과
    /// </summary>
    public StageResultResponse? SchemaCheck { get; set; }
    
    /// <summary>
    /// 3단계: 지오메트리 검수 결과
    /// </summary>
    public StageResultResponse? GeometryCheck { get; set; }
    
    /// <summary>
    /// 4단계: 속성 관계 검수 결과
    /// </summary>
    public StageResultResponse? AttributeCheck { get; set; }
    
    /// <summary>
    /// 5단계: 공간 관계 검수 결과
    /// </summary>
    public StageResultResponse? RelationCheck { get; set; }
    
    /// <summary>
    /// 검수 요약
    /// </summary>
    public ValidationSummaryResponse Summary { get; set; } = new();
}

/// <summary>
/// 검수 요약 정보
/// </summary>
public class ValidationSummaryResponse
{
    public int TotalStages { get; set; }
    public int CompletedStages { get; set; }
    public int FailedStages { get; set; }
    public int SkippedStages { get; set; }
    public int TotalChecks { get; set; }
    public int PassedChecks { get; set; }
    public int FailedChecks { get; set; }
    public Dictionary<string, int> ErrorsByStage { get; set; } = new();
    public Dictionary<string, int> ErrorsByType { get; set; } = new();
    public List<string> FailedLayers { get; set; } = new();
}

/// <summary>
/// 검수 작업 목록 응답
/// </summary>
public class ValidationJobListResponse
{
    public int TotalJobs { get; set; }
    public int ActiveJobs { get; set; }
    public int CompletedJobs { get; set; }
    public int FailedJobs { get; set; }
    public List<ValidationJobSummary> Jobs { get; set; } = new();
}

/// <summary>
/// 검수 작업 요약
/// </summary>
public class ValidationJobSummary
{
    public string JobId { get; set; } = string.Empty;
    public ValidationJobState State { get; set; }
    public string GdbPath { get; set; } = string.Empty;
    public double Progress { get; set; }
    public int CurrentStage { get; set; }
    public string CurrentStageName { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<int> SelectedStages { get; set; } = new();
}

/// <summary>
/// 단계 정보 응답
/// </summary>
public class StageInfoResponse
{
    public int StageNumber { get; set; }
    public string StageName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> CheckTypes { get; set; } = new();
}

#endregion

