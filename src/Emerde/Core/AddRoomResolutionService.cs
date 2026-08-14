namespace Emerde.Core;

internal sealed record AddRoomResolution(
    bool IsSuccess,
    string NickName,
    string RoomUrl,
    ISpiderResult? SpiderResult,
    string ErrorMessage,
    bool IsDeferred = false,
    bool IsWarning = false);

internal static class AddRoomResolutionService
{
    public static async Task<AddRoomResolution> ResolveAsync(string? input, bool forceAdd, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Failure("EnterRoomUrl".Tr(), true);
        }

        string? normalizedRoomUrl = await Task.Run(() => Spider.ParseUrl(input, allowNetwork: !forceAdd, token), token);
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(normalizedRoomUrl))
        {
            return Failure("ErrorRoomUrl".Tr());
        }

        if (HasDuplicateRoom(normalizedRoomUrl))
        {
            return Failure("AddRoomErrorDuplicated".Tr(normalizedRoomUrl), true);
        }

        if (forceAdd)
        {
            return ExternalStreamResolver.IsPersistableRoomUrl(normalizedRoomUrl)
                ? Success(normalizedRoomUrl, normalizedRoomUrl, null)
                : Failure("ErrorRoomUrl".Tr());
        }

        try
        {
            string preferredQuality = RoomRecordingSettings.GetGlobal().PreferredStreamQuality;
            ISpiderResult? spider = await GlobalMonitor.GetManualSpiderResultAsync(normalizedRoomUrl, preferredQuality, token);
            token.ThrowIfCancellationRequested();
            string roomUrl = string.IsNullOrWhiteSpace(spider?.RoomUrl)
                ? normalizedRoomUrl
                : Spider.ParseUrl(spider.RoomUrl!) ?? spider.RoomUrl!;

            if (spider == null && CanDeferRoomInfoResolution(normalizedRoomUrl, ExternalStreamResolver.GetLastError(normalizedRoomUrl)))
            {
                return Success(normalizedRoomUrl, normalizedRoomUrl, null, true);
            }

            if (spider == null || !HasAddableRoomInfo(spider, roomUrl) || !ExternalStreamResolver.IsPersistableRoomUrl(roomUrl))
            {
                return Failure("GetRoomInfoError".Tr());
            }

            if (HasDuplicateRoom(roomUrl, spider.PlatformName, spider.Uid))
            {
                return Failure("AddRoomErrorDuplicated".Tr(GetConfirmedNickName(spider)), true);
            }

            spider.RoomUrl = roomUrl;
            return Success(GetConfirmedNickName(spider), roomUrl, spider);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppSessionLogger.WriteException(exception);
            return Failure("GetRoomInfoError".Tr());
        }
    }

    internal static bool HasAddableRoomInfo(ISpiderResult? spider, string? roomUrl)
    {
        if (spider == null || string.IsNullOrWhiteSpace(roomUrl))
        {
            return false;
        }

        string platformName = string.IsNullOrWhiteSpace(spider.PlatformName)
            ? Spider.GetPlatformName(roomUrl)
            : spider.PlatformName;
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(spider.Nickname)
            || !string.IsNullOrWhiteSpace(spider.Uid)
            || spider.IsLiveStreaming == true
            || !string.IsNullOrWhiteSpace(spider.FlvUrl)
            || !string.IsNullOrWhiteSpace(spider.HlsUrl)
            || !string.IsNullOrWhiteSpace(spider.RecordUrl);
    }

    internal static bool CanDeferRoomInfoResolution(string? roomUrl, string? error)
    {
        return !string.IsNullOrWhiteSpace(roomUrl)
            && ExternalStreamResolver.IsPersistableRoomUrl(roomUrl)
            && string.Equals(Spider.GetPlatformName(roomUrl), "Douyin", StringComparison.OrdinalIgnoreCase)
            && StreamResolver.IsTransientDouyinFailure(error);
    }

    internal static string GetConfirmedNickName(ISpiderResult spider)
    {
        return string.IsNullOrWhiteSpace(spider.Nickname) ? spider.RoomUrl ?? string.Empty : spider.Nickname;
    }

    private static bool HasDuplicateRoom(string roomUrl, string? platformName = null, string? uid = null)
    {
        return (Configurations.Rooms.Get() ?? []).Any(room => ExternalStreamResolver.IsSameRoom(
            room.RoomUrl,
            room.PlatformName,
            room.Uid,
            roomUrl,
            platformName,
            uid));
    }

    private static AddRoomResolution Success(string nickName, string roomUrl, ISpiderResult? spiderResult, bool isDeferred = false)
    {
        return new(true, nickName, roomUrl, spiderResult, string.Empty, isDeferred);
    }

    private static AddRoomResolution Failure(string errorMessage, bool isWarning = false)
    {
        return new(false, string.Empty, string.Empty, null, errorMessage, false, isWarning);
    }
}
