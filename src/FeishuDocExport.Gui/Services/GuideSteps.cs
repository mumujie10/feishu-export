namespace FeishuDocExport.Gui.Services;

public enum GuideItemKind
{
    /// <summary>普通条目。</summary>
    Text,

    /// <summary>带「打开网页」按钮的条目。</summary>
    Link,

    /// <summary>多行清单，可整块复制。</summary>
    List,

    /// <summary>灰底提醒。</summary>
    Note,
}

/// <summary>引导页里的一行内容。</summary>
public sealed record GuideItem(
    GuideItemKind Kind,
    string Text,
    string? Url = null,
    string? CopyText = null,
    IReadOnlyList<string>? Lines = null);

/// <summary>引导的一节。</summary>
public sealed record GuideStep(
    string Id,
    string Title,
    string Summary,
    string Art,
    IReadOnlyList<GuideItem> Items);

/// <summary>
/// 内嵌使用教程的全部内容。界面上的首启向导和「怎么获取？」弹窗都读这里，
/// 改文案只需要改这一个文件。
/// </summary>
public static class GuideSteps
{
    /// <summary>飞书（国内版）开发者后台。</summary>
    public const string DeveloperConsoleFeishu = "https://open.feishu.cn/app";

    /// <summary>Lark（国际版）开发者后台。</summary>
    public const string DeveloperConsoleLark = "https://open.larksuite.com/app";

    /// <summary>需要在「权限管理」里逐项开通的 8 个权限，顺序与页面搜索顺序一致。</summary>
    public static readonly IReadOnlyList<string> RequiredPermissions = new[]
    {
        "查看新版文档",
        "查看、评论和下载云空间中所有文件",
        "查看、评论和导出文档",
        "查看、评论、编辑和管理云空间中所有文件",
        "查看、评论、编辑和管理多维表格",
        "查看、编辑和管理知识库",
        "查看、评论、编辑和管理电子表格",
        "导出云文档",
    };

    private static readonly IReadOnlyList<GuideStep> Steps =
    [
        new(
            Id: "app",
            Title: "创建一个企业自建应用",
            Summary: "导出靠的是「应用」的身份，所以先在飞书开发者后台建一个，拿到 AppId 和 AppSecret。",
            Art: "app",
            Items:
            [
                new(GuideItemKind.Link, "打开飞书开发者后台", DeveloperConsoleFeishu),
                new(GuideItemKind.Text, "点「创建企业自建应用」，名称、描述、图标都随便填，不影响导出。"),
                new(GuideItemKind.Text, "进入应用后，在左侧「凭证与基础信息」里复制 App ID 和 App Secret，待会儿贴回本工具。"),
                new(GuideItemKind.Text, "左侧「添加应用能力」→ 添加「机器人」能力。没有机器人能力，应用进不了群。"),
                new(GuideItemKind.Text, "左侧「版本管理与发布」→ 创建版本 → 申请发布上线，等企业管理员审核。"),
                new(
                    GuideItemKind.Note,
                    "只是自己测试的话，可以创建「测试企业」并在测试企业里发测试版本，免审核、立刻生效。"),
                new(
                    GuideItemKind.Note,
                    "应用没发布或没通过审核时，界面会提示「应用凭证无效」——这不是 AppId 填错了，回这一步检查发布状态。"),
            ]),

        new(
            Id: "permission",
            Title: "开通 8 项权限",
            Summary: "权限管理页面是分页的，漏勾很常见。建议照着下面的清单逐个核对，一键复制名单再去搜索。",
            Art: "permission",
            Items:
            [
                new(GuideItemKind.Link, "进入应用的「权限管理」页面", DeveloperConsoleFeishu),
                new(
                    GuideItemKind.List,
                    "下面 8 项全部开通：",
                    CopyText: string.Join(Environment.NewLine, RequiredPermissions),
                    Lines: RequiredPermissions),
                new(GuideItemKind.Text, "权限改动要重新创建一个版本并发布，才会真正生效。"),
                new(
                    GuideItemKind.Note,
                    "搜不到某个权限名时，试试在搜索框里输入关键词的一部分，比如「导出云文档」。"),
            ]),

        new(
            Id: "grant",
            Title: "把知识库授权给这个应用",
            Summary: "最容易漏、也最容易搜错对象的一步。应用自己搜不到，要搜的是「群」。",
            Art: "grant",
            Items:
            [
                new(GuideItemKind.Text, "先在飞书客户端新建一个群组，只有你自己也可以。"),
                new(GuideItemKind.Text, "群设置 → 群机器人 → 添加机器人 → 选中刚创建的那个自建应用。"),
                new(
                    GuideItemKind.Text,
                    "打开知识库 → 左下角设置 →「成员设置」→ 选「可编辑的成员」或「可阅读的成员」→ 在搜索框里输入刚才那个**群的名称**。"),
                new(
                    GuideItemKind.Note,
                    "搜索框里输应用名字是搜不到的——这个弹窗只接受用户、群组、部门、用户组。应用是通过「它所在的那个群」拿到权限的，所以要搜群名。"),
                new(
                    GuideItemKind.Text,
                    "如果成员列表里也加不进群，就换节点分享：在左侧目录里右键顶层那一篇 → 分享 → 添加这个群为可阅读。"),
                new(
                    GuideItemKind.Note,
                    "导出个人空间云文档走的是另一条路：开放平台不支持列举个人文件夹，必须把目标文件夹分享给自建应用，再把分享链接贴进本工具（下一节讲怎么贴）。"),
            ]),

        new(
            Id: "target",
            Title: "选中要导出的内容",
            Summary: "最省事的做法：把知识库链接整条复制过来贴进去，工具会自己把 Id 抠出来。",
            Art: "target",
            Items:
            [
                new(GuideItemKind.Text, "先在「应用凭证」里填好 AppId / AppSecret。"),
                new(
                    GuideItemKind.Text,
                    "打开要导出的知识库，把浏览器地址栏那条链接整条粘到「导出什么」下面的输入框——主页地址（…/wiki/space/xxx）和设置页地址（…/wiki/settings/xxx）都可以，点开始导出即可。"),
                new(
                    GuideItemKind.Text,
                    "「获取列表」能列出知识库的话，直接下拉选更省事，名称和 Id 会自动带出。"),
                new(
                    GuideItemKind.Note,
                    "列表是空的 ≠ 没授权。飞书的知识空间列表接口只认空间级授权，如果你授权的是单个节点，列表就看不到——但贴链接照样能导。先试贴链接，再回头查授权。"),
                new(
                    GuideItemKind.Note,
                    "贴单篇文档的链接（地址里只有 /wiki/xxxx、没有 /space/）是不行的，工具会明确告诉你它要的是知识库主页链接。"),
                new(
                    GuideItemKind.Text,
                    "个人空间云文档：把文件夹分享给自建应用后，将分享链接整条贴进 folderToken 输入框，Token 会自动提取。"),
                new(
                    GuideItemKind.Note,
                    "国际版 Lark 请在「开放平台地址」里选 Lark，否则请求会打到国内域名上、拿不到你的知识库。"),
            ]),

        new(
            Id: "export",
            Title: "选格式，开始导出",
            Summary: "docx 最快，pdf 最慢，markdown 需要额外的转换许可证文件。",
            Art: "export",
            Items:
            [
                new(
                    GuideItemKind.List,
                    "三种格式怎么选：",
                    Lines:
                    [
                        "docx —— 速度最快，先用它确认内容齐不齐",
                        "pdf —— 图片全部内嵌，体积和耗时都最大",
                        "md —— 由 docx 本地转换，引用语法、表格、行内代码会有损失",
                    ]),
                new(
                    GuideItemKind.Text,
                    "表格类文档固定导出成 xlsx；云空间里的普通文件（pdf、zip、图片等）按原格式下载，不参与上面的选择。"),
                new(
                    GuideItemKind.Text,
                    "导出 md 需要在「导出设置」里指定 Aspose.Words 许可证文件。不指定不会报错中断，但产物会带评估水印、超长文档会被截断。"),
                new(
                    GuideItemKind.Text,
                    "建议勾上「跳过已存在的文件」：中途断了可以直接重跑，已经导出的不会重复劳动。"),
                new(GuideItemKind.Text, "思维笔记、幻灯片、妙记无法通过开放接口导出，它们会单独出现在「未导出清单」里，不影响其它文档。"),
                new(
                    GuideItemKind.Note,
                    "700 多篇文档串行跑完大约 25 分钟，期间可以最小化窗口正常干活；随时点「取消」安全中断。"),
            ]),
        new(
            Id: "sync",
            Title: "只导新东西，还能定时跑",
            Summary: "第一次全量导完之后，没必要每回都把几百篇重下一遍。",
            Art: "sync",
            Items:
            [
                new(
                    GuideItemKind.List,
                    "「导出设置」里的同步方式，三种：",
                    Lines:
                    [
                        "全部重新导出 —— 每次都重下所有文档，只有想强制刷新时才用",
                        "跳过已存在的文件 —— 本地有的就不碰，适合中断后接着跑",
                        "只导新增和改动的（推荐）—— 按飞书编辑时间判断，新文档和改过的才重导",
                    ]),
                new(
                    GuideItemKind.Text,
                    "判断依据存在导出目录里的 .feishu-export-state.json。它跟着导出目录走，换电脑、换挂载路径都能接着增量，不会从头重导。"),
                new(
                    GuideItemKind.Note,
                    "增量模式不会删你任何东西：飞书里删掉的文档，本地文件和记录都保留；本地文件被你手动删了，下次会自动补回来。"),
                new(
                    GuideItemKind.Text,
                    "勾上「自动同步」，程序会按你定的时间自己在后台跑一轮增量，期间可以最小化窗口继续干别的活。"),
                new(
                    GuideItemKind.Note,
                    "自动同步要求程序保持开着。要真正无人值守（比如每天凌晨跑一次），用命令行的 --at=03:00 或 --interval=6h，或者拿仓库里的 docker-compose.yml 部署到 NAS —— 那种重启后也会自己续上。"),
            ]),
    ];

    /// <summary>全部章节，按阅读顺序。</summary>
    public static IReadOnlyList<GuideStep> All => Steps;

    /// <summary>按 Id 取一节，找不到返回 null。</summary>
    public static GuideStep? Find(string? id)
        => id is null ? null : Steps.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
}
