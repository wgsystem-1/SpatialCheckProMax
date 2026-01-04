#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using NetTopologySuite.Simplify;
using NetTopologySuite.Operation.Union;
using Geometry = NetTopologySuite.Geometries.Geometry;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.GUI.Models;
using GeometryValidationResult = SpatialCheckProMax.Models.GeometryValidationResult;
using SpatialCheckProMax.Services.Ai;
using SpatialCheckProMax.Services;

namespace SpatialCheckProMax.GUI.Services
{
    /// <summary>
    /// 지오메트리 편집 도구 서비스 구현 클래스
    /// </summary>
    public class GeometryEditToolService : IGeometryEditToolService
    {
        private readonly ILogger<GeometryEditToolService> _logger;
        private readonly GeometryFactory _geometryFactory;
        private readonly GeometryAiCorrector? _aiCorrector;
        private readonly GeometryAiValidator? _aiValidator;
        private readonly IAppSettingsService _appSettingsService;

        public GeometryEditToolService(
            ILogger<GeometryEditToolService> logger,
            GeometryAiCorrector? aiCorrector,
            GeometryAiValidator? aiValidator,
            IAppSettingsService appSettingsService)
        {
            _logger = logger;
            _geometryFactory = new GeometryFactory();
            _aiCorrector = aiCorrector;
            _aiValidator = aiValidator;
            _appSettingsService = appSettingsService;
        }

        /// <summary>
        /// 지오메트리 검증 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>검증 결과</returns>
        public GeometryValidationResult ValidateGeometry(NetTopologySuite.Geometries.Geometry geometry)
        {
            try
            {
                var result = new GeometryValidationResult
                {
                    IsValid = true,
                    ErrorMessage = string.Empty,
                    ValidationTime = DateTime.Now
                };

                if (geometry == null)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "지오메트리가 null입니다.";
                    return result;
                }

                // 기본 유효성 검사
                if (geometry.IsEmpty)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "빈 지오메트리입니다.";
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 검증 실패");
                return new GeometryValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"검증 중 오류 발생: {ex.Message}",
                    ValidationTime = DateTime.Now
                };
            }
        }

        /// <summary>
        /// 실시간 지오메트리 검증 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>검증 결과</returns>
        public GeometryValidationResult ValidateGeometryRealtime(NetTopologySuite.Geometries.Geometry geometry)
        {
            // 실시간 검증은 기본 검증과 동일하게 처리
            return ValidateGeometry(geometry);
        }

        /// <summary>
        /// 즉시 지오메트리 검증 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>검증 결과</returns>
        public GeometryValidationResult ValidateInstant(NetTopologySuite.Geometries.Geometry geometry)
        {
            // 즉시 검증은 기본 검증과 동일하게 처리
            return ValidateGeometry(geometry);
        }

        /// <summary>
        /// 저장용 지오메트리 검증 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">검증할 지오메트리</param>
        /// <returns>저장 가능 여부, 검증 결과, 차단 사유</returns>
        public (bool canSave, GeometryValidationResult validationResult, List<string> blockingReasons) ValidateForSave(NetTopologySuite.Geometries.Geometry geometry)
        {
            var validationResult = ValidateGeometry(geometry);
            var blockingReasons = new List<string>();

            if (!validationResult.IsValid)
            {
                blockingReasons.Add(validationResult.ErrorMessage);
            }

            return (validationResult.IsValid, validationResult, blockingReasons);
        }

        /// <summary>
        /// 점 이동 (인터페이스 구현)
        /// </summary>
        /// <param name="point">이동할 점</param>
        /// <param name="newX">새로운 X 좌표</param>
        /// <param name="newY">새로운 Y 좌표</param>
        /// <returns>이동 결과</returns>
        public GeometryEditResult MovePoint(NetTopologySuite.Geometries.Geometry point, double newX, double newY)
        {
            try
            {
                if (point is NetTopologySuite.Geometries.Point ntsPoint)
                {
                    var newPoint = _geometryFactory.CreatePoint(new Coordinate(newX, newY));
                    return GeometryEditResult.Success(newPoint, "점 이동 완료");
                }
                return GeometryEditResult.Failure("점 지오메트리가 아닙니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "점 이동 실패");
                return GeometryEditResult.Failure($"점 이동 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 선형 지오메트리의 버텍스 편집 (인터페이스 구현)
        /// </summary>
        /// <param name="lineString">편집할 선형 지오메트리</param>
        /// <param name="vertexIndex">버텍스 인덱스</param>
        /// <param name="newX">새로운 X 좌표</param>
        /// <param name="newY">새로운 Y 좌표</param>
        /// <returns>편집 결과</returns>
        public GeometryEditResult EditLineVertex(NetTopologySuite.Geometries.Geometry lineString, int vertexIndex, double newX, double newY)
        {
            try
            {
                if (lineString is LineString ntsLineString)
                {
                    var coordinates = ntsLineString.Coordinates.ToArray();
                    if (vertexIndex >= 0 && vertexIndex < coordinates.Length)
                    {
                        coordinates[vertexIndex] = new Coordinate(newX, newY);
                        var newLineString = _geometryFactory.CreateLineString(coordinates);
                        return GeometryEditResult.Success(newLineString, "버텍스 편집 완료");
                    }
                    return GeometryEditResult.Failure("잘못된 버텍스 인덱스입니다.");
                }
                return GeometryEditResult.Failure("선형 지오메트리가 아닙니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "버텍스 편집 실패");
                return GeometryEditResult.Failure($"버텍스 편집 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 버텍스 추가 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">대상 지오메트리</param>
        /// <param name="insertIndex">삽입 위치</param>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <returns>편집 결과</returns>
        public GeometryEditResult AddVertex(NetTopologySuite.Geometries.Geometry geometry, int insertIndex, double x, double y)
        {
            try
            {
                if (geometry is LineString ntsLineString)
                {
                    var coordinates = ntsLineString.Coordinates.ToList();
                    if (insertIndex >= 0 && insertIndex <= coordinates.Count)
                    {
                        coordinates.Insert(insertIndex, new Coordinate(x, y));
                        var newLineString = _geometryFactory.CreateLineString(coordinates.ToArray());
                        return GeometryEditResult.Success(newLineString, "버텍스 추가 완료");
                    }
                    return GeometryEditResult.Failure("잘못된 삽입 위치입니다.");
                }
                return GeometryEditResult.Failure("선형 지오메트리가 아닙니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "버텍스 추가 실패");
                return GeometryEditResult.Failure($"버텍스 추가 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 버텍스 제거 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">대상 지오메트리</param>
        /// <param name="vertexIndex">제거할 버텍스 인덱스</param>
        /// <returns>편집 결과</returns>
        public GeometryEditResult RemoveVertex(NetTopologySuite.Geometries.Geometry geometry, int vertexIndex)
        {
            try
            {
                if (geometry is LineString ntsLineString)
                {
                    var coordinates = ntsLineString.Coordinates.ToList();
                    if (vertexIndex >= 0 && vertexIndex < coordinates.Count && coordinates.Count > 2)
                    {
                        coordinates.RemoveAt(vertexIndex);
                        var newLineString = _geometryFactory.CreateLineString(coordinates.ToArray());
                        return GeometryEditResult.Success(newLineString, "버텍스 제거 완료");
                    }
                    return GeometryEditResult.Failure("잘못된 버텍스 인덱스이거나 최소 버텍스 수를 유지해야 합니다.");
                }
                return GeometryEditResult.Failure("선형 지오메트리가 아닙니다.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "버텍스 제거 실패");
                return GeometryEditResult.Failure($"버텍스 제거 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 지오메트리 단순화 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">단순화할 지오메트리</param>
        /// <param name="tolerance">허용 오차</param>
        /// <returns>단순화 결과</returns>
        public GeometryEditResult SimplifyGeometry(NetTopologySuite.Geometries.Geometry geometry, double tolerance)
        {
            try
            {
                var simplifiedGeometry = DouglasPeuckerSimplifier.Simplify(geometry, tolerance);
                return GeometryEditResult.Success(simplifiedGeometry, "지오메트리 단순화 완료");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 단순화 실패");
                return GeometryEditResult.Failure($"지오메트리 단순화 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 자동 수정 (인터페이스 구현)
        /// </summary>
        /// <param name="geometry">수정할 지오메트리</param>
        /// <param name="forceApply">이미 유효한 지오메트리에도 강제 적용 여부</param>
        /// <returns>수정 결과</returns>
        public async Task<(NetTopologySuite.Geometries.Geometry? fixedGeometry, List<string> fixActions)> AutoFixGeometryAsync(NetTopologySuite.Geometries.Geometry geometry, bool forceApply = false)
        {
            try
            {
                await Task.Delay(1); // 비동기 작업 시뮬레이션

                var fixActions = new List<string>();

                // 이미 유효한 지오메트리는 수정 불필요 (forceApply가 아닌 경우)
                if (geometry.IsValid && !forceApply)
                {
                    return (geometry, fixActions);
                }

                // AI 설정 확인
                var settings = _appSettingsService.LoadSettings();
                var aiEnabled = settings.AI?.Enabled == true;
                var fallbackToBuffer = settings.AI?.FallbackToBuffer ?? true;

                NetTopologySuite.Geometries.Geometry? fixedGeometry = null;

                // 1. AI 모델을 사용한 수정 시도
                if (aiEnabled && _aiCorrector != null && _aiValidator != null)
                {
                    try
                    {
                        _logger.LogInformation("AI 모델을 사용하여 지오메트리 자동 수정 시도");

                        // AI로 수정
                        var aiCorrectedGeometry = _aiCorrector.Correct(geometry);

                        if (aiCorrectedGeometry != null)
                        {
                            // 지오메트리 타입 보존 검증 (AI가 Polygon을 Point로 변환하는 등의 문제 방지)
                            var originalType = GetBaseGeometryType(geometry);
                            var correctedType = GetBaseGeometryType(aiCorrectedGeometry);

                            _logger.LogInformation("AI 타입 검증: 원본={Original}({OriginalNts}), 수정={Corrected}({CorrectedNts})",
                                originalType, geometry.GeometryType, correctedType, aiCorrectedGeometry.GeometryType);

                            if (originalType != correctedType)
                            {
                                fixActions.Add($"AI 수정 실패: 지오메트리 타입 변경됨 ({originalType} → {correctedType})");
                                _logger.LogWarning("AI 모델 수정 결과 지오메트리 타입 변경됨: {Original} → {Corrected}",
                                    originalType, correctedType);
                                // Buffer(0) 폴백으로 진행 (fixedGeometry는 null 유지)
                            }
                            else
                            {
                                // AI 검증기로 결과 확인
                                var validationResult = _aiValidator.Validate(geometry, aiCorrectedGeometry);

                                if (validationResult.IsValid)
                                {
                                    fixedGeometry = aiCorrectedGeometry;
                                    fixActions.Add($"AI 모델로 지오메트리 수정 성공 (면적 변화: {validationResult.AreaChangePercent:F2}%)");
                                    _logger.LogInformation("AI 모델로 지오메트리 수정 성공: {Type}", correctedType);
                                    return (fixedGeometry, fixActions);
                                }
                                else
                                {
                                    fixActions.Add($"AI 수정 실패: {validationResult.Message}");
                                    _logger.LogWarning("AI 모델 수정 결과가 검증 실패: {Message}", validationResult.Message);
                                }
                            }
                        }
                        else
                        {
                            fixActions.Add("AI 모델이 수정된 지오메트리를 반환하지 못함");
                            _logger.LogWarning("AI 모델이 null 반환");
                        }
                    }
                    catch (Exception aiEx)
                    {
                        fixActions.Add($"AI 수정 중 오류 발생: {aiEx.Message}");
                        _logger.LogError(aiEx, "AI 모델을 사용한 지오메트리 수정 중 오류 발생");
                    }
                }

                // 2. Buffer(0) 폴백 전략
                if (fixedGeometry == null && fallbackToBuffer)
                {
                    try
                    {
                        _logger.LogInformation("Buffer(0) 전략으로 지오메트리 수정 시도");
                        var bufferFixedGeometry = geometry.Buffer(0);

                        if (bufferFixedGeometry.IsValid)
                        {
                            fixedGeometry = bufferFixedGeometry;
                            fixActions.Add("Buffer(0) 적용으로 지오메트리 수정");
                            _logger.LogInformation("Buffer(0)로 지오메트리 수정 성공");
                            return (fixedGeometry, fixActions);
                        }
                        else
                        {
                            fixActions.Add("Buffer(0) 수정 실패: 결과가 여전히 유효하지 않음");
                            _logger.LogWarning("Buffer(0) 수정 후에도 지오메트리가 유효하지 않음");
                        }
                    }
                    catch (Exception bufferEx)
                    {
                        fixActions.Add($"Buffer(0) 수정 중 오류 발생: {bufferEx.Message}");
                        _logger.LogError(bufferEx, "Buffer(0) 적용 중 오류 발생");
                    }
                }

                // 3. 모든 수정 시도 실패
                if (fixedGeometry == null)
                {
                    fixActions.Add("모든 자동 수정 방법 실패");
                    _logger.LogWarning("지오메트리 자동 수정 실패");
                }

                return (fixedGeometry, fixActions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "자동 수정 중 예외 발생");
                return (null, new List<string> { $"자동 수정 실패: {ex.Message}" });
            }
        }


        /// <summary>
        /// 두 지오메트리의 타입이 호환되는지 확인합니다.
        /// Polygon/MultiPolygon, LineString/MultiLineString, Point/MultiPoint는 각각 호환으로 간주합니다.
        /// </summary>
        private bool IsGeometryTypeCompatible(NetTopologySuite.Geometries.Geometry original, NetTopologySuite.Geometries.Geometry corrected)
        {
            var originalType = GetBaseGeometryType(original);
            var correctedType = GetBaseGeometryType(corrected);

            return originalType == correctedType;
        }

        /// <summary>
        /// 지오메트리의 기본 타입을 반환합니다 (Polygon, LineString, Point 중 하나).
        /// Multi* 타입과 GeometryCollection은 해당하는 단일 타입으로 변환합니다.
        /// </summary>
        private string GetBaseGeometryType(NetTopologySuite.Geometries.Geometry geometry)
        {
            return geometry switch
            {
                Polygon => "Polygon",
                MultiPolygon => "Polygon",
                LinearRing => "LineString",  // LinearRing은 LineString보다 먼저 체크 (상속 관계)
                LineString => "LineString",
                MultiLineString => "LineString",
                NetTopologySuite.Geometries.Point => "Point",  // 명시적 네임스페이스 지정
                MultiPoint => "Point",
                GeometryCollection gc when gc.NumGeometries > 0 => GetBaseGeometryType(gc.GetGeometryN(0)),
                _ => geometry.GeometryType
            };
        }
    }
}
