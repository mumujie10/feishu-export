using System.Text;
using Xunit;

namespace FeishuDocExport.Tests;

public class FileNameHelperTests
{
    [Theory]
    [InlineData("a/b:c*d?e\"f<g>h|i", "a-b-c-d-e-f-g-h-i")]
    [InlineData("  前后空格  ", "前后空格")]
    [InlineData("结尾的点...", "结尾的点")]
    public void Sanitize_会替换非法字符并去掉首尾空白(string input, string expected)
        => Assert.Equal(expected, FileNameHelper.Sanitize(input));

    [Fact]
    public void Sanitize_空标题使用兜底名字()
    {
        Assert.Equal("未命名文档", FileNameHelper.Sanitize(null));
        Assert.Equal("未命名文档", FileNameHelper.Sanitize("   "));
        Assert.Equal("兜底", FileNameHelper.Sanitize(string.Empty, "兜底"));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("LPT9.txt")]
    public void Sanitize_规避Windows保留设备名(string input)
        => Assert.StartsWith("_", FileNameHelper.Sanitize(input));

    [Fact]
    public void Sanitize_超长中文名按字节截断且不超过上限()
    {
        // 旧实现用 PadLeft(61) 生成「截断后」的名字，但对超长字符串 PadLeft 不会截断，
        // 等于根本没做截断。这里验证真的按 UTF-8 字节数截断了。
        var name = new string('知', 300);

        var result = FileNameHelper.Sanitize(name);

        Assert.True(Encoding.UTF8.GetByteCount(result) <= FileNameHelper.DefaultMaxBytes);
        Assert.True(result.Length < 300);
        // 每个汉字 3 字节，200 字节最多 66 个
        Assert.Equal(66, result.Length);
    }

    [Fact]
    public void Sanitize_不会把代理对截成半个字符()
    {
        var emoji = string.Concat(Enumerable.Repeat("😀", 100)); // 每个 4 字节

        var result = FileNameHelper.Sanitize(emoji);

        Assert.Equal(FileNameHelper.DefaultMaxBytes, Encoding.UTF8.GetByteCount(result));
        Assert.Equal(50, result.EnumerateRunes().Count());
    }

    [Fact]
    public void TruncateToUtf8Bytes_短字符串原样返回()
        => Assert.Equal("abc", FileNameHelper.TruncateToUtf8Bytes("abc", 200));

    [Fact]
    public void MakeUnique_重名时追加序号()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("文档", FileNameHelper.MakeUnique("文档", used));
        Assert.Equal("文档 (2)", FileNameHelper.MakeUnique("文档", used));
        Assert.Equal("文档 (3)", FileNameHelper.MakeUnique("文档", used));
    }

    [Fact]
    public void MakeUnique_大小写不同也视为重名()
    {
        // macOS / Windows 默认大小写不敏感，Readme 与 Report 不能互相覆盖
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("Readme", FileNameHelper.MakeUnique("Readme", used));
        Assert.Equal("readme (2)", FileNameHelper.MakeUnique("readme", used));
    }
}
