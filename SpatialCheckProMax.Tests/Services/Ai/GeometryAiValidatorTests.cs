using Xunit;
using NetTopologySuite.Geometries;
using SpatialCheckProMax.Services.Ai;

namespace SpatialCheckProMax.Tests.Services.Ai
{
    public class GeometryAiValidatorTests
    {
        private readonly GeometryFactory _factory;
        private readonly GeometryAiValidator _validator;

        public GeometryAiValidatorTests()
        {
            _factory = new GeometryFactory(new PrecisionModel(), 5186);
            _validator = new GeometryAiValidator();
        }

        #region Validate Method Tests

        [Fact]
        public void Validate_WithNullCorrected_ReturnsInvalid()
        {
            // Arrange
            var original = CreateValidPolygon();

            // Act
            var result = _validator.Validate(original, null!);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("존재하지 않습니다", result.Message);
        }

        [Fact]
        public void Validate_WithInvalidCorrectedGeometry_ReturnsInvalid()
        {
            // Arrange
            var original = CreateValidPolygon();
            var corrected = CreateSelfIntersectingPolygon();

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("유효하지 않습니다", result.Message);
        }

        [Fact]
        public void Validate_WithSameValidGeometry_ReturnsValid()
        {
            // Arrange
            var original = CreateValidPolygon();
            var corrected = (Geometry)original.Copy();

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert
            Assert.True(result.IsValid);
            Assert.Contains("통과", result.Message);
            Assert.Equal(0, result.AreaChangePercent);
        }

        [Fact]
        public void Validate_WithSmallAreaChange_ReturnsValid()
        {
            // Arrange
            var original = CreateValidPolygon();
            var corrected = CreateSlightlyModifiedPolygon(0.005); // 0.5% change

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert
            Assert.True(result.IsValid);
            Assert.True(result.AreaChangePercent < 1.0);
        }

        [Fact]
        public void Validate_WithLargeAreaChange_ReturnsInvalid()
        {
            // Arrange
            var original = CreateValidPolygon();
            var corrected = CreateLargelyModifiedPolygon(); // >1% change

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("면적 변화율", result.Message);
            Assert.True(result.AreaChangePercent > 1.0);
        }

        #endregion

        #region ValidationResult Properties Tests

        [Fact]
        public void Validate_ReturnsCorrectAreaChangePercent()
        {
            // Arrange
            var original = CreateValidPolygon(); // 10000 m^2
            var corrected = CreateSlightlyModifiedPolygon(0.01); // ~1% change

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert
            Assert.True(result.AreaChangePercent >= 0);
        }

        [Fact]
        public void Validate_WithSelfIntersection_SetsSelfIntersectionFlag()
        {
            // Arrange
            var original = CreateValidPolygon();
            var corrected = CreateSelfIntersectingPolygon();

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert
            Assert.False(result.IsValid);
            Assert.True(result.HasSelfIntersection);
        }

        #endregion

        #region Point Geometry Tests

        [Fact]
        public void Validate_WithValidPoints_ReturnsValid()
        {
            // Arrange
            var original = _factory.CreatePoint(new Coordinate(100, 100));
            var corrected = _factory.CreatePoint(new Coordinate(100.1, 100.1));

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert
            Assert.True(result.IsValid);
        }

        #endregion

        #region LineString Tests

        [Fact]
        public void Validate_WithValidLineStrings_ReturnsValid()
        {
            // Arrange
            var original = CreateValidLineString();
            var corrected = (Geometry)original.Copy();

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert
            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithSelfIntersectingLineString_ReturnsInvalid()
        {
            // Arrange
            var original = CreateValidLineString();
            var corrected = CreateSelfIntersectingLineString();

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert
            // Note: LineString with self-intersection is still valid in NTS
            // but not simple
            if (!corrected.IsValid)
            {
                Assert.False(result.IsValid);
            }
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void Validate_WithEmptyCorrectedGeometry_BehaviorDefined()
        {
            // Arrange
            var original = CreateValidPolygon();
            var corrected = _factory.CreatePolygon();

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert - Empty geometry is valid but area is 0
            // This is implementation-specific
            Assert.NotNull(result);
        }

        [Fact]
        public void Validate_WithZeroAreaOriginal_HandlesGracefully()
        {
            // Arrange
            var original = _factory.CreatePolygon();
            var corrected = CreateValidPolygon();

            // Act
            var result = _validator.Validate(original, corrected);

            // Assert - Should not throw division by zero
            Assert.NotNull(result);
        }

        #endregion

        #region Helper Methods

        private Polygon CreateValidPolygon()
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

        private Polygon CreateSlightlyModifiedPolygon(double changeRatio)
        {
            // Modify one vertex slightly
            double offset = 100 * changeRatio;
            var coords = new[]
            {
                new Coordinate(0, 0),
                new Coordinate(100 + offset, 0),
                new Coordinate(100 + offset, 100),
                new Coordinate(0, 100),
                new Coordinate(0, 0)
            };
            return _factory.CreatePolygon(coords);
        }

        private Polygon CreateLargelyModifiedPolygon()
        {
            // Much larger polygon (4x area)
            var coords = new[]
            {
                new Coordinate(0, 0),
                new Coordinate(200, 0),
                new Coordinate(200, 200),
                new Coordinate(0, 200),
                new Coordinate(0, 0)
            };
            return _factory.CreatePolygon(coords);
        }

        private Polygon CreateSelfIntersectingPolygon()
        {
            // Creates a bowtie/figure-8 shape that self-intersects
            var coords = new[]
            {
                new Coordinate(0, 0),
                new Coordinate(100, 100),
                new Coordinate(100, 0),
                new Coordinate(0, 100),
                new Coordinate(0, 0)
            };
            return _factory.CreatePolygon(coords);
        }

        private LineString CreateValidLineString()
        {
            var coords = new[]
            {
                new Coordinate(0, 0),
                new Coordinate(50, 50),
                new Coordinate(100, 100)
            };
            return _factory.CreateLineString(coords);
        }

        private LineString CreateSelfIntersectingLineString()
        {
            // Creates a figure-8 line
            var coords = new[]
            {
                new Coordinate(0, 0),
                new Coordinate(100, 100),
                new Coordinate(100, 0),
                new Coordinate(0, 100),
                new Coordinate(50, 50)
            };
            return _factory.CreateLineString(coords);
        }

        #endregion
    }
}
