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
    [InlineData(1574, 748, 368)]
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
        Assert.Contains("固定每 10 秒检查一次", timingText);
        Assert.DoesNotContain("固定每 10 秒检查一次", monitorText);
        Assert.Contains("30 分钟内固定每 10 秒检查一次", text);
        Assert.Contains("FFmpeg 录制进程结束后", text);
        Assert.Contains("立即刷新对应卡片的录制状态", text);
        Assert.Contains("直播状态由下一次固定 10 秒检测更新", text);
        Assert.DoesNotContain("录制文件末尾可能出现几秒黑屏", text);
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

    [Fact]
    public void ShortcutGuidance_IncludesExpandedHomeVideoAndGlobalKeys()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));
        string text = string.Join('\n', document.Descendants().Attributes("Text").Select(attribute => attribute.Value));
        string[] labels = document.Descendants().Attributes("Text").Select(attribute => attribute.Value).ToArray();

        Assert.Contains("W/A/S/D", labels);
        Assert.Contains("M", labels);
        Assert.Contains("R", labels);
        Assert.Contains("Q", labels);
        Assert.Contains("E", labels);
        Assert.Contains("F", labels);
        Assert.Contains("C", labels);
        Assert.Contains("Shift+C", labels);
        Assert.Contains("Enter", labels);
        Assert.Contains("F5", labels);
        Assert.Contains("Ctrl+N", labels);
        Assert.Contains("Ctrl+T", labels);
        Assert.Contains("Ctrl+M", labels);
        Assert.Contains("Ctrl+R", labels);
        Assert.Contains("Ctrl+F", labels);
        Assert.Contains("Tab", labels);
        Assert.Contains("CapsLock", labels);
        Assert.Contains("Ctrl+W", labels);
        Assert.Contains("Ctrl+Shift+W", labels);
        Assert.Contains("Ctrl+Z", labels);
        Assert.Contains("Ctrl+Y", labels);
        Assert.Contains("Ctrl+C", labels);
        Assert.Contains("Ctrl+V", labels);
        Assert.Contains("Space", labels);
        Assert.Contains("G", labels);
        Assert.Contains("刷新预览", labels);
        Assert.Contains("V", labels);
        Assert.Contains("静音或恢复声音", labels);
        Assert.Contains("音量降低或增加 5%", labels);
        Assert.Contains("全屏或退出全屏", labels);
        Assert.Contains("退出全屏或关闭预览", labels);
        Assert.Contains("微调", labels);
        Assert.Contains("当前监控", labels);
        Assert.Contains("当前录制", labels);
        Assert.Contains("预览", labels);
        Assert.Contains("进入直播间", labels);
        Assert.Contains("刷新当前", labels);
        Assert.Contains("复制房间地址", labels);
        Assert.Contains("复制直播流", labels);
        Assert.Contains("打开视频", labels);
        Assert.Contains("刷新列表", labels);
        Assert.Contains("添加直播间", labels);
        Assert.Contains("测速", labels);
        Assert.Contains("全部监控", labels);
        Assert.Contains("全部录制", labels);
        Assert.Contains("刷新全部", labels);
        Assert.Contains("向上切换页面", labels);
        Assert.Contains("向下切换页面", labels);
        Assert.Contains("关闭窗口", labels);
        Assert.Contains("退出软件", labels);
        Assert.Contains("输入框撤销文字", labels);
        Assert.Contains("输入框恢复文字", labels);
        Assert.Contains("输入框复制文字", labels);
        Assert.Contains("输入框粘贴文字", labels);
        Assert.Contains("全选房间", labels);
        Assert.Contains("撤销选择或列表操作", labels);
        Assert.Contains("恢复选择或列表操作", labels);
        Assert.Contains("撤销视频选择", labels);
        Assert.Contains("恢复视频选择", labels);
        Assert.DoesNotContain("Ctrl+W/S  切换页面", text);
        Assert.DoesNotContain("Ctrl+Esc  关闭窗口", text);
        Assert.DoesNotContain("Ctrl+Shift+Esc  退出软件", text);
    }

    [Fact]
    public void ShortcutGuidance_UsesColumnRowsInsteadOfInlineKeyWraps()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));
        XElement shortcutItemStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "AboutShortcutItemStyle");
        XElement shortcutKeyPillStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "AboutShortcutKeyPillStyle");
        XElement shortcutDescriptionTextStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "AboutShortcutDescriptionTextStyle");
        XElement shortcutKeyTextStyle = document.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "AboutShortcutKeyTextStyle");

        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "UniformGrid" && (string?)element.Attribute("Columns") == "2");
        Assert.Contains(document.Descendants().Attributes("Style"), attribute => attribute.Value == "{StaticResource AboutShortcutItemStyle}");
        Assert.DoesNotContain(document.Descendants().Attributes("Style"), attribute => attribute.Value == "{StaticResource AboutKeyStyle}");
        Assert.Contains(shortcutItemStyle.Elements(), element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "BorderThickness" && (string?)element.Attribute("Value") == "0");
        Assert.Contains(shortcutKeyPillStyle.Elements(), element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "BorderThickness" && (string?)element.Attribute("Value") == "0");
        Assert.Contains(shortcutKeyTextStyle.Elements(), element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "FontWeight" && (string?)element.Attribute("Value") == "SemiBold");
        Assert.Contains(shortcutKeyTextStyle.Elements(), element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "Foreground" && (string?)element.Attribute("Value") == "{DynamicResource EmerdeAboutShortcutKeyBrush}");
        Assert.Contains(shortcutDescriptionTextStyle.Elements(), element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "FontSize" && (string?)element.Attribute("Value") == "12");
        Assert.Contains(shortcutDescriptionTextStyle.Elements(), element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == "Foreground" && (string?)element.Attribute("Value") == "{DynamicResource EmerdeAboutShortcutDescriptionBrush}");
    }

    [Fact]
    public void TitleStyles_SeparateAboutPageVisualHierarchy()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));
        XElement heroTitleStyle = FindStyle(document, "AboutHeroTitleStyle");
        XElement sectionTitleStyle = FindStyle(document, "AboutSectionTitleStyle");
        XElement cardTitleStyle = FindStyle(document, "AboutCardTitleStyle");
        XElement bodyTextStyle = FindStyle(document, "AboutBodyTextStyle");
        XElement shortcutDescriptionTextStyle = FindStyle(document, "AboutShortcutDescriptionTextStyle");
        XElement warningTitleStyle = FindStyle(document, "AboutWarningTitleStyle");

        Assert.True(double.Parse(SetterValue(heroTitleStyle, "FontSize")) > double.Parse(SetterValue(sectionTitleStyle, "FontSize")));
        Assert.True(double.Parse(SetterValue(sectionTitleStyle, "FontSize")) > double.Parse(SetterValue(cardTitleStyle, "FontSize")));
        Assert.True(double.Parse(SetterValue(cardTitleStyle, "FontSize")) > double.Parse(SetterValue(shortcutDescriptionTextStyle, "FontSize")));
        Assert.Equal(2, double.Parse(SetterValue(cardTitleStyle, "FontSize")) - double.Parse(SetterValue(bodyTextStyle, "FontSize")));
        Assert.Equal("Bold", SetterValue(heroTitleStyle, "FontWeight"));
        Assert.Equal("Bold", SetterValue(sectionTitleStyle, "FontWeight"));
        Assert.Equal("SemiBold", SetterValue(cardTitleStyle, "FontWeight"));
        Assert.Equal("Normal", SetterValue(shortcutDescriptionTextStyle, "FontWeight"));
        Assert.Equal("{DynamicResource SystemAccentColorPrimaryBrush}", SetterValue(sectionTitleStyle, "Foreground"));
        Assert.Equal("{DynamicResource EmerdeAboutCardTitleBrush}", SetterValue(cardTitleStyle, "Foreground"));
        Assert.Equal("{DynamicResource TextFillColorSecondaryBrush}", SetterValue(bodyTextStyle, "Foreground"));
        Assert.Equal("{DynamicResource EmerdeAboutShortcutDescriptionBrush}", SetterValue(shortcutDescriptionTextStyle, "Foreground"));
        Assert.Equal("#D83B01", SetterValue(warningTitleStyle, "Foreground"));
    }

    [Fact]
    public void HeroHeader_DoesNotShowRedundantFeatureBadges()
    {
        XDocument document = XDocument.Load(FindRepositoryFile("src", "Emerde", "Views", "AboutContentDialog.xaml"));

        Assert.DoesNotContain(document.Descendants().Attributes("Style"), attribute => attribute.Value == "{StaticResource AboutBadgeStyle}");
        Assert.DoesNotContain(document.Descendants().Attributes(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")), attribute => attribute.Value == "AboutBadgeStyle");
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

    private static XElement FindStyle(XDocument document, string key)
    {
        return document.Descendants()
            .Single(element => element.Name.LocalName == "Style" && (string?)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == key);
    }

    private static string SetterValue(XElement style, string property)
    {
        return style.Elements()
            .Single(element => element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") == property)
            .Attribute("Value")?.Value ?? string.Empty;
    }
}
