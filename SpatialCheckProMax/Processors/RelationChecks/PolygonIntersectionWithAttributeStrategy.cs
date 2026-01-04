using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using OgrEnvelope = OSGeo.OGR.Envelope;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    /// <summary>
    /// 폴리곤 객체 간 교차 검사 전략 (속성 기반)
    /// - 동일 속성값을 가진 폴리곤 객체 간 교차 검사
    /// </summary>
    public class PolygonIntersectionWithAttributeStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PolygonIntersectionWithAttribute";

        public PolygonIntersectionWithAttributeStrategy(ILogger logger) : base(logger)
        {
        }

        public override Task ExecuteAsync(
            DataSource ds,
            Func<string, Layer?> getLayer,
            ValidationResult result,
            RelationCheckConfig config,
            Action<RelationValidationProgressEventArgs> onProgress,
            CancellationToken token)
        {
            var poly = getLayer(config.MainTableId);
            if (poly == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {TableId}", CaseType, config.MainTableId);
                return Task.CompletedTask;
            }

            var tolerance = config.Tolerance ?? 0.0;
            var fieldFilter = config.FieldFilter ?? string.Empty;

            if (string.IsNullOrWhiteSpace(fieldFilter))
            {
                _logger.LogWarning("속성 필드명이 지정되지 않았습니다: {FieldFilter}", fieldFilter);
                return Task.CompletedTask;
            }

            var attributeField = fieldFilter.Trim();
            _logger.LogInformation("차도간 교차검사 시작 (면형): 레이어={Layer}, 속성필드={Field}, 허용오차={Tolerance}",
                config.MainTableId, attributeField, tolerance);
            var startTime = DateTime.Now;

            // 모든 폴리곤 피처 수집 (속성값 포함)
            poly.ResetReading();
            var featureCount = poly.GetFeatureCount(1);
            var allPolygons = new List<(long Oid, Geometry Geom, string? AttrValue)>();
            var defn = poly.GetLayerDefn();
            int attrFieldIdx = GetFieldIndexIgnoreCase(defn, attributeField);

            if (attrFieldIdx < 0)
            {
                _logger.LogWarning("속성 필드를 찾을 수 없습니다: {Field}", attributeField);
                return Task.CompletedTask;
            }

            var processedCount = 0;
            var maxIterations = featureCount > 0 ? Math.Max(10000, (int)(featureCount * 2.0)) : int.MaxValue;
            var useDynamicCounting = featureCount == 0;

            Feature? f;
            while ((f = poly.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;

                if (processedCount > maxIterations && !useDynamicCounting)
                {
                    _logger.LogWarning("안전장치 발동: 최대 반복 횟수({MaxIter})에 도달하여 강제 종료", maxIterations);
                    break;
                }

                using (f)
                {
                    var g = f.GetGeometryRef();
                    if (g == null || g.IsEmpty()) continue;

                    var oid = f.GetFID();
                    var attrValue = f.IsFieldNull(attrFieldIdx) ? null : f.GetFieldAsString(attrFieldIdx);
                    allPolygons.Add((oid, g.Clone(), attrValue));
                }
            }

            _logger.LogInformation("폴리곤 피처 수집 완료: {Count}개", allPolygons.Count);

            try
            {
                // 동일 속성값을 가진 폴리곤 객체 간 교차 검사
                var total = allPolygons.Count;
                var idx = 0;
                var checkedPairs = new HashSet<string>();
                var errorCount = 0;

                for (int i = 0; i < allPolygons.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    idx++;
                    if (idx % 50 == 0 || idx == total)
                    {
                        RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, idx, total);
                    }

                    var (oid1, geom1, attr1) = allPolygons[i];
                    if (string.IsNullOrWhiteSpace(attr1)) continue;

                    for (int j = i + 1; j < allPolygons.Count; j++)
                    {
                        var (oid2, geom2, attr2) = allPolygons[j];
                        if (string.IsNullOrWhiteSpace(attr2)) continue;

                        if (!attr1.Equals(attr2, StringComparison.OrdinalIgnoreCase)) continue;

                        var pairKey = oid1 < oid2 ? $"{oid1}_{oid2}" : $"{oid2}_{oid1}";
                        if (checkedPairs.Contains(pairKey)) continue;
                        checkedPairs.Add(pairKey);

                        try
                        {
                            // Envelope 기반 사전 필터링
                            var env1 = new OgrEnvelope();
                            geom1.GetEnvelope(env1);
                            var env2 = new OgrEnvelope();
                            geom2.GetEnvelope(env2);

                            bool envelopesIntersect = !(env1.MaxX < env2.MinX || env1.MinX > env2.MaxX ||
                                                         env1.MaxY < env2.MinY || env1.MinY > env2.MaxY);
                            if (!envelopesIntersect) continue;

                            using var intersection = geom1.Intersection(geom2);
                            if (intersection != null && !intersection.IsEmpty())
                            {
                                var area = GetSurfaceArea(intersection);
                                var effectiveTolerance = tolerance > 0 ? tolerance : 1e-4;

                                if (area > effectiveTolerance)
                                {
                                    errorCount++;
                                    var oid1Str = oid1.ToString(CultureInfo.InvariantCulture);
                                    var oid2Str = oid2.ToString(CultureInfo.InvariantCulture);
                                    string areaStr = area < 0.01 ? $"{area:F6}" : $"{area:F2}";

                                    var geomType = intersection.GetGeometryType();
                                    var isCollection = geomType == wkbGeometryType.wkbGeometryCollection ||
                                                       geomType == wkbGeometryType.wkbMultiPolygon;

                                    if (isCollection)
                                    {
                                        int count = intersection.GetGeometryCount();
                                        for (int partIdx = 0; partIdx < count; partIdx++)
                                        {
                                            using var subGeom = intersection.GetGeometryRef(partIdx).Clone();
                                            if (subGeom != null && !subGeom.IsEmpty())
                                            {
                                                var subArea = GetSurfaceArea(subGeom);
                                                if (subArea > 0)
                                                {
                                                    AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_037",
                                                        $"동일 {attributeField}({attr1})를 가진 폴리곤 객체가 교차함 (부분 {partIdx + 1}, 면적: {subArea:F2}㎡): OID {oid1Str} <-> {oid2Str}",
                                                        config.MainTableId, oid1Str, $"교차 객체: {oid2Str} (부분 {partIdx + 1}/{count})", subGeom, config.MainTableName,
                                                        config.RelatedTableId, config.RelatedTableName);
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_037",
                                            $"동일 {attributeField}({attr1})를 가진 폴리곤 객체가 교차함 (겹침 면적: {areaStr}㎡): OID {oid1Str} <-> {oid2Str}",
                                            config.MainTableId, oid1Str, $"교차 객체: {oid2Str}", intersection, config.MainTableName,
                                            config.RelatedTableId, config.RelatedTableName);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "교차 검사 중 오류: OID={Oid1} vs {Oid2}", oid1, oid2);
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("차도간 교차검사 완료 (면형): 처리 {Count}개, 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초",
                    total, errorCount, elapsed);

                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);
            }
            finally
            {
                foreach (var (_, geom, _) in allPolygons)
                {
                    geom?.Dispose();
                }
            }

            return Task.CompletedTask;
        }
    }
}
