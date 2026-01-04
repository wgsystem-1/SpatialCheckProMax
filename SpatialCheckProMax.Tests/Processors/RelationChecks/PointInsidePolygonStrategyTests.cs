using System;
using Microsoft.Extensions.Logging;
using Moq;
using SpatialCheckProMax.Processors.RelationChecks;
using Xunit;

namespace SpatialCheckProMax.Tests.Processors.RelationChecks
{
    public class PointInsidePolygonStrategyTests
    {
        [Fact]
        public void CaseType_ShouldBe_PointInsidePolygon()
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();
            var strategy = new PointInsidePolygonStrategy(loggerMock.Object);

            // Act
            var caseType = strategy.CaseType;

            // Assert
            Assert.Equal("PointInsidePolygon", caseType);
        }
    }
}

