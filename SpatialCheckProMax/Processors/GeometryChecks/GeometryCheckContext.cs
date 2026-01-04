using System;
using System.Collections.Concurrent;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Services;

namespace SpatialCheckProMax.Processors.GeometryChecks
{
    /// <summary>
    /// 지오메트리 검사 전략들이 공유하는 컨텍스트
    /// </summary>
    public class GeometryCheckContext
    {
        /// <summary>
        /// 현재 검사 중인 파일 경로
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 지오메트리 검사 기준값
        /// </summary>
        public GeometryCriteria Criteria { get; set; } = GeometryCriteria.CreateDefault();

        /// <summary>
        /// 피처 필터 서비스
        /// </summary>
        public IFeatureFilterService? FeatureFilterService { get; set; }

        /// <summary>
        /// 고성능 지오메트리 검증기
        /// </summary>
        public HighPerformanceGeometryValidator? HighPerfValidator { get; set; }

        /// <summary>
        /// 스트리밍 오류 작성기 (대용량 파일 처리용)
        /// </summary>
        public IStreamingErrorWriter? StreamingErrorWriter { get; set; }

        /// <summary>
        /// 스트리밍 모드 활성화 여부
        /// </summary>
        public bool IsStreamingMode => StreamingErrorWriter != null;

        /// <summary>
        /// 스트리밍 배치 크기
        /// </summary>
        public int StreamingBatchSize { get; set; } = 1000;

        /// <summary>
        /// 공간 인덱스 캐시
        /// </summary>
        public ConcurrentDictionary<string, object> SpatialIndexCache { get; } = new();

        /// <summary>
        /// 진행률 보고 콜백 (선택적)
        /// </summary>
        public Action<int, int>? OnProgress { get; set; }

        /// <summary>
        /// 공간 인덱스 캐시 정리
        /// </summary>
        public void ClearSpatialIndexCache()
        {
            SpatialIndexCache.Clear();
        }
    }
}
