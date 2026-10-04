using FeishuDocExport;
using Xunit;

namespace FeishuDocExport.Tests;

/// <summary>增量导出用的状态文件读写。损坏时必须退回空状态而不是把导出流程带崩。</summary>
public class ExportStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        $"feishu-state-{Guid.NewGuid().ToString("N")[..8]}");

    public ExportStateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录留给系统回收
        }
    }

    [Fact]
    public void 没有状态文件时返回空状态()
    {
        var state = ExportStateStore.Load(_dir);

        Assert.Empty(state.Docs);
    }

    [Fact]
    public void 读写往返保留编辑时间与相对路径()
    {
        var state = new ExportState();
        state.Docs["doc1"] = new ExportStateEntry
        {
            EditTime = "1700000000",
            RelativePath = "设计/首页.docx",
            ExportedAt = DateTimeOffset.UnixEpoch,
        };

        ExportStateStore.Save(_dir, state);
        var loaded = ExportStateStore.Load(_dir);

        Assert.Equal(ExportStateStore.CurrentVersion, loaded.Version);
        Assert.True(loaded.Docs.TryGetValue("doc1", out var entry));
        Assert.Equal("1700000000", entry!.EditTime);
        Assert.Equal("设计/首页.docx", entry.RelativePath);
    }

    [Theory]
    [InlineData("{ 这不是 JSON")]
    [InlineData("")]
    [InlineData("[]")]
    public void 状态文件损坏时退回空状态而不是抛异常(string content)
    {
        File.WriteAllText(ExportStateStore.PathOf(_dir), content);

        var state = ExportStateStore.Load(_dir);

        Assert.Empty(state.Docs);
    }

    [Fact]
    public void 版本对不上时当作没有历史()
    {
        File.WriteAllText(
            ExportStateStore.PathOf(_dir),
            """
            {"version":999,"docs":{"a":{"edit":"1","path":"a.docx","at":"2020-01-01T00:00:00+08:00"}}}
            """);

        Assert.Empty(ExportStateStore.Load(_dir).Docs);
    }

    [Fact]
    public void 保存之后不留临时文件()
    {
        ExportStateStore.Save(_dir, new ExportState());

        Assert.True(File.Exists(ExportStateStore.PathOf(_dir)));
        Assert.False(File.Exists(ExportStateStore.PathOf(_dir) + ".tmp"));
    }

    [Fact]
    public void 相对路径能还原成当前环境的绝对路径()
    {
        var fullPath = Path.Combine(_dir, "设计", "首页.docx");

        var relative = ExportStateStore.ToRelativePath(_dir, fullPath);
        var resolved = ExportStateStore.ResolvePath(_dir, new ExportStateEntry { RelativePath = relative });

        Assert.Equal("设计/首页.docx", relative);
        Assert.Equal(Path.GetFullPath(fullPath), resolved);
    }
}
