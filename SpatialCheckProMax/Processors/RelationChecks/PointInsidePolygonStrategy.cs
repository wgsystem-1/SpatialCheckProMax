using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;

namespace SpatialCheckProMax.Processors.RelationChecks
{
    public class PointInsidePolygonStrategy : BaseRelationCheckStrategy
    {
        public PointInsidePolygonStrategy(ILogger logger) : base(logger)
        {
        }

        public override string CaseType => "PointInsidePolygon";

        public override async Task ExecuteAsync(
            DataSource ds,
            Func<string, Layer?> getLayer,
            ValidationResult result,
            RelationCheckConfig config,
            Action<RelationValidationProgressEventArgs> onProgress,
            CancellationToken token)
        {
            var buld = getLayer(config.MainTableId);
            var ctpt = getLayer(config.RelatedTableId);
            if (buld == null || ctpt == null)
            {
                _logger.LogWarning("Case1: 레이어를 찾을 수 없습니다: {MainTable} 또는 {RelatedTable}", config.MainTableId, config.RelatedTableId);
                return;
            }

            // 필드 필터 적용 (RelatedTableId에만 적용: TN_BULD_CTPT)
            using var _attrFilterRestore = ApplyAttributeFilterIfMatch(ctpt, config.FieldFilter ?? string.Empty);

            _logger.LogInformation("건물중심점 검사 시작 (역 접근법 최적화: 점→건물 순서)");
            var startTime = DateTime.Now;

            // 1단계: 모든 건물 ID 수집 (빠름, O(N))
            var allBuildingIds = new HashSet<long>();
            buld.ResetReading();
            Feature? f;
            while ((f = buld.GetNextFeature()) != null)
            {
                using (f)
                {
                    allBuildingIds.Add(f.GetFID());
                }
            }
            
            _logger.LogInformation("건물 ID 수집 완료: {Count}개", allBuildingIds.Count);

            // 2단계: 점을 순회하며 포함하는 건물 찾기 (역 접근법, O(M log N))
            var buildingsWithPoints = new HashSet<long>();
            var pointsOutsideBuildings = new List<long>();
            
            ctpt.ResetReading();
            var pointCount = ctpt.GetFeatureCount(1);
            var processedPoints = 0;
            
            Feature? pf;
            while ((pf = ctpt.GetNextFeature()) != null)
            {
                token.ThrowIfCancellationRequested();
                processedPoints++;
                
                RaiseProgress(onProgress, config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, 
                    processedPoints, pointCount);
                
                using (pf)
                {
                    var pg = pf.GetGeometryRef();
                    if (pg == null) continue;
                    
                    var pointOid = pf.GetFID();
                    
                    // 점 위치로 공간 필터 설정 (GDAL 내부 공간 인덱스 활용)
                    buld.SetSpatialFilter(pg);
                    
                    bool foundBuilding = false;
                    Feature? bf;
                    while ((bf = buld.GetNextFeature()) != null)
                    {
                        using (bf)
                        {
                            var bg = bf.GetGeometryRef();
                            if (bg != null && (pg.Within(bg) || bg.Contains(pg)))
                            {
                                buildingsWithPoints.Add(bf.GetFID());
                                foundBuilding = true;
                                break; // 하나만 찾으면 충분
                            }
                        }
                    }
                    
                    if (!foundBuilding)
                    {
                        pointsOutsideBuildings.Add(pointOid);
                    }
                    
                    buld.ResetReading();
                }
            }
            
            buld.SetSpatialFilter(null);
            
            _logger.LogInformation("점→건물 검사 완료: 점 {PointCount}개 처리, 건물 매칭 {MatchCount}개", 
                pointCount, buildingsWithPoints.Count);

            // 3단계: 점이 없는 건물 찾기 (집합 차집합, O(N))
            var buildingsWithoutPoints = allBuildingIds.Except(buildingsWithPoints).ToList();
            
            foreach (var bldOid in buildingsWithoutPoints)
            {
                var geometry = GetGeometryByOID(buld, bldOid);
                AddError(result, config.RuleId ?? "LOG_TOP_REL_014", 
                    "건물 내 건물중심점이 없습니다", 
                    config.MainTableId, bldOid.ToString(CultureInfo.InvariantCulture), geometry, config.MainTableName);
                geometry?.Dispose();
            }
            
            // 4단계: 건물 밖 점 오류 추가
            foreach (var ptOid in pointsOutsideBuildings)
            {
                var geometry = GetGeometryByOID(ctpt, ptOid);
                AddError(result, config.RuleId ?? "LOG_TOP_REL_014", 
                    "건물 외부에 건물중심점이 존재합니다", 
                    config.RelatedTableId, ptOid.ToString(CultureInfo.InvariantCulture), geometry, config.RelatedTableName);
                geometry?.Dispose();
            }
            
            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            _logger.LogInformation("건물중심점 검사 완료 (역 접근법 최적화): 건물 {BuildingCount}개, 점 {PointCount}개, " +
                "점 없는 건물 {MissingCount}개, 밖 점 {OutsideCount}개, 소요시간: {Elapsed:F2}초 " +
                "(Union 생성 없음, SetSpatialFilter 활용)", 
                allBuildingIds.Count, pointCount, buildingsWithoutPoints.Count, 
                pointsOutsideBuildings.Count, elapsed);
            
            RaiseProgress(onProgress, config.RuleId ?? string.Empty, config.CaseType ?? string.Empty, 
                pointCount, pointCount, completed: true);
            
            await Task.CompletedTask;
        }
    }
}

