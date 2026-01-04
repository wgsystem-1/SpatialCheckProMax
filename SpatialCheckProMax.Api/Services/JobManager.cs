#nullable enable
using System.Collections.Concurrent;
using SpatialCheckProMax.Api.Models;

namespace SpatialCheckProMax.Api.Services;

/// <summary>
/// 변환 작업 정보
/// </summary>
public class ConvertJob
{
    public string JobId { get; set; } = string.Empty;
    public JobState State { get; set; } = JobState.Pending;
    public ConvertRequest Request { get; set; } = new();
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
    public string? ErrorMessage { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
    public ConvertResultResponse? Result { get; set; }
}

/// <summary>
/// 작업 관리 서비스
/// </summary>
public interface IJobManager
{
    string CreateJob(ConvertRequest request);
    ConvertJob? GetJob(string jobId);
    IEnumerable<ConvertJob> GetAllJobs();
    void UpdateJobProgress(string jobId, Action<ConvertJob> updateAction);
    void CompleteJob(string jobId, ConvertResultResponse result);
    void FailJob(string jobId, string errorMessage);
    bool CancelJob(string jobId);
    void RemoveJob(string jobId);
    void CleanupOldJobs(TimeSpan maxAge);
}

/// <summary>
/// 메모리 기반 작업 관리자
/// </summary>
public class InMemoryJobManager : IJobManager, IDisposable
{
    private readonly ConcurrentDictionary<string, ConvertJob> _jobs = new();
    private readonly Timer _cleanupTimer;
    private readonly ILogger<InMemoryJobManager> _logger;
    private readonly TimeSpan _jobRetentionPeriod = TimeSpan.FromHours(24);

    public InMemoryJobManager(ILogger<InMemoryJobManager> logger)
    {
        _logger = logger;
        // 1시간마다 오래된 작업 정리
        _cleanupTimer = new Timer(_ => CleanupOldJobs(_jobRetentionPeriod), null, 
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    public string CreateJob(ConvertRequest request)
    {
        var jobId = $"job_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}".Substring(0, 32);
        
        var job = new ConvertJob
        {
            JobId = jobId,
            Request = request,
            State = JobState.Pending,
            StartedAt = DateTime.Now,
            StatusMessage = "작업이 대기 중입니다"
        };

        _jobs[jobId] = job;
        _logger.LogInformation("작업 생성됨: {JobId}, GDB: {GdbPath}", jobId, request.GdbPath);
        
        return jobId;
    }

    public ConvertJob? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    public IEnumerable<ConvertJob> GetAllJobs()
    {
        return _jobs.Values.OrderByDescending(j => j.StartedAt);
    }

    public void UpdateJobProgress(string jobId, Action<ConvertJob> updateAction)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            updateAction(job);
        }
    }

    public void CompleteJob(string jobId, ConvertResultResponse result)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.State = JobState.Completed;
            job.Progress = 100;
            job.CompletedAt = DateTime.Now;
            job.StatusMessage = "변환 완료";
            job.Result = result;
            
            _logger.LogInformation("작업 완료: {JobId}, 생성 파일 수: {FileCount}", 
                jobId, result.TotalFilesCreated);
        }
    }

    public void FailJob(string jobId, string errorMessage)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.State = JobState.Failed;
            job.CompletedAt = DateTime.Now;
            job.ErrorMessage = errorMessage;
            job.StatusMessage = $"변환 실패: {errorMessage}";
            
            _logger.LogError("작업 실패: {JobId}, 오류: {Error}", jobId, errorMessage);
        }
    }

    public bool CancelJob(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (job.State == JobState.Pending || job.State == JobState.Analyzing || 
                job.State == JobState.Converting)
            {
                job.CancellationTokenSource.Cancel();
                job.State = JobState.Cancelled;
                job.CompletedAt = DateTime.Now;
                job.StatusMessage = "사용자에 의해 취소됨";
                
                _logger.LogInformation("작업 취소됨: {JobId}", jobId);
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
            _logger.LogInformation("작업 삭제됨: {JobId}", jobId);
        }
    }

    public void CleanupOldJobs(TimeSpan maxAge)
    {
        var cutoff = DateTime.Now - maxAge;
        var oldJobs = _jobs.Values
            .Where(j => j.CompletedAt.HasValue && j.CompletedAt.Value < cutoff)
            .ToList();

        foreach (var job in oldJobs)
        {
            RemoveJob(job.JobId);
        }

        if (oldJobs.Count > 0)
        {
            _logger.LogInformation("{Count}개의 오래된 작업 정리됨", oldJobs.Count);
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

