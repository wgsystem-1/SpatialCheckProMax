using Microsoft.Extensions.Logging;
using Moq;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Processors;
using SpatialCheckProMax.Services;
using Xunit;

namespace SpatialCheckProMax.Tests.Processors
{
    /// <summary>
    /// SchemaCheckProcessor 유닛 테스트
    /// </summary>
    public class SchemaCheckProcessorTests
    {
        private readonly Mock<ILogger<SchemaCheckProcessor>> _loggerMock;
        private readonly Mock<IFeatureFilterService> _featureFilterServiceMock;
        private readonly SchemaCheckProcessor _processor;

        public SchemaCheckProcessorTests()
        {
            _loggerMock = new Mock<ILogger<SchemaCheckProcessor>>();
            _featureFilterServiceMock = new Mock<IFeatureFilterService>();
            _processor = new SchemaCheckProcessor(_loggerMock.Object, _featureFilterServiceMock.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SchemaCheckProcessor(null!));
        }

        [Fact]
        public void Constructor_WithValidLogger_CreatesInstance()
        {
            // Act
            var processor = new SchemaCheckProcessor(_loggerMock.Object);

            // Assert
            Assert.NotNull(processor);
        }

        [Fact]
        public void Constructor_WithNullFeatureFilterService_CreatesInstance()
        {
            // Act
            var processor = new SchemaCheckProcessor(_loggerMock.Object, null);

            // Assert
            Assert.NotNull(processor);
        }

        [Fact]
        public void Constructor_WithAllParameters_CreatesInstance()
        {
            // Act
            var processor = new SchemaCheckProcessor(_loggerMock.Object, _featureFilterServiceMock.Object);

            // Assert
            Assert.NotNull(processor);
        }

        #endregion

        #region ProcessAsync Tests

        [Fact]
        public async Task ProcessAsync_ReturnsValidResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "TEST_COLUMN",
                DataType = "TEXT"
            };

            // Act
            var result = await _processor.ProcessAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsValid);
            Assert.Contains("개별 검수 메서드를 사용하세요", result.Message);
        }

        [Fact]
        public async Task ProcessAsync_WithCancellation_ReturnsResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "TEST_COLUMN",
                DataType = "INTEGER"
            };
            var cts = new CancellationTokenSource();

            // Act
            var result = await _processor.ProcessAsync("test.gdb", config, cts.Token);

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region ValidateColumnStructureAsync Tests

        [Fact]
        public async Task ValidateColumnStructureAsync_WithNonExistentFile_ReturnsInvalidResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "TEST_COLUMN",
                DataType = "TEXT"
            };

            // Act
            var result = await _processor.ValidateColumnStructureAsync("nonexistent_path.gdb", config);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.IsValid);
        }

        [Fact]
        public async Task ValidateColumnStructureAsync_WithCancellation_RespectsToken()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "TEST_COLUMN",
                DataType = "TEXT"
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await _processor.ValidateColumnStructureAsync("test.gdb", config, cts.Token));
        }

        #endregion

        #region ValidateDataTypesAsync Tests

        [Fact]
        public async Task ValidateDataTypesAsync_WithNonExistentFile_ReturnsResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "TEST_COLUMN",
                DataType = "INTEGER",
                Length = "10"
            };

            // Act
            var result = await _processor.ValidateDataTypesAsync("nonexistent_path.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region ValidatePrimaryForeignKeysAsync Tests

        [Fact]
        public async Task ValidatePrimaryForeignKeysAsync_WithNonExistentFile_ReturnsResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "ID",
                DataType = "INTEGER",
                PrimaryKey = "PK"
            };

            // Act
            var result = await _processor.ValidatePrimaryForeignKeysAsync("nonexistent_path.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task ValidatePrimaryForeignKeysAsync_WithForeignKey_ReturnsResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "REF_ID",
                DataType = "INTEGER",
                ForeignKey = "FK",
                ReferenceTable = "OTHER_TABLE",
                ReferenceColumn = "ID"
            };

            // Act
            var result = await _processor.ValidatePrimaryForeignKeysAsync("nonexistent_path.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region ValidateForeignKeyRelationsAsync Tests

        [Fact]
        public async Task ValidateForeignKeyRelationsAsync_WithNonExistentFile_ReturnsResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "FK_COLUMN",
                DataType = "INTEGER",
                ForeignKey = "FK",
                ReferenceTable = "REF_TABLE",
                ReferenceColumn = "ID"
            };

            // Act
            var result = await _processor.ValidateForeignKeyRelationsAsync("nonexistent_path.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task ValidateForeignKeyRelationsAsync_WithoutFKFlag_ReturnsEmptyResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "NOT_FK_COLUMN",
                DataType = "INTEGER",
                ForeignKey = "", // FK 플래그 없음
                ReferenceTable = "REF_TABLE",
                ReferenceColumn = "ID"
            };

            // Act
            var result = await _processor.ValidateForeignKeyRelationsAsync("test.gdb", config);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0, result.ErrorCount);
        }

        [Fact]
        public async Task ValidateForeignKeyRelationsAsync_WithMissingRefTable_ReturnsEmptyResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "FK_COLUMN",
                DataType = "INTEGER",
                ForeignKey = "FK",
                ReferenceTable = "", // 참조 테이블 누락
                ReferenceColumn = "ID"
            };

            // Act
            var result = await _processor.ValidateForeignKeyRelationsAsync("test.gdb", config);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0, result.ErrorCount);
        }

        #endregion

        #region SchemaCheckConfig Model Tests

        [Fact]
        public void SchemaCheckConfig_IsNotNullColumn_ReturnsTrueForY()
        {
            // Arrange
            var config = new SchemaCheckConfig { IsNotNull = "Y" };

            // Assert
            Assert.True(config.IsNotNullColumn);
        }

        [Fact]
        public void SchemaCheckConfig_IsNotNullColumn_ReturnsFalseForN()
        {
            // Arrange
            var config = new SchemaCheckConfig { IsNotNull = "N" };

            // Assert
            Assert.False(config.IsNotNullColumn);
        }

        [Fact]
        public void SchemaCheckConfig_IsNotNullColumn_ReturnsFalseForEmpty()
        {
            // Arrange
            var config = new SchemaCheckConfig { IsNotNull = "" };

            // Assert
            Assert.False(config.IsNotNullColumn);
        }

        [Fact]
        public void SchemaCheckConfig_IsPrimaryKeyColumn_ReturnsTrueForPK()
        {
            // Arrange
            var config = new SchemaCheckConfig { PrimaryKey = "PK" };

            // Assert
            Assert.True(config.IsPrimaryKeyColumn);
        }

        [Fact]
        public void SchemaCheckConfig_IsUniqueKeyColumn_ReturnsTrueForUK()
        {
            // Arrange
            var config = new SchemaCheckConfig { UniqueKey = "UK" };

            // Assert
            Assert.True(config.IsUniqueKeyColumn);
        }

        [Fact]
        public void SchemaCheckConfig_IsForeignKeyColumn_ReturnsTrueForFK()
        {
            // Arrange
            var config = new SchemaCheckConfig { ForeignKey = "FK" };

            // Assert
            Assert.True(config.IsForeignKeyColumn);
        }

        [Fact]
        public void SchemaCheckConfig_IsTextType_ReturnsTrueForTEXT()
        {
            // Arrange
            var config = new SchemaCheckConfig { DataType = "TEXT" };

            // Assert
            Assert.True(config.IsTextType);
        }

        [Fact]
        public void SchemaCheckConfig_IsTextType_ReturnsTrueForCHAR()
        {
            // Arrange
            var config = new SchemaCheckConfig { DataType = "CHAR" };

            // Assert
            Assert.True(config.IsTextType);
        }

        [Fact]
        public void SchemaCheckConfig_IsNumericType_ReturnsTrueForINTEGER()
        {
            // Arrange
            var config = new SchemaCheckConfig { DataType = "INTEGER" };

            // Assert
            Assert.True(config.IsNumericType);
            Assert.True(config.IsIntegerType);
        }

        [Fact]
        public void SchemaCheckConfig_IsDecimalType_ReturnsTrueForNUMERIC()
        {
            // Arrange
            var config = new SchemaCheckConfig { DataType = "NUMERIC" };

            // Assert
            Assert.True(config.IsDecimalType);
        }

        [Fact]
        public void SchemaCheckConfig_IsDateType_ReturnsTrueForDATE()
        {
            // Arrange
            var config = new SchemaCheckConfig { DataType = "DATE" };

            // Assert
            Assert.True(config.IsDateType);
        }

        [Fact]
        public void SchemaCheckConfig_IsValidDataType_ReturnsTrueForValidTypes()
        {
            // Assert
            Assert.True(new SchemaCheckConfig { DataType = "INTEGER" }.IsValidDataType());
            Assert.True(new SchemaCheckConfig { DataType = "TEXT" }.IsValidDataType());
            Assert.True(new SchemaCheckConfig { DataType = "NUMERIC" }.IsValidDataType());
            Assert.True(new SchemaCheckConfig { DataType = "CHAR" }.IsValidDataType());
            Assert.True(new SchemaCheckConfig { DataType = "DATE" }.IsValidDataType());
        }

        [Fact]
        public void SchemaCheckConfig_IsValidDataType_ReturnsFalseForInvalidType()
        {
            // Arrange
            var config = new SchemaCheckConfig { DataType = "INVALID_TYPE" };

            // Assert
            Assert.False(config.IsValidDataType());
        }

        [Fact]
        public void SchemaCheckConfig_GetIntegerLength_ParsesCorrectly()
        {
            // Arrange & Assert
            Assert.Equal(10, new SchemaCheckConfig { Length = "10" }.GetIntegerLength());
            Assert.Equal(20, new SchemaCheckConfig { Length = "20,5" }.GetIntegerLength());
            Assert.Equal(0, new SchemaCheckConfig { Length = "" }.GetIntegerLength());
            Assert.Equal(0, new SchemaCheckConfig { Length = "invalid" }.GetIntegerLength());
        }

        [Fact]
        public void SchemaCheckConfig_GetDecimalPlaces_ParsesCorrectly()
        {
            // Arrange & Assert
            Assert.Equal(0, new SchemaCheckConfig { Length = "10" }.GetDecimalPlaces());
            Assert.Equal(5, new SchemaCheckConfig { Length = "20,5" }.GetDecimalPlaces());
            Assert.Equal(2, new SchemaCheckConfig { Length = "10,2" }.GetDecimalPlaces());
            Assert.Equal(0, new SchemaCheckConfig { Length = "" }.GetDecimalPlaces());
        }

        [Fact]
        public void SchemaCheckConfig_Validate_ReturnsErrorsForMissingTableId()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "",
                ColumnName = "TEST_COLUMN",
                DataType = "TEXT"
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.MemberNames.Contains("TableId"));
        }

        [Fact]
        public void SchemaCheckConfig_Validate_ReturnsErrorsForInvalidDataType()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "TEST_COLUMN",
                DataType = "BLOB" // 지원하지 않는 타입
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.MemberNames.Contains("DataType"));
        }

        [Fact]
        public void SchemaCheckConfig_Validate_ReturnsErrorsForFKWithoutRefTable()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "FK_COLUMN",
                DataType = "INTEGER",
                ForeignKey = "FK",
                ReferenceTable = "", // 누락
                ReferenceColumn = "ID"
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.MemberNames.Contains("ReferenceTable"));
        }

        [Fact]
        public void SchemaCheckConfig_Validate_ReturnsErrorsForFKWithoutRefColumn()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "FK_COLUMN",
                DataType = "INTEGER",
                ForeignKey = "FK",
                ReferenceTable = "REF_TABLE",
                ReferenceColumn = "" // 누락
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.MemberNames.Contains("ReferenceColumn"));
        }

        [Fact]
        public void SchemaCheckConfig_Validate_ReturnsErrorsForPKAndUKTogether()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "ID",
                DataType = "INTEGER",
                PrimaryKey = "PK",
                UniqueKey = "UK"
            };

            // Act
            var errors = config.Validate();

            // Assert
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.MemberNames.Contains("PrimaryKey") || e.MemberNames.Contains("UniqueKey"));
        }

        [Fact]
        public void SchemaCheckConfig_ToString_ReturnsFormattedString()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "A0010000",
                ColumnName = "OBJ_ID",
                ColumnKoreanName = "객체ID",
                DataType = "INTEGER"
            };

            // Act
            var result = config.ToString();

            // Assert
            Assert.Contains("A0010000", result);
            Assert.Contains("OBJ_ID", result);
            Assert.Contains("객체ID", result);
            Assert.Contains("INTEGER", result);
        }

        #endregion

        #region Edge Cases Tests

        [Theory]
        [InlineData("uk")]
        [InlineData("UK")]
        [InlineData("Uk")]
        public void SchemaCheckConfig_UniqueKey_CaseInsensitive(string ukValue)
        {
            // Arrange
            var config = new SchemaCheckConfig { UniqueKey = ukValue };

            // Assert
            Assert.True(config.IsUniqueKeyColumn);
        }

        [Theory]
        [InlineData("fk")]
        [InlineData("FK")]
        [InlineData("Fk")]
        public void SchemaCheckConfig_ForeignKey_CaseInsensitive(string fkValue)
        {
            // Arrange
            var config = new SchemaCheckConfig { ForeignKey = fkValue };

            // Assert
            Assert.True(config.IsForeignKeyColumn);
        }

        [Theory]
        [InlineData("integer")]
        [InlineData("INTEGER")]
        [InlineData("Integer")]
        public void SchemaCheckConfig_DataType_CaseInsensitive(string dataType)
        {
            // Arrange
            var config = new SchemaCheckConfig { DataType = dataType };

            // Assert
            Assert.True(config.IsIntegerType);
            Assert.True(config.IsValidDataType());
        }

        #endregion

        #region ValidatePrimaryForeignKeysAsync Extended Tests

        [Fact]
        public async Task ValidatePrimaryForeignKeysAsync_WithNeitherUKNorNN_ReturnsEmptyResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "NORMAL_COLUMN",
                DataType = "TEXT",
                UniqueKey = "",
                IsNotNull = ""
            };

            // Act
            var result = await _processor.ValidatePrimaryForeignKeysAsync("test.gdb", config);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(0, result.ErrorCount);
        }

        [Fact]
        public async Task ValidatePrimaryForeignKeysAsync_WithUKOnly_ReturnsResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "UNIQUE_COLUMN",
                DataType = "TEXT",
                UniqueKey = "UK",
                IsNotNull = ""
            };

            // Act
            var result = await _processor.ValidatePrimaryForeignKeysAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task ValidatePrimaryForeignKeysAsync_WithNNOnly_ReturnsResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "REQUIRED_COLUMN",
                DataType = "TEXT",
                UniqueKey = "",
                IsNotNull = "Y"
            };

            // Act
            var result = await _processor.ValidatePrimaryForeignKeysAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region ValidateDataTypesAsync Extended Tests

        [Fact]
        public async Task ValidateDataTypesAsync_WithNumericType_ReturnsResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "DECIMAL_COLUMN",
                DataType = "NUMERIC",
                Length = "10,2"
            };

            // Act
            var result = await _processor.ValidateDataTypesAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task ValidateDataTypesAsync_WithEmptyLength_ReturnsResult()
        {
            // Arrange
            var config = new SchemaCheckConfig
            {
                TableId = "TEST_TABLE",
                ColumnName = "NO_LENGTH_COLUMN",
                DataType = "TEXT",
                Length = ""
            };

            // Act
            var result = await _processor.ValidateDataTypesAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        #endregion
    }
}
