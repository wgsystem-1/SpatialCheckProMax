using Microsoft.Extensions.Logging;
using Moq;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Services;
using Xunit;

namespace SpatialCheckProMax.Tests.Services
{
    /// <summary>
    /// CsvConfigService 유닛 테스트
    /// </summary>
    public class CsvConfigServiceTests : IDisposable
    {
        private readonly Mock<ILogger<CsvConfigService>> _loggerMock;
        private readonly CsvConfigService _service;
        private readonly string _testDirectory;

        public CsvConfigServiceTests()
        {
            _loggerMock = new Mock<ILogger<CsvConfigService>>();
            _service = new CsvConfigService(_loggerMock.Object);
            _testDirectory = Path.Combine(Path.GetTempPath(), $"CsvConfigServiceTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new CsvConfigService(null!));
        }

        #endregion

        #region LoadTableCheckConfigAsync Tests

        [Fact]
        public async Task LoadTableCheckConfigAsync_ValidFile_ReturnsConfigs()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "table_check.csv");
            var csvContent = @"TableId,TableName,GeometryType,CRS
TBL001,건물,POLYGON,EPSG:5179
TBL002,도로,LINESTRING,EPSG:5179";
            await File.WriteAllTextAsync(filePath, csvContent);

            // Act
            var result = await _service.LoadTableCheckConfigAsync(filePath);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal("TBL001", result[0].TableId);
            Assert.Equal("건물", result[0].TableName);
            Assert.Equal("POLYGON", result[0].GeometryType);
            Assert.Equal("EPSG:5179", result[0].CoordinateSystem);
        }

        [Fact]
        public async Task LoadTableCheckConfigAsync_WithCommentedLines_ExcludesComments()
        {
            // Arrange - # 으로 시작하는 줄은 제외되어야 함
            var filePath = Path.Combine(_testDirectory, "table_check_with_comments.csv");
            var csvContent = @"TableId,TableName,GeometryType,CRS
TBL001,건물,POLYGON,EPSG:5179
#TBL002,도로,LINESTRING,EPSG:5179
TBL003,하천,POLYGON,EPSG:5179";
            await File.WriteAllTextAsync(filePath, csvContent);

            // Act
            var result = await _service.LoadTableCheckConfigAsync(filePath);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal("TBL001", result[0].TableId);
            Assert.Equal("TBL003", result[1].TableId);
        }

        [Fact]
        public async Task LoadTableCheckConfigAsync_EmptyFile_ReturnsEmptyList()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "empty_table.csv");
            var csvContent = @"TableId,TableName,GeometryType,CRS";
            await File.WriteAllTextAsync(filePath, csvContent);

            // Act
            var result = await _service.LoadTableCheckConfigAsync(filePath);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task LoadTableCheckConfigAsync_NonExistentFile_ThrowsException()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "non_existent.csv");

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                _service.LoadTableCheckConfigAsync(filePath));
        }

        #endregion

        #region SaveTableCheckConfigAsync Tests

        [Fact]
        public async Task SaveTableCheckConfigAsync_ValidConfigs_SavesSuccessfully()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "saved_table.csv");
            var configs = new List<TableCheckConfig>
            {
                new TableCheckConfig
                {
                    TableId = "TBL001",
                    TableName = "건물",
                    GeometryType = "POLYGON",
                    CoordinateSystem = "EPSG:5179"
                }
            };

            // Act
            var result = await _service.SaveTableCheckConfigAsync(filePath, configs);

            // Assert
            Assert.True(result);
            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public async Task SaveAndLoadTableCheckConfig_RoundTrip_PreservesData()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "roundtrip_table.csv");
            var originalConfigs = new List<TableCheckConfig>
            {
                new TableCheckConfig
                {
                    TableId = "TBL001",
                    TableName = "건물",
                    GeometryType = "POLYGON",
                    CoordinateSystem = "EPSG:5179"
                },
                new TableCheckConfig
                {
                    TableId = "TBL002",
                    TableName = "도로",
                    GeometryType = "LINESTRING",
                    CoordinateSystem = "EPSG:4326"
                }
            };

            // Act
            await _service.SaveTableCheckConfigAsync(filePath, originalConfigs);
            var loadedConfigs = await _service.LoadTableCheckConfigAsync(filePath);

            // Assert
            Assert.Equal(originalConfigs.Count, loadedConfigs.Count);
            for (int i = 0; i < originalConfigs.Count; i++)
            {
                Assert.Equal(originalConfigs[i].TableId, loadedConfigs[i].TableId);
                Assert.Equal(originalConfigs[i].TableName, loadedConfigs[i].TableName);
                Assert.Equal(originalConfigs[i].GeometryType, loadedConfigs[i].GeometryType);
                Assert.Equal(originalConfigs[i].CoordinateSystem, loadedConfigs[i].CoordinateSystem);
            }
        }

        #endregion

        #region ValidateCsvFileAsync Tests

        [Fact]
        public async Task ValidateCsvFileAsync_NonExistentFile_ReturnsError()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "non_existent.csv");

            // Act
            var result = await _service.ValidateCsvFileAsync(filePath, ConfigType.TableCheck);

            // Assert
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
            Assert.Contains(result.Errors, e => e.Contains("존재하지 않습니다"));
        }

        [Fact]
        public async Task ValidateCsvFileAsync_EmptyFile_ReturnsError()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "empty.csv");
            await File.WriteAllTextAsync(filePath, "");

            // Act
            var result = await _service.ValidateCsvFileAsync(filePath, ConfigType.TableCheck);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("비어있습니다"));
        }

        [Fact]
        public async Task ValidateCsvFileAsync_ValidTableCheck_ReturnsValid()
        {
            // Arrange
            var filePath = Path.Combine(_testDirectory, "valid_table.csv");
            var csvContent = @"TableId,TableName,GeometryType,CRS
TBL001,건물,POLYGON,EPSG:5179";
            await File.WriteAllTextAsync(filePath, csvContent);

            // Act
            var result = await _service.ValidateCsvFileAsync(filePath, ConfigType.TableCheck);

            // Assert
            Assert.True(result.IsValid);
            Assert.Equal(1, result.RecordCount);
        }

        #endregion

        #region LoadValidationConfigAsync Tests

        [Fact]
        public async Task LoadValidationConfigAsync_WithTableCheckOnly_LoadsTableChecks()
        {
            // Arrange - 테이블 설정 파일만 생성
            await File.WriteAllTextAsync(
                Path.Combine(_testDirectory, "1_table_check.csv"),
                @"TableId,TableName,GeometryType,CRS
TBL001,건물,POLYGON,EPSG:5179
TBL002,도로,LINESTRING,EPSG:5179");

            // Act
            var result = await _service.LoadValidationConfigAsync(_testDirectory);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.TableChecks.Count);
            Assert.Equal("TBL001", result.TableChecks[0].TableId);
            Assert.Equal("TBL002", result.TableChecks[1].TableId);
        }

        [Fact]
        public async Task LoadValidationConfigAsync_MissingFiles_LoadsAvailableConfigs()
        {
            // Arrange - 일부 설정 파일만 생성
            await File.WriteAllTextAsync(
                Path.Combine(_testDirectory, "1_table_check.csv"),
                @"TableId,TableName,GeometryType,CRS
TBL001,건물,POLYGON,EPSG:5179");

            // Act
            var result = await _service.LoadValidationConfigAsync(_testDirectory);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.TableChecks);
            Assert.Empty(result.SchemaChecks);  // 파일 없음
            Assert.Empty(result.GeometryChecks);  // 파일 없음
            Assert.Empty(result.RelationChecks);  // 파일 없음
        }

        #endregion

        #region CreateDefaultConfigFilesAsync Tests

        [Fact]
        public async Task CreateDefaultConfigFilesAsync_CreatesDirectory_WhenNotExists()
        {
            // Arrange
            var newDirectory = Path.Combine(_testDirectory, "new_config_dir");

            // Act
            var result = await _service.CreateDefaultConfigFilesAsync(newDirectory);

            // Assert
            Assert.True(result);
            Assert.True(Directory.Exists(newDirectory));
        }

        #endregion
    }
}
