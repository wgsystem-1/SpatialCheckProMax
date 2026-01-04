#nullable enable
using System.Collections.Concurrent;
using SpatialCheckProMax.Api.Models;
using SpatialCheckProMax.Models;

namespace SpatialCheckProMax.Api.Services;

/// <summary>
/// 검수 작업 정보
/// </summary>
public class ValidationJob
{
    public string JobId { get; set; } = string.Empty;
    public ValidationJobState State { get; set; } = ValidationJobState.Pending;
    public ValidationRequest Request { get; set; } = new();
    public double Progress { get; set; }
    public int CurrentStage { get; set; }
    public string CurrentStageName { get; set; } = string.Empty;
    public string CurrentTask { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
    public ValidationResultResponse? Result { get; set; }
    public List<int> SelectedStages { get; set; } = new() { 1, 2, 3, 4 };
    
    /// <summary>
    /// 단계별 진행 정보
    /// </summary>
    public Dictionary<int, StageProgressInfo> StageProgress { get; set; } = new()
    {
        { 1, new StageProgressInfo { StageNumber = 1, StageName = "테이블 검수", Status = "Pending" } },
        { 2, new StageProgressInfo { StageNumber = 2, StageName = "스키마 검수", Status = "Pending" } },
        { 3, new StageProgressInfo { StageNumber = 3, StageName = "지오메트리 검수", Status = "Pending" } },
        { 4, new StageProgressInfo { StageNumber = 4, StageName = "관계 검수", Status = "Pending" } }
    };
    
    /// <summary>
    /// 원본 검수 결과 (내부용)
    /// </summary>
    public ValidationResult? InternalResult { get; set; }
}

/// <summary>
/// 검수 작업 관리 인터페이스
/// </summary>
public interface IValidationJobManager
{
    string CreateJob(ValidationRequest request);
    ValidationJob? GetJob(string jobId);
    IEnumerable<ValidationJob> GetAllJobs();
    void UpdateJobProgress(string jobId, Action<ValidationJob> updateAction);
    void CompleteJob(string jobId, ValidationResultResponse result, ValidationResult? internalResult = null);
    void FailJob(string jobId, string errorMessage);
    bool CancelJob(string jobId);
    void RemoveJob(string jobId);
}

/// <summary>
/// 메모리 기반 검수 작업 관리자
/// </summary>
public class InMemoryValidationJobManager : IValidationJobManager, IDisposable
{
    private readonly ConcurrentDictionary<string, ValidationJob> _jobs = new();
    private readonly Timer _cleanupTimer;
    private readonly ILogger<InMemoryValidationJobManager> _logger;
    private readonly TimeSpan _jobRetentionPeriod = TimeSpan.FromHours(24);

    public InMemoryValidationJobManager(ILogger<InMemoryValidationJobManager> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(_ => CleanupOldJobs(), null, 
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    public string CreateJob(ValidationRequest request)
    {
        var jobId = $"val_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..36];
        
        var selectedStages = request.Stages?.Count > 0 
            ? request.Stages.Where(s => s >= 1 && s <= 4).OrderBy(s => s).ToList()
            : new List<int> { 1, 2, 3, 4 };

        var job = new ValidationJob
        {
            JobId = jobId,
            Request = request,
            State = ValidationJobState.Pending,
            StartedAt = DateTime.Now,
            CurrentTask = "검수 대기 중",
            SelectedStages = selectedStages
        };

        // 선택되지 않은 단계는 Skipped로 표시
        foreach (var stage in job.StageProgress.Values)
        {
            if (!selectedStages.Contains(stage.StageNumber))
            {
                stage.Status = "Skipped";
            }
        }

        _jobs[jobId] = job;
        _logger.LogInformation("검수 작업 생성됨: {JobId}, GDB: {GdbPath}, 단계: {Stages}", 
            jobId, request.GdbPath, string.Join(",", selectedStages));
        
        return jobId;
    }

    public ValidationJob? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    public IEnumerable<ValidationJob> GetAllJobs()
    {
        return _jobs.Values.OrderByDescending(j => j.StartedAt);
    }

    public void UpdateJobProgress(string jobId, Action<ValidationJob> updateAction)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            updateAction(job);
        }
    }

    public void CompleteJob(string jobId, ValidationResultResponse result, ValidationResult? internalResult = null)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.State = ValidationJobState.Completed;
            job.Progress = 100;
            job.CompletedAt = DateTime.Now;
            job.CurrentTask = "검수 완료";
            job.Result = result;
            job.InternalResult = internalResult;
            job.ErrorCount = result.TotalErrors;
            job.WarningCount = result.TotalWarnings;
            
            _logger.LogInformation("검수 작업 완료: {JobId}, 오류: {Errors}, 경고: {Warnings}", 
                jobId, result.TotalErrors, result.TotalWarnings);
        }
    }

    public void FailJob(string jobId, string errorMessage)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.State = ValidationJobState.Failed;
            job.CompletedAt = DateTime.Now;
            job.ErrorMessage = errorMessage;
            job.CurrentTask = $"검수 실패: {errorMessage}";
            
            _logger.LogError("검수 작업 실패: {JobId}, 오류: {Error}", jobId, errorMessage);
        }
    }

    public bool CancelJob(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (job.State == ValidationJobState.Pending || job.State == ValidationJobState.Running)
            {
                job.CancellationTokenSource.Cancel();
                job.State = ValidationJobState.Cancelled;
                job.CompletedAt = DateTime.Now;
                job.CurrentTask = "사용자에 의해 취소됨";
                
                _logger.LogInformation("검수 작업 취소됨: {JobId}", jobId);
                return true;
            }
        }
        return false;
    }

    public void RemoveJob(string jobId)
    {
        if (_jobs.TryRemove(jobId, out var job))
        {
            job.CancellationTokenSource.Dispose();
            _logger.LogInformation("검수 작업 삭제됨: {JobId}", jobId);
        }
    }

    private void CleanupOldJobs()
    {
        var cutoff = DateTime.Now - _jobRetentionPeriod;
        var oldJobs = _jobs.Values
            .Where(j => j.CompletedAt.HasValue && j.CompletedAt.Value < cutoff)
            .ToList();

        foreach (var job in oldJobs)
        {
            RemoveJob(job.JobId);
        }

        if (oldJobs.Count > 0)
        {
            _logger.LogInformation("{Count}개의 오래된 검수 작업 정리됨", oldJobs.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        foreach (var job in _jobs.Values)
        {
            job.CancellationTokenSource.Dispose();
        }
        _jobs.Clear();
    }
}

