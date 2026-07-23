using Emerde.Views;
using System.Xml.Linq;

namespace Emerde.Tests;

public class AboutContentDialogTests
{
    [Fact]
    public void WarningCard_UsesTheSameRightSpacingAsContentCards()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));
        XElement warningCard = document.Descendants()
            .Single(element => element.Name.LocalName == "Border" && (string?)element.Attribute("Background") == "#14D83B01");

        Assert.Equal("0,0,12,0", (string?)warningCard.Attribute("Margin"));
    }

    [Theory]
    [InlineData(1174, 548, 268)]
    [InlineData(754, 688, 338)]
    [InlineData(500, 434, 434)]
    [InlineData(40, 0, 0)]
    public void CalculateCardWidths_UsesResponsiveColumnCounts(
        double controlWidth,
        double expectedCardWidth,
        double expectedWorkflowWidth)
    {
        (double cardWidth, double workflowWidth) = AboutContentDialog.CalculateCardWidths(controlWidth);

        Assert.Equal(expectedCardWidth, cardWidth);
        Assert.Equal(expectedWorkflowWidth, workflowWidth);
    }

    [Fact]
    public void OperationalGuidance_IncludesExitAndUserFacingMonitorTiming()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));
        string text = string.Join('\n', document.Descendants().Attributes("Text").Select(attribute => attribute.Value));
        XElement timingCard = document.Descendants()
            .Where(element => element.Name.LocalName == "Border")
            .Last(element => element.Descendants().Attributes("Text").Any(attribute => attribute.Value == "检测间隔"));
        XElement monitorCard = document.Descendants()
            .Where(element => element.Name.LocalName == "Border")
            .Last(element => element.Descendants().Attributes("Text").Any(attribute => attribute.Value == "监控、直播状态与连麦"));
        string timingText = string.Join('\n', timingCard.Descendants().Attributes("Text").Select(attribute => attribute.Value));
        string monitorText = string.Join('\n', monitorCard.Descendants().Attributes("Text").Select(attribute => attribute.Value));

        Assert.Contains("退出按钮、关闭窗口与恢复", text);
        Assert.Contains("侧边栏或托盘菜单中的“退出”", text);
        Assert.Contains("通常保持默认值即可", text);
        Assert.Contains("随机安排 2 次抽样检查", timingText);
        Assert.Contains("周期结束时再检查 1 次", timingText);
        Assert.DoesNotContain("随机安排 2 次抽样检查", monitorText);
        Assert.Contains("30 分钟内约每 20 秒检查一次", text);
        Assert.Contains("录制文件末尾可能出现几秒黑屏", text);
        Assert.Contains("降低平台风控风险", text);
        Assert.DoesNotContain("每批检查 1-5 个直播间", text);
        Assert.DoesNotContain("穿插 2 次随机检测", text);
    }

    [Fact]
    public void RootScrollViewer_DoesNotRenderAWindowSwitchFocusOutline()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));
        XElement scrollViewer = document.Descendants().First(element => element.Name.LocalName == "ScrollViewer");

        Assert.Equal("False", (string?)scrollViewer.Attribute("Focusable"));
        Assert.Equal("{x:Null}", (string?)scrollViewer.Attribute("FocusVisualStyle"));
        Assert.Equal("0", (string?)scrollViewer.Attribute("BorderThickness"));
    }

    [Fact]
    public void NetworkGuidance_UsesDownloadBandwidthTerminology()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));
        string text = string.Join('\n', document.Descendants().Attributes("Text").Select(attribute => attribute.Value));

        Assert.Contains("预留下载带宽和磁盘写入空间", text);
        Assert.Contains("首页底部状态栏的“测速”", text);
        Assert.Contains("预计能同时录制多少路直播", text);
        Assert.Contains("分别检查国内和国外线路", text);
        Assert.Contains("连续完成三轮下载并取平均值", text);
        Assert.Contains("按当前开播房间的平台选择对应线路", text);
        Assert.DoesNotContain("预留额外上传", text);
        Assert.DoesNotContain("同一房间每小时最多记录一次", text);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
