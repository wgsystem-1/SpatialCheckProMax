#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OSGeo.GDAL;
using OSGeo.OGR;

namespace SpatialCheckProMax.GUI.Services
{
    #region Extensions

    /// <summary>
    /// GDAL Envelope 확장 메서드
    /// </summary>
    internal static class EnvelopeExtensions
    {
        /// <summary>
        /// 다른 Envelope를 병합하여 확장합니다
        /// </summary>
        public static void MergeWith(this Envelope env, Envelope other)
        {
            if (other == null) return;
            
            if (env.MinX > other.MinX) env.MinX = other.MinX;
            if (env.MinY > other.MinY) env.MinY = other.MinY;
            if (env.MaxX < other.MaxX) env.MaxX = other.MaxX;
            if (env.MaxY < other.MaxY) env.MaxY = other.MaxY;
        }
    }

    #endregion

    #region Models

    /// <summary>
    /// 지오메트리 타입별 예상 바이트 크기 프로파일
    /// </summary>
    public static class GeometrySizeProfile
    {
        // Point 계열
        public const int Point = 100;
        public const int MultiPoint = 200;

        // Line 계열
        public const int SimpleLineString = 400;      // 도로, 하천 등
        public const int ComplexLineString = 1500;    // 등고선 (vertex 많음)

        // Polygon 계열
        public const int SimplePolygon = 350;         // 건물 (사각형 위주)
        public const int ComplexPolygon = 800;        // 필지, 행정구역
        public const int LargePolygon = 2000;         // 시군구 경계 등

        /// <summary>
        /// 평균 vertex 수 기반 예상 바이트 계산
        /// </summary>
        public static int EstimateBytes(wkbGeometryType geomType, double avgVertexCount)
        {
            // 기본 오버헤드 + (vertex당 16바이트: X,Y 각 8바이트)
            int baseOverhead = 50;
            int bytesPerVertex = 16;

            var flatType = (wkbGeometryType)((int)geomType & 0xFF);
            int multiplier = flatType switch
            {
                wkbGeometryType.wkbPoint or wkbGeometryType.wkbMultiPoint => 1,
                wkbGeometryType.wkbLineString or wkbGeometryType.wkbMultiLineString => 1,
                wkbGeometryType.wkbPolygon or wkbGeometryType.wkbMultiPolygon => 2, // 링 구조 오버헤드
                _ => 1
            };

            return baseOverhead + (int)(avgVertexCount * bytesPerVertex * multiplier);
        }
    }

    /// <summary>
    /// 레이어 분석 결과
    /// </summary>
    public class LayerAnalysis
    {
        public string Name { get; set; } = string.Empty;
        public string GeometryType { get; set; } = string.Empty;
        public wkbGeometryType GeometryTypeEnum { get; set; }
        public long FeatureCount { get; set; }
        public double AvgVertexCount { get; set; }
        public int EstimatedBytesPerFeature { get; set; }
        public long EstimatedTotalBytes { get; set; }
        public int RecommendedSplitCount { get; set; }
        public double[] Extent { get; set; } = new double[4]; // MinX, MinY, MaxX, MaxY
    }

    /// <summary>
    /// 분할 파일 정보
    /// </summary>
    public class SplitFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public int SplitIndex { get; set; }
        public long FeatureCount { get; set; }
        public long FileSize { get; set; }
        public double[] Extent { get; set; } = new double[4];
    }

    /// <summary>
    /// 레이어 변환 인덱스 정보
    /// </summary>
    public class LayerConvertIndex
    {
        public string LayerName { get; set; } = string.Empty;
        public long TotalFeatures { get; set; }
        public int TotalSplits { get; set; }
        public string GeometryType { get; set; } = string.Empty;
        public double[] TotalExtent { get; set; } = new double[4];
        public DateTime ConvertedAt { get; set; }
        public List<SplitFileInfo> Splits { get; set; } = new();
    }

    /// <summary>
    /// 스마트 변환 옵션
    /// </summary>
    public class SmartConvertOptions
    {
        /// <summary>
        /// 목표 파일 크기 (바이트), 기본값 1.3GB
        /// </summary>
        public long TargetFileSizeBytes { get; set; } = 1_300_000_000L;

        /// <summary>
        /// 최대 파일 크기 (바이트), 기본값 1.8GB (안전 마진)
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 1_800_000_000L;

        /// <summary>
        /// 공간 정렬 사용 여부 (false면 그리드 스트리밍 사용)
        /// </summary>
        public bool UseSpatialOrdering { get; set; } = true;

        /// <summary>
        /// 그리드 기반 스트리밍 분할 사용 (고성능 모드)
        /// </summary>
        public bool UseGridStreaming { get; set; } = false;

        /// <summary>
        /// 인덱스 파일 생성 여부
        /// </summary>
        public bool GenerateIndexFile { get; set; } = true;

        /// <summary>
        /// 수동 분할 수 (0이면 자동)
        /// </summary>
        public int ManualSplitCount { get; set; } = 0;

        /// <summary>
        /// 샘플링 피처 수 (용량 추정용)
        /// </summary>
        public int SampleSize { get; set; } = 100;

        /// <summary>
        /// 선택된 레이어 이름 목록 (null이면 전체)
        /// </summary>
        public List<string>? SelectedLayerNames { get; set; }
    }

    /// <summary>
    /// 스마트 변환 진행 상황
    /// </summary>
    public class SmartConvertProgress
    {
        public double OverallProgress { get; set; }
        public string CurrentLayer { get; set; } = string.Empty;
        public string CurrentPhase { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
        public int ProcessedLayers { get; set; }
        public int TotalLayers { get; set; }
        public int CurrentSplitIndex { get; set; }
        public int TotalSplits { get; set; }
        public long ProcessedFeatures { get; set; }
        public long TotalFeatures { get; set; }
    }

    /// <summary>
    /// 스마트 변환 결과
    /// </summary>
    public class SmartConvertResult
    {
        public bool Success { get; set; }
        public int ConvertedLayers { get; set; }
        public int TotalLayers { get; set; }
        public int TotalFilesCreated { get; set; }
        public long TotalFeaturesConverted { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> FailedLayers { get; set; } = new();
        public List<LayerConvertIndex> LayerIndices { get; set; } = new();
    }

    #endregion

    /// <summary>
    /// 스마트 FileGDB → Shapefile 변환 서비스
    /// 대용량 데이터 자동 분할, 공간 정렬, 용량 모니터링 지원
    /// </summary>
    public class SmartShpConvertService
    {
        private static bool _gdalInitialized;
        private static readonly string[] ExcludedLayerPrefixes = { "ORG_", "QC_" };

        public SmartShpConvertService()
        {
            EnsureGdalInitialized();
        }

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

        #region Layer Analysis

        /// <summary>
        /// FileGDB의 모든 레이어를 분석합니다
        /// </summary>
        public List<LayerAnalysis> AnalyzeLayers(string gdbPath, SmartConvertOptions? options = null)
        {
            options ??= new SmartConvertOptions();
            var analyses = new List<LayerAnalysis>();

            var driver = Ogr.GetDriverByName("OpenFileGDB");
            if (driver == null)
                throw new InvalidOperationException("OpenFileGDB 드라이버를 찾을 수 없습니다.");

            using var dataSource = driver.Open(gdbPath, 0);
            if (dataSource == null)
                throw new InvalidOperationException($"FileGDB를 열 수 없습니다: {gdbPath}");

            var layerCount = dataSource.GetLayerCount();
            for (int i = 0; i < layerCount; i++)
            {
                using var layer = dataSource.GetLayerByIndex(i);
                if (layer == null) continue;

                var layerName = layer.GetName();
                if (IsExcludedLayer(layerName)) continue;

                var analysis = AnalyzeSingleLayer(layer, options.SampleSize);
                analysis.RecommendedSplitCount = CalculateRecommendedSplitCount(
                    analysis.EstimatedTotalBytes, options.TargetFileSizeBytes, analysis.FeatureCount);

                analyses.Add(analysis);
            }

            return analyses;
        }

        /// <summary>
        /// 단일 레이어 분석
        /// </summary>
        private LayerAnalysis AnalyzeSingleLayer(Layer layer, int sampleSize)
        {
            // 피처 수 가져오기 (캐시 우선, 없으면 실제 카운트)
            var featureCount = layer.GetFeatureCount(0);
            if (featureCount < 0)
                featureCount = layer.GetFeatureCount(1);

            var analysis = new LayerAnalysis
            {
                Name = layer.GetName(),
                GeometryTypeEnum = layer.GetGeomType(),
                GeometryType = GetGeometryTypeName(layer.GetGeomType()),
                FeatureCount = featureCount
            };

            // Extent 가져오기 (캐시된 값 사용 - force=0)
            var extent = new Envelope();
            layer.GetExtent(extent, 0);  // 빠름: 캐시된 값 사용
            analysis.Extent = new[] { extent.MinX, extent.MinY, extent.MaxX, extent.MaxY };

            // 샘플링으로 평균 vertex 수 계산
            analysis.AvgVertexCount = CalculateAverageVertexCount(layer, sampleSize);

            // 예상 바이트 계산
            analysis.EstimatedBytesPerFeature = GeometrySizeProfile.EstimateBytes(
                analysis.GeometryTypeEnum, analysis.AvgVertexCount);

            // DBF 필드 오버헤드 추가 (대략 200바이트)
            analysis.EstimatedBytesPerFeature += 200;

            analysis.EstimatedTotalBytes = analysis.FeatureCount * analysis.EstimatedBytesPerFeature;

            return analysis;
        }

        /// <summary>
        /// 샘플링으로 평균 vertex 수 계산 (최적화: 처음 N개만 읽음)
        /// </summary>
        private double CalculateAverageVertexCount(Layer layer, int sampleSize)
        {
            // 캐시된 피처 수 사용 (force=0)
            var featureCount = layer.GetFeatureCount(0);
            if (featureCount <= 0) return 10; // 기본값

            // 처음 sampleSize개만 빠르게 읽음
            long totalVertices = 0;
            int sampledCount = 0;

            layer.ResetReading();

            Feature? feature;
            while ((feature = layer.GetNextFeature()) != null && sampledCount < sampleSize)
            {
                using (feature)
                {
                    var geometry = feature.GetGeometryRef();
                    if (geometry != null)
                    {
                        totalVertices += CountVertices(geometry);
                        sampledCount++;
                    }
                }
            }

            return sampledCount > 0 ? (double)totalVertices / sampledCount : 10;
        }

        /// <summary>
        /// 지오메트리의 vertex 수 계산
        /// </summary>
        private int CountVertices(Geometry geometry)
        {
            if (geometry == null) return 0;

            var geomType = geometry.GetGeometryType();
            var flatType = (wkbGeometryType)((int)geomType & 0xFF);

            return flatType switch
            {
                wkbGeometryType.wkbPoint => 1,
                wkbGeometryType.wkbLineString => geometry.GetPointCount(),
                wkbGeometryType.wkbPolygon => CountPolygonVertices(geometry),
                wkbGeometryType.wkbMultiPoint or
                wkbGeometryType.wkbMultiLineString or
                wkbGeometryType.wkbMultiPolygon or
                wkbGeometryType.wkbGeometryCollection => CountMultiGeometryVertices(geometry),
                _ => geometry.GetPointCount()
            };
        }

        private int CountPolygonVertices(Geometry polygon)
        {
            int total = 0;
            for (int i = 0; i < polygon.GetGeometryCount(); i++)
            {
                var ring = polygon.GetGeometryRef(i);
                if (ring != null)
                    total += ring.GetPointCount();
            }
            return total;
        }

        private int CountMultiGeometryVertices(Geometry multi)
        {
            int total = 0;
            for (int i = 0; i < multi.GetGeometryCount(); i++)
            {
                var part = multi.GetGeometryRef(i);
                if (part != null)
                    total += CountVertices(part);
            }
            return total;
        }

        /// <summary>
        /// 권장 분할 수 계산
        /// </summary>
        private int CalculateRecommendedSplitCount(long estimatedTotalBytes, long targetFileSizeBytes, long featureCount)
        {
            // 목표 크기 자체가 2GB의 75%이므로 추가 안전 계수 불필요
            // 용량 기반 계산
            int splitBySize = 1;
            if (estimatedTotalBytes > targetFileSizeBytes)
            {
                splitBySize = (int)Math.Ceiling((double)estimatedTotalBytes / targetFileSizeBytes);
            }

            // 최소 1, 최대 30
            return Math.Clamp(splitBySize, 1, 30);
        }

        #endregion

        #region Conversion

        /// <summary>
        /// 스마트 변환 수행
        /// </summary>
        public async Task<SmartConvertResult> ConvertAsync(
            string gdbPath,
            string outputPath,
            SmartConvertOptions? options = null,
            IProgress<SmartConvertProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new SmartConvertOptions();

            return await Task.Run(() =>
            {
                var result = new SmartConvertResult { Success = true };

                // 인코딩 설정
                var previousEncoding = Gdal.GetConfigOption("SHAPE_ENCODING", "");
                Gdal.SetConfigOption("SHAPE_ENCODING", "CP949");

                try
                {
                    // Phase 1: 레이어 분석
                    progress?.Report(new SmartConvertProgress
                    {
                        CurrentPhase = "분석",
                        StatusMessage = "레이어 분석 중..."
                    });

                    var analyses = AnalyzeLayers(gdbPath, options);
                    
                    // 선택된 레이어만 필터링
                    if (options.SelectedLayerNames != null && options.SelectedLayerNames.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SmartShpConvert] 선택된 레이어: {string.Join(", ", options.SelectedLayerNames)}");
                        System.Diagnostics.Debug.WriteLine($"[SmartShpConvert] 분석된 레이어: {string.Join(", ", analyses.Select(a => a.Name))}");
                        
                        analyses = analyses.Where(a => options.SelectedLayerNames.Contains(a.Name)).ToList();
                        
                        System.Diagnostics.Debug.WriteLine($"[SmartShpConvert] 필터 후 레이어 수: {analyses.Count}");
                    }
                    
                    if (analyses.Count == 0)
                    {
                        result.Success = false;
                        result.ErrorMessage = "변환 대상 레이어가 없습니다.";
                        return result;
                    }

                    result.TotalLayers = analyses.Count;

                    // 드라이버 준비
                    var srcDriver = Ogr.GetDriverByName("OpenFileGDB");
                    var dstDriver = Ogr.GetDriverByName("ESRI Shapefile");

                    if (srcDriver == null || dstDriver == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = "필요한 GDAL 드라이버를 찾을 수 없습니다.";
                        return result;
                    }

                    using var srcDataSource = srcDriver.Open(gdbPath, 0);
                    if (srcDataSource == null)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"FileGDB를 열 수 없습니다: {gdbPath}";
                        return result;
                    }

                    // Phase 2: 각 레이어 변환
                    for (int layerIdx = 0; layerIdx < analyses.Count; layerIdx++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var analysis = analyses[layerIdx];
                        var splitCount = options.ManualSplitCount > 0
                            ? options.ManualSplitCount
                            : analysis.RecommendedSplitCount;

                        progress?.Report(new SmartConvertProgress
                        {
                            OverallProgress = (double)layerIdx / analyses.Count * 100,
                            CurrentLayer = analysis.Name,
                            CurrentPhase = "변환",
                            StatusMessage = $"변환 중: {analysis.Name} ({layerIdx + 1}/{analyses.Count})",
                            ProcessedLayers = layerIdx,
                            TotalLayers = analyses.Count,
                            TotalSplits = splitCount
                        });

                        try
                        {
                            var layerIndex = ConvertSingleLayer(
                                srcDataSource,
                                dstDriver,
                                analysis,
                                outputPath,
                                splitCount,
                                options,
                                progress,
                                cancellationToken);

                            result.LayerIndices.Add(layerIndex);
                            result.ConvertedLayers++;
                            result.TotalFilesCreated += layerIndex.TotalSplits;
                            result.TotalFeaturesConverted += layerIndex.TotalFeatures;

                            // 레이어 완료 progress 보고
                            progress?.Report(new SmartConvertProgress
                            {
                                OverallProgress = (double)(layerIdx + 1) / analyses.Count * 100,
                                CurrentLayer = analysis.Name,
                                CurrentPhase = "완료",
                                TotalSplits = layerIndex.TotalSplits,
                                StatusMessage = $"{analysis.Name}: {layerIndex.TotalSplits}개 파일 생성 완료",
                                ProcessedLayers = layerIdx + 1,
                                TotalLayers = analyses.Count
                            });

                            // 인덱스 파일 생성
                            if (options.GenerateIndexFile)
                            {
                                SaveIndexFile(outputPath, layerIndex);
                            }
                        }
                        catch (Exception ex)
                        {
                            result.FailedLayers.Add($"{analysis.Name}: {ex.Message}");
                        }
                    }

                    // 완료
                    progress?.Report(new SmartConvertProgress
                    {
                        OverallProgress = 100,
                        CurrentPhase = "완료",
                        StatusMessage = $"완료: {result.ConvertedLayers}개 레이어, {result.TotalFilesCreated}개 파일 생성",
                        ProcessedLayers = analyses.Count,
                        TotalLayers = analyses.Count
                    });

                    if (result.FailedLayers.Count > 0 && result.ConvertedLayers == 0)
                    {
                        result.Success = false;
                        result.ErrorMessage = "모든 레이어 변환에 실패했습니다.";
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
                    Gdal.SetConfigOption("SHAPE_ENCODING",
                        string.IsNullOrEmpty(previousEncoding) ? null : previousEncoding);
                }

                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// 단일 레이어 변환 (분할 포함)
        /// </summary>
        private LayerConvertIndex ConvertSingleLayer(
            DataSource srcDataSource,
            OSGeo.OGR.Driver dstDriver,
            LayerAnalysis analysis,
            string outputPath,
            int splitCount,
            SmartConvertOptions options,
            IProgress<SmartConvertProgress>? progress,
            CancellationToken cancellationToken)
        {
            var index = new LayerConvertIndex
            {
                LayerName = analysis.Name,
                TotalFeatures = analysis.FeatureCount,
                TotalSplits = splitCount,
                GeometryType = analysis.GeometryType,
                TotalExtent = analysis.Extent,
                ConvertedAt = DateTime.Now
            };

            using var srcLayer = srcDataSource.GetLayerByName(analysis.Name);
            if (srcLayer == null)
                throw new InvalidOperationException($"레이어를 찾을 수 없습니다: {analysis.Name}");

            // 분할이 필요 없는 경우에도 스트리밍으로 진행률 보고
            if (splitCount <= 1)
            {
                ConvertSingleFileWithProgress(srcLayer, dstDriver, analysis, outputPath, index, progress, cancellationToken);
                return index;
            }

            // 그리드 기반 스트리밍 (고성능 모드)
            if (options.UseGridStreaming)
            {
                ConvertWithGridStreaming(srcLayer, dstDriver, analysis, outputPath,
                    splitCount, options, index, progress, cancellationToken);
            }
            // 공간 정렬이 필요한 경우 피처를 정렬하여 분할
            else if (options.UseSpatialOrdering)
            {
                ConvertWithSpatialOrdering(srcLayer, dstDriver, analysis, outputPath,
                    splitCount, options, index, progress, cancellationToken);
            }
            else
            {
                ConvertWithSimpleSplit(srcLayer, dstDriver, analysis, outputPath,
                    splitCount, options, index, progress, cancellationToken);
            }

            return index;
        }

        /// <summary>
        /// 분할 없이 변환
        /// </summary>
        /// <summary>
        /// 단일 파일 변환 (진행률 보고 포함)
        /// </summary>
        private void ConvertSingleFileWithProgress(
            Layer srcLayer,
            OSGeo.OGR.Driver dstDriver,
            LayerAnalysis analysis,
            string outputPath,
            LayerConvertIndex index,
            IProgress<SmartConvertProgress>? progress,
            CancellationToken cancellationToken)
        {
            var shpPath = Path.Combine(outputPath, $"{SanitizeFileName(analysis.Name)}.shp");
            DeleteShapefileIfExists(shpPath);

            using var dstDataSource = dstDriver.CreateDataSource(shpPath, null);
            if (dstDataSource == null)
                throw new InvalidOperationException($"Shapefile 생성 실패: {shpPath}");

            var dstLayer = CreateLayerWithSchema(dstDataSource, srcLayer, analysis.Name);
            
            var totalFeatures = srcLayer.GetFeatureCount(1);
            long processedCount = 0;
            int batchSize = 10000;
            int batchCount = 0;
            var currentExtent = new Envelope();

            progress?.Report(new SmartConvertProgress
            {
                CurrentLayer = analysis.Name,
                CurrentPhase = "변환",
                CurrentSplitIndex = 1,
                TotalSplits = 1,
                StatusMessage = $"{analysis.Name}: 변환 시작...",
                TotalFeatures = totalFeatures
            });

            srcLayer.ResetReading();
            Feature? feature;

            while ((feature = srcLayer.GetNextFeature()) != null)
            {
                using (feature)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var geometry = feature.GetGeometryRef();
                    if (geometry == null) continue;

                    var newFeature = new Feature(dstLayer.GetLayerDefn());
                    CopyFeatureFields(feature, newFeature);
                    newFeature.SetGeometry(geometry);
                    dstLayer.CreateFeature(newFeature);
                    newFeature.Dispose();

                    processedCount++;
                    batchCount++;

                    var geomEnv = new Envelope();
                    geometry.GetEnvelope(geomEnv);
                    currentExtent.MergeWith(geomEnv);

                    if (batchCount >= batchSize)
                    {
                        dstDataSource.FlushCache();

                        progress?.Report(new SmartConvertProgress
                        {
                            CurrentLayer = analysis.Name,
                            CurrentPhase = "변환",
                            CurrentSplitIndex = 1,
                            TotalSplits = 1,
                            StatusMessage = $"{analysis.Name}: {processedCount:N0}/{totalFeatures:N0} 피처 처리 중...",
                            ProcessedFeatures = processedCount,
                            TotalFeatures = totalFeatures,
                            OverallProgress = (double)processedCount / totalFeatures * 100
                        });

                        batchCount = 0;
                    }
                }
            }

            dstDataSource.FlushCache();
            CreateCpgFile(shpPath, "EUC-KR");

            var fileSize = File.Exists(shpPath) ? new FileInfo(shpPath).Length : 0;
            index.Splits.Add(new SplitFileInfo
            {
                FileName = Path.GetFileName(shpPath),
                SplitIndex = 1,
                FeatureCount = processedCount,
                FileSize = fileSize,
                Extent = new[] { currentExtent.MinX, currentExtent.MinY, currentExtent.MaxX, currentExtent.MaxY }
            });
            index.TotalSplits = 1;
        }

        private SplitFileInfo ConvertWithoutSplit(Layer srcLayer, OSGeo.OGR.Driver dstDriver, string layerName, string outputPath)
        {
            var shpPath = Path.Combine(outputPath, $"{SanitizeFileName(layerName)}.shp");
            DeleteShapefileIfExists(shpPath);

            using var dstDataSource = dstDriver.CreateDataSource(shpPath, null);
            if (dstDataSource == null)
                throw new InvalidOperationException($"Shapefile 생성 실패: {shpPath}");

            var dstLayer = dstDataSource.CopyLayer(srcLayer, layerName, null);
            if (dstLayer == null)
                throw new InvalidOperationException($"레이어 복사 실패: {layerName}");

            dstDataSource.FlushCache();
            CreateCpgFile(shpPath, "EUC-KR");

            var extent = new Envelope();
            srcLayer.GetExtent(extent, 1);

            return new SplitFileInfo
            {
                FileName = Path.GetFileName(shpPath),
                SplitIndex = 1,
                FeatureCount = srcLayer.GetFeatureCount(1),
                FileSize = new FileInfo(shpPath).Length,
                Extent = new[] { extent.MinX, extent.MinY, extent.MaxX, extent.MaxY }
            };
        }

        #region Grid Streaming (High Performance)

        /// <summary>
        /// 그리드 셀 정보
        /// </summary>
        private class GridCell
        {
            public int Row { get; set; }
            public int Col { get; set; }
            public DataSource? DataSource { get; set; }
            public Layer? Layer { get; set; }
            public string FilePath { get; set; } = string.Empty;
            public long FeatureCount { get; set; }
            public Envelope Extent { get; set; } = new Envelope();
            public int SubIndex { get; set; } = 1;  // 셀 내 추가 분할 시 사용
        }

        /// <summary>
        /// 순차 스트리밍 분할 변환 (고성능)
        /// - 한 번의 스캔으로 피처 수 기반 분할
        /// - 파일명: 레이어명_01.shp, 레이어명_02.shp ...
        /// </summary>
        private void ConvertWithGridStreaming(
            Layer srcLayer,
            OSGeo.OGR.Driver dstDriver,
            LayerAnalysis analysis,
            string outputPath,
            int splitCount,
            SmartConvertOptions options,
            LayerConvertIndex index,
            IProgress<SmartConvertProgress>? progress,
            CancellationToken cancellationToken)
        {
            var totalFeatures = srcLayer.GetFeatureCount(1);
            long processedCount = 0;
            int batchSize = 10000;
            int batchCount = 0;

            // 파일당 피처 수 계산
            long featuresPerFile = totalFeatures / Math.Max(splitCount, 1);
            if (featuresPerFile < 100000) featuresPerFile = 100000;

            // 현재 출력 파일
            int currentFileIndex = 1;
            DataSource? currentDataSource = null;
            Layer? currentLayer = null;
            string currentFilePath = string.Empty;
            long currentFeatureCount = 0;
            var currentExtent = new Envelope();

            progress?.Report(new SmartConvertProgress
            {
                CurrentLayer = analysis.Name,
                CurrentPhase = "스트리밍 변환",
                StatusMessage = $"{analysis.Name}: 스트리밍 변환 시작 (목표 {splitCount}개 파일)...",
                TotalFeatures = totalFeatures
            });

            try
            {
                // 첫 파일 생성
                string fileName = $"{SanitizeFileName(analysis.Name)}_{currentFileIndex:D2}.shp";
                currentFilePath = Path.Combine(outputPath, fileName);
                DeleteShapefileIfExists(currentFilePath);
                
                currentDataSource = dstDriver.CreateDataSource(currentFilePath, null);
                if (currentDataSource == null)
                    throw new InvalidOperationException($"Shapefile 생성 실패: {currentFilePath}");
                currentLayer = CreateLayerWithSchema(currentDataSource, srcLayer, analysis.Name);

                srcLayer.ResetReading();
                Feature? feature;

                while ((feature = srcLayer.GetNextFeature()) != null)
                {
                    using (feature)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var geometry = feature.GetGeometryRef();
                        if (geometry == null) continue;

                        // 파일 분할 필요 시 새 파일 생성
                        if (currentFeatureCount >= featuresPerFile && currentFileIndex < splitCount)
                        {
                            // 현재 파일 마무리
                            currentDataSource.FlushCache();
                            CreateCpgFile(currentFilePath, "EUC-KR");
                            
                            var fileSize = File.Exists(currentFilePath) ? new FileInfo(currentFilePath).Length : 0;
                            index.Splits.Add(new SplitFileInfo
                            {
                                FileName = Path.GetFileName(currentFilePath),
                                SplitIndex = currentFileIndex,
                                FeatureCount = currentFeatureCount,
                                FileSize = fileSize,
                                Extent = new[] { currentExtent.MinX, currentExtent.MinY, currentExtent.MaxX, currentExtent.MaxY }
                            });
                            
                            currentDataSource.Dispose();

                            // 새 파일 생성
                            currentFileIndex++;
                            fileName = $"{SanitizeFileName(analysis.Name)}_{currentFileIndex:D2}.shp";
                            currentFilePath = Path.Combine(outputPath, fileName);
                            DeleteShapefileIfExists(currentFilePath);
                            
                            currentDataSource = dstDriver.CreateDataSource(currentFilePath, null);
                            if (currentDataSource == null)
                                throw new InvalidOperationException($"Shapefile 생성 실패: {currentFilePath}");
                            currentLayer = CreateLayerWithSchema(currentDataSource, srcLayer, analysis.Name);
                            
                            currentFeatureCount = 0;
                            currentExtent = new Envelope();
                        }

                        // 피처 복사
                        if (currentLayer != null)
                        {
                            var newFeature = new Feature(currentLayer.GetLayerDefn());
                            CopyFeatureFields(feature, newFeature);
                            newFeature.SetGeometry(geometry);
                            currentLayer.CreateFeature(newFeature);
                            newFeature.Dispose();

                            currentFeatureCount++;
                            
                            var geomEnv = new Envelope();
                            geometry.GetEnvelope(geomEnv);
                            currentExtent.MergeWith(geomEnv);
                        }

                        processedCount++;
                        batchCount++;

                        if (batchCount >= batchSize)
                        {
                            currentDataSource?.FlushCache();

                            progress?.Report(new SmartConvertProgress
                            {
                                CurrentLayer = analysis.Name,
                                CurrentPhase = "스트리밍 변환",
                                CurrentSplitIndex = currentFileIndex,
                                TotalSplits = splitCount,
                                StatusMessage = $"{analysis.Name}: {processedCount:N0}/{totalFeatures:N0} (파일 {currentFileIndex}/{splitCount})",
                                ProcessedFeatures = processedCount,
                                TotalFeatures = totalFeatures,
                                OverallProgress = (double)processedCount / totalFeatures * 100
                            });

                            batchCount = 0;
                        }
                    }
                }

                // 마지막 파일 마무리
                if (currentDataSource != null && currentFeatureCount > 0)
                {
                    currentDataSource.FlushCache();
                    CreateCpgFile(currentFilePath, "EUC-KR");
                    
                    var fileSize = File.Exists(currentFilePath) ? new FileInfo(currentFilePath).Length : 0;
                    index.Splits.Add(new SplitFileInfo
                    {
                        FileName = Path.GetFileName(currentFilePath),
                        SplitIndex = currentFileIndex,
                        FeatureCount = currentFeatureCount,
                        FileSize = fileSize,
                        Extent = new[] { currentExtent.MinX, currentExtent.MinY, currentExtent.MaxX, currentExtent.MaxY }
                    });
                    
                    currentDataSource.Dispose();
                    currentDataSource = null;
                }

                index.TotalSplits = index.Splits.Count;

                progress?.Report(new SmartConvertProgress
                {
                    CurrentLayer = analysis.Name,
                    CurrentPhase = "완료",
                    TotalSplits = index.Splits.Count,  // 실제 생성된 파일 수
                    StatusMessage = $"{analysis.Name}: {index.Splits.Count}개 파일 생성 완료",
                    ProcessedFeatures = processedCount,
                    TotalFeatures = totalFeatures,
                    OverallProgress = 100
                });
            }
            finally
            {
                currentDataSource?.Dispose();
            }
        }

        #endregion

        /// <summary>
        /// 공간 정렬 기반 분할 변환
        /// </summary>
        private void ConvertWithSpatialOrdering(
            Layer srcLayer,
            OSGeo.OGR.Driver dstDriver,
            LayerAnalysis analysis,
            string outputPath,
            int splitCount,
            SmartConvertOptions options,
            LayerConvertIndex index,
            IProgress<SmartConvertProgress>? progress,
            CancellationToken cancellationToken)
        {
            // Phase 1: 모든 피처의 중심점과 Z-order 값 계산
            var featureOrders = new List<(long fid, ulong zOrder, double x, double y)>();

            srcLayer.ResetReading();
            Feature? feature;

            while ((feature = srcLayer.GetNextFeature()) != null)
            {
                using (feature)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var geometry = feature.GetGeometryRef();
                    if (geometry != null)
                    {
                        var centroid = geometry.Centroid();
                        if (centroid != null)
                        {
                            double x = centroid.GetX(0);
                            double y = centroid.GetY(0);
                            ulong zOrder = CalculateZOrder(x, y, analysis.Extent);
                            featureOrders.Add((feature.GetFID(), zOrder, x, y));
                            centroid.Dispose();
                        }
                    }
                }
            }

            // Phase 2: Z-order로 정렬
            featureOrders.Sort((a, b) => a.zOrder.CompareTo(b.zOrder));

            // Phase 3: 분할하여 저장
            var featuresPerSplit = (int)Math.Ceiling((double)featureOrders.Count / splitCount);
            var currentSplitFeatures = new List<long>();
            int currentSplitIndex = 1;
            DataSource? currentDstDataSource = null;
            Layer? currentDstLayer = null;
            string currentShpPath = "";
            var currentExtent = new Envelope();
            long currentFeatureCount = 0;

            try
            {
                for (int i = 0; i < featureOrders.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 새 분할 파일 시작
                    if (currentDstDataSource == null || currentFeatureCount >= featuresPerSplit)
                    {
                        // 이전 파일 저장
                        if (currentDstDataSource != null)
                        {
                            FinalizeSplitFile(currentDstDataSource, currentShpPath, currentSplitIndex - 1,
                                currentFeatureCount, currentExtent, index);
                            currentDstDataSource.Dispose();
                        }

                        // 새 파일 생성
                        currentShpPath = Path.Combine(outputPath,
                            $"{SanitizeFileName(analysis.Name)}_{currentSplitIndex:D2}.shp");
                        DeleteShapefileIfExists(currentShpPath);

                        currentDstDataSource = dstDriver.CreateDataSource(currentShpPath, null);
                        if (currentDstDataSource == null)
                            throw new InvalidOperationException($"Shapefile 생성 실패: {currentShpPath}");

                        // 레이어 구조 복사
                        currentDstLayer = CreateLayerWithSchema(currentDstDataSource, srcLayer, analysis.Name);
                        currentExtent = new Envelope();
                        currentFeatureCount = 0;
                        currentSplitIndex++;

                        progress?.Report(new SmartConvertProgress
                        {
                            CurrentLayer = analysis.Name,
                            CurrentPhase = "변환",
                            StatusMessage = $"{analysis.Name}: 파일 {currentSplitIndex - 1}/{splitCount} 생성 중...",
                            CurrentSplitIndex = currentSplitIndex - 1,
                            TotalSplits = splitCount,
                            ProcessedFeatures = i,
                            TotalFeatures = featureOrders.Count
                        });
                    }

                    // 피처 복사
                    var (fid, _, x, y) = featureOrders[i];
                    using var srcFeature = srcLayer.GetFeature(fid);
                    if (srcFeature != null && currentDstLayer != null)
                    {
                        var newFeature = new Feature(currentDstLayer.GetLayerDefn());
                        CopyFeatureFields(srcFeature, newFeature);

                        var geom = srcFeature.GetGeometryRef();
                        if (geom != null)
                        {
                            newFeature.SetGeometry(geom);
                            var geomEnvelope = new Envelope();
                            geom.GetEnvelope(geomEnvelope);
                            currentExtent.MergeWith(geomEnvelope);
                        }

                        currentDstLayer.CreateFeature(newFeature);
                        newFeature.Dispose();
                        currentFeatureCount++;
                    }
                }

                // 마지막 파일 저장
                if (currentDstDataSource != null)
                {
                    FinalizeSplitFile(currentDstDataSource, currentShpPath, currentSplitIndex - 1,
                        currentFeatureCount, currentExtent, index);
                    currentDstDataSource.Dispose();
                }
            }
            finally
            {
                currentDstDataSource?.Dispose();
            }
        }

        /// <summary>
        /// 단순 분할 변환 (공간 정렬 없음)
        /// </summary>
        private void ConvertWithSimpleSplit(
            Layer srcLayer,
            OSGeo.OGR.Driver dstDriver,
            LayerAnalysis analysis,
            string outputPath,
            int splitCount,
            SmartConvertOptions options,
            LayerConvertIndex index,
            IProgress<SmartConvertProgress>? progress,
            CancellationToken cancellationToken)
        {
            var totalFeatures = srcLayer.GetFeatureCount(1);
            var featuresPerSplit = (int)Math.Ceiling((double)totalFeatures / splitCount);

            srcLayer.ResetReading();
            Feature? feature;
            int currentSplitIndex = 1;
            long currentFeatureCount = 0;
            long totalProcessed = 0;
            DataSource? currentDstDataSource = null;
            Layer? currentDstLayer = null;
            string currentShpPath = "";
            var currentExtent = new Envelope();

            try
            {
                while ((feature = srcLayer.GetNextFeature()) != null)
                {
                    using (feature)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // 새 분할 파일 시작
                        if (currentDstDataSource == null || currentFeatureCount >= featuresPerSplit)
                        {
                            if (currentDstDataSource != null)
                            {
                                FinalizeSplitFile(currentDstDataSource, currentShpPath, currentSplitIndex - 1,
                                    currentFeatureCount, currentExtent, index);
                                currentDstDataSource.Dispose();
                            }

                            currentShpPath = Path.Combine(outputPath,
                                $"{SanitizeFileName(analysis.Name)}_{currentSplitIndex:D2}.shp");
                            DeleteShapefileIfExists(currentShpPath);

                            currentDstDataSource = dstDriver.CreateDataSource(currentShpPath, null);
                            if (currentDstDataSource == null)
                                throw new InvalidOperationException($"Shapefile 생성 실패: {currentShpPath}");

                            currentDstLayer = CreateLayerWithSchema(currentDstDataSource, srcLayer, analysis.Name);
                            currentExtent = new Envelope();
                            currentFeatureCount = 0;
                            currentSplitIndex++;

                            progress?.Report(new SmartConvertProgress
                            {
                                CurrentLayer = analysis.Name,
                                CurrentPhase = "변환",
                                StatusMessage = $"{analysis.Name}: 파일 {currentSplitIndex - 1}/{splitCount} 생성 중...",
                                CurrentSplitIndex = currentSplitIndex - 1,
                                TotalSplits = splitCount,
                                ProcessedFeatures = totalProcessed,
                                TotalFeatures = totalFeatures
                            });
                        }

                        // 피처 복사
                        if (currentDstLayer != null)
                        {
                            var newFeature = new Feature(currentDstLayer.GetLayerDefn());
                            CopyFeatureFields(feature, newFeature);

                            var geom = feature.GetGeometryRef();
                            if (geom != null)
                            {
                                newFeature.SetGeometry(geom);
                                var geomEnvelope = new Envelope();
                                geom.GetEnvelope(geomEnvelope);
                                currentExtent.MergeWith(geomEnvelope);
                            }

                            currentDstLayer.CreateFeature(newFeature);
                            newFeature.Dispose();
                            currentFeatureCount++;
                        }

                        totalProcessed++;
                    }
                }

                // 마지막 파일 저장
                if (currentDstDataSource != null)
                {
                    FinalizeSplitFile(currentDstDataSource, currentShpPath, currentSplitIndex - 1,
                        currentFeatureCount, currentExtent, index);
                    currentDstDataSource.Dispose();
                }
            }
            finally
            {
                currentDstDataSource?.Dispose();
            }
        }

        /// <summary>
        /// 레이어 스키마 복사하여 새 레이어 생성
        /// </summary>
        private Layer CreateLayerWithSchema(DataSource dataSource, Layer srcLayer, string layerName)
        {
            var srcDefn = srcLayer.GetLayerDefn();
            var geomType = srcLayer.GetGeomType();
            var srs = srcLayer.GetSpatialRef();

            var dstLayer = dataSource.CreateLayer(layerName, srs, geomType, null);
            if (dstLayer == null)
                throw new InvalidOperationException($"레이어 생성 실패: {layerName}");

            // 필드 복사
            for (int i = 0; i < srcDefn.GetFieldCount(); i++)
            {
                var fieldDefn = srcDefn.GetFieldDefn(i);
                
                // Shapefile 필드명 10자 제한
                var fieldName = fieldDefn.GetName();
                if (fieldName.Length > 10)
                    fieldName = fieldName.Substring(0, 10);

                var newFieldDefn = new FieldDefn(fieldName, fieldDefn.GetFieldType());
                newFieldDefn.SetWidth(fieldDefn.GetWidth());
                newFieldDefn.SetPrecision(fieldDefn.GetPrecision());
                dstLayer.CreateField(newFieldDefn, 1);
                newFieldDefn.Dispose();
            }

            return dstLayer;
        }

        /// <summary>
        /// 피처 필드 복사
        /// </summary>
        private void CopyFeatureFields(Feature srcFeature, Feature dstFeature)
        {
            var srcDefn = srcFeature.GetDefnRef();
            var dstDefn = dstFeature.GetDefnRef();

            for (int i = 0; i < srcDefn.GetFieldCount() && i < dstDefn.GetFieldCount(); i++)
            {
                if (!srcFeature.IsFieldSetAndNotNull(i)) continue;

                var fieldType = srcDefn.GetFieldDefn(i).GetFieldType();
                switch (fieldType)
                {
                    case FieldType.OFTInteger:
                        dstFeature.SetField(i, srcFeature.GetFieldAsInteger(i));
                        break;
                    case FieldType.OFTInteger64:
                        dstFeature.SetField(i, srcFeature.GetFieldAsInteger64(i));
                        break;
                    case FieldType.OFTReal:
                        dstFeature.SetField(i, srcFeature.GetFieldAsDouble(i));
                        break;
                    case FieldType.OFTString:
                        dstFeature.SetField(i, srcFeature.GetFieldAsString(i));
                        break;
                    default:
                        dstFeature.SetField(i, srcFeature.GetFieldAsString(i));
                        break;
                }
            }
        }

        /// <summary>
        /// 분할 파일 마무리
        /// </summary>
        private void FinalizeSplitFile(DataSource dataSource, string shpPath, int splitIndex,
            long featureCount, Envelope extent, LayerConvertIndex index)
        {
            dataSource.FlushCache();
            CreateCpgFile(shpPath, "EUC-KR");

            var fileSize = File.Exists(shpPath) ? new FileInfo(shpPath).Length : 0;

            index.Splits.Add(new SplitFileInfo
            {
                FileName = Path.GetFileName(shpPath),
                SplitIndex = splitIndex,
                FeatureCount = featureCount,
                FileSize = fileSize,
                Extent = new[] { extent.MinX, extent.MinY, extent.MaxX, extent.MaxY }
            });
        }

        #endregion

        #region Z-Order Calculation

        /// <summary>
        /// Z-order (Morton code) 계산
        /// </summary>
        private ulong CalculateZOrder(double x, double y, double[] extent)
        {
            // 좌표를 0-65535 범위로 정규화
            double minX = extent[0], minY = extent[1], maxX = extent[2], maxY = extent[3];

            uint normalizedX = (uint)Math.Clamp(
                (x - minX) / (maxX - minX) * 65535, 0, 65535);
            uint normalizedY = (uint)Math.Clamp(
                (y - minY) / (maxY - minY) * 65535, 0, 65535);

            return InterleaveBits(normalizedX, normalizedY);
        }

        /// <summary>
        /// 비트 인터리빙 (Morton code 생성)
        /// </summary>
        private ulong InterleaveBits(uint x, uint y)
        {
            ulong result = 0;
            for (int i = 0; i < 16; i++)
            {
                result |= ((ulong)((x >> i) & 1) << (2 * i));
                result |= ((ulong)((y >> i) & 1) << (2 * i + 1));
            }
            return result;
        }

        #endregion

        #region Utilities

        private static bool IsExcludedLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return true;
            return ExcludedLayerPrefixes.Any(prefix =>
                layerName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

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

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
                fileName = fileName.Replace(c, '_');
            return fileName;
        }

        private static void DeleteShapefileIfExists(string shpPath)
        {
            var basePath = Path.ChangeExtension(shpPath, null);
            var extensions = new[] { ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx",
                ".fbn", ".fbx", ".ain", ".aih", ".atx", ".ixs", ".mxs", ".xml" };

            foreach (var ext in extensions)
            {
                var filePath = basePath + ext;
                if (File.Exists(filePath))
                {
                    try { File.Delete(filePath); }
                    catch { /* ignore */ }
                }
            }
        }

        private static void CreateCpgFile(string shpPath, string encoding)
        {
            try
            {
                var cpgPath = Path.ChangeExtension(shpPath, ".cpg");
                File.WriteAllText(cpgPath, encoding, System.Text.Encoding.ASCII);
            }
            catch { /* ignore */ }
        }

        private void SaveIndexFile(string outputPath, LayerConvertIndex index)
        {
            try
            {
                var indexPath = Path.Combine(outputPath, $"{index.LayerName}_index.json");
                var json = JsonSerializer.Serialize(index, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(indexPath, json, System.Text.Encoding.UTF8);
            }
            catch { /* ignore */ }
        }

        #endregion
    }
}


