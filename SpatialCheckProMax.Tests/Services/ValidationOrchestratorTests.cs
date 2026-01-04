using SpatialCheckProMax.Models;
using SpatialCheckProMax.Models.Enums;
using Xunit;

namespace SpatialCheckProMax.Tests.Services
{
    /// <summary>
    /// ValidationOrchestrator 관련 이벤트 인자 유닛 테스트
    /// GUI 프로젝트는 Windows 전용이므로 핵심 모델 테스트에 집중
    /// </summary>
    public class ValidationOrchestratorTests
    {
        #region ValidationResult Integration Tests

        [Fact]
        public void ValidationResult_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var result = new ValidationResult();

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(0, result.ErrorCount);
            Assert.Equal(0, result.WarningCount);
        }

        [Fact]
        public void ValidationResult_CanSetProperties()
        {
            // Arrange & Act
            var result = new ValidationResult
            {
                IsValid = true,
                ErrorCount = 5,
                WarningCount = 10,
                TargetFile = "test.gdb",
                Message = "Test message"
            };

            // Assert
            Assert.True(result.IsValid);
            Assert.Equal(5, result.ErrorCount);
            Assert.Equal(10, result.WarningCount);
            Assert.Equal("test.gdb", result.TargetFile);
            Assert.Equal("Test message", result.Message);
        }

        #endregion

        #region TableCheckResult Tests

        [Fact]
        public void TableCheckResult_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var result = new TableCheckResult();

            // Assert
            Assert.Equal(0, result.TotalTableCount);
            Assert.NotNull(result.TableResults);
        }

        #endregion

        #region SchemaCheckResult Tests

        [Fact]
        public void SchemaCheckResult_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var result = new SchemaCheckResult();

            // Assert
            Assert.Equal(0, result.TotalColumnCount);
        }

        #endregion

        #region ValidationError Tests

        [Fact]
        public void ValidationError_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var error = new ValidationError();

            // Assert
            Assert.NotNull(error.ErrorId);
            Assert.Equal(string.Empty, error.Message);
            Assert.Equal(string.Empty, error.TableName);
        }

        [Fact]
        public void ValidationError_CanSetProperties()
        {
            // Arrange & Act
            var error = new ValidationError
            {
                TableId = "TestTable",
                TableName = "Test Table Name",
                FieldName = "TestColumn",
                Message = "Test error message",
                FeatureId = "123",
                Severity = ErrorSeverity.Error,
                ErrorType = ErrorType.Geometry
            };

            // Assert
            Assert.Equal("TestTable", error.TableId);
            Assert.Equal("Test Table Name", error.TableName);
            Assert.Equal("TestColumn", error.FieldName);
            Assert.Equal("Test error message", error.Message);
            Assert.Equal("123", error.FeatureId);
            Assert.Equal(ErrorSeverity.Error, error.Severity);
            Assert.Equal(ErrorType.Geometry, error.ErrorType);
        }

        [Fact]
        public void ValidationError_Location_CanBeSet()
        {
            // Arrange
            var error = new ValidationError();
            var location = new GeographicLocation(127.0, 37.0);

            // Act
            error.Location = location;

            // Assert
            Assert.NotNull(error.Location);
        }

        #endregion

        #region StageResult Tests

        [Fact]
        public void StageResult_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var result = new StageResult();

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(0, result.ErrorCount);
            Assert.Equal(0, result.WarningCount);
            Assert.NotNull(result.CheckResults);
            Assert.NotNull(result.Errors);
            Assert.NotNull(result.Warnings);
        }

        [Fact]
        public void StageResult_IsSuccess_ReturnsFalseWhenHasErrors()
        {
            // Arrange
            var result = new StageResult();
            result.Status = StageStatus.Completed;
            result.CheckResults.Add(new CheckResult { ErrorCount = 1 });

            // Assert
            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void StageResult_IsSuccess_ReturnsTrueWhenNoErrors()
        {
            // Arrange
            var result = new StageResult();
            result.Status = StageStatus.Completed;

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void StageResult_TotalErrors_SumsFromCheckResults()
        {
            // Arrange
            var result = new StageResult();
            result.CheckResults.Add(new CheckResult { ErrorCount = 5 });
            result.CheckResults.Add(new CheckResult { ErrorCount = 3 });

            // Assert
            Assert.Equal(8, result.TotalErrors);
            Assert.Equal(8, result.ErrorCount);
        }

        #endregion

        #region ProcessingTime Tests

        [Fact]
        public void ValidationResult_ProcessingTime_CanBeSet()
        {
            // Arrange
            var duration = TimeSpan.FromSeconds(30);
            var result = new ValidationResult();

            // Act
            result.ProcessingTime = duration;

            // Assert
            Assert.Equal(duration, result.ProcessingTime);
        }

        #endregion

        #region Errors Collection Tests

        [Fact]
        public void ValidationResult_Errors_IsInitialized()
        {
            // Arrange & Act
            var result = new ValidationResult();

            // Assert
            Assert.NotNull(result.Errors);
        }

        [Fact]
        public void ValidationResult_Errors_CanAddItems()
        {
            // Arrange
            var result = new ValidationResult();
            var error = new ValidationError { Message = "Test" };

            // Act
            result.Errors.Add(error);

            // Assert
            Assert.Single(result.Errors);
            Assert.Same(error, result.Errors[0]);
        }

        #endregion

        #region ErrorSeverity Enum Tests

        [Fact]
        public void ErrorSeverity_HasExpectedValues()
        {
            // Assert
            Assert.True(Enum.IsDefined(typeof(ErrorSeverity), ErrorSeverity.Error));
            Assert.True(Enum.IsDefined(typeof(ErrorSeverity), ErrorSeverity.Warning));
            Assert.True(Enum.IsDefined(typeof(ErrorSeverity), ErrorSeverity.Info));
        }

        #endregion

        #region ErrorType Enum Tests

        [Fact]
        public void ErrorType_HasExpectedValues()
        {
            // Assert
            Assert.True(Enum.IsDefined(typeof(ErrorType), ErrorType.System));
            Assert.True(Enum.IsDefined(typeof(ErrorType), ErrorType.Geometry));
            Assert.True(Enum.IsDefined(typeof(ErrorType), ErrorType.Schema));
            Assert.True(Enum.IsDefined(typeof(ErrorType), ErrorType.Relation));
        }

        #endregion

        #region StageStatus Enum Tests

        [Fact]
        public void StageStatus_HasExpectedValues()
        {
            // Assert
            Assert.True(Enum.IsDefined(typeof(StageStatus), StageStatus.NotStarted));
            Assert.True(Enum.IsDefined(typeof(StageStatus), StageStatus.Running));
            Assert.True(Enum.IsDefined(typeof(StageStatus), StageStatus.Completed));
        }

        #endregion
    }
}
