using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    /// <summary>
    /// 도로면형_경계선불일치 (PolygonBoundaryMatch) 전략
    /// 도로경계선(Line)의 정점이 도로경계면(Polygon)의 정점과 일치하는지 검사합니다.
    /// </summary>
    public class PolygonBoundaryMatchStrategy : BaseRelationCheckStrategy
    {
        public override string CaseType => "PolygonBoundaryMatch";

        public PolygonBoundaryMatchStrategy(ILogger logger) : base(logger)
        {
        }

        public override async Task ExecuteAsync(
            DataSource ds,
            Func<string, Layer?> getLayer,
            ValidationResult result,
            RelationCheckConfig config,
            Action<RelationValidationProgressEventArgs> onProgress,
            CancellationToken token)
        {
            // 1. 레이어 가져오기
            // CSV 설정: Main=Line(tn_rodway_bndryln), Related=Polygon(tn_rodway_bndry)
            var mainLayer = getLayer(config.MainTableName);
            var relatedLayer = getLayer(config.RelatedTableName);

            if (mainLayer == null || relatedLayer == null)
            {
                _logger.LogWarning($"레이어를 찾을 수 없습니다. Main: {config.MainTableName}, Related: {config.RelatedTableName}");
                return;
            }

            // 필터 적용 (필요한 경우)
            using var mainFilter = ApplyAttributeFilterIfMatch(mainLayer, config.FieldFilter);
            
            long totalCount = mainLayer.GetFeatureCount(1);
            long processedCount = 0;
            double tolerance = config.Tolerance ?? 0.1;

            // 2. Related Feature (Polygon) 메모리에 로드 (공간 인덱싱용)
            // 대용량일 경우 메모리 문제가 발생할 수 있으므로, 실제로는 QuadTree나 R-Tree를 사용해야 함.
            // 여기서는 간단한 Envelope 리스트로 최적화
            var polygons = new List<FeatureGeometry>();
            relatedLayer.ResetReading();
            Feature feat;
            while ((feat = relatedLayer.GetNextFeature()) != null)
            {
                var geom = feat.GetGeometryRef();
                if (geom != null && (geom.GetGeometryType() == wkbGeometryType.wkbPolygon || geom.GetGeometryType() == wkbGeometryType.wkbMultiPolygon))
                {
                    polygons.Add(new FeatureGeometry 
                    { 
                        Geometry = geom.Clone(), 
                        Envelope = GetEnvelope(geom) 
                    });
                }
                feat.Dispose();
            }

            // 3. Main Feature (Line) 순회 및 검증
            mainLayer.ResetReading();
            
            // 병렬 처리를 위해 Main Feature들을 리스트로 로드 (메모리 주의)
            // *메모리 최적화를 위해 배치 단위로 처리하는 것이 좋으나, 여기서는 전체 로드 방식 사용*
            var lines = new List<Feature>();
            while ((feat = mainLayer.GetNextFeature()) != null)
            {
                lines.Add(feat);
            }

            object lockObj = new object();

            await Task.Run(() =>
            {
                Parallel.ForEach(lines, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token }, (lineFeature) =>
                {
                    if (token.IsCancellationRequested) return;

                    try
                    {
                        var lineGeom = lineFeature.GetGeometryRef();
                        if (lineGeom == null) return;

                        // Line의 Envelope 계산
                        var lineEnv = GetEnvelope(lineGeom);
                        
                        // 후보 Polygon 검색 (Envelope 교차 검사)
                        // Tolerance 고려하여 확장
                        double minX = lineEnv.MinX - tolerance;
                        double maxX = lineEnv.MaxX + tolerance;
                        double minY = lineEnv.MinY - tolerance;
                        double maxY = lineEnv.MaxY + tolerance;

                        var candidatePolygons = polygons
                            .Where(p => !(p.Envelope.MinX > maxX || p.Envelope.MaxX < minX || 
                                          p.Envelope.MinY > maxY || p.Envelope.MaxY < minY))
                            .ToList();

                        if (candidatePolygons.Count == 0)
                        {
                            lock (lockObj)
                            {
                                AddDetailedError(result, "PolygonBoundaryMatch", 
                                    "인접한 도로경계면이 없습니다.", 
                                    config.RelatedTableId, lineFeature.GetFID().ToString(), 
                                    geometry: lineGeom, tableDisplayName: config.RelatedTableName);
                            }
                            return;
                        }

                        // Line의 각 정점 검사
                        // MultiLineString 지원을 위해 재귀적 포인트 추출 대신 단순화
                        // OGR Geometry는 GetPointCount()가 하위 지오메트리까지 포함하지 않을 수 있음 (버전에 따라 다름)
                        // 안전하게 모든 포인트를 검사하기 위해 WKB나 재귀 호출 필요하지만, 
                        // 여기서는 단순 LineString 가정 또는 OGR의 Flattening 활용 가능.
                        // *간단히 GetPointCount() 사용 (LineString 가정)*
                        
                        int pointCount = lineGeom.GetPointCount();
                        List<(double x, double y)> errorPoints = new List<(double x, double y)>();

                        for (int i = 0; i < pointCount; i++)
                        {
                            double vx = lineGeom.GetX(i);
                            double vy = lineGeom.GetY(i);
                            bool isVertexMatched = false;

                            foreach (var poly in candidatePolygons)
                            {
                                // Polygon의 경계(Boundary)와 거리 계산
                                // Polygon.Boundary()는 LineString(Ring)을 반환
                                using var boundary = poly.Geometry.GetBoundary();
                                if (boundary == null) continue;

                                // 정점 간 거리 비교 (Boundary의 모든 정점 순회)
                                // *성능 최적화: Boundary 전체와의 거리가 아니라, Boundary의 정점들과의 거리여야 함*
                                // 요구사항: "도로경계선이 공유하는 도로경계면의 엣지 정점과 일치 하지 않을 때"
                                
                                // Boundary는 MultiLineString일 수 있음 (MultiPolygon인 경우)
                                int geomCount = boundary.GetGeometryCount();
                                if (geomCount == 0) // 단순 Polygon
                                {
                                    if (CheckVertexMatch(boundary, vx, vy, tolerance))
                                    {
                                        isVertexMatched = true;
                                        break;
                                    }
                                }
                                else // MultiPolygon
                                {
                                    for (int k = 0; k < geomCount; k++)
                                    {
                                        using var ring = boundary.GetGeometryRef(k);
                                        if (CheckVertexMatch(ring, vx, vy, tolerance))
                                        {
                                            isVertexMatched = true;
                                            break;
                                        }
                                    }
                                    if (isVertexMatched) break;
                                }
                            }

                            if (!isVertexMatched)
                            {
                                errorPoints.Add((vx, vy));
                                // 하나라도 불일치하면 오류 (요구사항: 모든 정점이 일치해야 함)
                                break; 
                            }
                        }

                        if (errorPoints.Count > 0)
                        {
                            lock (lockObj)
                            {
                                var (errX, errY) = errorPoints[0];
                                using var errPt = new Geometry(wkbGeometryType.wkbPoint);
                                errPt.AddPoint(errX, errY, 0);
                                
                                AddDetailedError(result, config.RuleId ?? "LOG_TOP_REL_023", 
                                    $"도로경계선의 정점이 도로경계면의 정점과 일치하지 않음", 
                                    config.RelatedTableId, lineFeature.GetFID().ToString(), 
                                    $"불일치 정점: ({errX}, {errY})",
                                    geometry: errPt, tableDisplayName: config.RelatedTableName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Feature {lineFeature.GetFID()} 처리 중 오류 발생");
                    }
                    finally
                    {
                        // Feature는 메인 스레드에서 Dispose 하거나 여기서 Dispose (리스트에 담았으므로 여기서 Dispose)
                        lineFeature.Dispose();
                        
                        long current = Interlocked.Increment(ref processedCount);
                        RaiseProgress(onProgress, config.RuleId, CaseType, current, totalCount);
                    }
                });
            }, token);

            // 리소스 정리
            foreach (var p in polygons)
            {
                p.Geometry.Dispose();
            }
        }

        private bool CheckVertexMatch(Geometry ring, double vx, double vy, double tolerance)
        {
            int ringPointCount = ring.GetPointCount();
            for (int j = 0; j < ringPointCount; j++)
            {
                double px = ring.GetX(j);
                double py = ring.GetY(j);

                double distSq = (vx - px) * (vx - px) + (vy - py) * (vy - py);
                if (distSq <= tolerance * tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        private Envelope GetEnvelope(Geometry geom)
        {
            Envelope env = new Envelope();
            geom.GetEnvelope(env);
            return env;
        }

        private class FeatureGeometry
        {
            public Geometry Geometry { get; set; }
            public Envelope Envelope { get; set; }
        }
    }
}

