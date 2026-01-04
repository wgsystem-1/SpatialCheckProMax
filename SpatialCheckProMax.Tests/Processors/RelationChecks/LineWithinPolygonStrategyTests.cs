using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using OSGeo.OGR;
using SpatialCheckProMax.Processors.RelationChecks;
using SpatialCheckProMax.Services;
using Xunit;

namespace SpatialCheckProMax.Tests.Processors.RelationChecks
{
    public class LineWithinPolygonStrategyTests
    {
        [Fact]
        public void CaseType_ShouldBe_LineWithinPolygon()
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();
            var cache = new Dictionary<string, Geometry?>();
            var timestamps = new Dictionary<string, DateTime>();
            var strategy = new LineWithinPolygonStrategy(
                loggerMock.Object, 
                null, 
                cache, 
                timestamps, 
                0.001);

            // Act
            var caseType = strategy.CaseType;

            // Assert
            Assert.Equal("LineWithinPolygon", caseType);
        }
    }
}

