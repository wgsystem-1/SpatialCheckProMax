using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Index.Strtree;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using NtsEnvelope = NetTopologySuite.Geometries.Envelope;
using OgrEnvelope = OSGeo.OGR.Envelope;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    /// <summary>
    /// 폴리곤 겹침 금지 검사 전략
    /// - 메인 폴리곤(건물 등)이 관련 폴리곤(도로경계면 등)과 겹치면 안 됨
    /// </summary>
    public class PolygonNoOverlapStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PolygonNotOverlap";

        public PolygonNoOverlapStrategy(ILogger logger) : base(logger)
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
            var polyA = getLayer(config.MainTableId);
            var polyB = getLayer(config.RelatedTableId);

            if (polyA == null || polyB == null)
            {
                _logger.LogWarning("{CaseType}: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}",
                    CaseType, config.MainTableId, config.RelatedTableId);
                return Task.CompletedTask;
            }

            var tolerance = config.Tolerance ?? 0.0;

            using var _fa = ApplyAttributeFilterIfMatch(polyA, config.FieldFilter);
            using var _fb = ApplyAttributeFilterIfMatch(polyB, config.FieldFilter);

            _logger.LogInformation("폴리곤 겹침 금지 검사 시작: MainTable={MainTable}, RelatedTable={RelatedTable}",
                config.MainTableId, config.RelatedTableId);
            var startTime = DateTime.Now;

            // 관련 레이어 공간 인덱스 생성
            var (polygonIndex, indexedGeometries) = BuildPolygonIndex(polyB);
            if (indexedGeometries.Count == 0)
            {
                _logger.LogInformation("관련 레이어에 피처가 없습니다: {RelatedTable}", config.RelatedTableId);
                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, 0, 0, completed: true);
                return Task.CompletedTask;
            }

            try
            {
                polyA.ResetReading();
                var total = polyA.GetFeatureCount(1);
                _logger.LogInformation("검수 대상 피처 수: {Count}개", total);

                Feature? fa;
                var processed = 0;

                while ((fa = polyA.GetNextFeature()) != null)
                {
                    token.ThrowIfCancellationRequested();

                    using (fa)
                    {
                        processed++;
                        if (processed % 50 == 0 || processed == total)
                        {
                            RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, processed, total);
                        }

                        var ga = fa.GetGeometryRef();
                        if (ga == null || ga.IsEmpty()) continue;

                        // 후보 폴리곤만 질의 후 교차 검사
                        var envelope = new OgrEnvelope();
                        ga.GetEnvelope(envelope);
                        var queryEnvelope = new NtsEnvelope(envelope.MinX, envelope.MaxX, envelope.MinY, envelope.MaxY);
                        var candidates = polygonIndex.Query(queryEnvelope);
                        if (candidates == null || candidates.Count == 0)
                        {
                            continue;
                        }

                        var oid = fa.GetFID().ToString(CultureInfo.InvariantCulture);

                        foreach (Geometry candidate in candidates)
                        {
                            token.ThrowIfCancellationRequested();
                            try
                            {
                                using var inter = ga.Intersection(candidate);
                                if (inter == null || inter.IsEmpty())
                                {
                                    continue;
                                }

                                var area = GetSurfaceArea(inter);
                                if (area <= tolerance)
                                {
                                    continue;
                                }

                                var geomType = inter.GetGeometryType();
                                var isCollection = geomType == wkbGeometryType.wkbGeometryCollection ||
                                                   geomType == wkbGeometryType.wkbMultiPolygon;

                                if (isCollection)
                                {
                                    int count = inter.GetGeometryCount();
                                    for (int i = 0; i < count; i++)
                                    {
                                        using var subGeom = inter.GetGeometryRef(i)?.Clone();
                                        if (subGeom != null && !subGeom.IsEmpty())
                                        {
                                            var subArea = GetSurfaceArea(subGeom);
                                            if (subArea > 0)
                                            {
                                                AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_001",
                                                    $"{config.MainTableName}(이)가 {config.RelatedTableName}(을)를 침범함 (부분 {i + 1})",
                                                    config.MainTableId, oid, $"침범 부분 {i + 1}/{count}", subGeom, config.MainTableName,
                                                    config.RelatedTableId, config.RelatedTableName);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_001",
                                        $"{config.MainTableName}(이)가 {config.RelatedTableName}(을)를 침범함",
                                        config.MainTableId, oid, string.Empty, inter, config.MainTableName,
                                        config.RelatedTableId, config.RelatedTableName);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "교차 검사 중 오류 발생: OID={Oid}", fa.GetFID());
                            }
                        }
                    }
                }

                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                _logger.LogInformation("폴리곤 겹침 금지 검사 완료: {Count}개 피처, 소요시간: {Elapsed:F2}초",
                    total, elapsed);

                RaiseProgress(onProgress, config.RuleId ?? string.Empty, CaseType, total, total, completed: true);
            }
            finally
            {
                // 인덱싱된 지오메트리 정리
                foreach (var geom in indexedGeometries)
                {
                    geom?.Dispose();
                }
            }

            return Task.CompletedTask;
        }

        private (STRtree<Geometry>, List<Geometry>) BuildPolygonIndex(Layer layer)
        {
            var geometries = new List<Geometry>();
            var index = new STRtree<Geometry>();

            layer.ResetReading();
            Feature? feature;
            while ((feature = layer.GetNextFeature()) != null)
            {
                using (feature)
                {
                    var geometryRef = feature.GetGeometryRef();
                    if (geometryRef == null || geometryRef.IsEmpty())
                    {
                        continue;
                    }

                    var clone = geometryRef.Clone();
                    geometries.Add(clone);

                    var envelope = new OgrEnvelope();
                    clone.GetEnvelope(envelope);
                    var ntsEnvelope = new NtsEnvelope(envelope.MinX, envelope.MaxX, envelope.MinY, envelope.MaxY);
                    index.Insert(ntsEnvelope, clone);
                }
            }

            index.Build();
            return (index, geometries);
        }

        private static new double GetSurfaceArea(Geometry geometry)
        {
            if (geometry == null) return 0;

            var geomType = geometry.GetGeometryType();
            var flatType = (wkbGeometryType)((int)geomType & 0xFF);

            if (flatType == wkbGeometryType.wkbPolygon || flatType == wkbGeometryType.wkbMultiPolygon)
            {
                return geometry.GetArea();
            }

            if (flatType == wkbGeometryType.wkbGeometryCollection)
            {
                double totalArea = 0;
                int count = geometry.GetGeometryCount();
                for (int i = 0; i < count; i++)
                {
                    using var subGeom = geometry.GetGeometryRef(i);
                    if (subGeom != null)
                    {
                        totalArea += GetSurfaceArea(subGeom);
                    }
                }
                return totalArea;
            }

            return 0;
        }
    }
}
