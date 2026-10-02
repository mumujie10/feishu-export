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
            Summary: "最容易漏的一步。没授权时文档会成片出现在「未导出清单」里，报错误码 1069902。",
            Art: "grant",
            Items:
            [
                new(GuideItemKind.Text, "在飞书桌面客户端新建一个群组，只有你自己也可以。"),
                new(GuideItemKind.Text, "群设置 → 群机器人 → 添加机器人 → 选中刚创建的那个自建应用。"),
                new(
                    GuideItemKind.Text,
                    "打开要导出的知识库 → 「知识空间设置」→「成员管理」→「添加管理员」→ 搜索并选中刚才那个群组。"),
                new(
                    GuideItemKind.Note,
                    "原理是「应用进了群，群是知识库管理员」，所以授权对象是群组，不是应用本身。"),
                new(
                    GuideItemKind.Note,
                    "导出个人空间云文档走的是另一条路：开放平台不支持列举个人文件夹，必须先把目标文件夹分享给自建应用，再从分享链接里取 folderToken。"),
            ]),

        new(
            Id: "target",
            Title: "选中要导出的内容",
            Summary: "前四步做完，回到本工具，绝大多数情况下点一下按钮就能选到目标，不需要手填任何 Token。",
            Art: "target",
            Items:
            [
                new(GuideItemKind.Text, "在第 1 步填好 AppId / AppSecret。"),
                new(GuideItemKind.Text, "选「知识库」→ 点右侧「获取列表」→ 在下拉框里挑一个知识库，名称和 Id 会自动带出。"),
                new(
                    GuideItemKind.Text,
                    "列表里没有想要的知识库，说明那个知识库还没授权给应用，回到上一节继续设置管理员。"),
                new(
                    GuideItemKind.Text,
                    "想跳过下拉框，可以直接从知识库设置页的地址栏里取 spaceId 填进输入框。"),
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
    ];

    /// <summary>全部章节，按阅读顺序。</summary>
    public static IReadOnlyList<GuideStep> All => Steps;

    /// <summary>按 Id 取一节，找不到返回 null。</summary>
    public static GuideStep? Find(string? id)
        => id is null ? null : Steps.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
}
