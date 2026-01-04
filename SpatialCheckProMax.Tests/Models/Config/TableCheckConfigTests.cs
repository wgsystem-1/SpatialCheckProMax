using SpatialCheckProMax.Models.Config;
using Xunit;

namespace SpatialCheckProMax.Tests.Models.Config
{
    /// <summary>
    /// TableCheckConfig 모델 유닛 테스트
    /// </summary>
    public class TableCheckConfigTests
    {
        #region IsValidGeometryType Tests

        [Theory]
        [InlineData("POINT", true)]
        [InlineData("LINESTRING", true)]
        [InlineData("POLYGON", true)]
        [InlineData("MULTIPOINT", true)]
        [InlineData("MULTILINESTRING", true)]
        [InlineData("MULTIPOLYGON", true)]
        [InlineData("Point", true)]      // 대소문자 무시
        [InlineData("polygon", true)]    // 소문자
        [InlineData("INVALID", false)]
        [InlineData("CIRCLE", false)]
        [InlineData("", false)]
        public void IsValidGeometryType_ReturnsExpectedResult(string geometryType, bool expected)
        {
            // Arrange
            var config = new TableCheckConfig { GeometryType = geometryType };

            // Act
            var result = config.IsValidGeometryType();

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region IsEpsgCode Tests

        [Theory]
        [InlineData("EPSG:5179", true)]
        [InlineData("EPSG:4326", true)]
        [InlineData("epsg:5179", true)]  // 대소문자 무시
        [InlineData("WGS84", false)]
        [InlineData("5179", false)]
        [InlineData("", false)]
        public void IsEpsgCode_ReturnsExpectedResult(string coordinateSystem, bool expected)
        {
            // Arrange
            var config = new TableCheckConfig { CoordinateSystem = coordinateSystem };

            // Act
            var result = config.IsEpsgCode();

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region GetEpsgCode Tests

        [Theory]
        [InlineData("EPSG:5179", 5179)]
        [InlineData("EPSG:4326", 4326)]
        [InlineData("EPSG:32652", 32652)]
        public void GetEpsgCode_ValidCode_ReturnsCode(string coordinateSystem, int expectedCode)
        {
            // Arrange
            var config = new TableCheckConfig { CoordinateSystem = coordinateSystem };

            // Act
            var result = config.GetEpsgCode();

            // Assert
            Assert.Equal(expectedCode, result);
        }

        [Theory]
        [InlineData("WGS84")]
        [InlineData("5179")]
        [InlineData("EPSG:invalid")]
        [InlineData("")]
        public void GetEpsgCode_InvalidCode_ReturnsNull(string coordinateSystem)
        {
            // Arrange
            var config = new TableCheckConfig { CoordinateSystem = coordinateSystem };

            // Act
            var result = config.GetEpsgCode();

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region Validate Tests

        [Fact]
        public void Validate_ValidConfig_ReturnsEmptyList()
        {
            // Arrange
            var config = new TableCheckConfig
            {
                TableId = "TBL001",
                TableName = "테스트테이블",
                GeometryType = "POLYGON",
                CoordinateSystem = "EPSG:5179"
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void Validate_EmptyTableId_ReturnsError()
        {
            // Arrange
            var config = new TableCheckConfig
            {
                TableId = "",
                TableName = "테스트테이블",
                GeometryType = "POLYGON",
                CoordinateSystem = "EPSG:5179"
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.NotEmpty(result);
            Assert.Contains(result, r => r.MemberNames.Contains("TableId"));
        }

        [Fact]
        public void Validate_InvalidGeometryType_ReturnsError()
        {
            // Arrange
            var config = new TableCheckConfig
            {
                TableId = "TBL001",
                TableName = "테스트테이블",
                GeometryType = "INVALID_TYPE",
                CoordinateSystem = "EPSG:5179"
            };

            // Act
            var result = config.Validate();

            // Assert
            Assert.NotEmpty(result);
            Assert.Contains(result, r => r.MemberNames.Contains("GeometryType"));
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void ToString_ReturnsFormattedString()
        {
            // Arrange
            var config = new TableCheckConfig
            {
                TableId = "TBL001",
                TableName = "건물",
                GeometryType = "POLYGON",
                CoordinateSystem = "EPSG:5179"
            };

            // Act
            var result = config.ToString();

            // Assert
            Assert.Contains("TBL001", result);
            Assert.Contains("건물", result);
            Assert.Contains("POLYGON", result);
            Assert.Contains("EPSG:5179", result);
        }

        #endregion
    }
}
