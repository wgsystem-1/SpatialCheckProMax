using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Services;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    public class LineWithinPolygonStrategy : BaseRelationCheckStrategy
    {
        private readonly StreamingGeometryProcessor? _streamingProcessor;
        private readonly Dictionary<string, Geometry?> _unionGeometryCache;
        private readonly Dictionary<string, DateTime> _cacheTimestamps;
        private readonly double _defaultTolerance;

        public LineWithinPolygonStrategy(
            ILogger logger, 
            StreamingGeometryProcessor? streamingProcessor,
            Dictionary<string, Geometry?> unionGeometryCache,
            Dictionary<string, DateTime> cacheTimestamps,
            double defaultTolerance) : base(logger)
        {
            _streamingProcessor = streamingProcessor;
            _unionGeometryCache = unionGeometryCache;
            _cacheTimestamps = cacheTimestamps;
            _defaultTolerance = defaultTolerance;
        }

        public override string CaseType => "LineWithinPolygon";

        public override async Task ExecuteAsync(
            DataSource ds,
            Func<string, Layer?> getLayer,
            ValidationResult result,
            RelationCheckConfig config,
            Action<RelationValidationProgressEventArgs> onProgress,
            CancellationToken token)
        {
            var boundary = getLayer(config.MainTableId);
            var centerline = getLayer(config.RelatedTableId);
            if (boundary == null || centerline == null)
            {
                _logger.LogWarning("Case2: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}", config.MainTableId, config.RelatedTableId);
                return;
            }

            var tolerance = config.Tolerance ?? _defaultTolerance;

            // 필드 필터 적용 (RelatedTableId에만 적용: TN_RODWAY_CTLN)
            using var _attrFilterRestore = ApplyAttributeFilterIfMatch(centerline, config.FieldFilter ?? string.Empty);

            var boundaryUnion = BuildUnionGeometryWithCache(boundary, $"{config.MainTableId}_UNION");
            if (boundaryUnion == null) return;

            // 위상 정리: MakeValid 사용
            try { boundaryUnion = boundaryUnion.MakeValid(Array.Empty<string>()); } catch { }

            // 필터 적용 후 피처 개수 확인
            centerline.ResetReading();
            var totalFeatures = centerline.GetFeatureCount(1);
            
            centerline.ResetReading();
            Feature? lf;
            var processedCount = 0;
            
            while ((lf = centerline.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedCount++;
                
                RaiseProgress(onProgress, config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, 
                    processedCount, totalFeatures);
                
                using (lf)
                {
                    var lg = lf.GetGeometryRef();
                    if (lg == null) continue;

                    var oid = lf.GetFID().ToString(CultureInfo.InvariantCulture);
                    var roadSe = lf.GetFieldAsString("road_se") ?? string.Empty;
                    
                    // 선형 객체가 면형 객체 영역을 벗어나는지 검사
                    bool isWithinTolerance = false;
                    try
                    {
                        // 1차: Difference로 경계 밖 길이 계산
                        using var diff = lg.Difference(boundaryUnion);
                        double outsideLength = 0.0;
                        if (diff != null && !diff.IsEmpty())
                        {
                            outsideLength = Math.Abs(diff.Length());
                        }

                        // 2차: 경계면 경계선과의 거리 기반 허용오차 보정
                        if (outsideLength > 0 && tolerance > 0)
                        {
                            // 선의 모든 점이 경계선으로부터 tolerance 이내면 허용
                            bool allNear = IsLineWithinPolygonWithTolerance(lg, boundaryUnion, tolerance);
                            isWithinTolerance = allNear && outsideLength <= tolerance; // 길이도 허용오차 이내로 허용
                        }
                        else
                        {
                            // 밖으로 나간 길이가 거의 없는 경우 통과
                            isWithinTolerance = outsideLength <= tolerance;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "도로중심선 Within 검사 중 오류: OID={OID}", oid);
                        isWithinTolerance = false;
                    }

                    if (!isWithinTolerance)
                    {
                        AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_021", 
                            "도로중심선이 도로경계면을 허용오차를 초과하여 벗어났습니다", 
                            config.RelatedTableId, oid, 
                            $"ROAD_SE={roadSe}, 허용오차={tolerance}m", lg, config.RelatedTableName);
                    }
                }
            }
            
            RaiseProgress(onProgress, config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, 
                totalFeatures, totalFeatures, completed: true);
            
            await Task.CompletedTask;
        }

        private Geometry? BuildUnionGeometryWithCache(Layer layer, string cacheKey)
        {
            if (_unionGeometryCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
            
            Geometry? union = null;
            
            if (_streamingProcessor != null)
            {
                union = _streamingProcessor.CreateUnionGeometryStreaming(layer, null);
            }
            else
            {
                // Fallback: Simple union if streaming processor is missing
                // This is a simplified version for the strategy
                layer.ResetReading();
                Feature? f;
                while ((f = layer.GetNextFeature()) != null)
                {
                    using (f)
                    {
                        var g = f.GetGeometryRef();
                        if (g != null)
                        {
                            if (union == null) union = g.Clone();
                            else union = union.Union(g);
                        }
                    }
                }
            }
            
            _unionGeometryCache[cacheKey] = union;
            _cacheTimestamps[cacheKey] = DateTime.Now;
            
            return union;
        }
    }
}

