using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OSGeo.OGR;

namespace SpatialCheckProMax.Utils
{
    /// <summary>
    /// 간단한 SQL WHERE 절 파서 (IN, NOT IN, =, <>, != 지원)
    /// GDAL 드라이버가 지원하지 않는 필터를 메모리에서 처리하기 위함
    /// </summary>
    public static class FilterExpressionParser
    {
        /// <summary>
        /// 필터 표현식을 파싱하여 Feature 검증 함수를 반환합니다.
        /// 지원 구문:
        /// - field IN ('val1', 'val2', ...)
        /// - field NOT IN ('val1', 'val2', ...)
        /// - field = 'val'
        /// - field != 'val' (또는 <>)
        /// </summary>
        public static Func<Feature, bool> Parse(string filterExpression)
        {
            if (string.IsNullOrWhiteSpace(filterExpression))
                return _ => true;

            var trimmed = filterExpression.Trim();

            // 1. IN 구문 처리
            var inMatch = Regex.Match(trimmed, @"^(?i)([\w_]+)\s+(NOT\s+)?IN\s*\((.+)\)$");
            if (inMatch.Success)
            {
                var fieldName = inMatch.Groups[1].Value;
                var isNot = inMatch.Groups[2].Success; // "NOT "
                var valuesPart = inMatch.Groups[3].Value;

                var values = ParseValueList(valuesPart);
                
                return f =>
                {
                    var val = GetFieldValue(f, fieldName);
                    if (val == null) return false; // NULL 처리는 요구사항에 따라 달라질 수 있음 (여기선 매칭 안됨)
                    
                    var contains = values.Contains(val);
                    return isNot ? !contains : contains;
                };
            }

            // 2. 단순 비교 (=, !=, <>)
            var opMatch = Regex.Match(trimmed, @"^(?i)([\w_]+)\s*(=|!=|<>)\s*(.+)$");
            if (opMatch.Success)
            {
                var fieldName = opMatch.Groups[1].Value;
                var op = opMatch.Groups[2].Value;
                var targetValue = opMatch.Groups[3].Value.Trim(' ', '\'', '"');

                return f =>
                {
                    var val = GetFieldValue(f, fieldName);
                    if (val == null) return false;

                    bool isEqual = val.Equals(targetValue, StringComparison.OrdinalIgnoreCase); // 대소문자 무시 여부는 정책 결정 필요
                    
                    if (op == "=") return isEqual;
                    return !isEqual; // != or <>
                };
            }

            // 3. OR 조건 (단순 구현: field = val OR field = val2 ...)
            // 복잡한 중첩 OR/AND는 현재 범위 밖 (필요 시 확장)
            // 여기서는 파싱 실패 시 항상 True 반환 (GDAL 필터 실패 시 전체 검수와 동일 효과)
            // 하지만 로그를 통해 알릴 수 있도록 함.
            
            // 기본: 파싱 실패 시 True (필터링 안함 = 안전하지만 성능 저하/오류 과다)
            // 여기서는 명시적으로 지원하는 패턴만 처리하고 나머지는 True 리턴
            return _ => true;
        }

        private static HashSet<string> ParseValueList(string valuesPart)
        {
            // 쉼표로 분리하되 따옴표 안의 쉼표는 무시하는 정교한 파싱이 필요할 수 있음
            // 현재는 간단히 쉼표 분리 후 따옴표 제거
            var parts = valuesPart.Split(',');
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parts)
            {
                set.Add(p.Trim().Trim(' ', '\'', '"'));
            }
            return set;
        }

        private static string? GetFieldValue(Feature feature, string fieldName)
        {
            try
            {
                int idx = feature.GetFieldIndex(fieldName);
                if (idx < 0) return null;
                if (feature.IsFieldNull(idx)) return null;
                return feature.GetFieldAsString(idx);
            }
            catch
            {
                return null;
            }
        }
    }
}


