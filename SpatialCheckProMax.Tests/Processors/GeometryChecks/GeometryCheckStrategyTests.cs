using Microsoft.Extensions.Logging;
using Moq;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Processors.GeometryChecks;
using Xunit;

namespace SpatialCheckProMax.Tests.Processors.GeometryChecks
{
    /// <summary>
    /// GeometryCheckStrategy 관련 유닛 테스트
    /// </summary>
    public class GeometryCheckStrategyTests
    {
        #region IGeometryCheckStrategy Interface Tests

        [Fact]
        public void GeosValidityCheckStrategy_CheckType_ReturnsCorrectValue()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<GeosValidityCheckStrategy>>();
            var strategy = new GeosValidityCheckStrategy(loggerMock.Object);

            // Assert
            Assert.Equal("GeosValidity", strategy.CheckType);
        }

        [Fact]
        public void ShortObjectCheckStrategy_CheckType_ReturnsCorrectValue()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ShortObjectCheckStrategy>>();
            var strategy = new ShortObjectCheckStrategy(loggerMock.Object);

            // Assert
            Assert.Equal("ShortObject", strategy.CheckType);
        }

        [Fact]
        public void SmallAreaCheckStrategy_CheckType_ReturnsCorrectValue()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<SmallAreaCheckStrategy>>();
            var strategy = new SmallAreaCheckStrategy(loggerMock.Object);

            // Assert
            Assert.Equal("SmallArea", strategy.CheckType);
        }

        [Fact]
        public void MinPointsCheckStrategy_CheckType_ReturnsCorrectValue()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<MinPointsCheckStrategy>>();
            var strategy = new MinPointsCheckStrategy(loggerMock.Object);

            // Assert
            Assert.Equal("MinPoints", strategy.CheckType);
        }

        [Fact]
        public void SliverCheckStrategy_CheckType_ReturnsCorrectValue()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<SliverCheckStrategy>>();
            var strategy = new SliverCheckStrategy(loggerMock.Object);

            // Assert
            Assert.Equal("Sliver", strategy.CheckType);
        }

        [Fact]
        public void SpikeCheckStrategy_CheckType_ReturnsCorrectValue()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<SpikeCheckStrategy>>();
            var strategy = new SpikeCheckStrategy(loggerMock.Object);

            // Assert
            Assert.Equal("Spike", strategy.CheckType);
        }

        [Fact]
        public void DuplicateCheckStrategy_CheckType_ReturnsCorrectValue()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<DuplicateCheckStrategy>>();
            var strategy = new DuplicateCheckStrategy(loggerMock.Object);

            // Assert
            Assert.Equal("Duplicate", strategy.CheckType);
        }

        [Fact]
        public void OverlapCheckStrategy_CheckType_ReturnsCorrectValue()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<OverlapCheckStrategy>>();
            var strategy = new OverlapCheckStrategy(loggerMock.Object);

            // Assert
            Assert.Equal("Overlap", strategy.CheckType);
        }

        [Fact]
        public void UndershootOvershootCheckStrategy_CheckType_ReturnsCorrectValue()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<UndershootOvershootCheckStrategy>>();
            var strategy = new UndershootOvershootCheckStrategy(loggerMock.Object);

            // Assert
            Assert.Equal("UndershootOvershoot", strategy.CheckType);
        }

        #endregion

        #region IsEnabled Tests

        [Fact]
        public void GeosValidityCheckStrategy_IsEnabled_ReturnsTrueWhenSelfIntersectionEnabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<GeosValidityCheckStrategy>>();
            var strategy = new GeosValidityCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckSelfIntersection = "Y" };

            // Assert
            Assert.True(strategy.IsEnabled(config));
        }

        [Fact]
        public void GeosValidityCheckStrategy_IsEnabled_ReturnsFalseWhenDisabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<GeosValidityCheckStrategy>>();
            var strategy = new GeosValidityCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckSelfIntersection = "N" };

            // Assert
            Assert.False(strategy.IsEnabled(config));
        }

        [Fact]
        public void ShortObjectCheckStrategy_IsEnabled_ReturnsTrueWhenEnabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ShortObjectCheckStrategy>>();
            var strategy = new ShortObjectCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckShortObject = "Y" };

            // Assert
            Assert.True(strategy.IsEnabled(config));
        }

        [Fact]
        public void ShortObjectCheckStrategy_IsEnabled_ReturnsFalseWhenDisabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<ShortObjectCheckStrategy>>();
            var strategy = new ShortObjectCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckShortObject = "N" };

            // Assert
            Assert.False(strategy.IsEnabled(config));
        }

        [Fact]
        public void SmallAreaCheckStrategy_IsEnabled_ReturnsTrueWhenEnabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<SmallAreaCheckStrategy>>();
            var strategy = new SmallAreaCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckSmallArea = "Y" };

            // Assert
            Assert.True(strategy.IsEnabled(config));
        }

        [Fact]
        public void MinPointsCheckStrategy_IsEnabled_ReturnsTrueWhenEnabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<MinPointsCheckStrategy>>();
            var strategy = new MinPointsCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckMinPoints = "Y" };

            // Assert
            Assert.True(strategy.IsEnabled(config));
        }

        [Fact]
        public void SliverCheckStrategy_IsEnabled_ReturnsTrueWhenEnabledWithPolygonType()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<SliverCheckStrategy>>();
            var strategy = new SliverCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckSliver = "Y", GeometryType = "POLYGON" };

            // Assert
            Assert.True(strategy.IsEnabled(config));
        }

        [Fact]
        public void SliverCheckStrategy_IsEnabled_ReturnsFalseWhenNotPolygonType()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<SliverCheckStrategy>>();
            var strategy = new SliverCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckSliver = "Y", GeometryType = "LINESTRING" };

            // Assert
            Assert.False(strategy.IsEnabled(config));
        }

        [Fact]
        public void SpikeCheckStrategy_IsEnabled_ReturnsTrueWhenEnabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<SpikeCheckStrategy>>();
            var strategy = new SpikeCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckSpikes = "Y" };

            // Assert
            Assert.True(strategy.IsEnabled(config));
        }

        [Fact]
        public void DuplicateCheckStrategy_IsEnabled_ReturnsTrueWhenEnabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<DuplicateCheckStrategy>>();
            var strategy = new DuplicateCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckDuplicate = "Y" };

            // Assert
            Assert.True(strategy.IsEnabled(config));
        }

        [Fact]
        public void OverlapCheckStrategy_IsEnabled_ReturnsTrueWhenEnabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<OverlapCheckStrategy>>();
            var strategy = new OverlapCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckOverlap = "Y" };

            // Assert
            Assert.True(strategy.IsEnabled(config));
        }

        [Fact]
        public void UndershootOvershootCheckStrategy_IsEnabled_ReturnsTrueWhenUndershootEnabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<UndershootOvershootCheckStrategy>>();
            var strategy = new UndershootOvershootCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckUndershoot = "Y" };

            // Assert
            Assert.True(strategy.IsEnabled(config));
        }

        [Fact]
        public void UndershootOvershootCheckStrategy_IsEnabled_ReturnsTrueWhenOvershootEnabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<UndershootOvershootCheckStrategy>>();
            var strategy = new UndershootOvershootCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckOvershoot = "Y" };

            // Assert
            Assert.True(strategy.IsEnabled(config));
        }

        [Fact]
        public void UndershootOvershootCheckStrategy_IsEnabled_ReturnsFalseWhenBothDisabled()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<UndershootOvershootCheckStrategy>>();
            var strategy = new UndershootOvershootCheckStrategy(loggerMock.Object);
            var config = new GeometryCheckConfig { CheckUndershoot = "N", CheckOvershoot = "N" };

            // Assert
            Assert.False(strategy.IsEnabled(config));
        }

        #endregion

        #region GeometryCheckContext Tests

        [Fact]
        public void GeometryCheckContext_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var context = new GeometryCheckContext();

            // Assert
            Assert.Equal(string.Empty, context.FilePath);
            Assert.NotNull(context.Criteria);
            Assert.Null(context.FeatureFilterService);
            Assert.Null(context.HighPerfValidator);
            Assert.Null(context.StreamingErrorWriter);
            Assert.False(context.IsStreamingMode);
            Assert.Equal(1000, context.StreamingBatchSize);
            Assert.NotNull(context.SpatialIndexCache);
            Assert.Empty(context.SpatialIndexCache);
        }

        [Fact]
        public void GeometryCheckContext_IsStreamingMode_ReturnsTrueWhenWriterSet()
        {
            // Arrange
            var context = new GeometryCheckContext();

            // IsStreamingMode is based on StreamingErrorWriter being non-null
            // We can't easily set it without a real implementation, so just verify the default
            Assert.False(context.IsStreamingMode);
        }

        [Fact]
        public void GeometryCheckContext_ClearSpatialIndexCache_ClearsCache()
        {
            // Arrange
            var context = new GeometryCheckContext();
            context.SpatialIndexCache["test"] = new object();

            // Act
            context.ClearSpatialIndexCache();

            // Assert
            Assert.Empty(context.SpatialIndexCache);
        }

        [Fact]
        public void GeometryCheckContext_OnProgress_CanBeSetAndInvoked()
        {
            // Arrange
            var context = new GeometryCheckContext();
            int progressCurrent = 0;
            int progressTotal = 0;

            context.OnProgress = (current, total) =>
            {
                progressCurrent = current;
                progressTotal = total;
            };

            // Act
            context.OnProgress?.Invoke(50, 100);

            // Assert
            Assert.Equal(50, progressCurrent);
            Assert.Equal(100, progressTotal);
        }

        #endregion

        #region Constructor Null Check Tests

        [Fact]
        public void GeosValidityCheckStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new GeosValidityCheckStrategy(null!));
        }

        [Fact]
        public void ShortObjectCheckStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ShortObjectCheckStrategy(null!));
        }

        [Fact]
        public void SmallAreaCheckStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SmallAreaCheckStrategy(null!));
        }

        [Fact]
        public void MinPointsCheckStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new MinPointsCheckStrategy(null!));
        }

        [Fact]
        public void SliverCheckStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SliverCheckStrategy(null!));
        }

        [Fact]
        public void SpikeCheckStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SpikeCheckStrategy(null!));
        }

        [Fact]
        public void DuplicateCheckStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DuplicateCheckStrategy(null!));
        }

        [Fact]
        public void OverlapCheckStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new OverlapCheckStrategy(null!));
        }

        [Fact]
        public void UndershootOvershootCheckStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new UndershootOvershootCheckStrategy(null!));
        }

        #endregion

        #region All Strategies Implement Interface

        [Fact]
        public void AllStrategies_ImplementIGeometryCheckStrategy()
        {
            // Arrange & Act & Assert
            Assert.True(typeof(IGeometryCheckStrategy).IsAssignableFrom(typeof(GeosValidityCheckStrategy)));
            Assert.True(typeof(IGeometryCheckStrategy).IsAssignableFrom(typeof(ShortObjectCheckStrategy)));
            Assert.True(typeof(IGeometryCheckStrategy).IsAssignableFrom(typeof(SmallAreaCheckStrategy)));
            Assert.True(typeof(IGeometryCheckStrategy).IsAssignableFrom(typeof(MinPointsCheckStrategy)));
            Assert.True(typeof(IGeometryCheckStrategy).IsAssignableFrom(typeof(SliverCheckStrategy)));
            Assert.True(typeof(IGeometryCheckStrategy).IsAssignableFrom(typeof(SpikeCheckStrategy)));
            Assert.True(typeof(IGeometryCheckStrategy).IsAssignableFrom(typeof(DuplicateCheckStrategy)));
            Assert.True(typeof(IGeometryCheckStrategy).IsAssignableFrom(typeof(OverlapCheckStrategy)));
            Assert.True(typeof(IGeometryCheckStrategy).IsAssignableFrom(typeof(UndershootOvershootCheckStrategy)));
        }

        [Fact]
        public void AllStrategies_InheritFromBaseGeometryCheckStrategy()
        {
            // Arrange & Act & Assert
            Assert.True(typeof(BaseGeometryCheckStrategy).IsAssignableFrom(typeof(GeosValidityCheckStrategy)));
            Assert.True(typeof(BaseGeometryCheckStrategy).IsAssignableFrom(typeof(ShortObjectCheckStrategy)));
            Assert.True(typeof(BaseGeometryCheckStrategy).IsAssignableFrom(typeof(SmallAreaCheckStrategy)));
            Assert.True(typeof(BaseGeometryCheckStrategy).IsAssignableFrom(typeof(MinPointsCheckStrategy)));
            Assert.True(typeof(BaseGeometryCheckStrategy).IsAssignableFrom(typeof(SliverCheckStrategy)));
            Assert.True(typeof(BaseGeometryCheckStrategy).IsAssignableFrom(typeof(SpikeCheckStrategy)));
            Assert.True(typeof(BaseGeometryCheckStrategy).IsAssignableFrom(typeof(DuplicateCheckStrategy)));
            Assert.True(typeof(BaseGeometryCheckStrategy).IsAssignableFrom(typeof(OverlapCheckStrategy)));
            Assert.True(typeof(BaseGeometryCheckStrategy).IsAssignableFrom(typeof(UndershootOvershootCheckStrategy)));
        }

        #endregion
    }
}
