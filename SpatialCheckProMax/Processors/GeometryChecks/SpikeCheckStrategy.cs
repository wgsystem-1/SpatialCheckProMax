using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Utils;

namespace SpatialCheckProMax.Processors.GeometryChecks
{
    /// <summary>
    /// 스파이크 검사 전략 (뾰족한 돌출부)
    /// </summary>
    public class SpikeCheckStrategy : BaseGeometryCheckStrategy
    {
        public SpikeCheckStrategy(ILogger<SpikeCheckStrategy> logger) : base(logger)
        {
        }

        public override string CheckType => "Spike";

        public override bool IsEnabled(GeometryCheckConfig config)
        {
            return config.ShouldCheckSpikes;
        }

        public override async Task<List<ValidationError>> ExecuteAsync(
            Layer layer,
            GeometryCheckConfig config,
            GeometryCheckContext context,
            CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();
            var criteria = context.Criteria;

            await Task.Run(() =>
            {
                _logger.LogInformation("스파이크 검사 시작: {TableId}", config.TableId);

                layer.ResetReading();
                Feature? feature;
                int processedCount = 0;

                while ((feature = layer.GetNextFeature()) != null)
                {
                    using (feature)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedCount++;

                        var geometryRef = feature.GetGeometryRef();
                        if (geometryRef == null || geometryRef.IsEmpty()) continue;

                        var fid = feature.GetFID();

                        // 피처 필터링
                        if (context.FeatureFilterService?.ShouldSkipFeature(feature, config.TableId, out _) == true)
                        {
                            continue;
                        }

                        Geometry? workingGeometry = null;
                        try
                        {
                            workingGeometry = CloneAndLinearize(geometryRef);
                            if (workingGeometry == null || workingGeometry.IsEmpty()) continue;

                            workingGeometry.FlattenTo2D();

                            if (HasSpike(workingGeometry, criteria, out string spikeMessage, out double spikeX, out double spikeY))
                            {
                                workingGeometry.ExportToWkt(out string wkt);

                                errors.Add(CreateErrorWithMetadata(
                                    "LOG_TOP_GEO_009",
                                    spikeMessage,
                                    config.TableId,
                                    config.TableName,
                                    fid,
                                    spikeX,
                                    spikeY,
                                    new Dictionary<string, string>
                                    {
                                        ["X"] = spikeX.ToString(),
                                        ["Y"] = spikeY.ToString(),
                                        ["GeometryWkt"] = wkt,
                                        ["OriginalGeometryWKT"] = wkt
                                    }));
                            }
                        }
                        finally
                        {
                            workingGeometry?.Dispose();
                        }

                        // 진행률 로깅
                        if (processedCount % 100 == 0)
                        {
                            context.OnProgress?.Invoke(processedCount, 0);
                        }
                    }
                }

                _logger.LogInformation("스파이크 검사 완료: {TableId}, 오류 {Count}개",
                    config.TableId, errors.Count);
            }, cancellationToken);

            return errors;
        }

        /// <summary>
        /// 스파이크 검출 (뾰족한 돌출부)
        /// </summary>
        private bool HasSpike(Geometry geometry, GeometryCriteria criteria, out string message, out double spikeX, out double spikeY)
        {
            message = string.Empty;
            spikeX = 0;
            spikeY = 0;

            try
            {
                var flattened = WkbFlatten(geometry.GetGeometryType());

                // 멀티폴리곤: 각 폴리곤의 모든 링 검사
                if (flattened == wkbGeometryType.wkbMultiPolygon)
                {
                    var polyCount = geometry.GetGeometryCount();
                    for (int p = 0; p < polyCount; p++)
                    {
                        var polygon = geometry.GetGeometryRef(p);
                        if (polygon == null) continue;
                        if (CheckSpikeInSingleGeometry(polygon, criteria, out message, out spikeX, out spikeY))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                // 폴리곤 또는 기타: 단일 지오메트리 경로
                return CheckSpikeInSingleGeometry(geometry, criteria, out message, out spikeX, out spikeY);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "스파이크 검사 중 오류");
            }

            return false;
        }

        /// <summary>
        /// 단일 지오메트리에서 스파이크 검사
        /// </summary>
        private bool CheckSpikeInSingleGeometry(Geometry geometry, GeometryCriteria criteria, out string message, out double spikeX, out double spikeY)
        {
            message = string.Empty;
            spikeX = 0;
            spikeY = 0;

            var flattened = WkbFlatten(geometry.GetGeometryType());
            var threshold = criteria.SpikeAngleThresholdDegrees > 0 ? criteria.SpikeAngleThresholdDegrees : 10.0;

            // 폴리곤: 각 링 검사
            if (flattened == wkbGeometryType.wkbPolygon)
            {
                var ringCount = geometry.GetGeometryCount();
                for (int r = 0; r < ringCount; r++)
                {
                    var ring = geometry.GetGeometryRef(r);
                    if (ring == null) continue;
                    if (CheckSpikeInLinearRing(ring, threshold, out message, out spikeX, out spikeY))
                    {
                        return true;
                    }
                }
                return false;
            }

            // 링 또는 라인스트링: 직접 검사
            if (flattened == wkbGeometryType.wkbLinearRing || flattened == wkbGeometryType.wkbLineString)
            {
                return CheckSpikeInLinearRing(geometry, threshold, out message, out spikeX, out spikeY);
            }

            return false;
        }

        /// <summary>
        /// LinearRing/LineString에서 스파이크 검사 (폐합 고려 순환 인덱싱)
        /// </summary>
        private bool CheckSpikeInLinearRing(Geometry ring, double thresholdDeg, out string message, out double spikeX, out double spikeY)
        {
            message = string.Empty;
            spikeX = 0;
            spikeY = 0;

            var n = ring.GetPointCount();
            if (n < 3) return false;

            // 폐합 여부 확인 (첫점=마지막점)
            var firstX = ring.GetX(0);
            var firstY = ring.GetY(0);
            var lastX = ring.GetX(n - 1);
            var lastY = ring.GetY(n - 1);
            var closed = (Math.Abs(firstX - lastX) < 1e-9) && (Math.Abs(firstY - lastY) < 1e-9);

            // 중복 마지막점을 제외한 유효 정점 수
            var count = closed ? n - 1 : n;
            if (count < 3) return false;

            // 스파이크 후보 수집
            var spikeCandidates = new List<(int idx, double x, double y, double angle)>();
            (int idx, double x, double y, double angle) best = default;
            double minAngle = double.MaxValue;

            for (int i = 0; i < count; i++)
            {
                int prev = (i - 1 + count) % count;
                int next = (i + 1) % count;

                var x1 = ring.GetX(prev);
                var y1 = ring.GetY(prev);
                var x2 = ring.GetX(i);
                var y2 = ring.GetY(i);
                var x3 = ring.GetX(next);
                var y3 = ring.GetY(next);

                var angle = CalculateAngle(x1, y1, x2, y2, x3, y3);

                // 최소 각도 추적
                if (angle < minAngle)
                {
                    minAngle = angle;
                    best = (i, x2, y2, angle);
                }

                // 임계각도 미만 후보 저장
                if (angle < thresholdDeg)
                {
                    spikeCandidates.Add((i, x2, y2, angle));
                }
            }

            // 결과 확정
            if (spikeCandidates.Any())
            {
                spikeX = best.x;
                spikeY = best.y;
                message = $"스파이크 검출: 정점 {best.idx}번 각도 {best.angle:F1}도";
                return true;
            }

            return false;
        }

        /// <summary>
        /// 세 점으로 이루어진 각도 계산 (도 단위)
        /// </summary>
        private static double CalculateAngle(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            var v1x = x1 - x2;
            var v1y = y1 - y2;
            var v2x = x3 - x2;
            var v2y = y3 - y2;

            var dotProduct = v1x * v2x + v1y * v2y;
            var mag1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            var mag2 = Math.Sqrt(v2x * v2x + v2y * v2y);

            if (mag1 == 0 || mag2 == 0) return 180.0;

            var cosAngle = dotProduct / (mag1 * mag2);
            cosAngle = Math.Max(-1.0, Math.Min(1.0, cosAngle));

            var angleRadians = Math.Acos(cosAngle);
            return angleRadians * 180.0 / Math.PI;
        }
    }
}
