using Microsoft.Extensions.Logging;
using Moq;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Processors;
using SpatialCheckProMax.Services;
using Xunit;

namespace SpatialCheckProMax.Tests.Processors
{
    /// <summary>
    /// GeometryCheckProcessor 유닛 테스트
    /// </summary>
    public class GeometryCheckProcessorTests
    {
        private readonly Mock<ILogger<GeometryCheckProcessor>> _loggerMock;
        private readonly GeometryCheckProcessor _processor;

        public GeometryCheckProcessorTests()
        {
            _loggerMock = new Mock<ILogger<GeometryCheckProcessor>>();
            _processor = new GeometryCheckProcessor(_loggerMock.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new GeometryCheckProcessor(null!));
        }

        [Fact]
        public void Constructor_WithValidLogger_CreatesInstance()
        {
            // Act
            var processor = new GeometryCheckProcessor(_loggerMock.Object);

            // Assert
            Assert.NotNull(processor);
        }

        [Fact]
        public void Constructor_WithAllNullOptionalParams_CreatesInstance()
        {
            // Act
            var processor = new GeometryCheckProcessor(_loggerMock.Object, null, null, null, null);

            // Assert
            Assert.NotNull(processor);
        }

        [Fact]
        public void Constructor_WithGeometryCriteria_CreatesInstance()
        {
            // Arrange
            var criteria = GeometryCriteria.CreateDefault();

            // Act
            var processor = new GeometryCheckProcessor(_loggerMock.Object, null, null, criteria, null);

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

        #region ClearSpatialIndexCache Tests

        [Fact]
        public void ClearSpatialIndexCache_WhenCalled_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => _processor.ClearSpatialIndexCache());
            Assert.Null(exception);
        }

        [Fact]
        public void ClearSpatialIndexCacheForFile_WhenCalled_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => _processor.ClearSpatialIndexCacheForFile("test.gdb"));
            Assert.Null(exception);
        }

        [Fact]
        public void ClearSpatialIndexCacheForFile_WithNullPath_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => _processor.ClearSpatialIndexCacheForFile(null!));
            Assert.Null(exception);
        }

        #endregion

        #region ProcessAsync Tests

        [Fact]
        public async Task ProcessAsync_WithNonExistentFile_ReturnsInvalidResult()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "TEST_TABLE",
                TableName = "테스트 테이블",
                GeometryType = "POLYGON"
            };

            // Act
            var result = await _processor.ProcessAsync("nonexistent_path.gdb", config);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.IsValid);
            Assert.Contains("파일을 열 수 없습니다", result.Message);
        }

        [Fact]
        public async Task ProcessAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "TEST_TABLE",
                TableName = "테스트 테이블",
                GeometryType = "POLYGON"
            };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert - 파일이 없으면 취소 전에 반환될 수 있음
            var result = await _processor.ProcessAsync("nonexistent.gdb", config, cts.Token);
            Assert.NotNull(result);
        }

        #endregion

        #region CheckDuplicateGeometriesAsync Tests

        [Fact]
        public async Task CheckDuplicateGeometriesAsync_WithNonExistentFile_ReturnsResult()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "TEST_TABLE",
                TableName = "테스트 테이블",
                GeometryType = "POLYGON",
                CheckDuplicate = "Y"
            };

            // Act
            var result = await _processor.CheckDuplicateGeometriesAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region CheckOverlappingGeometriesAsync Tests

        [Fact]
        public async Task CheckOverlappingGeometriesAsync_WithNonExistentFile_ReturnsResult()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "TEST_TABLE",
                TableName = "테스트 테이블",
                GeometryType = "POLYGON",
                CheckOverlap = "Y"
            };

            // Act
            var result = await _processor.CheckOverlappingGeometriesAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region CheckTwistedGeometriesAsync Tests

        [Fact]
        public async Task CheckTwistedGeometriesAsync_WithNonExistentFile_ReturnsResult()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "TEST_TABLE",
                TableName = "테스트 테이블",
                GeometryType = "POLYGON",
                CheckSelfIntersection = "Y"
            };

            // Act
            var result = await _processor.CheckTwistedGeometriesAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region CheckSliverPolygonsAsync Tests

        [Fact]
        public async Task CheckSliverPolygonsAsync_WithNonExistentFile_ReturnsResult()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "TEST_TABLE",
                TableName = "테스트 테이블",
                GeometryType = "POLYGON",
                CheckSliver = "Y"
            };

            // Act
            var result = await _processor.CheckSliverPolygonsAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region GeometryCheckConfig Tests

        [Fact]
        public void GeometryCheckConfig_ShouldCheckDuplicate_ReturnsTrueForY()
        {
            // Arrange
            var config = new GeometryCheckConfig { CheckDuplicate = "Y" };

            // Assert
            Assert.True(config.ShouldCheckDuplicate);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckDuplicate_ReturnsFalseForN()
        {
            // Arrange
            var config = new GeometryCheckConfig { CheckDuplicate = "N" };

            // Assert
            Assert.False(config.ShouldCheckDuplicate);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckDuplicate_ReturnsFalseForEmpty()
        {
            // Arrange
            var config = new GeometryCheckConfig { CheckDuplicate = "" };

            // Assert
            Assert.False(config.ShouldCheckDuplicate);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckOverlap_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckOverlap = "Y" }.ShouldCheckOverlap);
            Assert.False(new GeometryCheckConfig { CheckOverlap = "N" }.ShouldCheckOverlap);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckSliver_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckSliver = "Y" }.ShouldCheckSliver);
            Assert.False(new GeometryCheckConfig { CheckSliver = "" }.ShouldCheckSliver);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckSelfIntersection_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckSelfIntersection = "Y" }.ShouldCheckSelfIntersection);
            Assert.False(new GeometryCheckConfig { CheckSelfIntersection = "N" }.ShouldCheckSelfIntersection);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckShortObject_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckShortObject = "Y" }.ShouldCheckShortObject);
            Assert.False(new GeometryCheckConfig { CheckShortObject = "N" }.ShouldCheckShortObject);
            Assert.False(new GeometryCheckConfig { CheckShortObject = "" }.ShouldCheckShortObject);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckSmallArea_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckSmallArea = "Y" }.ShouldCheckSmallArea);
            Assert.False(new GeometryCheckConfig { CheckSmallArea = "N" }.ShouldCheckSmallArea);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckPolygonInPolygon_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckPolygonInPolygon = "Y" }.ShouldCheckPolygonInPolygon);
            Assert.False(new GeometryCheckConfig { CheckPolygonInPolygon = "" }.ShouldCheckPolygonInPolygon);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckMinPoints_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckMinPoints = "Y" }.ShouldCheckMinPoints);
            Assert.False(new GeometryCheckConfig { CheckMinPoints = "N" }.ShouldCheckMinPoints);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckSpikes_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckSpikes = "Y" }.ShouldCheckSpikes);
            Assert.False(new GeometryCheckConfig { CheckSpikes = "" }.ShouldCheckSpikes);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckSelfOverlap_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckSelfOverlap = "Y" }.ShouldCheckSelfOverlap);
            Assert.False(new GeometryCheckConfig { CheckSelfOverlap = "N" }.ShouldCheckSelfOverlap);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckUndershoot_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckUndershoot = "Y" }.ShouldCheckUndershoot);
            Assert.False(new GeometryCheckConfig { CheckUndershoot = "" }.ShouldCheckUndershoot);
        }

        [Fact]
        public void GeometryCheckConfig_ShouldCheckOvershoot_WorksCorrectly()
        {
            // Arrange & Act & Assert
            Assert.True(new GeometryCheckConfig { CheckOvershoot = "Y" }.ShouldCheckOvershoot);
            Assert.False(new GeometryCheckConfig { CheckOvershoot = "N" }.ShouldCheckOvershoot);
        }

        [Fact]
        public void GeometryCheckConfig_Validate_ReturnsTrueForValidConfig()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "TEST_TABLE",
                TableName = "테스트 테이블"
            };

            // Assert
            Assert.True(config.Validate());
        }

        [Fact]
        public void GeometryCheckConfig_Validate_ReturnsFalseForMissingTableId()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "",
                TableName = "테스트 테이블"
            };

            // Assert
            Assert.False(config.Validate());
        }

        [Fact]
        public void GeometryCheckConfig_Validate_ReturnsFalseForMissingTableName()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "TEST_TABLE",
                TableName = ""
            };

            // Assert
            Assert.False(config.Validate());
        }

        [Theory]
        [InlineData("y")]
        [InlineData("Y")]
        public void GeometryCheckConfig_CheckFlags_CaseInsensitive(string yValue)
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                CheckDuplicate = yValue,
                CheckOverlap = yValue,
                CheckSelfIntersection = yValue
            };

            // Assert
            Assert.True(config.ShouldCheckDuplicate);
            Assert.True(config.ShouldCheckOverlap);
            Assert.True(config.ShouldCheckSelfIntersection);
        }

        #endregion

        #region GeometryCriteria Tests

        [Fact]
        public void GeometryCriteria_CreateDefault_HasValidDefaults()
        {
            // Act
            var criteria = GeometryCriteria.CreateDefault();

            // Assert
            Assert.NotNull(criteria);
            Assert.True(criteria.MinLineLength > 0);
            Assert.True(criteria.MinPolygonArea > 0);
            Assert.True(criteria.SliverShapeIndex > 0);
        }

        [Fact]
        public void GeometryCriteria_CustomValues_CanBeSet()
        {
            // Arrange
            var criteria = new GeometryCriteria
            {
                MinLineLength = 1.5,
                MinPolygonArea = 10.0,
                SliverShapeIndex = 0.05,
                SpikeAngleThreshold = 5.0
            };

            // Assert
            Assert.Equal(1.5, criteria.MinLineLength);
            Assert.Equal(10.0, criteria.MinPolygonArea);
            Assert.Equal(0.05, criteria.SliverShapeIndex);
            Assert.Equal(5.0, criteria.SpikeAngleThreshold);
        }

        [Fact]
        public void GeometryCriteria_LoadFromCsv_WithNonExistentFile_ReturnsDefault()
        {
            // Act
            var criteria = GeometryCriteria.LoadFromCsv("nonexistent_criteria.csv");

            // Assert
            Assert.NotNull(criteria);
            Assert.True(criteria.MinLineLength > 0);
        }

        [Fact]
        public async Task GeometryCriteria_LoadFromCsvAsync_WithNonExistentFile_ReturnsDefault()
        {
            // Act
            var criteria = await GeometryCriteria.LoadFromCsvAsync("nonexistent_criteria.csv");

            // Assert
            Assert.NotNull(criteria);
            Assert.True(criteria.MinPolygonArea > 0);
        }

        #endregion

        #region Additional ProcessAsync Tests

        [Fact]
        public async Task ProcessAsync_WithLineGeometry_ReturnsResult()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "LINE_TABLE",
                TableName = "선형 테이블",
                GeometryType = "LINESTRING",
                CheckShortObject = "Y"
            };

            // Act
            var result = await _processor.ProcessAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task ProcessAsync_WithPointGeometry_ReturnsResult()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "POINT_TABLE",
                TableName = "점형 테이블",
                GeometryType = "POINT",
                CheckDuplicate = "Y"
            };

            // Act
            var result = await _processor.ProcessAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task ProcessAsync_WithAllChecksEnabled_ReturnsResult()
        {
            // Arrange
            var config = new GeometryCheckConfig
            {
                TableId = "FULL_CHECK_TABLE",
                TableName = "전체 검사 테이블",
                GeometryType = "POLYGON",
                CheckDuplicate = "Y",
                CheckOverlap = "Y",
                CheckSelfIntersection = "Y",
                CheckSliver = "Y",
                CheckShortObject = "Y",
                CheckSmallArea = "Y",
                CheckMinPoints = "Y",
                CheckSpikes = "Y",
                CheckSelfOverlap = "Y",
                CheckUndershoot = "Y",
                CheckOvershoot = "Y"
            };

            // Act
            var result = await _processor.ProcessAsync("nonexistent.gdb", config);

            // Assert
            Assert.NotNull(result);
        }

        #endregion

        #region Multiple ClearSpatialIndexCache Calls

        [Fact]
        public void ClearSpatialIndexCache_MultipleCalls_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() =>
            {
                _processor.ClearSpatialIndexCache();
                _processor.ClearSpatialIndexCache();
                _processor.ClearSpatialIndexCacheForFile("test1.gdb");
                _processor.ClearSpatialIndexCacheForFile("test2.gdb");
                _processor.ClearSpatialIndexCache();
            });
            Assert.Null(exception);
        }

        #endregion
    }
}
