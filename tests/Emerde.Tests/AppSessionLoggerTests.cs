using Emerde.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Emerde.Tests;

public sealed class AppSessionLoggerTests
{
    [Fact]
    public void GetSessionLogPaths_UsesStartupTimestampAndProcessId()
    {
        string directory = Path.Combine("D:\\", "logs");
        DateTime startedAt = new(2026, 7, 22, 23, 59, 58, DateTimeKind.Local);

        (string filePath, string errorFilePath) = AppSessionLogger.GetSessionLogPaths(
            directory,
            startedAt,
            new DateTime(2026, 7, 22, 23, 59, 59, DateTimeKind.Local),
            21460);

        Assert.Equal(Path.Combine(directory, "20260722_235958_21460.log"), filePath);
        Assert.Equal(Path.Combine(directory, "20260722_235958_21460.error.log"), errorFilePath);
    }

    [Fact]
    public void GetSessionLogPaths_AddsDateWhenSessionCrossesMidnight()
    {
        string directory = Path.Combine("D:\\", "logs");
        DateTime startedAt = new(2026, 7, 22, 23, 59, 58, DateTimeKind.Local);

        (string firstFilePath, _) = AppSessionLogger.GetSessionLogPaths(
            directory,
            startedAt,
            new DateTime(2026, 7, 22, 23, 59, 59, DateTimeKind.Local),
            21460);
        (string secondFilePath, string secondErrorFilePath) = AppSessionLogger.GetSessionLogPaths(
            directory,
            startedAt,
            new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Local),
            21460);

        Assert.NotEqual(firstFilePath, secondFilePath);
        Assert.Equal(Path.Combine(directory, "20260722_235958_21460_20260723.log"), secondFilePath);
        Assert.Equal(Path.Combine(directory, "20260722_235958_21460_20260723.error.log"), secondErrorFilePath);
    }

    [Fact]
    public void BuildSessionHeader_StoresSharedEventContextOnce()
    {
        DateTime startedAt = new(2026, 7, 22, 23, 59, 58, 123, DateTimeKind.Local);
        const string filePath = "D:\\logs\\20260722_235958_21460.log";
        const string errorFilePath = "D:\\logs\\20260722_235958_21460.error.log";

        string header = AppSessionLogger.BuildSessionHeader(
            startedAt,
            new DateTime(2026, 7, 22),
            21460,
            filePath,
            errorFilePath);
        using JsonDocument document = JsonDocument.Parse(header);
        JsonElement root = document.RootElement;

        Assert.Equal("session", root.GetProperty("type").GetString());
        Assert.Equal(3, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Emerde", root.GetProperty("application").GetString());
        Assert.Equal("2026-07-22 23:59:58.123", root.GetProperty("startedAt").GetString());
        Assert.Equal("2026-07-22", root.GetProperty("logDate").GetString());
        Assert.Equal(21460, root.GetProperty("processId").GetInt32());
        Assert.Equal(filePath, root.GetProperty("file").GetString());
        Assert.Equal(errorFilePath, root.GetProperty("errorFile").GetString());
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(30, 30)]
    [InlineData(5000, 3650)]
    public void NormalizeRetentionDays_ClampsConfiguredValue(int value, int expected)
    {
        Assert.Equal(expected, AppSessionLogger.NormalizeRetentionDays(value));
    }

    [Fact]
    public void IsDisabledForDate_OnlySuppressesTheFailedLocalDate()
    {
        DateTime failedDate = new(2026, 7, 22);

        Assert.True(AppSessionLogger.IsDisabledForDate(new DateTime(2026, 7, 22, 23, 59, 59), failedDate));
        Assert.False(AppSessionLogger.IsDisabledForDate(new DateTime(2026, 7, 23), failedDate));
    }

    [Theory]
    [InlineData("info", false)]
    [InlineData("warn", false)]
    [InlineData("error", true)]
    [InlineData("fatal", true)]
    public void ShouldWriteToErrorLog_OnlyIncludesErrors(string level, bool expected)
    {
        Assert.Equal(expected, AppSessionLogger.ShouldWriteToErrorLog(level));
    }

    [Fact]
    public void LogContextCompactor_ReusesRoomAndErrorTextReferences()
    {
        LogContextCompactor compactor = new();
        DateTime date = new(2026, 7, 23);
        JsonNode first = JsonSerializer.SerializeToNode(new
        {
            RoomUrl = "https://live.douyin.com/72024000076",
            NickName = "(~3_3)~ 7hz",
            errorOutput = "Stream ends prematurely",
        })!;
        JsonNode second = JsonSerializer.SerializeToNode(new
        {
            RoomUrl = "https://live.douyin.com/72024000076",
            NickName = "(~3_3)~ 7hz",
            errorOutput = "Stream ends prematurely",
        })!;

        JsonObject firstResult = Assert.IsType<JsonObject>(compactor.Compact(first, "warn", date));
        JsonObject secondResult = Assert.IsType<JsonObject>(compactor.Compact(second, "warn", date));

        Assert.Equal("r1", firstResult["roomRef"]!.GetValue<string>());
        Assert.NotNull(firstResult["roomContext"]);
        Assert.Equal("e1", firstResult["errorOutputRef"]!.GetValue<string>());
        Assert.Equal("Stream ends prematurely", firstResult["errorOutput"]!.GetValue<string>());
        Assert.Equal("r1", secondResult["roomRef"]!.GetValue<string>());
        Assert.Null(secondResult["roomContext"]);
        Assert.Equal("e1", secondResult["errorOutputRef"]!.GetValue<string>());
        Assert.Null(secondResult["errorOutput"]);
        Assert.Null(secondResult["RoomUrl"]);
        Assert.Null(secondResult["NickName"]);
    }

    [Fact]
    public void LogContextCompactor_DefinesReferencesAgainForErrorLog()
    {
        LogContextCompactor compactor = new();
        DateTime date = new(2026, 7, 23);
        JsonNode warning = JsonSerializer.SerializeToNode(new
        {
            RoomUrl = "https://live.douyin.com/72024000076",
            NickName = "(~3_3)~ 7hz",
            stackTrace = "shared failure",
        })!;
        JsonNode error = warning.DeepClone();

        _ = compactor.Compact(warning, "warn", date);
        JsonObject errorResult = Assert.IsType<JsonObject>(compactor.Compact(error, "error", date));

        Assert.NotNull(errorResult["roomContext"]);
        Assert.Equal("shared failure", errorResult["stackTrace"]!.GetValue<string>());
        Assert.Equal("e1", errorResult["stackTraceRef"]!.GetValue<string>());
    }

    [Fact]
    public void LogContextCompactor_ResetMakesNewSessionSelfContained()
    {
        LogContextCompactor compactor = new();
        DateTime date = new(2026, 7, 23);
        JsonNode first = JsonSerializer.SerializeToNode(new
        {
            RoomUrl = "https://live.douyin.com/72024000076",
            NickName = "(~3_3)~ 7hz",
        })!;
        JsonNode nextSession = first.DeepClone();

        _ = compactor.Compact(first, "info", date);
        compactor.Reset(date);
        JsonObject result = Assert.IsType<JsonObject>(compactor.Compact(nextSession, "info", date));

        Assert.Equal("r1", result["roomRef"]!.GetValue<string>());
        Assert.NotNull(result["roomContext"]);
    }
}
