#nullable enable
using Microsoft.AspNetCore.Mvc;
using SpatialCheckProMax.Api.Models;
using SpatialCheckProMax.Api.Services;

namespace SpatialCheckProMax.Api.Controllers;

/// <summary>
/// FileGDB → Shapefile 변환 API 컨트롤러
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ShpConvertController : ControllerBase
{
    private readonly IShpConvertService _convertService;
    private readonly IJobManager _jobManager;
    private readonly ILogger<ShpConvertController> _logger;

    public ShpConvertController(
        IShpConvertService convertService,
        IJobManager jobManager,
        ILogger<ShpConvertController> logger)
    {
        _convertService = convertService;
        _jobManager = jobManager;
        _logger = logger;
    }

    #region Analysis

    /// <summary>
    /// FileGDB 레이어 분석
    /// </summary>
    /// <param name="request">분석 요청</param>
    /// <returns>레이어 분석 결과</returns>
    /// <response code="200">분석 성공</response>
    /// <response code="400">잘못된 요청</response>
    [HttpPost("analyze")]
    [ProducesResponseType(typeof(AnalyzeResponse), 200)]
    [ProducesResponseType(400)]
    public ActionResult<AnalyzeResponse> Analyze([FromBody] AnalyzeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GdbPath))
        {
            return BadRequest(new { error = "GdbPath는 필수입니다." });
        }

        if (!Directory.Exists(request.GdbPath))
        {
            return BadRequest(new { error = $"FileGDB를 찾을 수 없습니다: {request.GdbPath}" });
        }

        _logger.LogInformation("GDB 분석 요청: {Path}", request.GdbPath);

        var result = _convertService.AnalyzeGdb(request.GdbPath, request.SampleSize);
        return Ok(result);
    }

    #endregion

    #region Conversion

    /// <summary>
    /// 변환 작업 시작
    /// </summary>
    /// <param name="request">변환 요청</param>
    /// <returns>작업 ID</returns>
    /// <response code="202">작업 시작됨</response>
    /// <response code="400">잘못된 요청</response>
    [HttpPost("start")]
    [ProducesResponseType(typeof(ConvertStartResponse), 202)]
    [ProducesResponseType(400)]
    public ActionResult<ConvertStartResponse> StartConvert([FromBody] ConvertRequest request)
    {
        // 유효성 검사
        if (string.IsNullOrWhiteSpace(request.GdbPath))
        {
            return BadRequest(new { error = "GdbPath는 필수입니다." });
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return BadRequest(new { error = "OutputPath는 필수입니다." });
        }

        if (!Directory.Exists(request.GdbPath))
        {
            return BadRequest(new { error = $"FileGDB를 찾을 수 없습니다: {request.GdbPath}" });
        }

        _logger.LogInformation("변환 시작 요청: {GdbPath} -> {OutputPath}", 
            request.GdbPath, request.OutputPath);

        // 작업 생성
        var jobId = _jobManager.CreateJob(request);

        // 백그라운드에서 변환 실행
        var job = _jobManager.GetJob(jobId);
        if (job != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _convertService.ExecuteConvertAsync(jobId, request, job.CancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("작업 취소됨: {JobId}", jobId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "변환 실행 중 오류: {JobId}", jobId);
                    _jobManager.FailJob(jobId, ex.Message);
                }
            });
        }

        return Accepted(new ConvertStartResponse
        {
            Success = true,
            JobId = jobId,
            StartedAt = DateTime.Now
        });
    }

    /// <summary>
    /// 동기 변환 (소규모 데이터용)
    /// </summary>
    /// <param name="request">변환 요청</param>
    /// <returns>변환 결과</returns>
    /// <response code="200">변환 완료</response>
    /// <response code="400">잘못된 요청</response>
    [HttpPost("convert")]
    [ProducesResponseType(typeof(ConvertResultResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<ConvertResultResponse>> ConvertSync([FromBody] ConvertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GdbPath))
        {
            return BadRequest(new { error = "GdbPath는 필수입니다." });
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return BadRequest(new { error = "OutputPath는 필수입니다." });
        }

        if (!Directory.Exists(request.GdbPath))
        {
            return BadRequest(new { error = $"FileGDB를 찾을 수 없습니다: {request.GdbPath}" });
        }

        _logger.LogInformation("동기 변환 요청: {GdbPath} -> {OutputPath}", 
            request.GdbPath, request.OutputPath);

        var jobId = _jobManager.CreateJob(request);
        var job = _jobManager.GetJob(jobId);

        if (job == null)
        {
            return BadRequest(new { error = "작업 생성 실패" });
        }

        try
        {
            await _convertService.ExecuteConvertAsync(jobId, request, job.CancellationTokenSource.Token);
            
            var completedJob = _jobManager.GetJob(jobId);
            if (completedJob?.Result != null)
            {
                return Ok(completedJob.Result);
            }

            return BadRequest(new { error = "변환 결과를 가져올 수 없습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "동기 변환 실패: {JobId}", jobId);
            return BadRequest(new { error = $"변환 실패: {ex.Message}" });
        }
    }

    #endregion

    #region Job Management

    /// <summary>
    /// 작업 상태 조회
    /// </summary>
    /// <param name="jobId">작업 ID</param>
    /// <returns>작업 상태</returns>
    /// <response code="200">조회 성공</response>
    /// <response code="404">작업을 찾을 수 없음</response>
    [HttpGet("jobs/{jobId}/status")]
    [ProducesResponseType(typeof(JobStatusResponse), 200)]
    [ProducesResponseType(404)]
    public ActionResult<JobStatusResponse> GetJobStatus(string jobId)
    {
        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = $"작업을 찾을 수 없습니다: {jobId}" });
        }

        return Ok(new JobStatusResponse
        {
            JobId = job.JobId,
            State = job.State,
            Progress = job.Progress,
            CurrentPhase = job.CurrentPhase,
            CurrentLayer = job.CurrentLayer,
            StatusMessage = job.StatusMessage,
            ProcessedLayers = job.ProcessedLayers,
            TotalLayers = job.TotalLayers,
            CurrentSplitIndex = job.CurrentSplitIndex,
            TotalSplits = job.TotalSplits,
            ProcessedFeatures = job.ProcessedFeatures,
            TotalFeatures = job.TotalFeatures,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            ElapsedTime = DateTime.Now - job.StartedAt,
            ErrorMessage = job.ErrorMessage
        });
    }

    /// <summary>
    /// 변환 결과 조회
    /// </summary>
    /// <param name="jobId">작업 ID</param>
    /// <returns>변환 결과</returns>
    /// <response code="200">조회 성공</response>
    /// <response code="404">작업을 찾을 수 없음</response>
    /// <response code="400">작업이 완료되지 않음</response>
    [HttpGet("jobs/{jobId}/result")]
    [ProducesResponseType(typeof(ConvertResultResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    public ActionResult<ConvertResultResponse> GetJobResult(string jobId)
    {
        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = $"작업을 찾을 수 없습니다: {jobId}" });
        }

        if (job.State != JobState.Completed)
        {
            return BadRequest(new { 
                error = "작업이 아직 완료되지 않았습니다.",
                state = job.State.ToString(),
                progress = job.Progress
            });
        }

        if (job.Result == null)
        {
            return BadRequest(new { error = "변환 결과를 찾을 수 없습니다." });
        }

        return Ok(job.Result);
    }

    /// <summary>
    /// 작업 취소
    /// </summary>
    /// <param name="jobId">작업 ID</param>
    /// <returns>취소 결과</returns>
    /// <response code="200">취소 성공</response>
    /// <response code="404">작업을 찾을 수 없음</response>
    /// <response code="400">취소할 수 없는 상태</response>
    [HttpPost("jobs/{jobId}/cancel")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    public ActionResult CancelJob(string jobId)
    {
        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = $"작업을 찾을 수 없습니다: {jobId}" });
        }

        if (_jobManager.CancelJob(jobId))
        {
            _logger.LogInformation("작업 취소 요청: {JobId}", jobId);
            return Ok(new { message = "작업이 취소되었습니다.", jobId });
        }

        return BadRequest(new { 
            error = "작업을 취소할 수 없습니다.",
            state = job.State.ToString()
        });
    }

    /// <summary>
    /// 작업 삭제
    /// </summary>
    /// <param name="jobId">작업 ID</param>
    /// <returns>삭제 결과</returns>
    /// <response code="200">삭제 성공</response>
    /// <response code="404">작업을 찾을 수 없음</response>
    [HttpDelete("jobs/{jobId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public ActionResult DeleteJob(string jobId)
    {
        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = $"작업을 찾을 수 없습니다: {jobId}" });
        }

        // 실행 중인 작업은 먼저 취소
        if (job.State == JobState.Analyzing || job.State == JobState.Converting)
        {
            _jobManager.CancelJob(jobId);
        }

        _jobManager.RemoveJob(jobId);
        _logger.LogInformation("작업 삭제: {JobId}", jobId);

        return Ok(new { message = "작업이 삭제되었습니다.", jobId });
    }

    /// <summary>
    /// 모든 작업 목록 조회
    /// </summary>
    /// <returns>작업 목록</returns>
    /// <response code="200">조회 성공</response>
    [HttpGet("jobs")]
    [ProducesResponseType(typeof(JobListResponse), 200)]
    public ActionResult<JobListResponse> GetAllJobs()
    {
        var jobs = _jobManager.GetAllJobs().ToList();

        return Ok(new JobListResponse
        {
            TotalJobs = jobs.Count,
            ActiveJobs = jobs.Count(j => j.State == JobState.Analyzing || j.State == JobState.Converting),
            CompletedJobs = jobs.Count(j => j.State == JobState.Completed),
            FailedJobs = jobs.Count(j => j.State == JobState.Failed),
            Jobs = jobs.Select(j => new JobSummary
            {
                JobId = j.JobId,
                State = j.State,
                GdbPath = j.Request.GdbPath,
                OutputPath = j.Request.OutputPath,
                Progress = j.Progress,
                StartedAt = j.StartedAt,
                CompletedAt = j.CompletedAt
            }).ToList()
        });
    }

    #endregion

    #region Health Check

    /// <summary>
    /// API 상태 확인
    /// </summary>
    /// <returns>API 상태</returns>
    [HttpGet("health")]
    [ProducesResponseType(200)]
    public ActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.Now,
            version = "1.0.0",
            gdal = "initialized"
        });
    }

    #endregion
}

