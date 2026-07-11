using Emerde.Views;

namespace Emerde.Tests;

public sealed class AddRoomContentDialogTests
{
    [Fact]
    public void GetRoomInfoErrorMessage_AppendsResolverDetail()
    {
        string result = AddRoomContentDialog.GetRoomInfoErrorMessage(null, "resolver failed");

        Assert.Contains("resolver failed", result);
    }
}
