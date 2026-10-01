using FeishuDocExport.Models;
using Xunit;

namespace FeishuDocExport.Tests;

public class ExportPathBuilderTests
{
    private static DocNode Node(
        string nodeKey,
        string title,
        string objType = "docx",
        string? parentKey = null,
        string? docKey = null)
        => new()
        {
            NodeKey = nodeKey,
            DocKey = docKey ?? nodeKey,
            Title = title,
            ObjType = objType,
            ParentKey = parentKey,
        };

    private static string? Extension(DocNode node)
        => ObjTypeMapper.IsContainer(node.ObjType) ? null : "docx";

    [Fact]
    public void 按父子关系生成目录结构()
    {
        var nodes = new[]
        {
            Node("root1", "产品文档"),
            Node("child1", "需求说明", parentKey: "root1"),
            Node("grand1", "详细设计", parentKey: "child1"),
            Node("root2", "其它"),
        };

        var builder = ExportPathBuilder.Build(nodes, "/out", Extension);

        Assert.Equal(Path.Combine("/out", "产品文档"), builder.GetTargetByDocKey("root1")!.BasePath);
        Assert.Equal(
            Path.Combine("/out", "产品文档", "需求说明"),
            builder.GetTargetByDocKey("child1")!.BasePath);
        Assert.Equal(
            Path.Combine("/out", "产品文档", "需求说明", "详细设计"),
            builder.GetTargetByDocKey("grand1")!.BasePath);
        Assert.Equal(Path.Combine("/out", "其它"), builder.GetTargetByDocKey("root2")!.BasePath);
    }

    [Fact]
    public void 同目录同名文档不会互相覆盖()
    {
        var nodes = new[]
        {
            Node("a", "周报"),
            Node("b", "周报"),
            Node("c", "周报"),
        };

        var builder = ExportPathBuilder.Build(nodes, "/out", Extension);

        var paths = nodes.Select(n => builder.GetTargetByDocKey(n.NodeKey)!.BasePath).ToList();

        Assert.Equal(3, paths.Distinct().Count());
        Assert.Equal(Path.Combine("/out", "周报"), paths[0]);
        Assert.Equal(Path.Combine("/out", "周报 (2)"), paths[1]);
        Assert.Equal(Path.Combine("/out", "周报 (3)"), paths[2]);
    }

    [Fact]
    public void 标题里的非法字符会被替换()
    {
        var nodes = new[] { Node("a", "报告/2024:Q1") };

        var builder = ExportPathBuilder.Build(nodes, "/out", Extension);

        Assert.Equal(Path.Combine("/out", "报告-2024-Q1"), builder.GetTargetByDocKey("a")!.BasePath);
    }

    [Fact]
    public void 同一个文档挂在多个节点下时只落盘一次且引用指向落盘位置()
    {
        // 飞书允许把同一篇文档以「快捷方式」挂在知识库的多个位置
        var nodes = new[]
        {
            Node("folderA", "A 目录", "docx"),
            Node("folderB", "B 目录", "docx"),
            Node("shortcut", "副本入口", parentKey: "folderB", docKey: "doc-1"),
            Node("origin", "原始位置", parentKey: "folderA", docKey: "doc-1"),
        };

        var builder = ExportPathBuilder.Build(nodes, "/out", Extension);

        // 同一个文档只导出一次（父节点本身也是文档，会各自导出，属于正常现象）
        Assert.Single(builder.DocumentOrder, key => key == "doc-1");

        // 落盘位置是遍历中首次出现的位置（A 目录下的「原始位置」）
        var target = builder.GetTargetByDocKey("doc-1")!;
        Assert.Equal(Path.Combine("/out", "A 目录", "原始位置"), target.BasePath);

        // 另一个节点位置的引用也必须指向真正落盘的那一份，
        // 否则 markdown 里的链接会指向一个不存在的文件（旧实现只保留最后一个路径）
        Assert.Equal(target, builder.GetTargetByNodeKey("origin"));
        Assert.Equal(target, builder.GetTargetByNodeKey("shortcut"));
    }

    [Fact]
    public void 数据成环时不会无限递归()
    {
        var nodes = new[]
        {
            Node("a", "A", parentKey: "b"),
            Node("b", "B", parentKey: "a"),
        };

        var builder = ExportPathBuilder.Build(nodes, "/out", Extension);

        Assert.Equal(2, builder.DocumentOrder.Count);
        Assert.NotNull(builder.GetTargetByDocKey("a"));
        Assert.NotNull(builder.GetTargetByDocKey("b"));
    }

    [Fact]
    public void 父子互相指认时不会丢文档()
    {
        var nodes = new[]
        {
            Node("root", "根"),
            Node("orphan", "孤儿", parentKey: "not-exist"),
        };

        var builder = ExportPathBuilder.Build(nodes, "/out", Extension);

        Assert.Equal(Path.Combine("/out", "孤儿"), builder.GetTargetByDocKey("orphan")!.BasePath);
    }

    [Fact]
    public void 目录节点不产生落盘目标()
    {
        var nodes = new[]
        {
            Node("folder", "素材", "folder"),
            Node("file", "图片.png", "file", parentKey: "folder"),
        };

        var builder = ExportPathBuilder.Build(nodes, "/out", Extension);

        Assert.Null(builder.GetTargetByDocKey("folder"));
        Assert.Equal(Path.Combine("/out", "素材", "图片.png"), builder.GetTargetByDocKey("file")!.BasePath);
    }

    [Fact]
    public void 扩展名为空时路径不带点()
    {
        var nodes = new[] { Node("f", "无扩展名文件", "file") };

        var builder = ExportPathBuilder.Build(nodes, "/out", _ => string.Empty);

        Assert.Equal(Path.Combine("/out", "无扩展名文件"), builder.GetTargetByDocKey("f")!.FilePath);
    }
}
