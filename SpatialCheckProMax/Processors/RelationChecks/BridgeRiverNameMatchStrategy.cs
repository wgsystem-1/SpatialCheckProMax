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
    /// 교량과 하천 이름 일치 검사 전략
    /// - 교량의 하천명 속성과 하천중심선의 하천명 속성이 일치하는지 검사
    /// </summary>
    public class BridgeRiverNameMatchStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "BridgeRiverNameMatch";

        public BridgeRiverNameMatchStrategy(ILogger logger) : base(logger)
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
            // FieldFilter 형식: "pg_rdfc_se IN (PRC002|PRC003|PRC004|PRC005);feat_nm"
            var fieldFilter = config.FieldFilter ?? string.Empty;
            var filterParts = fieldFilter.Split(';');
            var bridgeFilter = filterParts.Length > 0 ? filterParts[0] : string.Empty;
            var riverNameField = filterParts.Length > 1 ? filterParts[1] : "feat_nm";

            var bridgeLayer = getLayer(config.MainTableId);
            var riverLayer = getLayer(config.RelatedTableId);

            if (bridgeLayer == null || riverLayer == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: Bridge={Bridge}, River={River}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            var tolerance = config.Tolerance ?? 0.5;

            using var _bridgeFilter = ApplyAttributeFilterIfMatch(bridgeLayer, bridgeFilter);

            _logger.LogInformation("교량하천명 일치 검사 시작: 교량 레이어={BridgeLayer}, 하천 레이어={RiverLayer}, 하천명 필드={Field}",
                config.MainTableId, config.RelatedTableId, riverNameField);
            var startTime = DateTime.Now;

            // 하천중심선의 하천명 인덱스
            var riverDefn = riverLayer.GetLayerDefn();
            int riverNameIdx = GetFieldIndexIgnoreCase(riverDefn, riverNameField);
            if (riverNameIdx < 0)
            {
                _logger.LogWarning("하천명 필드를 찾을 수 없습니다: {Field}", riverNameField);
                return Task.CompletedTask;
            }

            // 교량 레이어의 하천명 필드 인덱스
            var bridgeDefn = bridgeLayer.GetLayerDefn();
            int bridgeNameIdx = GetFieldIndexIgnoreCase(bridgeDefn, riverNameField);
            if (bridgeNameIdx < 0)
            {
                _logger.LogWarning("교량의 하천명 필드를 찾을 수 없습니다: {Field}", riverNameField);
                return Task.CompletedTask;
            }

            // 하천중심선을 리스트로 구성
            var riverFeatures = new List<(long oid, Geometry geom, string name)>();
            riverLayer.ResetReading();
            Feature? rf;
            while ((rf = riverLayer.GetNextFeature()) != null)
            {
                using (rf)
                {
                    var rg = rf.GetGeometryRef();
                    if (rg == null) continue;

                    try
                    {
                        if (rg.IsEmpty()) continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "하천중심선 지오메트리 IsEmpty() 검사 중 오류: FID={FID}", rf.GetFID());
                        continue;
                    }

                    Geometry? clonedGeom = null;
                    try
                    {
                        clonedGeom = rg.Clone();
                        if (clonedGeom == null) continue;

                        try
                        {
                            if (clonedGeom.IsEmpty())
                            {
                                clonedGeom.Dispose();
                                continue;
                            }
                        }
                        catch
                        {
                            clonedGeom.Dispose();
                            continue;
                        }
                    }
                    catch
                    {
                        clonedGeom?.Dispose();
                        continue;
                    }

                    var name = rf.IsFieldNull(riverNameIdx) ? string.Empty : (rf.GetFieldAsString(riverNameIdx) ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(name))
                    {
                        clonedGeom.Dispose();
                        continue;
                    }

                    riverFeatures.Add((rf.GetFID(), clonedGeom, name));
                }
            }

            _logger.LogInformation("하천중심선 수집 완료: {Count}개", riverFeatures.Count);

            try
            {
                // 교량 검사
                bridgeLayer.ResetReading();
                var total = bridgeLayer.GetFeatureCount(1);
                var processed = 0;

                Feature? bf;
                while ((bf = bridgeLayer.GetNextFeature()) != null)
                {
                    using (bf)
                    {
                        token.ThrowIfCancellationRequested();
                        processed++;
                        if (processed % 50 == 0 || processed == total)
                        {
                            RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processed, total);
                        }

                        var bg = bf.GetGeometryRef();
                        if (bg == null) continue;

                        try
                        {
                            if (bg.IsEmpty()) continue;
                        }
                        catch
                        {
                            continue;
                        }

                        var bridgeOid = bf.GetFID();
                        var bridgeName = bf.IsFieldNull(bridgeNameIdx) ? string.Empty : (bf.GetFieldAsString(bridgeNameIdx) ?? string.Empty).Trim();

                        // 교량의 버퍼 영역 내 하천중심선 검색
                        Geometry? buffer = null;
                        try
                        {
                            buffer = bg.Buffer(tolerance, 0);
                            if (buffer == null) continue;

                            try
                            {
                                if (buffer.IsEmpty())
                                {
                                    buffer.Dispose();
                                    continue;
                                }
                            }
                            catch
                            {
                                buffer?.Dispose();
                                continue;
                            }
                        }
                        catch
                        {
                            buffer?.Dispose();
                            continue;
                        }

                        using (buffer)
                        {
                            foreach (var (riverOid, riverGeom, riverName) in riverFeatures)
                            {
                                if (riverGeom == null) continue;

                                try
                                {
                                    if (riverGeom.IsEmpty()) continue;
                                }
                                catch
                                {
                                    continue;
                                }

                                bool intersects = false;
                                try
                                {
                                    intersects = buffer.Intersects(riverGeom);
                                }
                                catch
                                {
                                    continue;
                                }

                                if (intersects)
                                {
                                    if (!string.IsNullOrEmpty(bridgeName) && !string.IsNullOrEmpty(riverName))
                                    {
                                        if (!string.Equals(bridgeName, riverName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            AddDetailedError(result, config.RuleId ?? "THE_CLS_REL_001",
                                                $"교량의 하천명('{bridgeName}')과 하천중심선의 하천명('{riverName}')이 일치하지 않습니다",
                                                config.MainTableId, bridgeOid.ToString(CultureInfo.InvariantCulture),
                                                $"교량 하천명='{bridgeName}', 하천중심선 하천명='{riverName}'", bg, config.MainTableName,
                                                config.RelatedTableId, config.RelatedTableName);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("교량하천명 일치 검사 완료: 교량 {BridgeCount}개, 하천 {RiverCount}개, 소요시간: {Elapsed:F2}초",
                    total, riverFeatures.Count, elapsed);

                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);
            }
            finally
            {
                foreach (var (_, geom, _) in riverFeatures)
                {
                    geom?.Dispose();
                }
            }

            return Task.CompletedTask;
        }
    }
}
