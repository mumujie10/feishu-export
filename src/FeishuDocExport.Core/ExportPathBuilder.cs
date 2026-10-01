using FeishuDocExport.Models;

namespace FeishuDocExport;

/// <summary>一个可落盘文档的目标位置。</summary>
/// <param name="BasePath">不含扩展名的完整路径。</param>
/// <param name="Extension">扩展名（不含点）。为空表示该文件没有扩展名。</param>
public sealed record ExportTarget(string BasePath, string Extension)
{
    /// <summary>最终文件路径。</summary>
    public string FilePath => string.IsNullOrEmpty(Extension) ? BasePath : BasePath + "." + Extension;
}

/// <summary>
/// 根据节点树的父子关系生成「节点 → 本地路径」的映射，并保证：
///
/// 1. 同一目录下的同名文档不会互相覆盖（自动追加 " (2)"）；
/// 2. 数据里出现环或孤儿节点时不会无限递归（旧实现没有 visited 集合）；
/// 3. 同一个文档以快捷方式挂在多个节点下时只落盘一次，
///    但所有指向它的链接都会解析到真正落盘的那一份（旧实现只保留最后一个路径，
///    导致前一个位置的链接指向不存在的文件）。
/// </summary>
public sealed class ExportPathBuilder
{
    private readonly Dictionary<string, string> _docKeyByNodeKey;
    private readonly Dictionary<string, ExportTarget> _targetByDocKey;
    private readonly List<string> _documentOrder;

    private ExportPathBuilder(
        Dictionary<string, string> docKeyByNodeKey,
        Dictionary<string, ExportTarget> targetByDocKey,
        List<string> documentOrder)
    {
        _docKeyByNodeKey = docKeyByNodeKey;
        _targetByDocKey = targetByDocKey;
        _documentOrder = documentOrder;
    }

    /// <summary>
    /// 需要落盘的文档 token，按在文档树中首次出现的顺序排列。
    /// 每个文档只出现一次（同一个文档挂在多个节点下时以第一个位置为准）。
    /// </summary>
    public IReadOnlyList<string> DocumentOrder => _documentOrder;

    /// <summary>文档实际落盘的目标（按文档 token）。</summary>
    public IReadOnlyDictionary<string, ExportTarget> Targets => _targetByDocKey;

    /// <summary>
    /// 构建路径映射。
    /// </summary>
    /// <param name="nodes">扁平的全部节点。</param>
    /// <param name="rootPath">导出根目录。</param>
    /// <param name="extensionSelector">返回该节点落盘扩展名；返回 null 表示该节点不落盘（目录或不支持的类型）。</param>
    public static ExportPathBuilder Build(
        IReadOnlyList<DocNode> nodes,
        string rootPath,
        Func<DocNode, string?> extensionSelector)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(extensionSelector);

        var docKeyByNodeKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var targetByDocKey = new Dictionary<string, ExportTarget>(StringComparer.Ordinal);
        var documentOrder = new List<string>();
        var childrenByParent = new Dictionary<string, List<DocNode>>(StringComparer.Ordinal);
        var knownNodeKeys = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var usedNamesByDirectory = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            knownNodeKeys.Add(node.NodeKey);
        }

        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node.ParentKey))
            {
                continue;
            }

            if (!childrenByParent.TryGetValue(node.ParentKey, out var siblings))
            {
                siblings = new List<DocNode>();
                childrenByParent[node.ParentKey] = siblings;
            }

            siblings.Add(node);
        }

        // 根节点：没有父节点，或者父节点不在本次结果里
        // （云文档的顶层文件，其 parent_token 就是传入的 folderToken 本身，不在列表里）
        var roots = nodes
            .Where(n => string.IsNullOrEmpty(n.ParentKey) || !knownNodeKeys.Contains(n.ParentKey))
            .ToList();

        // 把节点挂到某个目录下，登记它的落盘目标，并返回它对应的路径（子节点以它为目录）
        string Assign(DocNode node, string directory)
        {
            if (!usedNamesByDirectory.TryGetValue(directory, out var usedNames))
            {
                usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                usedNamesByDirectory[directory] = usedNames;
            }

            var name = FileNameHelper.Sanitize(node.Title, fallback: node.NodeKey);
            name = FileNameHelper.MakeUnique(name, usedNames);
            var basePath = Path.Combine(directory, name);

            docKeyByNodeKey[node.NodeKey] = node.DocKey;

            var extension = extensionSelector(node);
            if (extension is not null && !targetByDocKey.ContainsKey(node.DocKey))
            {
                // 首次出现的位置就是实际落盘位置，后续同文档节点只做引用指向
                targetByDocKey[node.DocKey] = new ExportTarget(basePath, extension);
                documentOrder.Add(node.DocKey);
            }

            return basePath;
        }

        // 显式栈代替递归：避免异常数据造成的深递归爆栈；逆序入栈以保持接口返回的原始顺序
        var stack = new Stack<(DocNode Node, string Directory)>();
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            stack.Push((roots[i], rootPath));
        }

        while (stack.Count > 0)
        {
            var (node, directory) = stack.Pop();

            // 数据里出现环时在这里被截断
            if (!visited.Add(node.NodeKey))
            {
                continue;
            }

            var basePath = Assign(node, directory);

            if (childrenByParent.TryGetValue(node.NodeKey, out var children))
            {
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push((children[i], basePath));
                }
            }
        }

        // 兜底：环形引用等原因导致没被遍历到的节点，挂到根目录下，保证不丢文档
        foreach (var node in nodes)
        {
            if (visited.Add(node.NodeKey))
            {
                Assign(node, rootPath);
            }
        }

        return new ExportPathBuilder(docKeyByNodeKey, targetByDocKey, documentOrder);
    }

    /// <summary>
    /// 按文档 token 取落盘目标（下载完成后保存时使用）。
    /// </summary>
    public ExportTarget? GetTargetByDocKey(string docKey)
        => _targetByDocKey.GetValueOrDefault(docKey);

    /// <summary>
    /// 按节点 token 取落盘目标。同一个文档挂在多个节点下时，返回真正落盘的那一份。
    /// </summary>
    public ExportTarget? GetTargetByNodeKey(string nodeKey)
        => _docKeyByNodeKey.TryGetValue(nodeKey, out var docKey) ? _targetByDocKey.GetValueOrDefault(docKey) : null;
}
