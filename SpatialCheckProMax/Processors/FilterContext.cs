using System;
using SpatialCheckProMax.Utils;
using OSGeo.OGR;

namespace SpatialCheckProMax.Processors
{
    /// <summary>
    /// 필터 컨텍스트 (GDAL 필터 해제 및 2차 메모리 필터 제공)
    /// </summary>
    public sealed class FilterContext : IDisposable
    {
        private readonly IDisposable? _gdalFilterDisposer;
        private readonly Func<Feature, bool> _memoryPredicate;
        private readonly bool _isGdalFilterApplied;

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="gdalFilterDisposer">GDAL 필터 해제용 객체 (성공 시)</param>
        /// <param name="memoryPredicate">2차 메모리 필터 (GDAL 필터 실패 시 사용)</param>
        /// <param name="isGdalFilterApplied">GDAL 필터가 성공적으로 적용되었는지 여부</param>
        public FilterContext(IDisposable? gdalFilterDisposer, Func<Feature, bool> memoryPredicate, bool isGdalFilterApplied)
        {
            _gdalFilterDisposer = gdalFilterDisposer;
            _memoryPredicate = memoryPredicate ?? (_ => true);
            _isGdalFilterApplied = isGdalFilterApplied;
        }

        /// <summary>
        /// 필터가 없는 빈 컨텍스트 생성
        /// </summary>
        public static FilterContext Empty => new FilterContext(null, _ => true, false);

        /// <summary>
        /// 피처가 필터 조건을 만족하는지 검사합니다.
        /// GDAL 필터가 적용된 상태라면 항상 True를 반환하고 (이미 필터링됨),
        /// GDAL 필터 적용에 실패했다면 메모리 상에서 조건을 검사합니다.
        /// </summary>
        public bool Matches(Feature feature)
        {
            // GDAL 필터가 성공적으로 적용되었다면, 드라이버가 이미 필터링한 결과를 반환하므로 추가 검사 불필요
            if (_isGdalFilterApplied)
            {
                return true;
            }

            // GDAL 필터 실패 시(전체 스캔), 메모리에서 조건 검사
            return _memoryPredicate(feature);
        }

        public void Dispose()
        {
            _gdalFilterDisposer?.Dispose();
        }
    }
}

