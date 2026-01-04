using Microsoft.Extensions.Logging;
using Moq;
using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Config;
using SpatialCheckProMax.Processors;
using ConfigPerformanceSettings = SpatialCheckProMax.Models.Config.PerformanceSettings;
using SpatialCheckProMax.Processors.RelationChecks;
using SpatialCheckProMax.Services;
using Xunit;

namespace SpatialCheckProMax.Tests.Processors
{
    /// <summary>
    /// RelationCheckProcessor 유닛 테스트
    /// </summary>
    public class RelationCheckProcessorTests : IDisposable
    {
        private readonly Mock<ILogger<RelationCheckProcessor>> _loggerMock;
        private readonly RelationCheckProcessor _processor;

        public RelationCheckProcessorTests()
        {
            _loggerMock = new Mock<ILogger<RelationCheckProcessor>>();
            var geometryCriteria = GeometryCriteria.CreateDefault();
            var performanceSettings = new ConfigPerformanceSettings();

            _processor = new RelationCheckProcessor(
                _loggerMock.Object,
                geometryCriteria,
                null,
                performanceSettings,
                null,
                null);
        }

        public void Dispose()
        {
            _processor?.Dispose();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            // Assert
            Assert.NotNull(_processor);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsOnDispose()
        {
            // Note: Constructor doesn't validate null logger, but Dispose calls logger
            // This test documents that logger is required for proper operation
            // Arrange
            var geometryCriteria = GeometryCriteria.CreateDefault();

            // Act
            var processor = new RelationCheckProcessor(null!, geometryCriteria);

            // Assert
            Assert.NotNull(processor);
            Assert.Throws<ArgumentNullException>(() => processor.Dispose());
        }

        [Fact]
        public void Constructor_WithNullGeometryCriteria_UsesDefault()
        {
            // Arrange & Act
            using var processor = new RelationCheckProcessor(_loggerMock.Object, null);

            // Assert
            Assert.NotNull(processor);
        }

        #endregion

        #region ProcessAsync Tests

        [Fact]
        public async Task ProcessAsync_WithNonExistentFile_ReturnsInvalidResult()
        {
            // Arrange
            var config = new RelationCheckConfig
            {
                RuleId = "TEST_RULE",
                CaseType = "PointInsidePolygon",
                MainTableId = "TestTable"
            };

            // Act
            var result = await _processor.ProcessAsync(
                @"C:\NonExistent\Path\fake.gdb",
                config,
                CancellationToken.None);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("열 수 없습니다", result.Message);
        }

        [Fact]
        public async Task ProcessAsync_WithCancellation_ReturnsInvalidResult()
        {
            // Note: When file doesn't exist, ProcessAsync returns invalid result
            // before cancellation can be checked
            // Arrange
            var config = new RelationCheckConfig
            {
                RuleId = "TEST_RULE",
                CaseType = "PointInsidePolygon"
            };
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = await _processor.ProcessAsync(@"C:\test.gdb", config, cts.Token);

            // Assert - file open failure happens before cancellation check
            Assert.False(result.IsValid);
        }

        #endregion

        #region ClearCache Tests

        [Fact]
        public void ClearCache_WhenCalled_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => _processor.ClearCache());
            Assert.Null(exception);
        }

        #endregion

        #region ProgressUpdated Event Tests

        [Fact]
        public void ProgressUpdated_CanSubscribe_DoesNotThrow()
        {
            // Arrange
            bool eventRaised = false;
            _processor.ProgressUpdated += (sender, args) => eventRaised = true;

            // Assert - no exception during subscription
            Assert.False(eventRaised); // Event not raised yet
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
    }

    /// <summary>
    /// SharpBendCheckStrategy 유닛 테스트
    /// </summary>
    public class SharpBendCheckStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public SharpBendCheckStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_WithContourCaseType_SetsCaseTypeCorrectly()
        {
            // Arrange & Act
            var strategy = new SharpBendCheckStrategy(_loggerMock.Object, "ContourSharpBend");

            // Assert
            Assert.Equal("ContourSharpBend", strategy.CaseType);
        }

        [Fact]
        public void Constructor_WithRoadCaseType_SetsCaseTypeCorrectly()
        {
            // Arrange & Act
            var strategy = new SharpBendCheckStrategy(_loggerMock.Object, "RoadSharpBend");

            // Assert
            Assert.Equal("RoadSharpBend", strategy.CaseType);
        }

        [Fact]
        public void Constructor_WithDefaultCaseType_UsesContourSharpBend()
        {
            // Arrange & Act
            var strategy = new SharpBendCheckStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("ContourSharpBend", strategy.CaseType);
        }
    }

    /// <summary>
    /// BuildingCenterPointsStrategy 유닛 테스트
    /// </summary>
    public class BuildingCenterPointsStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public BuildingCenterPointsStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new BuildingCenterPointsStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsBuildingCenterPoints()
        {
            // Arrange
            var strategy = new BuildingCenterPointsStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("BuildingCenterPoints", strategy.CaseType);
        }
    }

    /// <summary>
    /// PointInsidePolygonStrategy 유닛 테스트
    /// </summary>
    public class PointInsidePolygonStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public PointInsidePolygonStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new PointInsidePolygonStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsPointInsidePolygon()
        {
            // Arrange
            var strategy = new PointInsidePolygonStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("PointInsidePolygon", strategy.CaseType);
        }
    }

    /// <summary>
    /// LineWithinPolygonStrategy 유닛 테스트
    /// </summary>
    public class LineWithinPolygonStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public LineWithinPolygonStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var unionCache = new Dictionary<string, OSGeo.OGR.Geometry?>();
            var timestamps = new Dictionary<string, DateTime>();
            var strategy = new LineWithinPolygonStrategy(
                _loggerMock.Object,
                null,
                unionCache,
                timestamps,
                0.001);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsLineWithinPolygon()
        {
            // Arrange
            var unionCache = new Dictionary<string, OSGeo.OGR.Geometry?>();
            var timestamps = new Dictionary<string, DateTime>();
            var strategy = new LineWithinPolygonStrategy(
                _loggerMock.Object,
                null,
                unionCache,
                timestamps,
                0.001);

            // Assert
            Assert.Equal("LineWithinPolygon", strategy.CaseType);
        }
    }

    /// <summary>
    /// PolygonBoundaryMatchStrategy 유닛 테스트
    /// </summary>
    public class PolygonBoundaryMatchStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public PolygonBoundaryMatchStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new PolygonBoundaryMatchStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsPolygonBoundaryMatch()
        {
            // Arrange
            var strategy = new PolygonBoundaryMatchStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("PolygonBoundaryMatch", strategy.CaseType);
        }
    }

    /// <summary>
    /// ContourIntersectionStrategy 유닛 테스트
    /// </summary>
    public class ContourIntersectionStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public ContourIntersectionStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new ContourIntersectionStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsContourIntersection()
        {
            // Arrange
            var strategy = new ContourIntersectionStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("ContourIntersection", strategy.CaseType);
        }
    }

    /// <summary>
    /// PolygonNotContainPointStrategy 유닛 테스트
    /// </summary>
    public class PolygonNotContainPointStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public PolygonNotContainPointStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new PolygonNotContainPointStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsPolygonNotContainPoint()
        {
            // Arrange
            var strategy = new PolygonNotContainPointStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("PolygonNotContainPoint", strategy.CaseType);
        }
    }

    /// <summary>
    /// PolygonMissingLineStrategy 유닛 테스트
    /// </summary>
    public class PolygonMissingLineStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public PolygonMissingLineStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new PolygonMissingLineStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsPolygonMissingLine()
        {
            // Arrange
            var strategy = new PolygonMissingLineStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("PolygonMissingLine", strategy.CaseType);
        }
    }

    /// <summary>
    /// PolygonNoOverlapStrategy 유닛 테스트
    /// </summary>
    public class PolygonNoOverlapStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public PolygonNoOverlapStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new PolygonNoOverlapStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsPolygonNotOverlap()
        {
            // Arrange
            var strategy = new PolygonNoOverlapStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("PolygonNotOverlap", strategy.CaseType);
        }
    }

    /// <summary>
    /// PolygonNotIntersectLineStrategy 유닛 테스트
    /// </summary>
    public class PolygonNotIntersectLineStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public PolygonNotIntersectLineStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new PolygonNotIntersectLineStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsPolygonNotIntersectLine()
        {
            // Arrange
            var strategy = new PolygonNotIntersectLineStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("PolygonNotIntersectLine", strategy.CaseType);
        }
    }

    /// <summary>
    /// LineConnectivityStrategy 유닛 테스트
    /// </summary>
    public class LineConnectivityStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public LineConnectivityStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new LineConnectivityStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsLineConnectivity()
        {
            // Arrange
            var strategy = new LineConnectivityStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("LineConnectivity", strategy.CaseType);
        }
    }

    /// <summary>
    /// PolygonWithinPolygonStrategy 유닛 테스트
    /// </summary>
    public class PolygonWithinPolygonStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public PolygonWithinPolygonStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new PolygonWithinPolygonStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsPolygonWithinPolygon()
        {
            // Arrange
            var strategy = new PolygonWithinPolygonStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("PolygonWithinPolygon", strategy.CaseType);
        }
    }

    /// <summary>
    /// PolygonContainsLineStrategy 유닛 테스트
    /// </summary>
    public class PolygonContainsLineStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public PolygonContainsLineStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new PolygonContainsLineStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsPolygonContainsLine()
        {
            // Arrange
            var strategy = new PolygonContainsLineStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("PolygonContainsLine", strategy.CaseType);
        }
    }

    /// <summary>
    /// LineEndpointWithinPolygonStrategy 유닛 테스트
    /// </summary>
    public class LineEndpointWithinPolygonStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public LineEndpointWithinPolygonStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            // Arrange & Act
            var strategy = new LineEndpointWithinPolygonStrategy(_loggerMock.Object);

            // Assert
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsLineEndpointWithinPolygon()
        {
            // Arrange
            var strategy = new LineEndpointWithinPolygonStrategy(_loggerMock.Object);

            // Assert
            Assert.Equal("LineEndpointWithinPolygon", strategy.CaseType);
        }
    }

    /// <summary>
    /// ConnectedLinesSameAttributeStrategy 유닛 테스트
    /// </summary>
    public class ConnectedLinesSameAttributeStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public ConnectedLinesSameAttributeStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            var strategy = new ConnectedLinesSameAttributeStrategy(_loggerMock.Object);
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsConnectedLinesSameAttribute()
        {
            var strategy = new ConnectedLinesSameAttributeStrategy(_loggerMock.Object);
            Assert.Equal("ConnectedLinesSameAttribute", strategy.CaseType);
        }
    }

    /// <summary>
    /// LineDisconnectionStrategy 유닛 테스트
    /// </summary>
    public class LineDisconnectionStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public LineDisconnectionStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            var strategy = new LineDisconnectionStrategy(_loggerMock.Object);
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsLineDisconnection()
        {
            var strategy = new LineDisconnectionStrategy(_loggerMock.Object);
            Assert.Equal("LineDisconnection", strategy.CaseType);
        }
    }

    /// <summary>
    /// LineDisconnectionWithAttributeStrategy 유닛 테스트
    /// </summary>
    public class LineDisconnectionWithAttributeStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public LineDisconnectionWithAttributeStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            var strategy = new LineDisconnectionWithAttributeStrategy(_loggerMock.Object);
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsLineDisconnectionWithAttribute()
        {
            var strategy = new LineDisconnectionWithAttributeStrategy(_loggerMock.Object);
            Assert.Equal("LineDisconnectionWithAttribute", strategy.CaseType);
        }
    }

    /// <summary>
    /// DefectiveConnectionStrategy 유닛 테스트
    /// </summary>
    public class DefectiveConnectionStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public DefectiveConnectionStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            var strategy = new DefectiveConnectionStrategy(_loggerMock.Object);
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsDefectiveConnection()
        {
            var strategy = new DefectiveConnectionStrategy(_loggerMock.Object);
            Assert.Equal("DefectiveConnection", strategy.CaseType);
        }
    }

    /// <summary>
    /// LineIntersectionWithAttributeStrategy 유닛 테스트
    /// </summary>
    public class LineIntersectionWithAttributeStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public LineIntersectionWithAttributeStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            var strategy = new LineIntersectionWithAttributeStrategy(_loggerMock.Object);
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsLineIntersectionWithAttribute()
        {
            var strategy = new LineIntersectionWithAttributeStrategy(_loggerMock.Object);
            Assert.Equal("LineIntersectionWithAttribute", strategy.CaseType);
        }
    }

    /// <summary>
    /// PolygonIntersectionWithAttributeStrategy 유닛 테스트
    /// </summary>
    public class PolygonIntersectionWithAttributeStrategyTests
    {
        private readonly Mock<ILogger> _loggerMock;

        public PolygonIntersectionWithAttributeStrategyTests()
        {
            _loggerMock = new Mock<ILogger>();
        }

        [Fact]
        public void Constructor_CreatesInstance()
        {
            var strategy = new PolygonIntersectionWithAttributeStrategy(_loggerMock.Object);
            Assert.NotNull(strategy);
        }

        [Fact]
        public void CaseType_ReturnsPolygonIntersectionWithAttribute()
        {
            var strategy = new PolygonIntersectionWithAttributeStrategy(_loggerMock.Object);
            Assert.Equal("PolygonIntersectionWithAttribute", strategy.CaseType);
        }
    }
}
