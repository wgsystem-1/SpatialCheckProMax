#nullable enable
using Microsoft.AspNetCore.Mvc;
using SpatialCheckProMax.Api.Models;
using SpatialCheckProMax.Api.Services;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Enums;
using SpatialCheckProMax.Services;

namespace SpatialCheckProMax.Api.Controllers;

/// <summary>
/// FileGDB 검수 API 컨트롤러
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ValidationController : ControllerBase
{
    private readonly IValidationService _validationService;
    private readonly IValidationJobManager _jobManager;
    private readonly ILogger<ValidationController> _logger;
    private readonly string _defaultConfigDirectory;

    public ValidationController(
        IValidationService validationService,
        IValidationJobManager jobManager,
        ILogger<ValidationController> logger,
        IConfiguration configuration)
    {
        _validationService = validationService;
        _jobManager = jobManager;
        _logger = logger;
        _defaultConfigDirectory = configuration["ValidationConfigDirectory"] 
            ?? Path.Combine(AppContext.BaseDirectory, "Config");
    }

    #region 검수 단계 정보

    /// <summary>
    /// 사용 가능한 검수 단계 목록 조회
    /// </summary>
    [HttpGet("stages")]
    [ProducesResponseType(typeof(List<StageInfoResponse>), 200)]
    public ActionResult<List<StageInfoResponse>> GetStages()
    {
        var stages = new List<StageInfoResponse>
        {
            new()
            {
                StageNumber = 1,
                StageName = "테이블 검수",
                Description = "테이블 리스트, 좌표계, 지오메트리 타입 검증",
                CheckTypes = new List<string> { "TABLE_LIST_CHECK", "COORDINATE_SYSTEM_CHECK", "GEOMETRY_TYPE_CHECK" }
            },
            new()
            {
                StageNumber = 2,
                StageName = "스키마 검수",
                Description = "컬럼 구조, 데이터 타입, PK/FK 검증",
                CheckTypes = new List<string> { "COLUMN_STRUCTURE_CHECK", "DATA_TYPE_CHECK", "PK_FK_CHECK", "FK_RELATION_CHECK" }
            },
            new()
            {
                StageNumber = 3,
                StageName = "지오메트리 검수",
                Description = "중복, 겹침, 꼬임, 슬리버 폴리곤 검사",
                CheckTypes = new List<string> { "DUPLICATE_GEOMETRY_CHECK", "OVERLAPPING_GEOMETRY_CHECK", "TWISTED_GEOMETRY_CHECK", "SLIVER_POLYGON_CHECK" }
            },
            new()
            {
                StageNumber = 4,
                StageName = "속성 관계 검수",
                Description = "속성값 유효성, 코드리스트 검증, 필수값 검사",
                CheckTypes = new List<string> { "ATTRIBUTE_VALUE_CHECK", "CODELIST_CHECK", "REQUIRED_VALUE_CHECK", "VALUE_RANGE_CHECK" }
            },
            new()
            {
                StageNumber = 5,
                StageName = "공간 관계 검수",
                Description = "테이블 간 공간 관계 검증 (포함, 경계 일치 등)",
                CheckTypes = new List<string> { "POINT_INSIDE_POLYGON", "LINE_WITHIN_POLYGON", "POLYGON_BOUNDARY_MATCH" }
            }
        };

        return Ok(stages);
    }

    #endregion

    #region 검수 시작

    /// <summary>
    /// 비동기 검수 시작
    /// </summary>
    /// <param name="request">검수 요청</param>
    /// <returns>작업 ID</returns>
    [HttpPost("start")]
    [ProducesResponseType(typeof(ValidationStartResponse), 202)]
    [ProducesResponseType(400)]
    public ActionResult<ValidationStartResponse> StartValidation([FromBody] ValidationRequest request)
    {
        // 유효성 검사
        if (string.IsNullOrWhiteSpace(request.GdbPath))
        {
            return BadRequest(new { error = "GdbPath는 필수입니다." });
        }

        if (!Directory.Exists(request.GdbPath))
        {
            return BadRequest(new { error = $"FileGDB를 찾을 수 없습니다: {request.GdbPath}" });
        }

        var configDir = request.ConfigDirectory ?? _defaultConfigDirectory;
        if (!Directory.Exists(configDir))
        {
            return BadRequest(new { error = $"설정 디렉토리를 찾을 수 없습니다: {configDir}" });
        }

        _logger.LogInformation("검수 시작 요청: {GdbPath}, 단계: {Stages}", 
            request.GdbPath, 
            request.Stages != null ? string.Join(",", request.Stages) : "전체");

        // 작업 생성
        var jobId = _jobManager.CreateJob(request);
        var job = _jobManager.GetJob(jobId);

        if (job == null)
        {
            return BadRequest(new { error = "작업 생성 실패" });
        }

        // 백그라운드에서 검수 실행
        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteValidationAsync(jobId, request, configDir, job.CancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("검수 작업 취소됨: {JobId}", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "검수 실행 중 오류: {JobId}", jobId);
                _jobManager.FailJob(jobId, ex.Message);
            }
        });

        return Accepted(new ValidationStartResponse
        {
            Success = true,
            JobId = jobId,
            StartedAt = DateTime.Now,
            SelectedStages = job.SelectedStages
        });
    }

    /// <summary>
    /// 동기 검수 (소규모 데이터용)
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ValidationResultResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<ValidationResultResponse>> ValidateSync([FromBody] ValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GdbPath))
        {
            return BadRequest(new { error = "GdbPath는 필수입니다." });
        }

        if (!Directory.Exists(request.GdbPath))
        {
            return BadRequest(new { error = $"FileGDB를 찾을 수 없습니다: {request.GdbPath}" });
        }

        var configDir = request.ConfigDirectory ?? _defaultConfigDirectory;
        
        _logger.LogInformation("동기 검수 요청: {GdbPath}", request.GdbPath);

        var jobId = _jobManager.CreateJob(request);
        var job = _jobManager.GetJob(jobId);

        if (job == null)
        {
            return BadRequest(new { error = "작업 생성 실패" });
        }

        try
        {
            await ExecuteValidationAsync(jobId, request, configDir, job.CancellationTokenSource.Token);
            
            var completedJob = _jobManager.GetJob(jobId);
            if (completedJob?.Result != null)
            {
                return Ok(completedJob.Result);
            }

            return BadRequest(new { error = "검수 결과를 가져올 수 없습니다." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "동기 검수 실패: {JobId}", jobId);
            return BadRequest(new { error = $"검수 실패: {ex.Message}" });
        }
    }

    #endregion

    #region 작업 관리

    /// <summary>
    /// 검수 작업 상태 조회
    /// </summary>
    [HttpGet("jobs/{jobId}/status")]
    [ProducesResponseType(typeof(ValidationJobStatusResponse), 200)]
    [ProducesResponseType(404)]
    public ActionResult<ValidationJobStatusResponse> GetJobStatus(string jobId)
    {
        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = $"작업을 찾을 수 없습니다: {jobId}" });
        }

        return Ok(new ValidationJobStatusResponse
        {
            JobId = job.JobId,
            State = job.State,
            Progress = job.Progress,
            CurrentStage = job.CurrentStage,
            CurrentStageName = job.CurrentStageName,
            CurrentTask = job.CurrentTask,
            ErrorCount = job.ErrorCount,
            WarningCount = job.WarningCount,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            ElapsedTime = DateTime.Now - job.StartedAt,
            ErrorMessage = job.ErrorMessage,
            StageProgress = job.StageProgress.Values
                .Where(s => job.SelectedStages.Contains(s.StageNumber))
                .OrderBy(s => s.StageNumber)
                .ToList()
        });
    }

    /// <summary>
    /// 검수 결과 조회
    /// </summary>
    [HttpGet("jobs/{jobId}/result")]
    [ProducesResponseType(typeof(ValidationResultResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    public ActionResult<ValidationResultResponse> GetJobResult(string jobId)
    {
        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = $"작업을 찾을 수 없습니다: {jobId}" });
        }

        if (job.State != ValidationJobState.Completed && job.State != ValidationJobState.Failed)
        {
            return BadRequest(new { 
                error = "검수가 아직 완료되지 않았습니다.",
                state = job.State.ToString(),
                progress = job.Progress
            });
        }

        if (job.Result == null)
        {
            return BadRequest(new { error = "검수 결과를 찾을 수 없습니다." });
        }

        return Ok(job.Result);
    }

    /// <summary>
    /// 검수 오류 목록 조회 (페이징)
    /// </summary>
    [HttpGet("jobs/{jobId}/errors")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(404)]
    public ActionResult GetJobErrors(string jobId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] int? stage = null)
    {
        var job = _jobManager.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = $"작업을 찾을 수 없습니다: {jobId}" });
        }

        if (job.Result == null)
        {
            return BadRequest(new { error = "검수 결과가 없습니다." });
        }

        var allErrors = new List<ValidationErrorResponse>();
        
        // 단계별 오류 수집
        if (stage == null || stage == 1)
            AddStageErrors(allErrors, job.Result.TableCheck, 1);
        if (stage == null || stage == 2)
            AddStageErrors(allErrors, job.Result.SchemaCheck, 2);
        if (stage == null || stage == 3)
            AddStageErrors(allErrors, job.Result.GeometryCheck, 3);
        if (stage == null || stage == 4)
            AddStageErrors(allErrors, job.Result.RelationCheck, 4);

        var totalCount = allErrors.Count;
        var pagedErrors = allErrors.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new
        {
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
            errors = pagedErrors
        });
    }

    /// <summary>
    /// 검수 작업 취소
    /// </summary>
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
            _logger.LogInformation("검수 작업 취소 요청: {JobId}", jobId);
            return Ok(new { message = "검수 작업이 취소되었습니다.", jobId });
        }

        return BadRequest(new { 
            error = "작업을 취소할 수 없습니다.",
            state = job.State.ToString()
        });
    }

    /// <summary>
    /// 검수 작업 삭제
    /// </summary>
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

        if (job.State == ValidationJobState.Running)
        {
            _jobManager.CancelJob(jobId);
        }

        _jobManager.RemoveJob(jobId);
        _logger.LogInformation("검수 작업 삭제: {JobId}", jobId);

        return Ok(new { message = "검수 작업이 삭제되었습니다.", jobId });
    }

    /// <summary>
    /// 모든 검수 작업 목록 조회
    /// </summary>
    [HttpGet("jobs")]
    [ProducesResponseType(typeof(ValidationJobListResponse), 200)]
    public ActionResult<ValidationJobListResponse> GetAllJobs()
    {
        var jobs = _jobManager.GetAllJobs().ToList();

        return Ok(new ValidationJobListResponse
        {
            TotalJobs = jobs.Count,
            ActiveJobs = jobs.Count(j => j.State == ValidationJobState.Running),
            CompletedJobs = jobs.Count(j => j.State == ValidationJobState.Completed),
            FailedJobs = jobs.Count(j => j.State == ValidationJobState.Failed),
            Jobs = jobs.Select(j => new ValidationJobSummary
            {
                JobId = j.JobId,
                State = j.State,
                GdbPath = j.Request.GdbPath,
                Progress = j.Progress,
                CurrentStage = j.CurrentStage,
                CurrentStageName = j.CurrentStageName,
                ErrorCount = j.ErrorCount,
                WarningCount = j.WarningCount,
                StartedAt = j.StartedAt,
                CompletedAt = j.CompletedAt,
                SelectedStages = j.SelectedStages
            }).ToList()
        });
    }

    #endregion

    #region Health Check

    /// <summary>
    /// API 상태 확인
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(200)]
    public ActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.Now,
            version = "1.0.0",
            configDirectory = _defaultConfigDirectory,
            configExists = Directory.Exists(_defaultConfigDirectory)
        });
    }

    #endregion

    #region Private Methods

    private async Task ExecuteValidationAsync(
        string jobId, 
        ValidationRequest request, 
        string configDir,
        CancellationToken cancellationToken)
    {
        var job = _jobManager.GetJob(jobId);
        if (job == null) return;

        _jobManager.UpdateJobProgress(jobId, j =>
        {
            j.State = ValidationJobState.Running;
            j.CurrentTask = "검수 준비 중...";
        });

        try
        {
            // SpatialFileInfo 생성
            var spatialFile = new SpatialFileInfo
            {
                FilePath = request.GdbPath,
                Format = SpatialFileFormat.FileGDB,
                FileSize = GetDirectorySize(request.GdbPath)
            };

            // 진행률 보고 설정
            var progress = new Progress<ValidationProgress>(p =>
            {
                _jobManager.UpdateJobProgress(jobId, j =>
                {
                    j.Progress = p.OverallPercentage;
                    j.CurrentStage = p.CurrentStage;
                    j.CurrentStageName = p.CurrentStageName;
                    j.CurrentTask = p.CurrentTask;
                    j.ErrorCount = p.ErrorCount;
                    j.WarningCount = p.WarningCount;

                    // 단계별 진행 상황 업데이트
                    if (p.CurrentStage > 0 && j.StageProgress.TryGetValue(p.CurrentStage, out var stageInfo))
                    {
                        stageInfo.Status = "Running";
                        stageInfo.StartedAt ??= DateTime.Now;
                        stageInfo.ErrorCount = p.ErrorCount;
                        stageInfo.WarningCount = p.WarningCount;
                    }

                    // 이전 단계 완료 처리
                    for (int i = 1; i < p.CurrentStage; i++)
                    {
                        if (j.StageProgress.TryGetValue(i, out var prevStage) && prevStage.Status == "Running")
                        {
                            prevStage.Status = "Completed";
                            prevStage.CompletedAt = DateTime.Now;
                            prevStage.Progress = 100;
                        }
                    }
                });
            });

            // 검수 실행
            var result = await _validationService.ExecuteValidationAsync(
                spatialFile, configDir, progress, cancellationToken);

            // 결과 변환
            var response = ConvertToResponse(jobId, result);

            // 완료 처리
            _jobManager.UpdateJobProgress(jobId, j =>
            {
                // 모든 실행된 단계 완료 처리
                foreach (var stage in j.SelectedStages)
                {
                    if (j.StageProgress.TryGetValue(stage, out var stageInfo))
                    {
                        if (stageInfo.Status == "Running" || stageInfo.Status == "Pending")
                        {
                            stageInfo.Status = "Completed";
                            stageInfo.CompletedAt = DateTime.Now;
                            stageInfo.Progress = 100;
                        }
                    }
                }
            });

            _jobManager.CompleteJob(jobId, response, result);
        }
        catch (OperationCanceledException)
        {
            _jobManager.UpdateJobProgress(jobId, j =>
            {
                j.State = ValidationJobState.Cancelled;
                j.CurrentTask = "사용자에 의해 취소됨";
            });
            throw;
        }
        catch (Exception ex)
        {
            _jobManager.FailJob(jobId, ex.Message);
            throw;
        }
    }

    private ValidationResultResponse ConvertToResponse(string jobId, ValidationResult result)
    {
        var response = new ValidationResultResponse
        {
            JobId = jobId,
            Success = result.Status == ValidationStatus.Completed,
            Status = result.Status.ToString(),
            TargetFile = result.TargetFile,
            TotalErrors = result.TotalErrors,
            TotalWarnings = result.TotalWarnings,
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt ?? DateTime.Now,
            Duration = (result.CompletedAt ?? DateTime.Now) - result.StartedAt,
            ErrorMessage = result.ErrorMessage
        };

        // 단계별 결과 변환
        if (result.TableCheckResult != null)
        {
            response.TableCheck = ConvertStageResult(1, "테이블 검수", result.TableCheckResult);
        }
        if (result.SchemaCheckResult != null)
        {
            response.SchemaCheck = ConvertStageResult(2, "스키마 검수", result.SchemaCheckResult);
        }
        if (result.GeometryCheckResult != null)
        {
            response.GeometryCheck = ConvertStageResult(3, "지오메트리 검수", result.GeometryCheckResult);
        }
        if (result.RelationCheckResult != null)
        {
            response.RelationCheck = ConvertStageResult(4, "관계 검수", result.RelationCheckResult);
        }

        // 요약 생성
        response.Summary = CreateSummary(response);

        return response;
    }

    private StageResultResponse ConvertStageResult(int stageNumber, string stageName, CheckResult checkResult)
    {
        return new StageResultResponse
        {
            StageNumber = stageNumber,
            StageName = stageName,
            Status = checkResult.Status.ToString(),
            ErrorCount = checkResult.ErrorCount,
            WarningCount = checkResult.WarningCount,
            StartedAt = DateTime.Now, // 실제 시작 시간 필요
            CompletedAt = DateTime.Now,
            CheckResults = new List<CheckResultResponse>
            {
                new()
                {
                    CheckId = checkResult.CheckId,
                    CheckName = checkResult.CheckName,
                    Status = checkResult.Status.ToString(),
                    ErrorCount = checkResult.ErrorCount,
                    WarningCount = checkResult.WarningCount,
                    Errors = checkResult.Errors?.Select(ConvertError).ToList() ?? new(),
                    Warnings = checkResult.Warnings?.Select(ConvertError).ToList() ?? new()
                }
            }
        };
    }

    private ValidationErrorResponse ConvertError(ValidationError error)
    {
        // FeatureId를 long?로 변환 시도
        long? featureIdLong = null;
        if (!string.IsNullOrEmpty(error.FeatureId) && long.TryParse(error.FeatureId, out var parsedId))
        {
            featureIdLong = parsedId;
        }
        else if (error.SourceObjectId.HasValue)
        {
            featureIdLong = error.SourceObjectId;
        }

        return new ValidationErrorResponse
        {
            ErrorCode = error.ErrorCode,
            Message = error.Message,
            Severity = error.Severity.ToString(),
            LayerName = error.TableName ?? error.SourceTable,
            FeatureId = featureIdLong,
            FieldName = error.FieldName,
            FieldValue = error.ActualValue,
            Coordinates = error.X.HasValue && error.Y.HasValue 
                ? new[] { error.X.Value, error.Y.Value } 
                : null,
            Metadata = error.Metadata?.ToDictionary(
                kvp => kvp.Key, 
                kvp => kvp.Value?.ToString() ?? string.Empty) ?? new()
        };
    }

    private ValidationSummaryResponse CreateSummary(ValidationResultResponse response)
    {
        var summary = new ValidationSummaryResponse();
        var stages = new[] { response.TableCheck, response.SchemaCheck, response.GeometryCheck, response.AttributeCheck, response.RelationCheck };
        
        foreach (var stage in stages.Where(s => s != null))
        {
            summary.TotalStages++;
            if (stage!.Status == "Passed" || stage.Status == "Completed")
                summary.CompletedStages++;
            else if (stage.Status == "Failed")
                summary.FailedStages++;
            else if (stage.Status == "Skipped")
                summary.SkippedStages++;

            summary.ErrorsByStage[stage.StageName] = stage.ErrorCount;
            summary.TotalChecks += stage.CheckResults.Count;
            summary.PassedChecks += stage.CheckResults.Count(c => c.Status == "Passed");
            summary.FailedChecks += stage.CheckResults.Count(c => c.Status == "Failed");
        }

        return summary;
    }

    private void AddStageErrors(List<ValidationErrorResponse> allErrors, StageResultResponse? stage, int stageNumber)
    {
        if (stage == null) return;
        
        foreach (var check in stage.CheckResults)
        {
            foreach (var error in check.Errors)
            {
                error.Metadata["Stage"] = stageNumber.ToString();
                error.Metadata["CheckId"] = check.CheckId;
                allErrors.Add(error);
            }
        }
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    #endregion
}

