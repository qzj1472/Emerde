using Emerde.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Emerde.Tests;

public sealed class AppSessionLoggerTests
{
    [Fact]
    public void Event_DoesNotPropagateUnsupportedPayloadSerialization()
    {
        List<object> payload = [];
        payload.Add(payload);

        Exception? error = Record.Exception(() => AppSessionLogger.Event(
            "info",
            "test",
            "cyclic_payload",
            data: payload));

        Assert.Null(error);
    }

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

        Assert.Equal(Path.Combine(directory, "20260722_235958_000_21460.log"), filePath);
        Assert.Equal(Path.Combine(directory, "20260722_235958_000_21460.error.log"), errorFilePath);
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
        Assert.Equal(Path.Combine(directory, "20260722_235958_000_21460_20260723.log"), secondFilePath);
        Assert.Equal(Path.Combine(directory, "20260722_235958_000_21460_20260723.error.log"), secondErrorFilePath);
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
            "session-identifier",
            filePath,
            errorFilePath);
        using JsonDocument document = JsonDocument.Parse(header);
        JsonElement root = document.RootElement;

        Assert.Equal("session", root.GetProperty("type").GetString());
        Assert.Equal(5, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Emerde", root.GetProperty("application").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("buildId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("buildConfiguration").GetString()));
        Assert.Equal("session-identifier", root.GetProperty("sessionId").GetString());
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
            resolverError = "Douyin room data was empty or blocked.",
        })!;
        JsonNode second = JsonSerializer.SerializeToNode(new
        {
            RoomUrl = "https://live.douyin.com/72024000076",
            NickName = "(~3_3)~ 7hz",
            errorOutput = "Stream ends prematurely",
            resolverError = "Douyin room data was empty or blocked.",
        })!;

        JsonObject firstResult = Assert.IsType<JsonObject>(compactor.Compact(first, "warn", date));
        JsonObject secondResult = Assert.IsType<JsonObject>(compactor.Compact(second, "warn", date));

        Assert.Equal("r1", firstResult["roomRef"]!.GetValue<string>());
        Assert.NotNull(firstResult["roomContext"]);
        Assert.Equal("e1", firstResult["errorOutputRef"]!.GetValue<string>());
        Assert.Equal("Stream ends prematurely", firstResult["errorOutput"]!.GetValue<string>());
        Assert.Equal("e2", firstResult["resolverErrorRef"]!.GetValue<string>());
        Assert.Equal("Douyin room data was empty or blocked.", firstResult["resolverError"]!.GetValue<string>());
        Assert.Equal("r1", secondResult["roomRef"]!.GetValue<string>());
        Assert.Null(secondResult["roomContext"]);
        Assert.Equal("e1", secondResult["errorOutputRef"]!.GetValue<string>());
        Assert.Equal("e2", secondResult["resolverErrorRef"]!.GetValue<string>());
        Assert.Null(secondResult["errorOutput"]);
        Assert.Null(secondResult["resolverError"]);
        Assert.Null(secondResult["RoomUrl"]);
        Assert.Null(secondResult["NickName"]);
    }

    [Fact]
    public void LogContextCompactor_ReusesNestedPreviewRoomReferences()
    {
        LogContextCompactor compactor = new();
        DateTime date = new(2026, 7, 27);
        JsonNode first = JsonSerializer.SerializeToNode(new
        {
            previousRoom = new
            {
                RoomUrl = "https://live.douyin.com/first",
                NickName = "First",
                PlatformName = "Douyin",
            },
            targetRoom = new
            {
                RoomUrl = "https://live.douyin.com/second",
                NickName = "Second",
                PlatformName = "Douyin",
            },
        })!;
        JsonNode second = first.DeepClone();

        JsonObject firstResult = Assert.IsType<JsonObject>(compactor.Compact(first, "info", date));
        JsonObject secondResult = Assert.IsType<JsonObject>(compactor.Compact(second, "info", date));
        JsonObject firstPrevious = Assert.IsType<JsonObject>(firstResult["previousRoom"]);
        JsonObject firstTarget = Assert.IsType<JsonObject>(firstResult["targetRoom"]);
        JsonObject secondPrevious = Assert.IsType<JsonObject>(secondResult["previousRoom"]);
        JsonObject secondTarget = Assert.IsType<JsonObject>(secondResult["targetRoom"]);

        Assert.Equal("r1", firstPrevious["roomRef"]!.GetValue<string>());
        Assert.Equal("r2", firstTarget["roomRef"]!.GetValue<string>());
        Assert.NotNull(firstPrevious["roomContext"]);
        Assert.NotNull(firstTarget["roomContext"]);
        Assert.Equal("r1", secondPrevious["roomRef"]!.GetValue<string>());
        Assert.Equal("r2", secondTarget["roomRef"]!.GetValue<string>());
        Assert.Null(secondPrevious["roomContext"]);
        Assert.Null(secondTarget["roomContext"]);
    }

    [Fact]
    public void LogContextCompactor_ReusesPayloadMessageReferences()
    {
        LogContextCompactor compactor = new();
        DateTime date = new(2026, 7, 23);
        JsonObject first = new()
        {
            ["message"] = "room check returned no result and the previous stream state was preserved",
            ["data"] = JsonSerializer.SerializeToNode(new
            {
                resolverError = "Douyin room data was empty or blocked.",
            }),
        };
        JsonObject second = first.DeepClone().AsObject();

        compactor.CompactPayload(first, "warn", date);
        compactor.CompactPayload(second, "warn", date);

        Assert.Equal("e1", first["messageRef"]!.GetValue<string>());
        Assert.Equal("room check returned no result and the previous stream state was preserved", first["message"]!.GetValue<string>());
        Assert.Equal("e1", second["messageRef"]!.GetValue<string>());
        Assert.Null(second["message"]);
        JsonObject secondData = Assert.IsType<JsonObject>(second["data"]);
        Assert.Equal("e2", secondData["resolverErrorRef"]!.GetValue<string>());
        Assert.Null(secondData["resolverError"]);
    }

    [Fact]
    public void LogContextCompactor_ReusesEventIdentityWhileKeepingActionSearchable()
    {
        LogContextCompactor compactor = new();
        DateTime date = new(2026, 7, 27);
        JsonObject first = new()
        {
            ["level"] = "info",
            ["category"] = "preview",
            ["action"] = "preview_transition_requested",
            ["message"] = "preview transition was requested",
            ["data"] = new JsonObject { ["requestId"] = 1 },
        };
        JsonObject second = first.DeepClone().AsObject();
        second["data"]!["requestId"] = 2;

        compactor.CompactPayload(first, "info", date);
        compactor.CompactPayload(second, "info", date);

        Assert.Equal("v1", first["eventRef"]!.GetValue<string>());
        Assert.Null(first["action"]);
        JsonObject context = Assert.IsType<JsonObject>(first["eventContext"]);
        Assert.Equal("info", context["level"]!.GetValue<string>());
        Assert.Equal("preview", context["category"]!.GetValue<string>());
        Assert.Equal("preview_transition_requested", context["action"]!.GetValue<string>());
        Assert.Equal("preview transition was requested", context["message"]!.GetValue<string>());
        Assert.Equal("v1", second["eventRef"]!.GetValue<string>());
        Assert.Null(second["action"]);
        Assert.Null(second["level"]);
        Assert.Null(second["category"]);
        Assert.Null(second["message"]);
        Assert.Null(second["eventContext"]);
    }

    [Fact]
    public void LogContextCompactor_RemovesEmptyTopLevelData()
    {
        LogContextCompactor compactor = new();
        JsonObject payload = new()
        {
            ["level"] = "info",
            ["category"] = "preview",
            ["action"] = "preview_closed",
            ["message"] = "preview closed",
            ["data"] = null,
        };

        compactor.CompactPayload(payload, "info", new DateTime(2026, 7, 27));

        Assert.False(payload.ContainsKey("data"));
    }

    [Fact]
    public void LogContextCompactor_DefinesEventIdentityAgainForErrorLog()
    {
        LogContextCompactor compactor = new();
        DateTime date = new(2026, 7, 27);
        JsonObject first = new()
        {
            ["level"] = "error",
            ["category"] = "preview",
            ["action"] = "preview_transition_failed",
            ["message"] = "playback failed",
        };
        JsonObject second = first.DeepClone().AsObject();

        compactor.CompactPayload(first, "warn", date);
        compactor.CompactPayload(second, "error", date);

        Assert.NotNull(first["eventContext"]);
        Assert.NotNull(second["eventContext"]);
        Assert.Equal(first["eventRef"]!.GetValue<string>(), second["eventRef"]!.GetValue<string>());
    }

    [Fact]
    public void LogContextCompactor_ReusesEventDataShapeAndRemovesNulls()
    {
        LogContextCompactor compactor = new();
        DateTime date = new(2026, 7, 27);
        JsonObject first = new()
        {
            ["level"] = "info",
            ["category"] = "preview",
            ["action"] = "preview_transition_summary",
            ["message"] = "preview transition timing summary",
            ["data"] = new JsonObject
            {
                ["requestId"] = 1,
                ["outcome"] = "playing",
                ["failureType"] = null,
            },
        };
        JsonObject second = first.DeepClone().AsObject();
        second["data"]!["requestId"] = 2;

        compactor.CompactPayload(first, "info", date);
        compactor.CompactPayload(second, "info", date);

        Assert.Equal("d1", first["dataRef"]!.GetValue<string>());
        JsonObject firstData = Assert.IsType<JsonObject>(first["data"]);
        Assert.Equal(1, firstData["requestId"]!.GetValue<int>());
        Assert.Equal("playing", firstData["outcome"]!.GetValue<string>());
        Assert.Null(firstData["failureType"]);
        Assert.Equal("d1", second["dataRef"]!.GetValue<string>());
        JsonArray secondData = Assert.IsType<JsonArray>(second["data"]);
        Assert.Equal(2, secondData[0]!.GetValue<int>());
        Assert.Equal("playing", secondData[1]!.GetValue<string>());
    }

    [Fact]
    public void LogContextCompactor_ReusesOutputFileNameReferences()
    {
        LogContextCompactor compactor = new();
        DateTime date = new(2026, 7, 25);
        JsonNode first = JsonSerializer.SerializeToNode(new
        {
            outputFileName = "D:\\records\\host\\2026-07\\25\\host_2026-07-25_20-30-00_000.ts",
        })!;
        JsonNode second = first.DeepClone();

        JsonObject firstResult = Assert.IsType<JsonObject>(compactor.Compact(first, "info", date));
        JsonObject secondResult = Assert.IsType<JsonObject>(compactor.Compact(second, "info", date));

        Assert.Equal("e1", firstResult["outputFileNameRef"]!.GetValue<string>());
        Assert.NotNull(firstResult["outputFileName"]);
        Assert.Equal("e1", secondResult["outputFileNameRef"]!.GetValue<string>());
        Assert.Null(secondResult["outputFileName"]);
    }

    [Fact]
    public void LogContextCompactor_ReusesExceptionMessageReferences()
    {
        LogContextCompactor compactor = new();
        DateTime date = new(2026, 7, 24);
        JsonNode first = JsonSerializer.SerializeToNode(new
        {
            Message = "Could not find file while refreshing videos.",
            stackTrace = "shared stack trace",
        })!;
        JsonNode second = first.DeepClone();

        JsonObject firstResult = Assert.IsType<JsonObject>(compactor.Compact(first, "error", date));
        JsonObject secondResult = Assert.IsType<JsonObject>(compactor.Compact(second, "error", date));

        Assert.Equal("e1", firstResult["MessageRef"]!.GetValue<string>());
        Assert.Equal("Could not find file while refreshing videos.", firstResult["Message"]!.GetValue<string>());
        Assert.Equal("e1", secondResult["MessageRef"]!.GetValue<string>());
        Assert.Null(secondResult["Message"]);
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
