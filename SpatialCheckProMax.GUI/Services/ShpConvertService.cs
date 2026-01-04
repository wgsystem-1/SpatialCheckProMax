#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OSGeo.GDAL;
using OSGeo.OGR;

namespace SpatialCheckProMax.GUI.Services
{
    /// <summary>
    /// 변환 진행 상황 정보
    /// </summary>
    public class ShpConvertProgress
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
    /// 변환 결과 정보
    /// </summary>
    public class ShpConvertResult
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
    /// 레이어 정보
    /// </summary>
    public class LayerInfo
    {
        /// <summary>
        /// 레이어명
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 지오메트리 타입
        /// </summary>
        public string GeometryType { get; set; } = string.Empty;

        /// <summary>
        /// 피처 수
        /// </summary>
        public long FeatureCount { get; set; }
    }

    /// <summary>
    /// FileGDB → Shapefile 변환 서비스
    /// GDAL/OGR을 사용하여 FileGDB의 모든 레이어를 Shapefile로 변환합니다.
    /// </summary>
    public class ShpConvertService
    {
        /// <summary>
        /// GDAL 드라이버 초기화 여부
        /// </summary>
        private static bool _gdalInitialized;

        /// <summary>
        /// 생성자
        /// </summary>
        public ShpConvertService()
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
        /// 제외할 레이어 접두사 목록
        /// </summary>
        private static readonly string[] ExcludedLayerPrefixes = { "ORG_", "QC_" };

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
        /// FileGDB의 레이어 정보를 조회합니다 (ORG_, QC_ 접두사 레이어 제외)
        /// </summary>
        /// <param name="gdbPath">FileGDB 경로</param>
        /// <returns>레이어 정보 목록</returns>
        public List<LayerInfo> GetLayerInfos(string gdbPath)
        {
            var layerInfos = new List<LayerInfo>();

            var driver = Ogr.GetDriverByName("OpenFileGDB");
            if (driver == null)
            {
                throw new InvalidOperationException("OpenFileGDB 드라이버를 찾을 수 없습니다.");
            }

            using var dataSource = driver.Open(gdbPath, 0);
            if (dataSource == null)
            {
                throw new InvalidOperationException($"FileGDB를 열 수 없습니다: {gdbPath}");
            }

            var layerCount = dataSource.GetLayerCount();
            for (int i = 0; i < layerCount; i++)
            {
                using var layer = dataSource.GetLayerByIndex(i);
                if (layer == null) continue;

                var layerName = layer.GetName();
                
                // 제외 대상 레이어 건너뛰기
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
        /// FileGDB를 Shapefile로 변환합니다
        /// </summary>
        /// <param name="gdbPath">소스 FileGDB 경로</param>
        /// <param name="outputPath">출력 폴더 경로</param>
        /// <param name="progress">진행률 콜백</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>변환 결과</returns>
        public async Task<ShpConvertResult> ConvertAsync(
            string gdbPath,
            string outputPath,
            IProgress<ShpConvertProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var result = new ShpConvertResult { Success = true };

                // 기존 인코딩 설정 백업 및 CP949(한글 Windows)로 설정
                var previousEncoding = Gdal.GetConfigOption("SHAPE_ENCODING", "");
                Gdal.SetConfigOption("SHAPE_ENCODING", "CP949");

                try
                {
                    // 소스 드라이버 (OpenFileGDB: 읽기 전용)
                    var srcDriver = Ogr.GetDriverByName("OpenFileGDB");
                    if (srcDriver == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = "OpenFileGDB 드라이버를 찾을 수 없습니다.";
                        return result;
                    }

                    // 대상 드라이버 (ESRI Shapefile)
                    var dstDriver = Ogr.GetDriverByName("ESRI Shapefile");
                    if (dstDriver == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = "ESRI Shapefile 드라이버를 찾을 수 없습니다.";
                        return result;
                    }

                    // 소스 FileGDB 열기
                    using var srcDataSource = srcDriver.Open(gdbPath, 0);
                    if (srcDataSource == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"FileGDB를 열 수 없습니다: {gdbPath}";
                        return result;
                    }

                    // 변환 대상 레이어 목록 수집 (제외 레이어 필터링)
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
                        result.ErrorMessage = "변환 대상 레이어가 없습니다. (ORG_, QC_ 접두사 레이어는 제외됩니다)";
                        return result;
                    }

                    progress?.Report(new ShpConvertProgress
                    {
                        OverallProgress = 0,
                        StatusMessage = $"총 {targetLayers.Count}개 레이어 변환 시작... (제외: {totalLayerCount - targetLayers.Count}개)",
                        TotalCount = targetLayers.Count
                    });

                    // 각 레이어별 변환
                    for (int idx = 0; idx < targetLayers.Count; idx++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var (layerIndex, layerName) = targetLayers[idx];
                        
                        using var srcLayer = srcDataSource.GetLayerByIndex(layerIndex);
                        if (srcLayer == null) continue;
                        
                        progress?.Report(new ShpConvertProgress
                        {
                            OverallProgress = (double)idx / targetLayers.Count * 100,
                            CurrentLayer = layerName,
                            StatusMessage = $"변환 중: {layerName} ({idx + 1}/{targetLayers.Count})",
                            ProcessedCount = idx,
                            TotalCount = targetLayers.Count
                        });

                        try
                        {
                            // Shapefile 경로 생성
                            var shpPath = Path.Combine(outputPath, $"{SanitizeFileName(layerName)}.shp");

                            // 기존 Shapefile이 있으면 삭제
                            DeleteShapefileIfExists(shpPath);

                            // 새 Shapefile 생성
                            // 참고: SHAPE_ENCODING 전역 설정이 DBF 인코딩을 UTF-8로 지정
                            using var dstDataSource = dstDriver.CreateDataSource(shpPath, null);
                            if (dstDataSource == null)
                            {
                                result.FailedLayers.Add(layerName);
                                result.FailedCount++;
                                continue;
                            }

                            // 레이어 복사
                            var dstLayer = dstDataSource.CopyLayer(srcLayer, layerName, null);
                            if (dstLayer == null)
                            {
                                result.FailedLayers.Add(layerName);
                                result.FailedCount++;
                                continue;
                            }

                            // CopyLayer는 참조를 반환하므로 명시적 Dispose 필요 없음
                            dstDataSource.FlushCache();
                            
                            // CPG 파일 생성 (인코딩 정보 파일)
                            CreateCpgFile(shpPath, "EUC-KR");
                            
                            result.ConvertedCount++;
                        }
                        catch (Exception ex)
                        {
                            result.FailedLayers.Add($"{layerName}: {ex.Message}");
                            result.FailedCount++;
                        }
                    }

                    // 최종 결과
                    progress?.Report(new ShpConvertProgress
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
                        result.ErrorMessage = $"{result.FailedCount}개 레이어 변환 실패: {string.Join(", ", result.FailedLayers)}";
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
                    // 인코딩 설정 복원
                    Gdal.SetConfigOption("SHAPE_ENCODING", string.IsNullOrEmpty(previousEncoding) ? null : previousEncoding);
                }

                return result;
            }, cancellationToken);
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
        /// CPG 파일을 생성합니다 (Shapefile 인코딩 정보 파일)
        /// </summary>
        /// <param name="shpPath">Shapefile 경로</param>
        /// <param name="encoding">인코딩명 (예: UTF-8)</param>
        private static void CreateCpgFile(string shpPath, string encoding)
        {
            try
            {
                var cpgPath = Path.ChangeExtension(shpPath, ".cpg");
                File.WriteAllText(cpgPath, encoding, System.Text.Encoding.ASCII);
            }
            catch
            {
                // CPG 파일 생성 실패 시 무시 (필수 파일이 아님)
            }
        }
    }
}


