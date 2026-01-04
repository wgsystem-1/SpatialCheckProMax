using Xunit;
using NetTopologySuite.Geometries;
using SpatialCheckProMax.Services.Ai;
using Microsoft.Extensions.Logging;
using Moq;

namespace SpatialCheckProMax.Tests.Services.Ai
{
    public class GeometryAiCorrectorTests
    {
        private readonly GeometryFactory _factory;
        private readonly Mock<ILogger> _mockLogger;

        public GeometryAiCorrectorTests()
        {
            _factory = new GeometryFactory(new PrecisionModel(), 5186);
            _mockLogger = new Mock<ILogger>();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullPath_DisablesModel()
        {
            // Act
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);

            // Assert
            Assert.False(corrector.IsModelLoaded);
            Assert.Equal("unknown", corrector.ModelVersion);
        }

        [Fact]
        public void Constructor_WithEmptyPath_DisablesModel()
        {
            // Act
            var corrector = new GeometryAiCorrector("", _mockLogger.Object);

            // Assert
            Assert.False(corrector.IsModelLoaded);
        }

        [Fact]
        public void Constructor_WithNonExistentPath_DisablesModel()
        {
            // Act
            var corrector = new GeometryAiCorrector("nonexistent/model.onnx", _mockLogger.Object);

            // Assert
            Assert.False(corrector.IsModelLoaded);
        }

        [Fact]
        public void Constructor_WithNullLogger_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => new GeometryAiCorrector(null, null));
            Assert.Null(exception);
        }

        #endregion

        #region Correct Method Tests

        [Fact]
        public void Correct_WithNullGeometry_ReturnsNull()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);

            // Act
            var result = corrector.Correct(null!);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Correct_WithEmptyGeometry_ReturnsOriginal()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);
            var emptyPolygon = _factory.CreatePolygon();

            // Act
            var result = corrector.Correct(emptyPolygon);

            // Assert
            Assert.Same(emptyPolygon, result);
        }

        [Fact]
        public void Correct_WithModelNotLoaded_ReturnsOriginal()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);
            var polygon = CreateSimplePolygon();

            // Act
            var result = corrector.Correct(polygon);

            // Assert
            Assert.Same(polygon, result);
        }

        [Fact]
        public void Correct_WithTooManyVertices_ReturnsOriginal()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object, maxVertices: 10);
            var polygon = CreatePolygonWithManyVertices(20);

            // Act
            var result = corrector.Correct(polygon);

            // Assert
            Assert.Same(polygon, result);
        }

        #endregion

        #region CorrectBatch Tests

        [Fact]
        public void CorrectBatch_WithEmptyCollection_ReturnsEmpty()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);

            // Act
            var results = corrector.CorrectBatch(Array.Empty<Geometry>()).ToList();

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public void CorrectBatch_WithModelNotLoaded_ReturnsOriginals()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);
            var geometries = new Geometry[] { CreateSimplePolygon(), CreateSimpleLineString() };

            // Act
            var results = corrector.CorrectBatch(geometries).ToList();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Same(geometries[0], results[0]);
            Assert.Same(geometries[1], results[1]);
        }

        #endregion

        #region GetCorrectionConfidence Tests

        [Fact]
        public void GetCorrectionConfidence_WithNullInput_ReturnsZero()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);

            // Act
            var confidence = corrector.GetCorrectionConfidence(null!, CreateSimplePolygon());

            // Assert
            Assert.Equal(0.0, confidence);
        }

        [Fact]
        public void GetCorrectionConfidence_WithNullCorrected_ReturnsZero()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);

            // Act
            var confidence = corrector.GetCorrectionConfidence(CreateSimplePolygon(), null!);

            // Assert
            Assert.Equal(0.0, confidence);
        }

        [Fact]
        public void GetCorrectionConfidence_WithDifferentVertexCount_ReturnsZero()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);
            var input = CreateSimplePolygon();
            var corrected = CreatePolygonWithManyVertices(10);

            // Act
            var confidence = corrector.GetCorrectionConfidence(input, corrected);

            // Assert
            Assert.Equal(0.0, confidence);
        }

        [Fact]
        public void GetCorrectionConfidence_WithIdenticalGeometries_ReturnsOne()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);
            var polygon = CreateSimplePolygon();
            var copy = (Geometry)polygon.Copy();

            // Act
            var confidence = corrector.GetCorrectionConfidence(polygon, copy);

            // Assert
            Assert.Equal(1.0, confidence);
        }

        [Fact]
        public void GetCorrectionConfidence_WithSmallOffset_ReturnsHighConfidence()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);
            var input = CreateSimplePolygon();
            var corrected = CreateOffsetPolygon(0.5); // 0.5m offset

            // Act
            var confidence = corrector.GetCorrectionConfidence(input, corrected);

            // Assert
            Assert.True(confidence > 0.9);
        }

        [Fact]
        public void GetCorrectionConfidence_WithLargeOffset_ReturnsLowConfidence()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);
            var input = CreateSimplePolygon();
            var corrected = CreateOffsetPolygon(15); // 15m offset

            // Act
            var confidence = corrector.GetCorrectionConfidence(input, corrected);

            // Assert
            Assert.True(confidence < 0.5);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_MultipleCalls_DoesNotThrow()
        {
            // Arrange
            var corrector = new GeometryAiCorrector(null, _mockLogger.Object);

            // Act & Assert
            var exception = Record.Exception(() =>
            {
                corrector.Dispose();
                corrector.Dispose();
            });
            Assert.Null(exception);
        }

        #endregion

        #region Helper Methods

        private Polygon CreateSimplePolygon()
        {
            var coords = new[]
            {
                new Coordinate(0, 0),
                new Coordinate(100, 0),
                new Coordinate(100, 100),
                new Coordinate(0, 100),
                new Coordinate(0, 0)
            };
            return _factory.CreatePolygon(coords);
        }

        private LineString CreateSimpleLineString()
        {
            var coords = new[]
            {
                new Coordinate(0, 0),
                new Coordinate(50, 50),
                new Coordinate(100, 100)
            };
            return _factory.CreateLineString(coords);
        }

        private Polygon CreatePolygonWithManyVertices(int vertexCount)
        {
            var coords = new Coordinate[vertexCount + 1];
            for (int i = 0; i < vertexCount; i++)
            {
                double angle = 2 * Math.PI * i / vertexCount;
                coords[i] = new Coordinate(
                    50 + 50 * Math.Cos(angle),
                    50 + 50 * Math.Sin(angle)
                );
            }
            coords[vertexCount] = coords[0]; // Close polygon
            return _factory.CreatePolygon(coords);
        }

        private Polygon CreateOffsetPolygon(double offset)
        {
            var coords = new[]
            {
                new Coordinate(0 + offset, 0 + offset),
                new Coordinate(100 + offset, 0 + offset),
                new Coordinate(100 + offset, 100 + offset),
                new Coordinate(0 + offset, 100 + offset),
                new Coordinate(0 + offset, 0 + offset)
            };
            return _factory.CreatePolygon(coords);
        }

        #endregion
    }
}
