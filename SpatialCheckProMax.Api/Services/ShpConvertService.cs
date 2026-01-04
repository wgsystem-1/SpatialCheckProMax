#nullable enable
using System.Text.Json;
using OSGeo.GDAL;
using OSGeo.OGR;
using SpatialCheckProMax.Api.Models;

namespace SpatialCheckProMax.Api.Services;

#region Internal Models

/// <summary>
/// GDAL Envelope 확장 메서드
/// </summary>
internal static class EnvelopeExtensions
{
    public static void MergeWith(this Envelope env, Envelope other)
    {
        if (other == null) return;
        if (env.MinX > other.MinX) env.MinX = other.MinX;
        if (env.MinY > other.MinY) env.MinY = other.MinY;
        if (env.MaxX < other.MaxX) env.MaxX = other.MaxX;
        if (env.MaxY < other.MaxY) env.MaxY = other.MaxY;
    }
}

/// <summary>
/// 지오메트리 타입별 예상 바이트 크기 프로파일
/// </summary>
internal static class GeometrySizeProfile
{
    public const int Point = 100;
    public const int MultiPoint = 200;
    public const int SimpleLineString = 400;
    public const int ComplexLineString = 1500;
    public const int SimplePolygon = 350;
    public const int ComplexPolygon = 800;
    public const int LargePolygon = 2000;

    public static int EstimateBytes(wkbGeometryType geomType, double avgVertexCount)
    {
        int baseOverhead = 50;
        int bytesPerVertex = 16;

        var flatType = (wkbGeometryType)((int)geomType & 0xFF);
        int multiplier = flatType switch
        {
            wkbGeometryType.wkbPoint or wkbGeometryType.wkbMultiPoint => 1,
            wkbGeometryType.wkbLineString or wkbGeometryType.wkbMultiLineString => 1,
            wkbGeometryType.wkbPolygon or wkbGeometryType.wkbMultiPolygon => 2,
            _ => 1
        };

        return baseOverhead + (int)(avgVertexCount * bytesPerVertex * multiplier);
    }
}

/// <summary>
/// 내부 레이어 분석 결과
/// </summary>
internal class LayerAnalysis
{
    public string Name { get; set; } = string.Empty;
    public string GeometryType { get; set; } = string.Empty;
    public wkbGeometryType GeometryTypeEnum { get; set; }
    public long FeatureCount { get; set; }
    public double AvgVertexCount { get; set; }
    public int EstimatedBytesPerFeature { get; set; }
    public long EstimatedTotalBytes { get; set; }
    public int RecommendedSplitCount { get; set; }
    public double[] Extent { get; set; } = new double[4];
}

/// <summary>
/// 분할 파일 정보
/// </summary>
internal class SplitFileInfo
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
internal class LayerConvertIndex
{
    public string LayerName { get; set; } = string.Empty;
    public long TotalFeatures { get; set; }
    public int TotalSplits { get; set; }
    public string GeometryType { get; set; } = string.Empty;
    public double[] TotalExtent { get; set; } = new double[4];
    public DateTime ConvertedAt { get; set; }
    public List<SplitFileInfo> Splits { get; set; } = new();
}

#endregion

/// <summary>
/// SHP 변환 서비스 인터페이스
/// </summary>
public interface IShpConvertService
{
    AnalyzeResponse AnalyzeGdb(string gdbPath, int sampleSize = 100);
    Task ExecuteConvertAsync(string jobId, ConvertRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// FileGDB → Shapefile 변환 서비스
/// </summary>
public class ShpConvertService : IShpConvertService
{
    private static bool _gdalInitialized;
    private static readonly object _initLock = new();
    private static readonly string[] ExcludedLayerPrefixes = { "ORG_", "QC_" };

    private readonly IJobManager _jobManager;
    private readonly ILogger<ShpConvertService> _logger;

    public ShpConvertService(IJobManager jobManager, ILogger<ShpConvertService> logger)
    {
        _jobManager = jobManager;
        _logger = logger;
        EnsureGdalInitialized();
    }

    private static void EnsureGdalInitialized()
    {
        if (_gdalInitialized) return;
        lock (_initLock)
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
    }

    #region Public Methods

    public AnalyzeResponse AnalyzeGdb(string gdbPath, int sampleSize = 100)
    {
        var response = new AnalyzeResponse();

        try
        {
            var driver = Ogr.GetDriverByName("OpenFileGDB");
            if (driver == null)
            {
                response.ErrorMessage = "OpenFileGDB 드라이버를 찾을 수 없습니다.";
                return response;
            }

            using var dataSource = driver.Open(gdbPath, 0);
            if (dataSource == null)
            {
                response.ErrorMessage = $"FileGDB를 열 수 없습니다: {gdbPath}";
                return response;
            }

            var layerCount = dataSource.GetLayerCount();
            long totalBytes = 0;

            for (int i = 0; i < layerCount; i++)
            {
                using var layer = dataSource.GetLayerByIndex(i);
                if (layer == null) continue;

                var layerName = layer.GetName();
                if (IsExcludedLayer(layerName)) continue;

                var analysis = AnalyzeSingleLayer(layer, sampleSize);
                
                response.Layers.Add(new LayerAnalysisResponse
                {
                    Name = analysis.Name,
                    GeometryType = analysis.GeometryType,
                    FeatureCount = analysis.FeatureCount,
                    AvgVertexCount = Math.Round(analysis.AvgVertexCount, 2),
                    EstimatedTotalBytes = analysis.EstimatedTotalBytes,
                    EstimatedSize = FormatBytes(analysis.EstimatedTotalBytes),
                    RecommendedSplitCount = analysis.RecommendedSplitCount,
                    Extent = analysis.Extent
                });

                totalBytes += analysis.EstimatedTotalBytes;
            }

            response.Success = true;
            response.TotalLayers = response.Layers.Count;
            response.TotalEstimatedBytes = totalBytes;
            response.TotalEstimatedSize = FormatBytes(totalBytes);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = $"분석 중 오류 발생: {ex.Message}";
            _logger.LogError(ex, "GDB 분석 실패: {Path}", gdbPath);
        }

        return response;
    }

    public Task ExecuteConvertAsync(string jobId, ConvertRequest request, CancellationToken cancellationToken)
    {
        var previousEncoding = Gdal.GetConfigOption("SHAPE_ENCODING", "");
        Gdal.SetConfigOption("SHAPE_ENCODING", "CP949");

        try
        {
            _jobManager.UpdateJobProgress(jobId, job =>
            {
                job.State = JobState.Analyzing;
                job.CurrentPhase = "분석";
                job.StatusMessage = "레이어 분석 중...";
            });

            // 출력 디렉토리 생성
            if (!Directory.Exists(request.OutputPath))
            {
                Directory.CreateDirectory(request.OutputPath);
            }

            // 레이어 분석
            var analyses = AnalyzeLayersInternal(request.GdbPath, 100);
            
            // 선택된 레이어 필터링
            if (request.SelectedLayers != null && request.SelectedLayers.Count > 0)
            {
                analyses = analyses.Where(a => request.SelectedLayers.Contains(a.Name)).ToList();
            }

            if (analyses.Count == 0)
            {
                _jobManager.FailJob(jobId, "변환 대상 레이어가 없습니다.");
                return Task.CompletedTask;
            }

            _jobManager.UpdateJobProgress(jobId, job =>
            {
                job.TotalLayers = analyses.Count;
                job.TotalFeatures = analyses.Sum(a => a.FeatureCount);
            });

            var driver = Ogr.GetDriverByName("OpenFileGDB");
            var dstDriver = Ogr.GetDriverByName("ESRI Shapefile");

            if (driver == null || dstDriver == null)
            {
                _jobManager.FailJob(jobId, "필요한 GDAL 드라이버를 찾을 수 없습니다.");
                return Task.CompletedTask;
            }

            using var srcDataSource = driver.Open(request.GdbPath, 0);
            if (srcDataSource == null)
            {
                _jobManager.FailJob(jobId, $"FileGDB를 열 수 없습니다: {request.GdbPath}");
                return Task.CompletedTask;
            }

            var result = new ConvertResultResponse
            {
                JobId = jobId,
                Success = true,
                OutputPath = request.OutputPath,
                TotalLayers = analyses.Count,
                StartedAt = DateTime.Now
            };

            var failedLayers = new List<string>();
            var targetFileSizeBytes = (long)request.TargetFileSizeMB * 1024 * 1024;

            // 각 레이어 변환
            for (int layerIdx = 0; layerIdx < analyses.Count; layerIdx++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var analysis = analyses[layerIdx];
                var splitCount = request.ManualSplitCount > 0
                    ? request.ManualSplitCount
                    : CalculateRecommendedSplitCount(analysis.EstimatedTotalBytes, targetFileSizeBytes, analysis.FeatureCount);

                _jobManager.UpdateJobProgress(jobId, job =>
                {
                    job.State = JobState.Converting;
                    job.CurrentPhase = "변환";
                    job.CurrentLayer = analysis.Name;
                    job.ProcessedLayers = layerIdx;
                    job.TotalSplits = splitCount;
                    job.StatusMessage = $"변환 중: {analysis.Name} ({layerIdx + 1}/{analyses.Count})";
                    job.Progress = (double)layerIdx / analyses.Count * 100;
                });

                try
                {
                    var layerIndex = ConvertSingleLayer(
                        srcDataSource, dstDriver, analysis, request.OutputPath,
                        splitCount, request, jobId, cancellationToken);

                    result.Layers.Add(new ConvertedLayerResponse
                    {
                        LayerName = layerIndex.LayerName,
                        TotalFeatures = layerIndex.TotalFeatures,
                        TotalSplits = layerIndex.TotalSplits,
                        GeometryType = layerIndex.GeometryType,
                        TotalExtent = layerIndex.TotalExtent,
                        Splits = layerIndex.Splits.Select(s => new SplitFileResponse
                        {
                            FileName = s.FileName,
                            SplitIndex = s.SplitIndex,
                            FeatureCount = s.FeatureCount,
                            FileSize = s.FileSize,
                            FileSizeFormatted = FormatBytes(s.FileSize),
                            Extent = s.Extent
                        }).ToList()
                    });

                    result.ConvertedLayers++;
                    result.TotalFilesCreated += layerIndex.TotalSplits;
                    result.TotalFeaturesConverted += layerIndex.TotalFeatures;

                    // 인덱스 파일 생성
                    if (request.GenerateIndexFile)
                    {
                        SaveIndexFile(request.OutputPath, layerIndex);
                    }
                }
                catch (Exception ex)
                {
                    failedLayers.Add($"{analysis.Name}: {ex.Message}");
                    _logger.LogError(ex, "레이어 변환 실패: {LayerName}", analysis.Name);
                }
            }

            result.CompletedAt = DateTime.Now;
            result.Duration = result.CompletedAt - result.StartedAt;
            result.FailedLayers = failedLayers;

            if (failedLayers.Count > 0 && result.ConvertedLayers == 0)
            {
                result.Success = false;
                result.ErrorMessage = "모든 레이어 변환에 실패했습니다.";
                _jobManager.FailJob(jobId, result.ErrorMessage);
            }
            else
            {
                _jobManager.CompleteJob(jobId, result);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("작업 취소됨: {JobId}", jobId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "변환 중 오류 발생: {JobId}", jobId);
            _jobManager.FailJob(jobId, $"변환 중 오류 발생: {ex.Message}");
        }
        finally
        {
            Gdal.SetConfigOption("SHAPE_ENCODING", 
                string.IsNullOrEmpty(previousEncoding) ? null : previousEncoding);
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Private Methods - Analysis

    private List<LayerAnalysis> AnalyzeLayersInternal(string gdbPath, int sampleSize)
    {
        var analyses = new List<LayerAnalysis>();

        var driver = Ogr.GetDriverByName("OpenFileGDB");
        using var dataSource = driver!.Open(gdbPath, 0);

        var layerCount = dataSource!.GetLayerCount();
        for (int i = 0; i < layerCount; i++)
        {
            using var layer = dataSource.GetLayerByIndex(i);
            if (layer == null) continue;

            var layerName = layer.GetName();
            if (IsExcludedLayer(layerName)) continue;

            var analysis = AnalyzeSingleLayer(layer, sampleSize);
            analysis.RecommendedSplitCount = CalculateRecommendedSplitCount(
                analysis.EstimatedTotalBytes, 1_300_000_000L, analysis.FeatureCount);

            analyses.Add(analysis);
        }

        return analyses;
    }

    private LayerAnalysis AnalyzeSingleLayer(Layer layer, int sampleSize)
    {
        var featureCount = layer.GetFeatureCount(0);
        if (featureCount < 0) featureCount = layer.GetFeatureCount(1);

        var analysis = new LayerAnalysis
        {
            Name = layer.GetName(),
            GeometryTypeEnum = layer.GetGeomType(),
            GeometryType = GetGeometryTypeName(layer.GetGeomType()),
            FeatureCount = featureCount
        };

        var extent = new Envelope();
        layer.GetExtent(extent, 0);
        analysis.Extent = new[] { extent.MinX, extent.MinY, extent.MaxX, extent.MaxY };

        analysis.AvgVertexCount = CalculateAverageVertexCount(layer, sampleSize);
        analysis.EstimatedBytesPerFeature = GeometrySizeProfile.EstimateBytes(
            analysis.GeometryTypeEnum, analysis.AvgVertexCount) + 200;
        analysis.EstimatedTotalBytes = analysis.FeatureCount * analysis.EstimatedBytesPerFeature;

        return analysis;
    }

    private double CalculateAverageVertexCount(Layer layer, int sampleSize)
    {
        var featureCount = layer.GetFeatureCount(0);
        if (featureCount <= 0) return 10;

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

    private int CountVertices(Geometry geometry)
    {
        if (geometry == null) return 0;

        var flatType = (wkbGeometryType)((int)geometry.GetGeometryType() & 0xFF);
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
            if (ring != null) total += ring.GetPointCount();
        }
        return total;
    }

    private int CountMultiGeometryVertices(Geometry multi)
    {
        int total = 0;
        for (int i = 0; i < multi.GetGeometryCount(); i++)
        {
            var part = multi.GetGeometryRef(i);
            if (part != null) total += CountVertices(part);
        }
        return total;
    }

    private int CalculateRecommendedSplitCount(long estimatedTotalBytes, long targetFileSizeBytes, long featureCount)
    {
        int splitBySize = 1;
        if (estimatedTotalBytes > targetFileSizeBytes)
        {
            splitBySize = (int)Math.Ceiling((double)estimatedTotalBytes / targetFileSizeBytes);
        }
        return Math.Clamp(splitBySize, 1, 30);
    }

    #endregion

    #region Private Methods - Conversion

    private LayerConvertIndex ConvertSingleLayer(
        DataSource srcDataSource,
        OSGeo.OGR.Driver dstDriver,
        LayerAnalysis analysis,
        string outputPath,
        int splitCount,
        ConvertRequest request,
        string jobId,
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

        // 항상 스트리밍 방식으로 변환 (진행률 보고 포함)
        ConvertWithStreaming(srcLayer, dstDriver, analysis, outputPath, 
            splitCount, index, jobId, cancellationToken);

        return index;
    }

    private void ConvertWithStreaming(
        Layer srcLayer,
        OSGeo.OGR.Driver dstDriver,
        LayerAnalysis analysis,
        string outputPath,
        int splitCount,
        LayerConvertIndex index,
        string jobId,
        CancellationToken cancellationToken)
    {
        var totalFeatures = srcLayer.GetFeatureCount(1);
        long processedCount = 0;
        int batchSize = 10000;
        int batchCount = 0;

        long featuresPerFile = totalFeatures / Math.Max(splitCount, 1);
        if (featuresPerFile < 100000) featuresPerFile = 100000;

        int currentFileIndex = 1;
        DataSource? currentDataSource = null;
        Layer? currentLayer = null;
        string currentFilePath = string.Empty;
        long currentFeatureCount = 0;
        var currentExtent = new Envelope();

        try
        {
            // 첫 파일 생성
            string fileName = splitCount > 1 
                ? $"{SanitizeFileName(analysis.Name)}_{currentFileIndex:D2}.shp"
                : $"{SanitizeFileName(analysis.Name)}.shp";
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

                    // 분할이 필요한 경우 새 파일 생성
                    if (splitCount > 1 && currentFeatureCount >= featuresPerFile && currentFileIndex < splitCount)
                    {
                        // 현재 파일 마무리
                        if (currentDataSource == null)
                            throw new InvalidOperationException("데이터소스가 초기화되지 않았습니다.");

                        FinalizeSplitFile(currentDataSource, currentFilePath, currentFileIndex,
                            currentFeatureCount, currentExtent, index);
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

                        _jobManager.UpdateJobProgress(jobId, job =>
                        {
                            job.CurrentSplitIndex = currentFileIndex;
                            job.ProcessedFeatures = processedCount;
                            job.StatusMessage = $"{analysis.Name}: {processedCount:N0}/{totalFeatures:N0} " +
                                (splitCount > 1 ? $"(파일 {currentFileIndex}/{splitCount})" : "");
                        });

                        batchCount = 0;
                    }
                }
            }

            // 마지막 파일 마무리
            if (currentDataSource != null && currentFeatureCount > 0)
            {
                FinalizeSplitFile(currentDataSource, currentFilePath, currentFileIndex,
                    currentFeatureCount, currentExtent, index);
                currentDataSource.Dispose();
                currentDataSource = null;
            }

            index.TotalSplits = index.Splits.Count;
        }
        finally
        {
            currentDataSource?.Dispose();
        }
    }

    private Layer CreateLayerWithSchema(DataSource dataSource, Layer srcLayer, string layerName)
    {
        var srcDefn = srcLayer.GetLayerDefn();
        var geomType = srcLayer.GetGeomType();
        var srs = srcLayer.GetSpatialRef();

        var dstLayer = dataSource.CreateLayer(layerName, srs, geomType, null);
        if (dstLayer == null)
            throw new InvalidOperationException($"레이어 생성 실패: {layerName}");

        for (int i = 0; i < srcDefn.GetFieldCount(); i++)
        {
            var fieldDefn = srcDefn.GetFieldDefn(i);
            var fieldName = fieldDefn.GetName();
            if (fieldName.Length > 10) fieldName = fieldName.Substring(0, 10);

            var newFieldDefn = new FieldDefn(fieldName, fieldDefn.GetFieldType());
            newFieldDefn.SetWidth(fieldDefn.GetWidth());
            newFieldDefn.SetPrecision(fieldDefn.GetPrecision());
            dstLayer.CreateField(newFieldDefn, 1);
            newFieldDefn.Dispose();
        }

        return dstLayer;
    }

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
                default:
                    dstFeature.SetField(i, srcFeature.GetFieldAsString(i));
                    break;
            }
        }
    }

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

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    #endregion
}

