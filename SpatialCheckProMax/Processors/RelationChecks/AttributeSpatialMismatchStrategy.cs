using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    /// <summary>
    /// 속성-공간 불일치 검사 전략
    /// - 차도와 도로시설물 관계 검사
    /// </summary>
    public class AttributeSpatialMismatchStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "AttributeSpatialMismatch";

        public AttributeSpatialMismatchStrategy(ILogger logger) : base(logger)
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
            var mainLayer = getLayer(config.MainTableId);
            var facilityLayer = getLayer(config.RelatedTableId);

            if (mainLayer == null || facilityLayer == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: Main={MainTable}, Related={RelatedTable}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            var fieldFilter = config.FieldFilter ?? string.Empty;
            var tolerance = config.Tolerance ?? 0.0;

            // FieldFilter 형식: "road_se;pg_rdfc_se"
            if (string.IsNullOrWhiteSpace(fieldFilter))
            {
                _logger.LogWarning("속성 필드명이 지정되지 않았습니다: {FieldFilter}", fieldFilter);
                return Task.CompletedTask;
            }

            var fields = fieldFilter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 2)
            {
                _logger.LogWarning("속성 필드명이 올바르게 지정되지 않았습니다: {FieldFilter}", fieldFilter);
                return Task.CompletedTask;
            }

            var roadField = fields[0];
            var facilityField = fields[1];

            _logger.LogInformation("차도와 도로시설물관계 검사 시작: 도로={RoadLayer}, 도로시설={FacilityLayer}, 도로필드={RoadField}, 시설필드={FacilityField}",
                config.MainTableId, config.RelatedTableId, roadField, facilityField);
            var startTime = DateTime.Now;

            // 도로 레이어 필드 인덱스 확인
            var roadDefn = mainLayer.GetLayerDefn();
            int roadFieldIdx = GetFieldIndexIgnoreCase(roadDefn, roadField);
            if (roadFieldIdx < 0)
            {
                _logger.LogWarning("도로 속성 필드를 찾을 수 없습니다: {Field}", roadField);
                return Task.CompletedTask;
            }

            // 도로시설 레이어 필드 인덱스 확인
            var facilityDefn = facilityLayer.GetLayerDefn();
            int facilityFieldIdx = GetFieldIndexIgnoreCase(facilityDefn, facilityField);
            if (facilityFieldIdx < 0)
            {
                _logger.LogWarning("도로시설 속성 필드를 찾을 수 없습니다: {Field}", facilityField);
                return Task.CompletedTask;
            }

            // 도로 피처 수집
            mainLayer.ResetReading();
            var roadFeatures = new List<(long Oid, Geometry Geom, string? RoadAttr)>();
            var roadFeatureCount = mainLayer.GetFeatureCount(1);
            var processedCount = 0;
            var maxIterations = roadFeatureCount > 0 ? Math.Max(10000, (int)(roadFeatureCount * 2.0)) : int.MaxValue;
            var useDynamicCounting = roadFeatureCount == 0;

            Feature? f;
            while ((f = mainLayer.GetNextFeature()) != null)
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
                    var roadAttr = f.IsFieldNull(roadFieldIdx) ? null : f.GetFieldAsString(roadFieldIdx);
                    roadFeatures.Add((oid, g.Clone(), roadAttr));
                }
            }

            _logger.LogInformation("도로 피처 수집 완료: {Count}개", roadFeatures.Count);

            try
            {
                // 검사: 도로에 도로시설 레이어가 공간중첩되나 속성이 없는 경우
                var errorCount = 0;
                var checkedPairs = new HashSet<string>();

                foreach (var (roadOid, roadGeom, roadAttr) in roadFeatures)
                {
                    token.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(roadAttr)) continue;

                    try
                    {
                        facilityLayer.SetSpatialFilter(roadGeom);

                        Feature? facilityF;
                        while ((facilityF = facilityLayer.GetNextFeature()) != null)
                        {
                            using (facilityF)
                            {
                                var facilityGeom = facilityF.GetGeometryRef();
                                if (facilityGeom == null || facilityGeom.IsEmpty()) continue;

                                var facilityOid = facilityF.GetFID();
                                var pairKey = $"{roadOid}_{facilityOid}";
                                if (checkedPairs.Contains(pairKey)) continue;
                                checkedPairs.Add(pairKey);

                                bool isOverlapping = false;
                                try
                                {
                                    if (roadGeom.Overlaps(facilityGeom) || facilityGeom.Overlaps(roadGeom) ||
                                        roadGeom.Contains(facilityGeom) || facilityGeom.Within(roadGeom))
                                    {
                                        isOverlapping = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "공간 중첩 확인 중 오류: RoadOID={RoadOid}, FacilityOID={FacilityOid}", roadOid, facilityOid);
                                    continue;
                                }

                                if (isOverlapping)
                                {
                                    var facilityAttr = facilityF.IsFieldNull(facilityFieldIdx) ? null : facilityF.GetFieldAsString(facilityFieldIdx);

                                    if (string.IsNullOrWhiteSpace(facilityAttr))
                                    {
                                        errorCount++;
                                        var roadOidStr = roadOid.ToString(CultureInfo.InvariantCulture);
                                        var facilityOidStr = facilityOid.ToString(CultureInfo.InvariantCulture);
                                        AddDetailedError(result, config.RuleId ?? "LOG_CNC_REL_003",
                                            $"도로({roadField}={roadAttr})에 도로시설 레이어가 중첩되나 {facilityField} 속성이 없음: OID {facilityOidStr}",
                                            config.RelatedTableId, facilityOidStr, $"도로 OID: {roadOidStr}", facilityGeom, config.RelatedTableName,
                                            config.MainTableId, config.MainTableName);
                                    }
                                }
                            }
                        }

                        facilityLayer.SetSpatialFilter(null);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "도로시설 검사 중 오류: RoadOID={RoadOid}", roadOid);
                        try { facilityLayer?.SetSpatialFilter(null); } catch { }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("차도와 도로시설물관계 검사 완료: 오류 {ErrorCount}개, 소요시간: {Elapsed:F2}초", errorCount, elapsed);
                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, roadFeatures.Count, roadFeatures.Count, completed: true);
            }
            finally
            {
                foreach (var (_, geom, _) in roadFeatures)
                {
                    geom?.Dispose();
                }
            }

            return Task.CompletedTask;
        }
    }
}
