using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;

namespace SpatialCheckProMax.Processors.GeometryChecks
{
    /// <summary>
    /// 지오메트리 검사 전략 인터페이스
    /// </summary>
    public interface IGeometryCheckStrategy
    {
        /// <summary>
        /// 이 전략이 처리하는 검사 유형 (예: "GeosValidity", "ShortObject")
        /// </summary>
        string CheckType { get; }

        /// <summary>
        /// 지오메트리 검사 실행
        /// </summary>
        /// <param name="layer">검사 대상 레이어</param>
        /// <param name="config">검사 설정</param>
        /// <param name="context">검사 컨텍스트 (공유 데이터)</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>발견된 오류 목록</returns>
        Task<List<ValidationError>> ExecuteAsync(
            Layer layer,
            GeometryCheckConfig config,
            GeometryCheckContext context,
            CancellationToken cancellationToken);

        /// <summary>
        /// 이 전략이 현재 설정에서 활성화되어 있는지 확인
        /// </summary>
        bool IsEnabled(GeometryCheckConfig config);
    }
}
