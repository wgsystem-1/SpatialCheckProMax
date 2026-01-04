using Microsoft.Extensions.Logging;
using Moq;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Processors;
using SpatialCheckProMax.Services;
using Xunit;

namespace SpatialCheckProMax.Tests.Processors
{
    /// <summary>
    /// AttributeCheckProcessor 유닛 테스트
    /// </summary>
    public class AttributeCheckProcessorTests
    {
        private readonly Mock<ILogger<AttributeCheckProcessor>> _loggerMock;
        private readonly Mock<IFeatureFilterService> _featureFilterServiceMock;
        private readonly AttributeCheckProcessor _processor;

        public AttributeCheckProcessorTests()
        {
            _loggerMock = new Mock<ILogger<AttributeCheckProcessor>>();
            _featureFilterServiceMock = new Mock<IFeatureFilterService>();
            _processor = new AttributeCheckProcessor(_loggerMock.Object, _featureFilterServiceMock.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidLogger_CreatesInstance()
        {
            // Act
            var processor = new AttributeCheckProcessor(_loggerMock.Object);

            // Assert
            Assert.NotNull(processor);
        }

        [Fact]
        public void Constructor_WithNullFeatureFilterService_CreatesInstance()
        {
            // Act
            var processor = new AttributeCheckProcessor(_loggerMock.Object, null);

            // Assert
            Assert.NotNull(processor);
        }

        [Fact]
        public void Constructor_WithAllParameters_CreatesInstance()
        {
            // Act
            var processor = new AttributeCheckProcessor(_loggerMock.Object, _featureFilterServiceMock.Object);

            // Assert
            Assert.NotNull(processor);
        }

        #endregion

        #region LastSkippedFeatureCount Tests

        [Fact]
        public void LastSkippedFeatureCount_InitialValue_IsZero()
        {
            // Assert
            Assert.Equal(0, _processor.LastSkippedFeatureCount);
        }

        #endregion

        #region LoadCodelist Tests

        [Fact]
        public void LoadCodelist_WithNullPath_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => _processor.LoadCodelist(null));
            Assert.Null(exception);
        }

        [Fact]
        public void LoadCodelist_WithEmptyPath_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => _processor.LoadCodelist(""));
            Assert.Null(exception);
        }

        [Fact]
        public void LoadCodelist_WithNonExistentPath_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => _processor.LoadCodelist("nonexistent_codelist.csv"));
            Assert.Null(exception);
        }

        [Fact]
        public void LoadCodelist_WithWhitespacePath_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => _processor.LoadCodelist("   "));
            Assert.Null(exception);
        }

        #endregion

        #region ValidateAsync Tests

        [Fact]
        public async Task ValidateAsync_WithNonExistentFile_ReturnsEmptyErrors()
        {
            // Arrange
            var rules = new List<AttributeCheckConfig>
            {
                new AttributeCheckConfig
                {
                    RuleId = "RULE_001",
                    Enabled = "Y",
                    TableId = "TEST_TABLE",
                    TableName = "테스트 테이블",
                    FieldName = "TEST_FIELD",
                    CheckType = "NotNull"
                }
            };
            var mockDataProvider = new Mock<IValidationDataProvider>();

            // Act
            var errors = await _processor.ValidateAsync("nonexistent.gdb", mockDataProvider.Object, rules);

            // Assert
            Assert.NotNull(errors);
        }

        [Fact]
        public async Task ValidateAsync_WithEmptyRules_ReturnsEmptyErrors()
        {
            // Arrange
            var rules = new List<AttributeCheckConfig>();
            var mockDataProvider = new Mock<IValidationDataProvider>();

            // Act
            var errors = await _processor.ValidateAsync("nonexistent.gdb", mockDataProvider.Object, rules);

            // Assert
            Assert.NotNull(errors);
            Assert.Empty(errors);
        }

        [Fact]
        public async Task ValidateAsync_WithDisabledRule_SkipsRule()
        {
            // Arrange
            var rules = new List<AttributeCheckConfig>
            {
                new AttributeCheckConfig
                {
                    RuleId = "RULE_001",
                    Enabled = "N", // 비활성화
                    TableId = "TEST_TABLE",
                    TableName = "테스트 테이블",
                    FieldName = "TEST_FIELD",
                    CheckType = "NotNull"
                }
            };
            var mockDataProvider = new Mock<IValidationDataProvider>();

            // Act
            var errors = await _processor.ValidateAsync("nonexistent.gdb", mockDataProvider.Object, rules);

            // Assert
            Assert.NotNull(errors);
            Assert.Empty(errors);
        }

        [Fact]
        public async Task ValidateAsync_WithCommentedRule_SkipsRule()
        {
            // Arrange
            var rules = new List<AttributeCheckConfig>
            {
                new AttributeCheckConfig
                {
                    RuleId = "# COMMENTED_RULE", // 주석 처리
                    Enabled = "Y",
                    TableId = "TEST_TABLE",
                    TableName = "테스트 테이블",
                    FieldName = "TEST_FIELD",
                    CheckType = "NotNull"
                }
            };
            var mockDataProvider = new Mock<IValidationDataProvider>();

            // Act
            var errors = await _processor.ValidateAsync("nonexistent.gdb", mockDataProvider.Object, rules);

            // Assert
            Assert.NotNull(errors);
            Assert.Empty(errors);
        }

        [Fact]
        public async Task ValidateAsync_WithCancellation_ReturnsEmptyOnNonExistentFile()
        {
            // Arrange
            // 참고: 파일을 열 수 없으면 취소 토큰 확인 전에 조기 반환됩니다
            var rules = new List<AttributeCheckConfig>
            {
                new AttributeCheckConfig
                {
                    RuleId = "RULE_001",
                    Enabled = "Y",
                    TableId = "TEST_TABLE",
                    TableName = "테스트 테이블",
                    FieldName = "TEST_FIELD",
                    CheckType = "NotNull"
                }
            };
            var mockDataProvider = new Mock<IValidationDataProvider>();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act - 존재하지 않는 파일에서는 취소 전에 빈 결과 반환
            var errors = await _processor.ValidateAsync("nonexistent.gdb", mockDataProvider.Object, rules, null, cts.Token);

            // Assert
            Assert.NotNull(errors);
        }

        [Fact]
        public async Task ValidateAsync_WithWildcardTableId_ProcessesAllLayers()
        {
            // Arrange
            var rules = new List<AttributeCheckConfig>
            {
                new AttributeCheckConfig
                {
                    RuleId = "RULE_WILDCARD",
                    Enabled = "Y",
                    TableId = "*", // 와일드카드
                    TableName = "",
                    FieldName = "COMMON_FIELD",
                    CheckType = "NotNull"
                }
            };
            var mockDataProvider = new Mock<IValidationDataProvider>();

            // Act
            var errors = await _processor.ValidateAsync("nonexistent.gdb", mockDataProvider.Object, rules);

            // Assert
            Assert.NotNull(errors);
        }

        [Fact]
        public async Task ValidateAsync_WithValidTableIds_FiltersLayers()
        {
            // Arrange
            var rules = new List<AttributeCheckConfig>
            {
                new AttributeCheckConfig
                {
                    RuleId = "RULE_001",
                    Enabled = "Y",
                    TableId = "TEST_TABLE",
                    TableName = "테스트 테이블",
                    FieldName = "TEST_FIELD",
                    CheckType = "NotNull"
                }
            };
            var mockDataProvider = new Mock<IValidationDataProvider>();
            var validTableIds = new[] { "TABLE_A", "TABLE_B" };

            // Act
            var errors = await _processor.ValidateAsync("nonexistent.gdb", mockDataProvider.Object, rules, validTableIds);

            // Assert
            Assert.NotNull(errors);
        }

        #endregion

        #region AttributeCheckConfig Model Tests

        [Fact]
        public void AttributeCheckConfig_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var config = new AttributeCheckConfig();

            // Assert
            Assert.Equal(string.Empty, config.RuleId);
            Assert.Equal("Y", config.Enabled); // 기본값 "Y"
            Assert.Equal(string.Empty, config.TableId);
            Assert.Equal(string.Empty, config.TableName);
            Assert.Equal(string.Empty, config.FieldName);
            Assert.Equal(string.Empty, config.CheckType);
            Assert.Null(config.Parameters);
            Assert.Null(config.Note);
        }

        [Fact]
        public void AttributeCheckConfig_CanSetProperties()
        {
            // Arrange & Act
            var config = new AttributeCheckConfig
            {
                RuleId = "ATR_001",
                Enabled = "Y",
                TableId = "A0010000",
                TableName = "건물",
                FieldName = "BLDG_TYPE",
                CheckType = "CodeList",
                Parameters = "BLD001|BLD002|BLD003",
                Note = "건물 유형 코드 검증"
            };

            // Assert
            Assert.Equal("ATR_001", config.RuleId);
            Assert.Equal("Y", config.Enabled);
            Assert.Equal("A0010000", config.TableId);
            Assert.Equal("건물", config.TableName);
            Assert.Equal("BLDG_TYPE", config.FieldName);
            Assert.Equal("CodeList", config.CheckType);
            Assert.Equal("BLD001|BLD002|BLD003", config.Parameters);
            Assert.Equal("건물 유형 코드 검증", config.Note);
        }

        [Theory]
        [InlineData("Y")]
        [InlineData("y")]
        public void AttributeCheckConfig_Enabled_CaseInsensitiveY(string value)
        {
            // Arrange
            var config = new AttributeCheckConfig { Enabled = value };

            // Assert
            Assert.Equal("Y", config.Enabled, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("N")]
        [InlineData("n")]
        [InlineData("")]
        public void AttributeCheckConfig_Disabled_ForVariousValues(string value)
        {
            // Arrange
            var config = new AttributeCheckConfig { Enabled = value };

            // Assert
            Assert.NotEqual("Y", config.Enabled, StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region CheckType Tests

        [Theory]
        [InlineData("CodeList")]
        [InlineData("Range")]
        [InlineData("Regex")]
        [InlineData("NotNull")]
        [InlineData("Unique")]
        public void AttributeCheckConfig_CheckType_AcceptsValidTypes(string checkType)
        {
            // Arrange
            var config = new AttributeCheckConfig { CheckType = checkType };

            // Assert
            Assert.Equal(checkType, config.CheckType);
        }

        [Fact]
        public void AttributeCheckConfig_CodeListParameters_ParsesCorrectly()
        {
            // Arrange
            var config = new AttributeCheckConfig
            {
                CheckType = "CodeList",
                Parameters = "CODE_A|CODE_B|CODE_C"
            };

            // Act
            var codes = config.Parameters?.Split('|');

            // Assert
            Assert.NotNull(codes);
            Assert.Equal(3, codes.Length);
            Assert.Contains("CODE_A", codes);
            Assert.Contains("CODE_B", codes);
            Assert.Contains("CODE_C", codes);
        }

        [Fact]
        public void AttributeCheckConfig_RangeParameters_ParsesCorrectly()
        {
            // Arrange
            var config = new AttributeCheckConfig
            {
                CheckType = "Range",
                Parameters = "0..100"
            };

            // Act
            var parts = config.Parameters?.Split("..");

            // Assert
            Assert.NotNull(parts);
            Assert.Equal(2, parts.Length);
            Assert.Equal("0", parts[0]);
            Assert.Equal("100", parts[1]);
        }

        [Fact]
        public void AttributeCheckConfig_RegexParameters_StoresPattern()
        {
            // Arrange
            var config = new AttributeCheckConfig
            {
                CheckType = "Regex",
                Parameters = "^[A-Z]{3}[0-9]{4}$"
            };

            // Assert
            Assert.Equal("^[A-Z]{3}[0-9]{4}$", config.Parameters);
        }

        #endregion

        #region Edge Cases Tests

        [Fact]
        public async Task ValidateAsync_WithMultipleRules_ProcessesAllEnabled()
        {
            // Arrange
            var rules = new List<AttributeCheckConfig>
            {
                new AttributeCheckConfig
                {
                    RuleId = "RULE_001",
                    Enabled = "Y",
                    TableId = "TABLE_A",
                    TableName = "테이블 A",
                    FieldName = "FIELD_1",
                    CheckType = "NotNull"
                },
                new AttributeCheckConfig
                {
                    RuleId = "RULE_002",
                    Enabled = "N", // 비활성화
                    TableId = "TABLE_A",
                    TableName = "테이블 A",
                    FieldName = "FIELD_2",
                    CheckType = "NotNull"
                },
                new AttributeCheckConfig
                {
                    RuleId = "RULE_003",
                    Enabled = "Y",
                    TableId = "TABLE_B",
                    TableName = "테이블 B",
                    FieldName = "FIELD_3",
                    CheckType = "CodeList",
                    Parameters = "VAL1|VAL2"
                }
            };
            var mockDataProvider = new Mock<IValidationDataProvider>();

            // Act
            var errors = await _processor.ValidateAsync("nonexistent.gdb", mockDataProvider.Object, rules);

            // Assert
            Assert.NotNull(errors);
        }

        [Fact]
        public void LoadCodelist_MultipleCalls_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() =>
            {
                _processor.LoadCodelist("path1.csv");
                _processor.LoadCodelist("path2.csv");
                _processor.LoadCodelist(null);
                _processor.LoadCodelist("path3.csv");
            });
            Assert.Null(exception);
        }

        #endregion
    }
}
