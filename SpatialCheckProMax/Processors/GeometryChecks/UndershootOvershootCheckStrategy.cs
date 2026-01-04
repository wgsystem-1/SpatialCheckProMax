using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetTopologySuite.IO;
using OSGeo.OGR;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Models.Enums;

namespace SpatialCheckProMax.Processors.GeometryChecks
{
    /// <summary>
    /// 언더슛/오버슛 검사 전략 (선형 객체의 네트워크 연결성 검증)
    /// </summary>
    public class UndershootOvershootCheckStrategy : BaseGeometryCheckStrategy
    {
        public UndershootOvershootCheckStrategy(ILogger<UndershootOvershootCheckStrategy> logger) : base(logger)
        {
        }

        public override string CheckType => "UndershootOvershoot";

        public override bool IsEnabled(GeometryCheckConfig config)
        {
            return config.ShouldCheckUndershoot || config.ShouldCheckOvershoot;
        }

        public override async Task<List<ValidationError>> ExecuteAsync(
            Layer layer,
            GeometryCheckConfig config,
            GeometryCheckContext context,
            CancellationToken cancellationToken)
        {
            var errors = new List<ValidationError>();

            // 선형 객체만 검사
            if (!GeometryTypeIsLine(config.GeometryType))
            {
                _logger.LogDebug("선형 객체가 아니므로 언더슛/오버슛 검사 스킵: {GeometryType}", config.GeometryType);
                return errors;
            }

            var searchDistance = context.Criteria.NetworkSearchDistance;

            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("언더슛/오버슛 검사 시작: {TableId}, 검색 거리: {Distance}m",
                        config.TableId, searchDistance);

                    var reader = new WKTReader();
                    var lines = new List<(long Fid, NetTopologySuite.Geometries.LineString Geometry)>();

                    // 1단계: 모든 선형 객체 수집
                    layer.ResetReading();
                    Feature? feature;
                    while ((feature = layer.GetNextFeature()) != null)
                    {
                        using (feature)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // 피처 필터링
                            if (context.FeatureFilterService?.ShouldSkipFeature(feature, config.TableId, out _) == true)
                            {
                                continue;
                            }

                            var geom = feature.GetGeometryRef();
                            if (geom != null && !geom.IsEmpty())
                            {
                                geom.ExportToWkt(out string wkt);
                                var ntsGeom = reader.Read(wkt);

                                // MultiLineString의 경우 각 LineString을 개별 처리
                                if (ntsGeom is NetTopologySuite.Geometries.MultiLineString mls)
                                {
                                    for (int i = 0; i < mls.NumGeometries; i++)
                                    {
                                        var lineString = (NetTopologySuite.Geometries.LineString)mls.GetGeometryN(i);
                                        if (lineString != null && !lineString.IsEmpty)
                                        {
                                            lines.Add((feature.GetFID(), lineString));
                                        }
                                    }
                                }
                                else if (ntsGeom is NetTopologySuite.Geometries.LineString ls)
                                {
                                    if (!ls.IsEmpty)
                                    {
                                        lines.Add((feature.GetFID(), ls));
                                    }
                                }
                            }
                        }
                    }

                    if (lines.Count < 2)
                    {
                        _logger.LogDebug("언더슛/오버슛 검사: 선형 객체가 2개 미만이므로 스킵");
                        return errors;
                    }

                    _logger.LogInformation("언더슛/오버슛 검사: {Count}개 선형 객체 수집 완료", lines.Count);

                    // 2단계: 각 선의 끝점에 대해 연결성 검사
                    int undershootCount = 0;
                    int overshootCount = 0;

                    for (int i = 0; i < lines.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var (fid, line) = lines[i];
                        var startPoint = line.StartPoint;
                        var endPoint = line.EndPoint;
                        var endPoints = new[] { (startPoint, "시작점"), (endPoint, "끝점") };

                        foreach (var (point, pointName) in endPoints)
                        {
                            bool isConnected = false;
                            double minDistance = double.MaxValue;
                            NetTopologySuite.Geometries.LineString? closestLine = null;
                            long closestFid = -1;

                            // 다른 모든 선과의 거리 계산
                            for (int j = 0; j < lines.Count; j++)
                            {
                                if (i == j) continue;

                                var (otherFid, otherLine) = lines[j];
                                var distance = point.Distance(otherLine);

                                if (distance < minDistance)
                                {
                                    minDistance = distance;
                                    closestLine = otherLine;
                                    closestFid = otherFid;
                                }

                                // 연결됨 (허용오차 1mm)
                                if (distance < 0.001)
                                {
                                    isConnected = true;
                                    break;
                                }
                            }

                            // 연결되지 않았고, 검색 거리 내에 다른 선이 있으면 오류
                            if (!isConnected && minDistance < searchDistance && closestLine != null)
                            {
                                // 가장 가까운 점 찾기
                                var nearestPoints = new NetTopologySuite.Operation.Distance.DistanceOp(point, closestLine).NearestPoints();
                                var closestPointOnTarget = new NetTopologySuite.Geometries.Point(nearestPoints[1]);

                                var targetStart = closestLine.StartPoint;
                                var targetEnd = closestLine.EndPoint;

                                // 오버슛: 가장 가까운 점이 대상 선의 끝점인 경우
                                bool isEndpoint = closestPointOnTarget.Distance(targetStart) < 0.001 ||
                                                 closestPointOnTarget.Distance(targetEnd) < 0.001;

                                var errorType = isEndpoint ? "오버슛" : "언더슛";
                                var errorCode = isEndpoint ? "LOG_TOP_GEO_012" : "LOG_TOP_GEO_011";

                                if (isEndpoint)
                                    overshootCount++;
                                else
                                    undershootCount++;

                                // 간격 선분 WKT 생성
                                var gapLineString = new NetTopologySuite.Geometries.LineString(
                                    new[] { point.Coordinate, closestPointOnTarget.Coordinate });
                                string gapLineWkt = gapLineString.ToText();

                                errors.Add(new ValidationError
                                {
                                    ErrorCode = errorCode,
                                    Message = $"{errorType}: {pointName}이 다른 선과 연결되지 않음 (이격거리: {minDistance:F3}m, 대상 FID: {closestFid})",
                                    TableId = config.TableId,
                                    TableName = ResolveTableName(config.TableId, config.TableName),
                                    FeatureId = fid.ToString(),
                                    Severity = ErrorSeverity.Error,
                                    X = point.X,
                                    Y = point.Y,
                                    GeometryWKT = gapLineWkt,
                                    Metadata =
                                    {
                                        ["X"] = point.X.ToString(),
                                        ["Y"] = point.Y.ToString(),
                                        ["Distance"] = minDistance.ToString("F3"),
                                        ["TargetFID"] = closestFid.ToString(),
                                        ["ErrorType"] = errorType,
                                        ["GeometryWkt"] = gapLineWkt
                                    }
                                });

                                // 한 피처당 하나의 오류만 보고 (성능 최적화)
                                break;
                            }
                        }
                    }

                    _logger.LogInformation("언더슛/오버슛 검사 완료: 언더슛 {Undershoot}개, 오버슛 {Overshoot}개, 총 {Total}개",
                        undershootCount, overshootCount, errors.Count);

                    return errors;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "언더슛/오버슛 검사 중 오류 발생");
                    return errors;
                }
            }, cancellationToken);
        }
    }
}
