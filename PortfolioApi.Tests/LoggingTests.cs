using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Moq;

using PortfolioApi.Exceptions;

using Xunit;

namespace PortfolioApi.Tests;

public class LoggingTests
{
    [Fact]
    public async Task Handler_Logs_Unhandled_Exceptions_At_Error_Level()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<GlobalExceptionHandler>>();
        var handler = new GlobalExceptionHandler(mockLogger.Object);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/test";
        var exception = new Exception("Test system crash");

        // Act
        var result = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(result);
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Unhandled exception")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}