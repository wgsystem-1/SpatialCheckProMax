#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using NetTopologySuite.IO;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using OgrGeometry = OSGeo.OGR.Geometry;

namespace SpatialCheckProMax.Services.IO
{
    /// <summary>
    /// GDAL을 사용하여 GDB 파일에 지오메트리를 업데이트하는 서비스
    /// </summary>
    public interface IGdalGeometryWriter
    {
        /// <summary>
        /// 단일 피처의 지오메트리를 업데이트합니다
        /// </summary>
        /// <param name="gdbPath">GDB 파일 경로</param>
        /// <param name="tableName">테이블 이름</param>
        /// <param name="objectId">피처 ObjectId</param>
        /// <param name="geometry">새 지오메트리 (NTS)</param>
        /// <returns>성공 여부</returns>
        Task<bool> UpdateGeometryAsync(string gdbPath, string tableName, long objectId, NtsGeometry geometry);

        /// <summary>
        /// 여러 피처의 지오메트리를 일괄 업데이트합니다
        /// </summary>
        /// <param name="gdbPath">GDB 파일 경로</param>
        /// <param name="tableName">테이블 이름</param>
        /// <param name="updates">ObjectId와 지오메트리 쌍</param>
        /// <returns>성공/실패 수</returns>
        Task<(int success, int failed)> UpdateGeometriesBatchAsync(string gdbPath, string tableName,
            IEnumerable<(long objectId, NtsGeometry geometry)> updates);

        /// <summary>
        /// FGDB에서 특정 ObjectId의 원본 지오메트리를 읽어옵니다
        /// </summary>
        /// <param name="gdbPath">GDB 파일 경로</param>
        /// <param name="tableName">테이블 이름</param>
        /// <param name="objectId">피처 ObjectId</param>
        /// <returns>NTS 지오메트리 (없으면 null)</returns>
        Task<NtsGeometry?> ReadGeometryAsync(string gdbPath, string tableName, long objectId);
    }

    /// <summary>
    /// GDAL 기반 지오메트리 쓰기 서비스 구현
    /// </summary>
    public class GdalGeometryWriter : IGdalGeometryWriter
    {
        private readonly ILogger<GdalGeometryWriter> _logger;

        public GdalGeometryWriter(ILogger<GdalGeometryWriter> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<bool> UpdateGeometryAsync(string gdbPath, string tableName, long objectId, NtsGeometry geometry)
        {
            return await Task.Run(() =>
            {
                DataSource? dataSource = null;
                Layer? layer = null;
                Feature? feature = null;

                try
                {
                    // OpenFileGDB 드라이버 사용 (오픈소스, GDAL 3.6+ 쓰기 지원)
                    // 참고: ESRI FileGDB SDK 드라이버는 상용 의존성이므로 사용하지 않음
                    var openFileGdbDriver = Ogr.GetDriverByName("OpenFileGDB");
                    if (openFileGdbDriver != null)
                    {
                        dataSource = openFileGdbDriver.Open(gdbPath, 1);
                    }

                    // OpenFileGDB 실패 시 기본 드라이버로 폴백
                    if (dataSource == null)
                    {
                        dataSource = Ogr.Open(gdbPath, 1);
                    }

                    if (dataSource == null)
                    {
                        _logger.LogError("GDB 파일을 열 수 없습니다: {GdbPath}", gdbPath);
                        return false;
                    }

                    layer = dataSource.GetLayerByName(tableName);
                    if (layer == null)
                    {
                        _logger.LogError("테이블을 찾을 수 없습니다: {TableName}", tableName);
                        return false;
                    }

                    // 레이어 기능 확인 (OpenFileGDB는 DeleteFeature만 지원하는 경우가 많음)
                    var supportsSetFeature = layer.TestCapability("SetFeature");
                    var supportsDeleteFeature = layer.TestCapability("DeleteFeature");
                    var supportsCreateFeature = layer.TestCapability("CreateFeature");
                    bool useDeleteAndCreate = !supportsSetFeature && supportsDeleteFeature && supportsCreateFeature;

                    // 레이어의 지오메트리 타입 확인
                    var layerGeomType = layer.GetGeomType();

                    // ObjectId로 피처 가져오기
                    feature = layer.GetFeature(objectId);
                    if (feature == null)
                    {
                        _logger.LogWarning("피처를 찾을 수 없습니다: {TableName} OID={ObjectId}", tableName, objectId);
                        return false;
                    }

                    // 레이어 타입에 맞게 지오메트리 정규화
                    var normalizedGeometry = NormalizeGeometryForLayer(geometry, layerGeomType);
                    if (normalizedGeometry == null)
                    {
                        _logger.LogError("지오메트리 정규화 실패: {TableName} OID={ObjectId}", tableName, objectId);
                        return false;
                    }

                    // NTS Geometry를 OGR Geometry로 변환
                    var ogrGeometry = ConvertToOgrGeometry(normalizedGeometry);
                    if (ogrGeometry == null)
                    {
                        _logger.LogError("지오메트리 변환 실패: {TableName} OID={ObjectId}", tableName, objectId);
                        return false;
                    }

                    bool updateSuccess = false;

                    if (useDeleteAndCreate)
                    {
                        updateSuccess = UpdateByDeleteAndCreate(layer, feature, ogrGeometry, objectId);
                    }
                    else
                    {
                        // SetFeature 시도
                        feature.SetGeometry(ogrGeometry);
                        var result = layer.SetFeature(feature);
                        updateSuccess = (result == 0);

                        if (!updateSuccess)
                        {
                            // 폴백: Delete + Create
                            updateSuccess = UpdateByDeleteAndCreate(layer, feature, ogrGeometry, objectId);
                        }
                    }

                    if (updateSuccess)
                    {
                        // 변경사항 동기화
                        layer.SyncToDisk();
                        dataSource.FlushCache();
                        _logger.LogInformation("지오메트리 업데이트 성공: {TableName} OID={ObjectId}", tableName, objectId);
                    }

                    return updateSuccess;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "지오메트리 업데이트 중 오류: {TableName} OID={ObjectId}", tableName, objectId);
                    return false;
                }
                finally
                {
                    feature?.Dispose();
                    layer?.Dispose();
                    dataSource?.Dispose();
                }
            });
        }

        /// <inheritdoc/>
        public async Task<(int success, int failed)> UpdateGeometriesBatchAsync(string gdbPath, string tableName,
            IEnumerable<(long objectId, NtsGeometry geometry)> updates)
        {
            return await Task.Run(() =>
            {
                int successCount = 0;
                int failedCount = 0;

                DataSource? dataSource = null;
                Layer? layer = null;

                try
                {
                    // OpenFileGDB 드라이버 사용 (오픈소스, GDAL 3.6+ 쓰기 지원)
                    // 참고: ESRI FileGDB SDK 드라이버는 상용 의존성이므로 사용하지 않음
                    var openFileGdbDriver = Ogr.GetDriverByName("OpenFileGDB");
                    if (openFileGdbDriver != null)
                    {
                        dataSource = openFileGdbDriver.Open(gdbPath, 1);
                    }

                    // OpenFileGDB 실패 시 기본 드라이버로 폴백
                    if (dataSource == null)
                    {
                        _logger.LogWarning("OpenFileGDB 드라이버 실패. 기본 드라이버로 시도합니다.");
                        dataSource = Ogr.Open(gdbPath, 1);
                    }

                    if (dataSource == null)
                    {
                        _logger.LogError("GDB 파일을 열 수 없습니다: {GdbPath}", gdbPath);
                        return (0, -1);
                    }

                    var driverName = dataSource.GetDriver().GetName();
                    _logger.LogInformation("GDB 드라이버: {DriverName}", driverName);

                    layer = dataSource.GetLayerByName(tableName);
                    if (layer == null)
                    {
                        _logger.LogError("테이블을 찾을 수 없습니다: {TableName}", tableName);
                        return (0, -1);
                    }

                    // 레이어 기능 확인
                    var supportsSetFeature = layer.TestCapability("SetFeature");
                    var supportsDeleteFeature = layer.TestCapability("DeleteFeature");
                    var supportsCreateFeature = layer.TestCapability("CreateFeature");

                    _logger.LogInformation("레이어 기능 - SetFeature: {Set}, DeleteFeature: {Delete}, CreateFeature: {Create}",
                        supportsSetFeature, supportsDeleteFeature, supportsCreateFeature);

                    // 업데이트 전략 결정
                    bool useDeleteAndCreate = !supportsSetFeature && supportsDeleteFeature && supportsCreateFeature;

                    if (useDeleteAndCreate)
                    {
                        _logger.LogInformation("SetFeature 미지원 - Delete + Create 전략 사용");
                    }
                    else if (!supportsSetFeature)
                    {
                        _logger.LogWarning("이 레이어는 피처 수정을 지원하지 않습니다.");
                    }

                    // 레이어의 지오메트리 타입 확인
                    var layerGeomType = layer.GetGeomType();
                    _logger.LogDebug("레이어 지오메트리 타입: {GeomType}", layerGeomType);

                    foreach (var (objectId, geometry) in updates)
                    {
                        Feature? feature = null;
                        try
                        {
                            feature = layer.GetFeature(objectId);
                            if (feature == null)
                            {
                                _logger.LogWarning("피처를 찾을 수 없습니다: OID={ObjectId}", objectId);
                                failedCount++;
                                continue;
                            }

                            // 레이어 타입에 맞게 지오메트리 변환
                            var normalizedGeometry = NormalizeGeometryForLayer(geometry, layerGeomType);
                            if (normalizedGeometry == null)
                            {
                                _logger.LogWarning("지오메트리 정규화 실패: OID={ObjectId}", objectId);
                                failedCount++;
                                continue;
                            }

                            var ogrGeometry = ConvertToOgrGeometry(normalizedGeometry);
                            if (ogrGeometry == null)
                            {
                                failedCount++;
                                continue;
                            }

                            // 입력/출력 지오메트리 타입 로깅
                            _logger.LogDebug("OID={ObjectId}: 입력={InputType}, 정규화={NormalizedType}, 레이어={LayerType}",
                                objectId, geometry.GeometryType, normalizedGeometry.GeometryType, layerGeomType);

                            bool updateSuccess = false;

                            try
                            {
                                if (useDeleteAndCreate)
                                {
                                    // Delete + Create 전략
                                    updateSuccess = UpdateByDeleteAndCreate(layer, feature, ogrGeometry, objectId);
                                }
                                else
                                {
                                    // SetFeature 시도
                                    feature.SetGeometry(ogrGeometry);
                                    var result = layer.SetFeature(feature);
                                    updateSuccess = (result == 0);

                                    if (!updateSuccess)
                                    {
                                        _logger.LogDebug("SetFeature 실패 (코드: {Result}), Delete+Create 시도", result);
                                        // 폴백: Delete + Create
                                        updateSuccess = UpdateByDeleteAndCreate(layer, feature, ogrGeometry, objectId);
                                    }
                                }
                            }
                            catch (Exception featureEx)
                            {
                                _logger.LogWarning(featureEx, "피처 업데이트 중 예외: OID={ObjectId}, GeomType={GeomType}",
                                    objectId, normalizedGeometry.GeometryType);
                                updateSuccess = false;
                            }

                            if (updateSuccess)
                            {
                                successCount++;
                                _logger.LogDebug("피처 업데이트 성공: OID={ObjectId}", objectId);
                            }
                            else
                            {
                                failedCount++;
                            }
                        }
                        finally
                        {
                            feature?.Dispose();
                        }
                    }

                    // 모든 변경사항 동기화
                    layer.SyncToDisk();

                    // 데이터소스도 플러시
                    dataSource.FlushCache();

                    _logger.LogInformation("일괄 지오메트리 업데이트 완료: {TableName} 성공={Success}, 실패={Failed}",
                        tableName, successCount, failedCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "일괄 지오메트리 업데이트 중 오류: {TableName}", tableName);
                }
                finally
                {
                    layer?.Dispose();
                    dataSource?.Dispose();
                }

                return (successCount, failedCount);
            });
        }

        /// <summary>
        /// Delete + Create 전략으로 피처를 업데이트합니다
        /// </summary>
        private bool UpdateByDeleteAndCreate(Layer layer, Feature originalFeature, OgrGeometry newGeometry, long objectId)
        {
            try
            {
                // 원본 피처의 모든 속성 값 복사를 위해 새 피처 생성
                var newFeature = new Feature(layer.GetLayerDefn());

                // 모든 필드 값 복사
                var fieldCount = originalFeature.GetFieldCount();
                for (int i = 0; i < fieldCount; i++)
                {
                    var fieldDefn = originalFeature.GetFieldDefnRef(i);
                    var fieldType = fieldDefn.GetFieldType();

                    if (!originalFeature.IsFieldSet(i) || originalFeature.IsFieldNull(i))
                    {
                        newFeature.SetFieldNull(i);
                        continue;
                    }

                    switch (fieldType)
                    {
                        case FieldType.OFTInteger:
                            newFeature.SetField(i, originalFeature.GetFieldAsInteger(i));
                            break;
                        case FieldType.OFTInteger64:
                            newFeature.SetField(i, originalFeature.GetFieldAsInteger64(i));
                            break;
                        case FieldType.OFTReal:
                            newFeature.SetField(i, originalFeature.GetFieldAsDouble(i));
                            break;
                        case FieldType.OFTString:
                            newFeature.SetField(i, originalFeature.GetFieldAsString(i));
                            break;
                        case FieldType.OFTDate:
                        case FieldType.OFTDateTime:
                        case FieldType.OFTTime:
                            int year, month, day, hour, minute, tzFlag;
                            float second;
                            originalFeature.GetFieldAsDateTime(i, out year, out month, out day,
                                out hour, out minute, out second, out tzFlag);
                            newFeature.SetField(i, year, month, day, hour, minute, second, tzFlag);
                            break;
                        default:
                            // 기타 타입은 문자열로 복사
                            newFeature.SetField(i, originalFeature.GetFieldAsString(i));
                            break;
                    }
                }

                // 새 지오메트리 설정
                newFeature.SetGeometry(newGeometry);

                // 원본 피처 삭제
                var deleteResult = layer.DeleteFeature(objectId);
                if (deleteResult != 0)
                {
                    _logger.LogWarning("피처 삭제 실패: OID={ObjectId}, 오류코드={Result}", objectId, deleteResult);
                    newFeature.Dispose();
                    return false;
                }

                // 새 피처 생성
                var createResult = layer.CreateFeature(newFeature);
                newFeature.Dispose();

                if (createResult != 0)
                {
                    _logger.LogWarning("새 피처 생성 실패: 원본 OID={ObjectId}, 오류코드={Result}", objectId, createResult);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete+Create 업데이트 실패: OID={ObjectId}", objectId);
                return false;
            }
        }

        /// <summary>
        /// 레이어 타입에 맞게 지오메트리를 정규화합니다
        /// Buffer(0) 적용 후 GeometryCollection이 될 수 있으므로 레이어 타입에 맞게 변환
        /// </summary>
        private NtsGeometry? NormalizeGeometryForLayer(NtsGeometry geometry, wkbGeometryType layerGeomType)
        {
            try
            {
                // 레이어가 Polygon 타입인지 확인
                bool isPolygonLayer = layerGeomType == wkbGeometryType.wkbPolygon ||
                                       layerGeomType == wkbGeometryType.wkbMultiPolygon ||
                                       layerGeomType == wkbGeometryType.wkbPolygon25D ||
                                       layerGeomType == wkbGeometryType.wkbMultiPolygon25D ||
                                       layerGeomType == wkbGeometryType.wkbPolygonM ||
                                       layerGeomType == wkbGeometryType.wkbMultiPolygonM ||
                                       layerGeomType == wkbGeometryType.wkbPolygonZM ||
                                       layerGeomType == wkbGeometryType.wkbMultiPolygonZM;

                bool isLineLayer = layerGeomType == wkbGeometryType.wkbLineString ||
                                   layerGeomType == wkbGeometryType.wkbMultiLineString ||
                                   layerGeomType == wkbGeometryType.wkbLineString25D ||
                                   layerGeomType == wkbGeometryType.wkbMultiLineString25D ||
                                   layerGeomType == wkbGeometryType.wkbLineStringM ||
                                   layerGeomType == wkbGeometryType.wkbMultiLineStringM ||
                                   layerGeomType == wkbGeometryType.wkbLineStringZM ||
                                   layerGeomType == wkbGeometryType.wkbMultiLineStringZM;

                bool isPointLayer = layerGeomType == wkbGeometryType.wkbPoint ||
                                    layerGeomType == wkbGeometryType.wkbMultiPoint ||
                                    layerGeomType == wkbGeometryType.wkbPoint25D ||
                                    layerGeomType == wkbGeometryType.wkbMultiPoint25D ||
                                    layerGeomType == wkbGeometryType.wkbPointM ||
                                    layerGeomType == wkbGeometryType.wkbMultiPointM ||
                                    layerGeomType == wkbGeometryType.wkbPointZM ||
                                    layerGeomType == wkbGeometryType.wkbMultiPointZM;

                // 1. Polygon 레이어에 대한 처리
                if (isPolygonLayer)
                {
                    // 이미 Polygon이면 그대로 반환
                    if (geometry is Polygon)
                        return geometry;

                    // MultiPolygon인 경우 - 단일이면 추출
                    if (geometry is MultiPolygon mp)
                    {
                        if (mp.NumGeometries == 1)
                            return (Polygon)mp.GetGeometryN(0);
                        return mp; // MultiPolygon 그대로
                    }

                    // GeometryCollection에서 Polygon 추출
                    if (geometry is GeometryCollection gc)
                    {
                        var polygons = ExtractPolygons(gc);
                        if (polygons.Count == 1)
                            return polygons[0];
                        if (polygons.Count > 1)
                            return geometry.Factory.CreateMultiPolygon(polygons.ToArray());
                        _logger.LogWarning("GeometryCollection에서 Polygon을 추출할 수 없음");
                        return null;
                    }

                    // 타입 불일치
                    _logger.LogWarning("Polygon 레이어에 {GeomType} 지오메트리 삽입 불가", geometry.GeometryType);
                    return null;
                }

                // 2. LineString 레이어에 대한 처리
                if (isLineLayer)
                {
                    // 이미 LineString이면 그대로 반환
                    if (geometry is LineString)
                        return geometry;

                    // MultiLineString인 경우 - 단일이면 추출
                    if (geometry is MultiLineString mls)
                    {
                        if (mls.NumGeometries == 1)
                            return (LineString)mls.GetGeometryN(0);
                        return mls; // MultiLineString 그대로
                    }

                    // GeometryCollection에서 LineString 추출
                    if (geometry is GeometryCollection gc)
                    {
                        var lines = ExtractLineStrings(gc);
                        if (lines.Count == 1)
                            return lines[0];
                        if (lines.Count > 1)
                            return geometry.Factory.CreateMultiLineString(lines.ToArray());
                        _logger.LogWarning("GeometryCollection에서 LineString을 추출할 수 없음");
                        return null;
                    }

                    // 타입 불일치
                    _logger.LogWarning("LineString 레이어에 {GeomType} 지오메트리 삽입 불가", geometry.GeometryType);
                    return null;
                }

                // 3. Point 레이어에 대한 처리
                if (isPointLayer)
                {
                    // 이미 Point면 그대로 반환
                    if (geometry is Point)
                        return geometry;

                    // MultiPoint인 경우 - 단일이면 추출
                    if (geometry is MultiPoint mpt)
                    {
                        if (mpt.NumGeometries == 1)
                            return (Point)mpt.GetGeometryN(0);
                        return mpt; // MultiPoint 그대로
                    }

                    // GeometryCollection에서 Point 추출
                    if (geometry is GeometryCollection gc)
                    {
                        var points = ExtractPoints(gc);
                        if (points.Count == 1)
                            return points[0];
                        if (points.Count > 1)
                            return geometry.Factory.CreateMultiPoint(points.ToArray());
                        _logger.LogWarning("GeometryCollection에서 Point를 추출할 수 없음");
                        return null;
                    }

                    // 타입 불일치
                    _logger.LogWarning("Point 레이어에 {GeomType} 지오메트리 삽입 불가", geometry.GeometryType);
                    return null;
                }

                // 알 수 없는 레이어 타입 - 원본 반환
                return geometry;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 정규화 실패");
                return geometry; // 실패 시 원본 반환
            }
        }

        /// <summary>
        /// GeometryCollection에서 Polygon 추출
        /// </summary>
        private List<Polygon> ExtractPolygons(GeometryCollection collection)
        {
            var polygons = new List<Polygon>();
            for (int i = 0; i < collection.NumGeometries; i++)
            {
                var geom = collection.GetGeometryN(i);
                if (geom is Polygon p)
                    polygons.Add(p);
                else if (geom is MultiPolygon mp)
                {
                    for (int j = 0; j < mp.NumGeometries; j++)
                        polygons.Add((Polygon)mp.GetGeometryN(j));
                }
            }
            return polygons;
        }

        /// <summary>
        /// GeometryCollection에서 LineString 추출
        /// </summary>
        private List<LineString> ExtractLineStrings(GeometryCollection collection)
        {
            var lines = new List<LineString>();
            for (int i = 0; i < collection.NumGeometries; i++)
            {
                var geom = collection.GetGeometryN(i);
                if (geom is LineString ls)
                    lines.Add(ls);
                else if (geom is MultiLineString mls)
                {
                    for (int j = 0; j < mls.NumGeometries; j++)
                        lines.Add((LineString)mls.GetGeometryN(j));
                }
            }
            return lines;
        }

        /// <summary>
        /// GeometryCollection에서 Point 추출
        /// </summary>
        private List<Point> ExtractPoints(GeometryCollection collection)
        {
            var points = new List<Point>();
            for (int i = 0; i < collection.NumGeometries; i++)
            {
                var geom = collection.GetGeometryN(i);
                if (geom is Point p)
                    points.Add(p);
                else if (geom is MultiPoint mp)
                {
                    for (int j = 0; j < mp.NumGeometries; j++)
                        points.Add((Point)mp.GetGeometryN(j));
                }
            }
            return points;
        }

        /// <summary>
        /// NTS Geometry를 OGR Geometry로 변환합니다
        /// </summary>
        private OgrGeometry? ConvertToOgrGeometry(NtsGeometry ntsGeometry)
        {
            try
            {
                // NTS Geometry를 WKT로 변환
                var wktWriter = new WKTWriter();
                var wkt = wktWriter.Write(ntsGeometry);

                // WKT에서 OGR Geometry 생성
                var ogrGeometry = OgrGeometry.CreateFromWkt(wkt);
                return ogrGeometry;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NTS -> OGR 지오메트리 변환 실패");
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<NtsGeometry?> ReadGeometryAsync(string gdbPath, string tableName, long objectId)
        {
            return await Task.Run(() =>
            {
                DataSource? dataSource = null;
                Layer? layer = null;
                Feature? feature = null;

                try
                {
                    // OpenFileGDB 드라이버 사용 (읽기 전용)
                    var openFileGdbDriver = Ogr.GetDriverByName("OpenFileGDB");
                    if (openFileGdbDriver != null)
                    {
                        dataSource = openFileGdbDriver.Open(gdbPath, 0); // 읽기 전용
                    }

                    // OpenFileGDB 실패 시 기본 드라이버로 폴백
                    if (dataSource == null)
                    {
                        dataSource = Ogr.Open(gdbPath, 0);
                    }

                    if (dataSource == null)
                    {
                        _logger.LogError("GDB 파일을 열 수 없습니다: {GdbPath}", gdbPath);
                        return null;
                    }

                    layer = dataSource.GetLayerByName(tableName);
                    if (layer == null)
                    {
                        _logger.LogError("테이블을 찾을 수 없습니다: {TableName}", tableName);
                        return null;
                    }

                    // ObjectId로 피처 가져오기
                    feature = layer.GetFeature(objectId);
                    if (feature == null)
                    {
                        _logger.LogWarning("피처를 찾을 수 없습니다: {TableName} OID={ObjectId}", tableName, objectId);
                        return null;
                    }

                    // OGR Geometry 가져오기
                    var ogrGeometry = feature.GetGeometryRef();
                    if (ogrGeometry == null || ogrGeometry.IsEmpty())
                    {
                        _logger.LogWarning("피처에 지오메트리가 없습니다: {TableName} OID={ObjectId}", tableName, objectId);
                        return null;
                    }

                    // OGR Geometry -> WKT -> NTS Geometry
                    ogrGeometry.ExportToWkt(out string wkt);
                    if (string.IsNullOrWhiteSpace(wkt))
                    {
                        _logger.LogWarning("WKT 변환 실패: {TableName} OID={ObjectId}", tableName, objectId);
                        return null;
                    }

                    var wktReader = new WKTReader();
                    var ntsGeometry = wktReader.Read(wkt);

                    _logger.LogDebug("지오메트리 읽기 성공: {TableName} OID={ObjectId}, Type={Type}",
                        tableName, objectId, ntsGeometry.GeometryType);

                    return ntsGeometry;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "지오메트리 읽기 중 오류: {TableName} OID={ObjectId}", tableName, objectId);
                    return null;
                }
                finally
                {
                    feature?.Dispose();
                    layer?.Dispose();
                    dataSource?.Dispose();
                }
            });
        }
    }
}
