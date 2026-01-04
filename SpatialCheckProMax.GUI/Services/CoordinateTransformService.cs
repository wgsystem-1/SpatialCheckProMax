#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;

namespace SpatialCheckProMax.GUI.Services
{
    /// <summary>
    /// 좌표 변환 진행 상황 정보
    /// </summary>
    public class CoordinateTransformProgress
    {
        /// <summary>
        /// 전체 진행률 (0-100)
        /// </summary>
        public double OverallProgress { get; set; }

        /// <summary>
        /// 현재 처리 중인 레이어명
        /// </summary>
        public string CurrentLayer { get; set; } = string.Empty;

        /// <summary>
        /// 상태 메시지
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// 처리된 레이어 수
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// 전체 레이어 수
        /// </summary>
        public int TotalCount { get; set; }
    }

    /// <summary>
    /// 좌표 변환 결과 정보
    /// </summary>
    public class CoordinateTransformResult
    {
        /// <summary>
        /// 성공 여부
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 변환된 레이어 수
        /// </summary>
        public int ConvertedCount { get; set; }

        /// <summary>
        /// 실패한 레이어 수
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 실패한 레이어 목록
        /// </summary>
        public List<string> FailedLayers { get; set; } = new();
    }

    /// <summary>
    /// 좌표계 정보
    /// </summary>
    public class CrsInfo
    {
        /// <summary>
        /// EPSG 코드
        /// </summary>
        public int? Epsg { get; set; }

        /// <summary>
        /// 좌표계 이름
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// WKT 문자열
        /// </summary>
        public string? Wkt { get; set; }
    }

    /// <summary>
    /// 좌표 변환 서비스
    /// GDAL/OGR을 사용하여 좌표계 변환을 수행합니다.
    /// </summary>
    public class CoordinateTransformService
    {
        /// <summary>
        /// 제외할 레이어 접두사 목록
        /// </summary>
        private static readonly string[] ExcludedLayerPrefixes = { "ORG_", "QC_" };

        /// <summary>
        /// GDAL 드라이버 초기화 여부
        /// </summary>
        private static bool _gdalInitialized;

        /// <summary>
        /// 생성자
        /// </summary>
        public CoordinateTransformService()
        {
            EnsureGdalInitialized();
        }

        /// <summary>
        /// GDAL 초기화
        /// </summary>
        private static void EnsureGdalInitialized()
        {
            if (_gdalInitialized) return;

            try
            {
                Gdal.AllRegister();
                Ogr.RegisterAll();
                _gdalInitialized = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"GDAL 초기화 실패: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 레이어가 제외 대상인지 확인합니다
        /// </summary>
        private static bool IsExcludedLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return true;
            
            foreach (var prefix in ExcludedLayerPrefixes)
            {
                if (layerName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 소스 데이터의 좌표계를 감지합니다
        /// </summary>
        public CrsInfo DetectCrs(string sourcePath)
        {
            var crsInfo = new CrsInfo();

            var driver = GetReadDriver(sourcePath);
            if (driver == null)
            {
                throw new InvalidOperationException("적합한 드라이버를 찾을 수 없습니다.");
            }

            using var dataSource = driver.Open(sourcePath, 0);
            if (dataSource == null)
            {
                throw new InvalidOperationException($"데이터소스를 열 수 없습니다: {sourcePath}");
            }

            // 첫 번째 레이어의 좌표계 확인
            for (int i = 0; i < dataSource.GetLayerCount(); i++)
            {
                using var layer = dataSource.GetLayerByIndex(i);
                if (layer == null) continue;
                
                var layerName = layer.GetName();
                if (IsExcludedLayer(layerName)) continue;

                var spatialRef = layer.GetSpatialRef();
                if (spatialRef != null)
                {
                    // EPSG 코드 추출 시도
                    var authorityCode = spatialRef.GetAuthorityCode(null);
                    if (!string.IsNullOrEmpty(authorityCode) && int.TryParse(authorityCode, out int epsg))
                    {
                        crsInfo.Epsg = epsg;
                    }

                    // 좌표계 이름
                    crsInfo.Name = spatialRef.GetName() ?? spatialRef.GetAttrValue("PROJCS", 0) ?? spatialRef.GetAttrValue("GEOGCS", 0) ?? "Unknown";
                    
                    // WKT
                    spatialRef.ExportToWkt(out string wkt, null);
                    crsInfo.Wkt = wkt;

                    break;
                }
            }

            return crsInfo;
        }

        /// <summary>
        /// 레이어 정보를 조회합니다
        /// </summary>
        public List<LayerInfo> GetLayerInfos(string sourcePath)
        {
            var layerInfos = new List<LayerInfo>();

            var driver = GetReadDriver(sourcePath);
            if (driver == null)
            {
                throw new InvalidOperationException("적합한 드라이버를 찾을 수 없습니다.");
            }

            using var dataSource = driver.Open(sourcePath, 0);
            if (dataSource == null)
            {
                throw new InvalidOperationException($"데이터소스를 열 수 없습니다: {sourcePath}");
            }

            var layerCount = dataSource.GetLayerCount();
            for (int i = 0; i < layerCount; i++)
            {
                using var layer = dataSource.GetLayerByIndex(i);
                if (layer == null) continue;

                var layerName = layer.GetName();
                if (IsExcludedLayer(layerName)) continue;

                var geomType = layer.GetGeomType();
                var geomTypeName = GetGeometryTypeName(geomType);

                layerInfos.Add(new LayerInfo
                {
                    Name = layerName,
                    GeometryType = geomTypeName,
                    FeatureCount = layer.GetFeatureCount(1)
                });
            }

            return layerInfos;
        }

        /// <summary>
        /// 좌표 변환을 수행합니다
        /// </summary>
        public async Task<CoordinateTransformResult> TransformAsync(
            string sourcePath,
            string outputPath,
            int sourceEpsg,
            int targetEpsg,
            bool outputAsGdb,
            IProgress<CoordinateTransformProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var result = new CoordinateTransformResult { Success = true };

                // Shapefile 출력 시 인코딩 설정
                string? previousEncoding = null;
                if (!outputAsGdb)
                {
                    previousEncoding = Gdal.GetConfigOption("SHAPE_ENCODING", "");
                    Gdal.SetConfigOption("SHAPE_ENCODING", "CP949");
                }

                try
                {
                    // 소스 드라이버
                    var srcDriver = GetReadDriver(sourcePath);
                    if (srcDriver == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = "소스 데이터용 드라이버를 찾을 수 없습니다.";
                        return result;
                    }

                    // 대상 드라이버
                    // OpenFileGDB 드라이버는 GDAL 3.6+ 에서 쓰기 지원
                    var dstDriverName = outputAsGdb ? "OpenFileGDB" : "ESRI Shapefile";
                    var dstDriver = Ogr.GetDriverByName(dstDriverName);
                    if (dstDriver == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"{dstDriverName} 드라이버를 찾을 수 없습니다.";
                        return result;
                    }
                    
                    // OpenFileGDB 쓰기 지원 확인 (GDAL 3.6+)
                    if (outputAsGdb && !dstDriver.TestCapability("CreateDataSource"))
                    {
                        result.Success = false;
                        result.ErrorMessage = "현재 GDAL 버전이 FileGDB 쓰기를 지원하지 않습니다. Shapefile로 출력하거나 GDAL 3.6 이상으로 업그레이드하세요.";
                        return result;
                    }

                    // 소스 데이터소스 열기
                    using var srcDataSource = srcDriver.Open(sourcePath, 0);
                    if (srcDataSource == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"소스 데이터를 열 수 없습니다: {sourcePath}";
                        return result;
                    }

                    // 변환 대상 레이어 수집
                    var targetLayers = new List<(int Index, string Name)>();
                    var totalLayerCount = srcDataSource.GetLayerCount();

                    for (int i = 0; i < totalLayerCount; i++)
                    {
                        using var layer = srcDataSource.GetLayerByIndex(i);
                        if (layer == null) continue;

                        var name = layer.GetName();
                        if (!IsExcludedLayer(name))
                        {
                            targetLayers.Add((i, name));
                        }
                    }

                    if (targetLayers.Count == 0)
                    {
                        result.Success = false;
                        result.ErrorMessage = "변환 대상 레이어가 없습니다.";
                        return result;
                    }

                    // 대상 좌표계 생성
                    // GDAL 3.0 이후 축 순서 문제 방지: 전통적인 GIS 순서(X=경도/Easting, Y=위도/Northing) 사용
                    var dstSrs = new SpatialReference("");
                    dstSrs.ImportFromEPSG(targetEpsg);
                    dstSrs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);

                    // 대상 데이터소스 생성
                    DataSource? dstDataSource = null;
                    
                    if (outputAsGdb)
                    {
                        // FileGDB 출력
                        if (Directory.Exists(outputPath))
                        {
                            Directory.Delete(outputPath, true);
                        }
                        dstDataSource = dstDriver.CreateDataSource(outputPath, null);
                    }

                    progress?.Report(new CoordinateTransformProgress
                    {
                        OverallProgress = 0,
                        StatusMessage = $"총 {targetLayers.Count}개 레이어 변환 시작...",
                        TotalCount = targetLayers.Count
                    });

                    // 각 레이어별 변환
                    for (int idx = 0; idx < targetLayers.Count; idx++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var (layerIndex, layerName) = targetLayers[idx];

                        using var srcLayer = srcDataSource.GetLayerByIndex(layerIndex);
                        if (srcLayer == null) continue;

                        progress?.Report(new CoordinateTransformProgress
                        {
                            OverallProgress = (double)idx / targetLayers.Count * 100,
                            CurrentLayer = layerName,
                            StatusMessage = $"변환 중: {layerName} ({idx + 1}/{targetLayers.Count})",
                            ProcessedCount = idx,
                            TotalCount = targetLayers.Count
                        });

                        try
                        {
                            // 소스 좌표계
                            var srcSrs = srcLayer.GetSpatialRef();
                            if (srcSrs == null)
                            {
                                srcSrs = new SpatialReference("");
                                srcSrs.ImportFromEPSG(sourceEpsg);
                            }
                            // GDAL 3.0 이후 축 순서 문제 방지
                            srcSrs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);

                            // 좌표 변환 객체 생성
                            var coordTransform = new CoordinateTransformation(srcSrs, dstSrs);

                            if (outputAsGdb)
                            {
                                // FileGDB로 레이어 복사 및 변환
                                TransformLayerToGdb(srcLayer, dstDataSource!, layerName, dstSrs, coordTransform, result);
                            }
                            else
                            {
                                // Shapefile로 레이어 복사 및 변환
                                TransformLayerToShapefile(srcLayer, dstDriver, outputPath, layerName, dstSrs, coordTransform, result);
                            }
                        }
                        catch (Exception ex)
                        {
                            result.FailedLayers.Add($"{layerName}: {ex.Message}");
                            result.FailedCount++;
                        }
                    }

                    dstDataSource?.FlushCache();
                    dstDataSource?.Dispose();

                    // 최종 결과
                    progress?.Report(new CoordinateTransformProgress
                    {
                        OverallProgress = 100,
                        StatusMessage = $"완료: {result.ConvertedCount}개 성공, {result.FailedCount}개 실패",
                        ProcessedCount = targetLayers.Count,
                        TotalCount = targetLayers.Count
                    });

                    if (result.FailedCount > 0 && result.ConvertedCount == 0)
                    {
                        result.Success = false;
                        result.ErrorMessage = "모든 레이어 변환에 실패했습니다.";
                    }
                    else if (result.FailedCount > 0)
                    {
                        result.ErrorMessage = $"{result.FailedCount}개 레이어 변환 실패";
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"변환 중 오류 발생: {ex.Message}";
                }
                finally
                {
                    // Shapefile 인코딩 복원
                    if (!outputAsGdb)
                    {
                        Gdal.SetConfigOption("SHAPE_ENCODING", string.IsNullOrEmpty(previousEncoding) ? null : previousEncoding);
                    }
                }

                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// 레이어를 FileGDB로 변환합니다
        /// </summary>
        private void TransformLayerToGdb(
            Layer srcLayer,
            DataSource dstDataSource,
            string layerName,
            SpatialReference dstSrs,
            CoordinateTransformation coordTransform,
            CoordinateTransformResult result)
        {
            var geomType = srcLayer.GetGeomType();
            
            // 새 레이어 생성
            var dstLayer = dstDataSource.CreateLayer(layerName, dstSrs, geomType, null);
            if (dstLayer == null)
            {
                result.FailedLayers.Add(layerName);
                result.FailedCount++;
                return;
            }

            // 필드 정의 복사
            var srcLayerDefn = srcLayer.GetLayerDefn();
            for (int i = 0; i < srcLayerDefn.GetFieldCount(); i++)
            {
                var fieldDefn = srcLayerDefn.GetFieldDefn(i);
                dstLayer.CreateField(fieldDefn, 1);
            }

            // 피처 복사 및 변환
            srcLayer.ResetReading();
            Feature? srcFeature;
            while ((srcFeature = srcLayer.GetNextFeature()) != null)
            {
                using (srcFeature)
                {
                    using var dstFeature = new Feature(dstLayer.GetLayerDefn());
                    
                    // 속성 복사
                    for (int i = 0; i < srcLayerDefn.GetFieldCount(); i++)
                    {
                        dstFeature.SetField(i, srcFeature.GetFieldAsString(i));
                    }

                    // 지오메트리 변환
                    var srcGeom = srcFeature.GetGeometryRef();
                    if (srcGeom != null)
                    {
                        var dstGeom = srcGeom.Clone();
                        dstGeom.Transform(coordTransform);
                        dstFeature.SetGeometry(dstGeom);
                    }

                    dstLayer.CreateFeature(dstFeature);
                }
            }

            result.ConvertedCount++;
        }

        /// <summary>
        /// 레이어를 Shapefile로 변환합니다
        /// </summary>
        private void TransformLayerToShapefile(
            Layer srcLayer,
            OSGeo.OGR.Driver dstDriver,
            string outputPath,
            string layerName,
            SpatialReference dstSrs,
            CoordinateTransformation coordTransform,
            CoordinateTransformResult result)
        {
            var geomType = srcLayer.GetGeomType();
            var shpPath = Path.Combine(outputPath, $"{SanitizeFileName(layerName)}.shp");

            // 기존 파일 삭제
            DeleteShapefileIfExists(shpPath);

            // 새 Shapefile 생성
            using var dstDataSource = dstDriver.CreateDataSource(shpPath, null);
            if (dstDataSource == null)
            {
                result.FailedLayers.Add(layerName);
                result.FailedCount++;
                return;
            }

            var dstLayer = dstDataSource.CreateLayer(layerName, dstSrs, geomType, null);
            if (dstLayer == null)
            {
                result.FailedLayers.Add(layerName);
                result.FailedCount++;
                return;
            }

            // 필드 정의 복사
            var srcLayerDefn = srcLayer.GetLayerDefn();
            for (int i = 0; i < srcLayerDefn.GetFieldCount(); i++)
            {
                var fieldDefn = srcLayerDefn.GetFieldDefn(i);
                dstLayer.CreateField(fieldDefn, 1);
            }

            // 피처 복사 및 변환
            srcLayer.ResetReading();
            Feature? srcFeature;
            while ((srcFeature = srcLayer.GetNextFeature()) != null)
            {
                using (srcFeature)
                {
                    using var dstFeature = new Feature(dstLayer.GetLayerDefn());
                    
                    // 속성 복사
                    for (int i = 0; i < srcLayerDefn.GetFieldCount(); i++)
                    {
                        dstFeature.SetField(i, srcFeature.GetFieldAsString(i));
                    }

                    // 지오메트리 변환
                    var srcGeom = srcFeature.GetGeometryRef();
                    if (srcGeom != null)
                    {
                        var dstGeom = srcGeom.Clone();
                        dstGeom.Transform(coordTransform);
                        dstFeature.SetGeometry(dstGeom);
                    }

                    dstLayer.CreateFeature(dstFeature);
                }
            }

            dstDataSource.FlushCache();
            
            // CPG 파일 생성
            CreateCpgFile(shpPath, "EUC-KR");
            
            result.ConvertedCount++;
        }

        /// <summary>
        /// 소스 경로에 맞는 읽기 드라이버를 반환합니다
        /// </summary>
        private static OSGeo.OGR.Driver? GetReadDriver(string sourcePath)
        {
            if (sourcePath.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
            {
                return Ogr.GetDriverByName("OpenFileGDB");
            }
            else if (Directory.Exists(sourcePath) && Directory.GetFiles(sourcePath, "*.shp").Length > 0)
            {
                return Ogr.GetDriverByName("ESRI Shapefile");
            }
            else if (File.Exists(sourcePath) && sourcePath.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
            {
                return Ogr.GetDriverByName("ESRI Shapefile");
            }
            
            return null;
        }

        /// <summary>
        /// 지오메트리 타입 이름 반환
        /// </summary>
        private static string GetGeometryTypeName(wkbGeometryType geomType)
        {
            var flatType = (wkbGeometryType)((int)geomType & 0xFF);
            return flatType switch
            {
                wkbGeometryType.wkbPoint => "Point",
                wkbGeometryType.wkbMultiPoint => "MultiPoint",
                wkbGeometryType.wkbLineString => "LineString",
                wkbGeometryType.wkbMultiLineString => "MultiLineString",
                wkbGeometryType.wkbPolygon => "Polygon",
                wkbGeometryType.wkbMultiPolygon => "MultiPolygon",
                wkbGeometryType.wkbGeometryCollection => "GeometryCollection",
                wkbGeometryType.wkbNone => "None",
                wkbGeometryType.wkbUnknown => "Unknown",
                _ => geomType.ToString()
            };
        }

        /// <summary>
        /// 파일명에 사용할 수 없는 문자를 제거합니다
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }

        /// <summary>
        /// Shapefile 관련 파일들을 삭제합니다
        /// </summary>
        private static void DeleteShapefileIfExists(string shpPath)
        {
            var basePath = Path.ChangeExtension(shpPath, null);
            var extensions = new[] { ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx", ".fbn", ".fbx", ".ain", ".aih", ".atx", ".ixs", ".mxs", ".xml" };

            foreach (var ext in extensions)
            {
                var filePath = basePath + ext;
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch
                    {
                        // 삭제 실패 시 무시
                    }
                }
            }
        }

        /// <summary>
        /// CPG 파일을 생성합니다
        /// </summary>
        private static void CreateCpgFile(string shpPath, string encoding)
        {
            try
            {
                var cpgPath = Path.ChangeExtension(shpPath, ".cpg");
                File.WriteAllText(cpgPath, encoding, System.Text.Encoding.ASCII);
            }
            catch
            {
                // CPG 파일 생성 실패 시 무시
            }
        }
    }
}


