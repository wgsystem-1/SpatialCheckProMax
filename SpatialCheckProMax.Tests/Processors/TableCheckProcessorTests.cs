using Microsoft.Extensions.Logging;
using Moq;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Models.Enums;
using SpatialCheckProMax.Processors;
using Xunit;

namespace SpatialCheckProMax.Tests.Processors
{
    /// <summary>
    /// TableCheckProcessor 유닛 테스트
    /// </summary>
    public class TableCheckProcessorTests
    {
        private readonly Mock<ILogger<TableCheckProcessor>> _loggerMock;
        private readonly TableCheckProcessor _processor;

        public TableCheckProcessorTests()
        {
            _loggerMock = new Mock<ILogger<TableCheckProcessor>>();
            _processor = new TableCheckProcessor(_loggerMock.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new TableCheckProcessor(null!));
        }

        [Fact]
        public void Constructor_WithValidLogger_CreatesInstance()
        {
            // Act
            var processor = new TableCheckProcessor(_loggerMock.Object);

            // Assert
            Assert.NotNull(processor);
        }

        #endregion

        #region ValidateTableListAsync Tests

        [Fact]
        public async Task ValidateTableListAsync_AllTablesExist_ReturnsPassed()
        {
            // Arrange
            var spatialFile = CreateSpatialFileInfo(
                ("TABLE_A", "테이블A"),
                ("TABLE_B", "테이블B")
            );

            var configs = new List<TableCheckConfig>
            {
                new TableCheckConfig { TableId = "TABLE_A", TableName = "테이블A" },
                new TableCheckConfig { TableId = "TABLE_B", TableName = "테이블B" }
            };

            // Act
            var result = await _processor.ValidateTableListAsync(spatialFile, configs);

            // Assert
            Assert.Equal(CheckStatus.Passed, result.Status);
            Assert.Equal(0, result.ErrorCount);
            Assert.Equal(0, result.WarningCount);
        }

        [Fact]
        public async Task ValidateTableListAsync_MissingTable_ReturnsFailed()
        {
            // Arrange
            var spatialFile = CreateSpatialFileInfo(
                ("TABLE_A", "테이블A")
            );

            var configs = new List<TableCheckConfig>
            {
                new TableCheckConfig { TableId = "TABLE_A", TableName = "테이블A" },
                new TableCheckConfig { TableId = "TABLE_B", TableName = "테이블B" }  // 파일에 없음
            };

            // Act
            var result = await _processor.ValidateTableListAsync(spatialFile, configs);

            // Assert
            Assert.Equal(CheckStatus.Failed, result.Status);
            Assert.Equal(1, result.ErrorCount);
            Assert.Contains(result.Errors, e => e.TableName == "테이블B");
        }

        [Fact]
        public async Task ValidateTableListAsync_ExtraTableInFile_ReturnsWarning()
        {
            // Arrange
            var spatialFile = CreateSpatialFileInfo(
                ("TABLE_A", "테이블A"),
                ("TABLE_B", "테이블B"),
                ("TABLE_C", "테이블C")  // 설정에 없음
            );

            var configs = new List<TableCheckConfig>
            {
                new TableCheckConfig { TableId = "TABLE_A", TableName = "테이블A" },
                new TableCheckConfig { TableId = "TABLE_B", TableName = "테이블B" }
            };

            // Act
            var result = await _processor.ValidateTableListAsync(spatialFile, configs);

            // Assert
            Assert.Equal(CheckStatus.Passed, result.Status);  // 경고만 있으므로 Passed
            Assert.Equal(0, result.ErrorCount);
            Assert.Equal(1, result.WarningCount);
            Assert.Contains(result.Warnings, w => w.TableName == "테이블C");
        }

        [Fact]
        public async Task ValidateTableListAsync_ORG_PrefixTable_IsExcluded()
        {
            // Arrange - ORG_ 접두사 테이블은 ArcGIS Pro 백업이므로 제외되어야 함
            var spatialFile = CreateSpatialFileInfo(
                ("TABLE_A", "테이블A"),
                ("ORG_TABLE_A", "ORG_테이블A")  // 백업 테이블
            );

            var configs = new List<TableCheckConfig>
            {
                new TableCheckConfig { TableId = "TABLE_A", TableName = "테이블A" }
            };

            // Act
            var result = await _processor.ValidateTableListAsync(spatialFile, configs);

            // Assert
            Assert.Equal(CheckStatus.Passed, result.Status);
            Assert.Equal(0, result.ErrorCount);
            Assert.Equal(0, result.WarningCount);  // ORG_ 테이블은 경고 없음
        }

        [Fact]
        public async Task ValidateTableListAsync_CaseInsensitiveMatch_Works()
        {
            // Arrange - 대소문자 구분 없이 매칭되어야 함
            var spatialFile = CreateSpatialFileInfo(
                ("table_a", "테이블A")  // 소문자
            );

            var configs = new List<TableCheckConfig>
            {
                new TableCheckConfig { TableId = "TABLE_A", TableName = "테이블A" }  // 대문자
            };

            // Act
            var result = await _processor.ValidateTableListAsync(spatialFile, configs);

            // Assert
            Assert.Equal(CheckStatus.Passed, result.Status);
            Assert.Equal(0, result.ErrorCount);
        }

        [Fact]
        public async Task ValidateTableListAsync_EmptyConfig_ReturnsPassed()
        {
            // Arrange
            var spatialFile = CreateSpatialFileInfo(
                ("TABLE_A", "테이블A")
            );

            var configs = new List<TableCheckConfig>();

            // Act
            var result = await _processor.ValidateTableListAsync(spatialFile, configs);

            // Assert
            Assert.Equal(CheckStatus.Passed, result.Status);
            Assert.Equal(0, result.TotalCount);
        }

        [Fact]
        public async Task ValidateTableListAsync_EmptyFile_ReturnsFailed()
        {
            // Arrange
            var spatialFile = CreateSpatialFileInfo();  // 테이블 없음

            var configs = new List<TableCheckConfig>
            {
                new TableCheckConfig { TableId = "TABLE_A", TableName = "테이블A" }
            };

            // Act
            var result = await _processor.ValidateTableListAsync(spatialFile, configs);

            // Assert
            Assert.Equal(CheckStatus.Failed, result.Status);
            Assert.Equal(1, result.ErrorCount);
        }

        [Fact]
        public async Task ValidateTableListAsync_MatchByTableName_Works()
        {
            // Arrange - TableId가 다르지만 TableName으로 매칭
            var spatialFile = new SpatialFileInfo
            {
                FilePath = "test.gdb",
                Tables = new List<TableInfo>
                {
                    new TableInfo { TableId = "DIFFERENT_ID", TableName = "테이블A" }
                }
            };

            var configs = new List<TableCheckConfig>
            {
                new TableCheckConfig { TableId = "TABLE_A", TableName = "테이블A" }
            };

            // Act
            var result = await _processor.ValidateTableListAsync(spatialFile, configs);

            // Assert
            Assert.Equal(CheckStatus.Passed, result.Status);
        }

        #endregion

        #region Helper Methods

        private static SpatialFileInfo CreateSpatialFileInfo(params (string Id, string Name)[] tables)
        {
            return new SpatialFileInfo
            {
                FilePath = "test.gdb",
                Tables = tables.Select(t => new TableInfo
                {
                    TableId = t.Id,
                    TableName = t.Name
                }).ToList()
            };
        }

        #endregion
    }
}
