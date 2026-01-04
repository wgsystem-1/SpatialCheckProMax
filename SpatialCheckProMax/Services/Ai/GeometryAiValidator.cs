using System;
using NetTopologySuite.Geometries;

namespace SpatialCheckProMax.Services.Ai
{
    /// <summary>
    /// AI 교정 결과물의 위상 유효성 및 QA를 수행하는 클래스 (에이전트 C 구현)
    /// </summary>
    public class GeometryAiValidator
    {
        private const double AreaTolerancePercent = 1.0; // 면적 변화 허용 오차 1%

        public record ValidationResult(
            bool IsValid, 
            string Message, 
            double AreaChangePercent, 
            bool HasSelfIntersection);

        /// <summary>
        /// 원본과 교정본을 비교하여 위상학적 유효성을 검사합니다.
        /// </summary>
        public ValidationResult Validate(Geometry original, Geometry corrected)
        {
            if (corrected == null)
                return new ValidationResult(false, "교정본이 존재하지 않습니다.", 0, false);

            // 1. NTS 기본 유효성 검사 (Self-intersection 포함)
            bool isValid = corrected.IsValid;
            bool hasSelfIntersection = !isValid && IsSelfIntersecting(corrected);

            if (!isValid)
            {
                return new ValidationResult(false, "교정된 지오메트리가 위상학적으로 유효하지 않습니다.", 0, hasSelfIntersection);
            }

            // 2. 면적 변화율 검사 (폴리곤인 경우)
            double areaChange = 0;
            if (original is Polygon || original is MultiPolygon)
            {
                double originalArea = original.Area;
                double correctedArea = corrected.Area;

                if (originalArea > 0)
                {
                    areaChange = Math.Abs(correctedArea - originalArea) / originalArea * 100;
                    if (areaChange > AreaTolerancePercent)
                    {
                        return new ValidationResult(false, $"면적 변화율({areaChange:F2}%)이 허용치({AreaTolerancePercent}%)를 초과했습니다.", areaChange, false);
                    }
                }
            }

            return new ValidationResult(true, "유효성 검사 통과", areaChange, false);
        }

        private bool IsSelfIntersecting(Geometry geom)
        {
            // NTS IsValid가 false인 주요 원인 중 하나인 Self-intersection 확인
            // 실제 구현에서는 TopologyPreservingSimplifier 등을 사용할 수 있으나 
            // 여기서는 기본 유효성 플래그를 기반으로 함
            return !geom.IsSimple;
        }
    }
}

