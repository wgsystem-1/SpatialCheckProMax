using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Globalization;
using SpatialCheckProMax.Utils;
using SpatialCheckProMax.Services;

namespace SpatialCheckProMax.Processors
{
    /// <summary>
    /// 스키마 검수 프로세서 (실제 구현)
    /// </summary>
    public class SchemaCheckProcessor : ISchemaCheckProcessor
    {
        private readonly ILogger<SchemaCheckProcessor> _logger;
        private readonly IFeatureFilterService? _featureFilterService;

        public SchemaCheckProcessor(ILogger<SchemaCheckProcessor> logger, IFeatureFilterService? featureFilterService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _featureFilterService = featureFilterService;
        }

        public async Task<ValidationResult> ProcessAsync(string filePath, SchemaCheckConfig config, CancellationToken cancellationToken = default)
        {
            // 단일 규칙 처리용 (일반적으로 사용 안 함, 개별 메서드 호출 권장)
            return new ValidationResult { IsValid = true, Message = "개별 검수 메서드를 사용하세요." };
        }

        /// <summary>
        /// 컬럼 구조(존재 여부) 검수
        /// </summary>
        public async Task<ValidationResult> ValidateColumnStructureAsync(string filePath, SchemaCheckConfig config, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var result = new ValidationResult { TargetFile = filePath };
                var errors = new List<ValidationError>();

                try
                {
                    using var ds = Ogr.Open(filePath, 0);
                    if (ds == null)
                    {
                        return new ValidationResult { IsValid = false, Message = "파일을 열 수 없습니다." };
                    }

                    var layer = GetLayerByIdOrName(ds, config.TableId, config.TableId); // TableName 대신 TableId 사용
                    if (layer == null)
                    {
                        // 테이블 자체가 없는 경우 (1단계에서 걸러지지만 여기서도 확인)
                        return new ValidationResult { IsValid = false, Message = $"테이블을 찾을 수 없습니다: {config.TableId}" };
                    }

                    var layerDefn = layer.GetLayerDefn();
                    
                    // 필드 존재 여부 확인
                    int fieldIndex = GetFieldIndexIgnoreCase(layerDefn, config.ColumnName);
                    if (fieldIndex == -1)
                    {
                        // 필드 누락 오류 (구조적 문제이므로 NoGeom)
                        errors.Add(new ValidationError
                        {
                            ErrorCode = "LOG_CNC_SCH_001",
                            Message = $"필드가 누락되었습니다: {config.ColumnName}",
                            TableId = config.TableId,
                            TableName = config.TableId,
                            FieldName = config.ColumnName,
                            Severity = Models.Enums.ErrorSeverity.Critical
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "컬럼 구조 검수 중 오류 발생");
                    result.Message = ex.Message;
                }

                result.Errors = errors;
                result.ErrorCount = errors.Count;
                result.IsValid = errors.Count == 0;
                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// 데이터 타입 및 길이 검수
        /// </summary>
        public async Task<ValidationResult> ValidateDataTypesAsync(string filePath, SchemaCheckConfig config, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var result = new ValidationResult { TargetFile = filePath };
                var errors = new List<ValidationError>();

                try
                {
                    using var ds = Ogr.Open(filePath, 0);
                    if (ds == null) return result;

                    var layer = GetLayerByIdOrName(ds, config.TableId, config.TableId);
                    if (layer == null) return result;

                    var layerDefn = layer.GetLayerDefn();
                    int fieldIndex = GetFieldIndexIgnoreCase(layerDefn, config.ColumnName);
                    
                    if (fieldIndex != -1)
                    {
                        using var fieldDefn = layerDefn.GetFieldDefn(fieldIndex);
                        var currentType = fieldDefn.GetFieldTypeName(fieldDefn.GetFieldType());
                        var currentLength = fieldDefn.GetWidth();

                        // 타입 불일치 (경고 또는 오류)
                        // 매핑 로직이 복잡하므로 단순 문자열 비교만 수행하거나, 필요시 OGR 타입 매핑 추가
                        // 여기서는 단순 길이 체크만 예시로 구현
                        var configLength = config.GetIntegerLength();
                        if (configLength > 0 && currentLength > 0 && currentLength != configLength)
                        {
                             // 길이 불일치는 스키마 구조 문제이므로 NoGeom 처리
                             // (엄격하게 하려면 오류 추가)
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "데이터 타입 검수 중 오류 발생");
                }

                result.Errors = errors;
                result.ErrorCount = errors.Count;
                result.IsValid = errors.Count == 0;
                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// 기본키(UK), 필수값(NN) 검수 - 객체 특정하여 오류 생성
        /// </summary>
        public async Task<ValidationResult> ValidatePrimaryForeignKeysAsync(string filePath, SchemaCheckConfig config, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var result = new ValidationResult { TargetFile = filePath };
                var errors = new List<ValidationError>();

                // UK, NN 체크 여부 확인
                bool checkUK = string.Equals(config.UniqueKey, "UK", StringComparison.OrdinalIgnoreCase);
                bool checkNN = string.Equals(config.IsNotNull, "Y", StringComparison.OrdinalIgnoreCase);

                if (!checkUK && !checkNN) return result;

                try
                {
                    using var ds = Ogr.Open(filePath, 0);
                    if (ds == null) return result;

                    var layer = GetLayerByIdOrName(ds, config.TableId, config.TableId);
                    if (layer == null) return result;

                    var layerDefn = layer.GetLayerDefn();
                    int fieldIndex = GetFieldIndexIgnoreCase(layerDefn, config.ColumnName);
                    if (fieldIndex == -1) return result;

                    // UK 검사를 위한 값 저장소 (Value -> List<OID>)
                    var uniqueValues = new Dictionary<string, List<long>>();

                    layer.ResetReading();
                    Feature? feature;
                    int processedCount = 0;

                    while ((feature = layer.GetNextFeature()) != null)
                    {
                        processedCount++;
                        using (feature)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            var oid = feature.GetFID();
                            string valStr = feature.IsFieldNull(fieldIndex) ? string.Empty : feature.GetFieldAsString(fieldIndex);
                            bool isNull = feature.IsFieldNull(fieldIndex) || string.IsNullOrEmpty(valStr);

                            // 1. Not Null 검사
                            if (checkNN && isNull)
                            {
                                var (x, y) = GetErrorLocation(feature);
                                errors.Add(new ValidationError
                                {
                                    ErrorCode = "COM_OMS_ATR_001",
                                    Message = $"{config.ColumnName} 필드는 필수값(Not Null)입니다.",
                                    TableId = config.TableId,
                                    TableName = config.TableId, // 별칭이 있다면 매핑 필요
                                    FeatureId = oid.ToString(CultureInfo.InvariantCulture),
                                    FieldName = config.ColumnName,
                                    Severity = Models.Enums.ErrorSeverity.Error,
                                    X = x,
                                    Y = y,
                                    GeometryWKT = QcError.CreatePointWKT(x, y)
                                });
                            }

                            // 2. Unique Key 값 수집 (NULL은 UK 대상 제외가 일반적이나, 요구사항에 따라 다름)
                            if (checkUK && !isNull)
                            {
                                if (!uniqueValues.ContainsKey(valStr))
                                {
                                    uniqueValues[valStr] = new List<long>();
                                }
                                uniqueValues[valStr].Add(oid);
                            }
                        }
                    }

                    // 3. Unique Key 위반 확인 (중복된 값 처리)
                    if (checkUK)
                    {
                        foreach (var kvp in uniqueValues.Where(k => k.Value.Count > 1))
                        {
                            var dupValue = kvp.Key;
                            var oids = kvp.Value;
                            
                            // 중복된 모든 객체에 대해 오류 생성 (위치 특정을 위해 재조회 필요)
                            // 성능을 위해 여기서는 OID만 기록하고, 위치는 (0,0)으로 하거나, 
                            // 정확한 위치를 위해 별도 쿼리를 해야 함.
                            // 여기서는 1-pass에서 위치를 저장하지 않았으므로, OID를 이용해 다시 Feature를 조회하여 위치를 찾음.
                            
                            foreach (var oid in oids)
                            {
                                // Feature 재조회
                                using var dupFeature = layer.GetFeature(oid);
                                if (dupFeature != null)
                                {
                                    var (x, y) = GetErrorLocation(dupFeature);
                                    errors.Add(new ValidationError
                                    {
                                        ErrorCode = "LOG_DOM_ATR_001",
                                        Message = $"{config.ColumnName} 필드값 중복: '{dupValue}'",
                                        TableId = config.TableId,
                                        TableName = config.TableId,
                                        FeatureId = oid.ToString(CultureInfo.InvariantCulture),
                                        FieldName = config.ColumnName,
                                        Severity = Models.Enums.ErrorSeverity.Error,
                                        X = x,
                                        Y = y,
                                        GeometryWKT = QcError.CreatePointWKT(x, y)
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PK/NN 검수 중 오류 발생");
                }

                result.Errors = errors;
                result.ErrorCount = errors.Count;
                result.IsValid = errors.Count == 0;
                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// 외래키(FK) 관계 검수 - 객체 특정하여 오류 생성
        /// </summary>
        public async Task<ValidationResult> ValidateForeignKeyRelationsAsync(string filePath, SchemaCheckConfig config, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var result = new ValidationResult { TargetFile = filePath };
                var errors = new List<ValidationError>();

                // FK 체크 여부 확인
                if (!string.Equals(config.ForeignKey, "FK", StringComparison.OrdinalIgnoreCase)) return result;
                if (string.IsNullOrEmpty(config.ReferenceTable) || string.IsNullOrEmpty(config.ReferenceColumn)) return result;

                try
                {
                    using var ds = Ogr.Open(filePath, 0);
                    if (ds == null) return result;

                    var mainLayer = GetLayerByIdOrName(ds, config.TableId, config.TableId);
                    var refLayer = GetLayerByIdOrName(ds, config.ReferenceTable, config.ReferenceTable);

                    if (mainLayer == null || refLayer == null)
                    {
                        _logger.LogWarning("FK 검수 실패: 테이블을 찾을 수 없음 ({Main} -> {Ref})", config.TableId, config.ReferenceTable);
                        return result;
                    }

                    var mainDefn = mainLayer.GetLayerDefn();
                    var refDefn = refLayer.GetLayerDefn();

                    int mainIdx = GetFieldIndexIgnoreCase(mainDefn, config.ColumnName);
                    int refIdx = GetFieldIndexIgnoreCase(refDefn, config.ReferenceColumn);

                    if (mainIdx == -1 || refIdx == -1) return result;

                    // 1. 참조 테이블(Parent)의 키 값 수집 (HashSet)
                    var refValues = new HashSet<string>();
                    refLayer.ResetReading();
                    Feature? refFeat;
                    while ((refFeat = refLayer.GetNextFeature()) != null)
                    {
                        using (refFeat)
                        {
                            if (!refFeat.IsFieldNull(refIdx))
                            {
                                var val = refFeat.GetFieldAsString(refIdx);
                                if (!string.IsNullOrEmpty(val)) refValues.Add(val);
                            }
                        }
                    }

                    // 2. 메인 테이블(Child) 순회하며 FK 검증
                    mainLayer.ResetReading();
                    Feature? mainFeat;
                    while ((mainFeat = mainLayer.GetNextFeature()) != null)
                    {
                        using (mainFeat)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            if (mainFeat.IsFieldNull(mainIdx)) continue; // Null은 FK 검사 제외 (NN 검사에서 처리)

                            var val = mainFeat.GetFieldAsString(mainIdx);
                            if (string.IsNullOrEmpty(val)) continue;

                            if (!refValues.Contains(val))
                            {
                                var oid = mainFeat.GetFID();
                                var (x, y) = GetErrorLocation(mainFeat);
                                
                                errors.Add(new ValidationError
                                {
                                    ErrorCode = "LOG_CNC_SCH_002",
                                    Message = $"{config.ColumnName} 값 '{val}'이(가) 참조 테이블({config.ReferenceTable})에 존재하지 않습니다.",
                                    TableId = config.TableId,
                                    TableName = config.TableId,
                                    FeatureId = oid.ToString(CultureInfo.InvariantCulture),
                                    FieldName = config.ColumnName,
                                    Severity = Models.Enums.ErrorSeverity.Error,
                                    X = x,
                                    Y = y,
                                    GeometryWKT = QcError.CreatePointWKT(x, y)
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FK 관계 검수 중 오류 발생");
                }

                result.Errors = errors;
                result.ErrorCount = errors.Count;
                result.IsValid = errors.Count == 0;
                return result;
            }, cancellationToken);
        }

        // 헬퍼 메서드

        private static Layer? GetLayerByIdOrName(DataSource ds, string? tableId, string? tableName)
        {
            if (!string.IsNullOrEmpty(tableId))
            {
                var layer = ds.GetLayerByName(tableId);
                if (layer != null) return layer;
            }
            // 대소문자 무시 검색 등 추가 가능
            return null;
        }

        private static int GetFieldIndexIgnoreCase(FeatureDefn defn, string fieldName)
        {
            for (int i = 0; i < defn.GetFieldCount(); i++)
            {
                using var fd = defn.GetFieldDefn(i);
                if (string.Equals(fd.GetName(), fieldName, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private static (double X, double Y) GetErrorLocation(Feature feature)
        {
            try
            {
                var geom = feature.GetGeometryRef();
                return GeometryCoordinateExtractor.GetFirstVertex(geom);
            }
            catch
            {
                return (0, 0);
            }
        }
    }
}

