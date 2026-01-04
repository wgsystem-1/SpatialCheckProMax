#nullable enable
using System.Text.Json.Serialization;

namespace SpatialCheckProMax.Api.Models;

#region Request Models

/// <summary>
/// GDB 분석 요청
/// </summary>
public class AnalyzeRequest
{
    /// <summary>
    /// FileGDB 경로
    /// </summary>
    public string GdbPath { get; set; } = string.Empty;
    
    /// <summary>
    /// 샘플 피처 수 (용량 추정용)
    /// </summary>
    public int SampleSize { get; set; } = 100;
}

/// <summary>
/// 변환 시작 요청
/// </summary>
public class ConvertRequest
{
    /// <summary>
    /// FileGDB 경로
    /// </summary>
    public string GdbPath { get; set; } = string.Empty;
    
    /// <summary>
    /// 출력 디렉토리 경로
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;
    
    /// <summary>
    /// 선택된 레이어 목록 (null이면 전체)
    /// </summary>
    public List<string>? SelectedLayers { get; set; }
    
    /// <summary>
    /// 목표 파일 크기 (MB), 기본 1300MB
    /// </summary>
    public int TargetFileSizeMB { get; set; } = 1300;
    
    /// <summary>
    /// 수동 분할 수 (0이면 자동)
    /// </summary>
    public int ManualSplitCount { get; set; } = 0;
    
    /// <summary>
    /// 공간 정렬 사용 여부
    /// </summary>
    public bool UseSpatialOrdering { get; set; } = false;
    
    /// <summary>
    /// 그리드 스트리밍 사용 (고성능 모드)
    /// </summary>
    public bool UseGridStreaming { get; set; } = true;
    
    /// <summary>
    /// 인덱스 파일 생성 여부
    /// </summary>
    public bool GenerateIndexFile { get; set; } = true;
}

#endregion

#region Response Models

/// <summary>
/// 레이어 분석 결과
/// </summary>
public class LayerAnalysisResponse
{
    public string Name { get; set; } = string.Empty;
    public string GeometryType { get; set; } = string.Empty;
    public long FeatureCount { get; set; }
    public double AvgVertexCount { get; set; }
    public long EstimatedTotalBytes { get; set; }
    public string EstimatedSize { get; set; } = string.Empty;
    public int RecommendedSplitCount { get; set; }
    public double[] Extent { get; set; } = new double[4];
}

/// <summary>
/// 분석 응답
/// </summary>
public class AnalyzeResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalLayers { get; set; }
    public long TotalEstimatedBytes { get; set; }
    public string TotalEstimatedSize { get; set; } = string.Empty;
    public List<LayerAnalysisResponse> Layers { get; set; } = new();
}

/// <summary>
/// 변환 시작 응답
/// </summary>
public class ConvertStartResponse
{
    public bool Success { get; set; }
    public string? JobId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
}

/// <summary>
/// 작업 상태
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobState
{
    Pending,
    Analyzing,
    Converting,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// 작업 상태 응답
/// </summary>
public class JobStatusResponse
{
    public string JobId { get; set; } = string.Empty;
    public JobState State { get; set; }
    public double Progress { get; set; }
    public string CurrentPhase { get; set; } = string.Empty;
    public string CurrentLayer { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public int ProcessedLayers { get; set; }
    public int TotalLayers { get; set; }
    public int CurrentSplitIndex { get; set; }
    public int TotalSplits { get; set; }
    public long ProcessedFeatures { get; set; }
    public long TotalFeatures { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? ElapsedTime { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 분할 파일 정보
/// </summary>
public class SplitFileResponse
{
    public string FileName { get; set; } = string.Empty;
    public int SplitIndex { get; set; }
    public long FeatureCount { get; set; }
    public long FileSize { get; set; }
    public string FileSizeFormatted { get; set; } = string.Empty;
    public double[] Extent { get; set; } = new double[4];
}

/// <summary>
/// 변환된 레이어 정보
/// </summary>
public class ConvertedLayerResponse
{
    public string LayerName { get; set; } = string.Empty;
    public long TotalFeatures { get; set; }
    public int TotalSplits { get; set; }
    public string GeometryType { get; set; } = string.Empty;
    public double[] TotalExtent { get; set; } = new double[4];
    public List<SplitFileResponse> Splits { get; set; } = new();
}

/// <summary>
/// 변환 결과 응답
/// </summary>
public class ConvertResultResponse
{
    public string JobId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public int ConvertedLayers { get; set; }
    public int TotalLayers { get; set; }
    public int TotalFilesCreated { get; set; }
    public long TotalFeaturesConverted { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> FailedLayers { get; set; } = new();
    public List<ConvertedLayerResponse> Layers { get; set; } = new();
}

/// <summary>
/// 작업 목록 응답
/// </summary>
public class JobListResponse
{
    public int TotalJobs { get; set; }
    public int ActiveJobs { get; set; }
    public int CompletedJobs { get; set; }
    public int FailedJobs { get; set; }
    public List<JobSummary> Jobs { get; set; } = new();
}

/// <summary>
/// 작업 요약
/// </summary>
public class JobSummary
{
    public string JobId { get; set; } = string.Empty;
    public JobState State { get; set; }
    public string GdbPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public double Progress { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

#endregion

