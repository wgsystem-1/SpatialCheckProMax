using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OSGeo.OGR;

namespace SpatialCheckProMax.Utils
{
    /// <summary>
    /// SQL 스타일 필터 표현식을 파싱하고 메모리 내에서 평가하는 헬퍼 클래스
    /// GDAL 드라이버가 지원하지 않는 복잡한 필터(IN, NOT IN 등)를 위한 폴백 메커니즘 제공
    /// </summary>
    public static class FilterHelper
    {
        /// <summary>
        /// 필터 문자열을 파싱하여 Feature에 대한 Predicate를 생성합니다.
        /// </summary>
        public static Func<Feature, bool> CreateFilterPredicate(string filterExpression)
        {
            if (string.IsNullOrWhiteSpace(filterExpression))
                return _ => true;

            var conditions = new List<Func<Feature, bool>>();

            // 정규식 패턴 정의
            // 1. IN 구문: field IN (val1, val2, ...)
            // 2. NOT IN 구문: field NOT IN (val1, val2, ...)
            // 3. 단순 비교: field = val, field <> val 등 (추후 확장 가능, 현재는 IN/NOT IN 집중)

            // 정규화된 필터 문자열 사용 (쉼표 처리 등이 이미 되어있다고 가정)
            // 하지만 여기서는 원본 문자열을 파싱하는 것이 더 안전할 수 있음

            // 1. NOT IN 처리
            var notInMatches = Regex.Matches(filterExpression, @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s+NOT\s+IN\s*\(([^)]+)\)");
            foreach (Match match in notInMatches)
            {
                var fieldName = match.Groups[1].Value;
                var valuesStr = match.Groups[2].Value;
                var values = ParseValues(valuesStr);
                
                conditions.Add(f => 
                {
                    var val = GetFeatureValue(f, fieldName);
                    return val == null || !values.Contains(val); // NULL은 필터에 따라 다를 수 있으나 보통 제외되지 않음? SQL에서는 NULL != any 항상 false
                    // SQL 표준: NULL NOT IN (...) -> Unknown (False 취급)
                    // 여기서는 값이 존재하고 목록에 없으면 true
                });
            }

            // 2. IN 처리 (NOT IN에 매칭되지 않은 부분에서 찾아야 함 - 간단히 별도 처리)
            // 정규식 중복 매칭 방지를 위해 NOT IN을 먼저 제거하거나, 더 정교한 파서 필요
            // 여기서는 단순하게 처리: NOT IN이 아닌 IN만 찾기 위해 부정형 전방 탐색 사용하거나,
            // 이미 정규화된 문자열을 가정
            
            var inMatches = Regex.Matches(filterExpression, @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s+(?<!NOT\s+)IN\s*\(([^)]+)\)");
            foreach (Match match in inMatches)
            {
                var fieldName = match.Groups[1].Value;
                var valuesStr = match.Groups[2].Value;
                var values = ParseValues(valuesStr);

                conditions.Add(f =>
                {
                    var val = GetFeatureValue(f, fieldName);
                    return val != null && values.Contains(val);
                });
            }

            // 3. 등호 (=) 처리
            var equalMatches = Regex.Matches(filterExpression, @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s*=\s*['""]?([^'""\s]+)['""]?");
            foreach (Match match in equalMatches)
            {
                // IN/NOT IN 내부의 = 는 제외해야 함 (이 정규식은 간단해서 오탐 가능성 있음)
                // 따라서 RelationCheckProcessor의 NormalizeFilterExpression과 로직을 공유하거나
                // 여기서는 간단히 구현
                var fieldName = match.Groups[1].Value;
                var value = match.Groups[2].Value;
                
                conditions.Add(f =>
                {
                    var val = GetFeatureValue(f, fieldName);
                    return val != null && val.Equals(value, StringComparison.OrdinalIgnoreCase);
                });
            }
            
            // 4. 부등호 (<>) 처리
            var notEqualMatches = Regex.Matches(filterExpression, @"(?i)\b([A-Za-z_][A-Za-z0-9_]*)\s*<>\s*['""]?([^'""\s]+)['""]?");
            foreach (Match match in notEqualMatches)
            {
                var fieldName = match.Groups[1].Value;
                var value = match.Groups[2].Value;

                conditions.Add(f =>
                {
                    var val = GetFeatureValue(f, fieldName);
                    return val != null && !val.Equals(value, StringComparison.OrdinalIgnoreCase);
                });
            }

            if (conditions.Count == 0)
            {
                // 파싱된 조건이 없으면 (복잡한 쿼리 등) 일단 Pass (로그에 경고는 호출측에서)
                // 또는 False를 리턴해서 안전하게? -> 원본 필터가 있는데 파싱 못했으면 False가 맞을 수도 있지만
                // GDAL이 실패하고 이것도 실패하면 데이터가 다 스킵될 위험.
                // 현재는 IN/NOT IN 지원이 주 목적이므로, 해당 구문이 없으면 True 반환 (필터 없음 간주)
                return _ => true; 
            }

            // 모든 조건을 만족해야 함 (AND 가정)
            return f => conditions.All(c => c(f));
        }

        private static HashSet<string> ParseValues(string valuesStr)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parts = valuesStr.Split(',');
            foreach (var part in parts)
            {
                var val = part.Trim().Trim('\'', '"');
                set.Add(val);
            }
            return set;
        }

        private static string? GetFeatureValue(Feature feature, string fieldName)
        {
            try
            {
                // 필드 인덱스 캐싱을 하면 좋겠지만 FeatureDefn 생명주기 문제로 매번 조회
                // 성능 영향 최소화를 위해 Try/Catch
                var idx = feature.GetFieldIndex(fieldName);
                if (idx >= 0 && feature.IsFieldSet(idx))
                {
                    return feature.GetFieldAsString(idx);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}


