using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NetTopologySuite.Geometries;

namespace SpatialCheckProMax.Services.Ai
{
    /// <summary>
    /// GeometryGNN 모델을 사용하여 지오메트리 좌표를 교정하는 AI 엔진 클래스
    ///
    /// 모델: GeometryGNN (Graph Neural Network)
    /// 입력: coordinates [batch, num_vertices, 2], mask [batch, num_vertices]
    /// 출력: offsets [batch, num_vertices, 2] (보정 오프셋 dx, dy)
    /// 사용법: corrected_coords = input_coords + offsets
    /// </summary>
    public class GeometryAiCorrector : IDisposable
    {
        private readonly InferenceSession? _session;
        private readonly ILogger? _logger;
        private readonly int _maxVertices;
        private readonly ModelMetadata? _metadata;
        private bool _disposed = false;

        /// <summary>
        /// 모델이 로드되었는지 여부
        /// </summary>
        public bool IsModelLoaded => _session != null;

        /// <summary>
        /// 모델 버전 정보
        /// </summary>
        public string ModelVersion => _metadata?.Version ?? "unknown";

        public GeometryAiCorrector(string modelPath, ILogger? logger = null, int maxVertices = 500)
        {
            _logger = logger;
            _maxVertices = maxVertices;

            if (string.IsNullOrEmpty(modelPath))
            {
                _logger?.LogWarning("AI 모델 경로가 지정되지 않았습니다. AI 보정 기능이 비활성화됩니다.");
                return;
            }

            if (!File.Exists(modelPath))
            {
                _logger?.LogWarning("AI 모델 파일을 찾을 수 없습니다: {ModelPath}. AI 보정 기능이 비활성화됩니다.", modelPath);
                return;
            }

            try
            {
                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

                _session = new InferenceSession(modelPath, sessionOptions);
                _logger?.LogInformation("AI 모델 로드 완료: {ModelPath}", modelPath);

                // 메타데이터 로드 시도
                var metadataPath = Path.Combine(
                    Path.GetDirectoryName(modelPath) ?? "",
                    "model_metadata.json"
                );
                if (File.Exists(metadataPath))
                {
                    var json = File.ReadAllText(metadataPath);
                    _metadata = JsonSerializer.Deserialize<ModelMetadata>(json);
                    _logger?.LogInformation("모델 메타데이터 로드됨: 버전 {Version}", _metadata?.Version);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "AI 모델 로드 실패: {ModelPath}", modelPath);
                _session = null;
            }
        }

        /// <summary>
        /// 입력 지오메트리를 AI 모델로 분석하여 교정된 지오메트리를 반환합니다.
        /// </summary>
        /// <param name="inputGeom">교정할 지오메트리</param>
        /// <returns>교정된 지오메트리 (모델 미로드 시 원본 반환)</returns>
        public Geometry Correct(Geometry inputGeom)
        {
            if (inputGeom == null || inputGeom.IsEmpty)
                return inputGeom;

            if (_session == null)
            {
                _logger?.LogDebug("AI 모델이 로드되지 않아 원본 지오메트리를 반환합니다.");
                return inputGeom;
            }

            try
            {
                var coords = inputGeom.Coordinates;
                int numVertices = coords.Length;

                // 최대 정점 수 초과 시 원본 반환
                if (numVertices > _maxVertices)
                {
                    _logger?.LogWarning("정점 수({NumVertices})가 최대 허용값({MaxVertices})을 초과하여 원본을 반환합니다.",
                        numVertices, _maxVertices);
                    return inputGeom;
                }

                // 폴리곤(링 구조)의 경우 마지막 좌표(폐합점) 제외
                bool isRingStructure = inputGeom is Polygon || inputGeom is MultiPolygon || inputGeom is LinearRing;
                int processVertices = isRingStructure ? numVertices - 1 : numVertices;

                // 1. 입력 텐서 준비 (패딩 적용)
                var inputData = new float[_maxVertices * 2];
                var maskData = new float[_maxVertices];

                for (int i = 0; i < processVertices; i++)
                {
                    inputData[i * 2] = (float)coords[i].X;
                    inputData[i * 2 + 1] = (float)coords[i].Y;
                    maskData[i] = 1.0f;
                }
                // 나머지는 0으로 패딩됨 (기본값)

                // [batch_size, num_vertices, 2]
                var inputTensor = new DenseTensor<float>(
                    inputData,
                    new[] { 1, _maxVertices, 2 }
                );

                // [batch_size, num_vertices]
                var maskTensor = new DenseTensor<float>(
                    maskData,
                    new[] { 1, _maxVertices }
                );

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("coordinates", inputTensor),
                    NamedOnnxValue.CreateFromTensor("mask", maskTensor)
                };

                // 2. AI 추론 실행
                using var results = _session.Run(inputs);
                var output = results.First().AsTensor<float>();

                // 3. 오프셋을 적용하여 보정된 좌표 생성
                var correctedCoords = new Coordinate[numVertices];

                for (int i = 0; i < processVertices; i++)
                {
                    double dx = output[0, i, 0];
                    double dy = output[0, i, 1];

                    // 보정된 좌표 = 원본 + 오프셋
                    correctedCoords[i] = new Coordinate(
                        coords[i].X + dx,
                        coords[i].Y + dy
                    );
                }

                // 링 구조의 경우 마지막 좌표는 첫 번째와 동일하게 설정 (폐합)
                if (isRingStructure)
                {
                    correctedCoords[numVertices - 1] = new Coordinate(
                        correctedCoords[0].X,
                        correctedCoords[0].Y
                    );
                }

                var result = CreateCorrectedGeometry(inputGeom, correctedCoords);

                // 유효성 검사
                if (!result.IsValid)
                {
                    _logger?.LogWarning("보정된 지오메트리가 유효하지 않습니다. 원본을 반환합니다.");
                    return inputGeom;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "AI 보정 중 오류 발생");
                return inputGeom;
            }
        }

        /// <summary>
        /// 여러 지오메트리를 일괄 교정합니다.
        /// </summary>
        public IEnumerable<Geometry> CorrectBatch(IEnumerable<Geometry> geometries)
        {
            foreach (var geom in geometries)
            {
                yield return Correct(geom);
            }
        }

        /// <summary>
        /// AI 모델 추론 결과의 신뢰도를 반환합니다.
        /// </summary>
        /// <param name="inputGeom">입력 지오메트리</param>
        /// <param name="correctedGeom">보정된 지오메트리</param>
        /// <returns>보정 신뢰도 (0.0 ~ 1.0)</returns>
        public double GetCorrectionConfidence(Geometry inputGeom, Geometry correctedGeom)
        {
            if (inputGeom == null || correctedGeom == null)
                return 0.0;

            // 간단한 휴리스틱: 변화량이 적을수록 높은 신뢰도
            var inputCoords = inputGeom.Coordinates;
            var correctedCoords = correctedGeom.Coordinates;

            if (inputCoords.Length != correctedCoords.Length)
                return 0.0;

            double totalDist = 0;
            for (int i = 0; i < inputCoords.Length; i++)
            {
                var dist = inputCoords[i].Distance(correctedCoords[i]);
                totalDist += dist;
            }

            double avgDist = totalDist / inputCoords.Length;

            // 평균 이동 거리가 1m 이하면 높은 신뢰도
            // 10m 이상이면 낮은 신뢰도
            double confidence = Math.Max(0, 1.0 - avgDist / 10.0);
            return Math.Min(1.0, confidence);
        }

        private Geometry CreateCorrectedGeometry(Geometry original, Coordinate[] newCoords)
        {
            try
            {
                return original switch
                {
                    Polygon => original.Factory.CreatePolygon(newCoords),
                    LinearRing => original.Factory.CreateLinearRing(newCoords),
                    LineString => original.Factory.CreateLineString(newCoords),
                    Point => original.Factory.CreatePoint(newCoords[0]),
                    // Multi* 타입은 단일 지오메트리로 변환 (좌표 배열이 하나이므로)
                    MultiPolygon mp when mp.NumGeometries == 1 => original.Factory.CreatePolygon(newCoords),
                    MultiLineString mls when mls.NumGeometries == 1 => original.Factory.CreateLineString(newCoords),
                    MultiPoint mpt when mpt.NumGeometries == 1 => original.Factory.CreatePoint(newCoords[0]),
                    // 복수 지오메트리가 있는 Multi* 타입은 원본 반환 (개별 처리 필요)
                    _ => original
                };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "보정된 지오메트리 생성 실패, 원본 반환");
                return original;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _session?.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// 모델 메타데이터
        /// </summary>
        private class ModelMetadata
        {
            public string? ModelName { get; set; }
            public string? Version { get; set; }
            public string? Created { get; set; }
        }
    }
}
