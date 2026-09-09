namespace Piko.Runtime.Ipc;

public static class AgentInteractionText
{
    public static string DescribeFailure(string reason) => reason switch
    {
        "model_disabled" or "cloud_ai_disabled" => "模型尚未开启。打开设置选择模型；桌面陪伴仍可正常使用。",
        "api_key_unavailable" => "尚未配置 API Key，请在设置中填写并测试连接。",
        "credential_unavailable" => "暂时无法读取模型凭据，请在设置中重新保存 API Key。",
        "http_401" => "API Key 无效或已过期，请在设置中更新。",
        "http_403" => "账号没有访问权限，请检查模型权限。",
        "http_404" => "找不到模型或接口，请检查模型名称和地址。",
        "http_400" or "http_422" => "模型接口不兼容，请检查是否支持结构化回复。",
        "http_429" => "请求过于频繁或额度不足，请检查额度后稍后重试。",
        "timeout" => "模型回复超时。问题已保留，可以稍后重试。",
        "agent_busy" => "Piko 正在处理另一个请求，请稍后重试。",
        "provider_error" => "暂时无法获得有效回复，请检查模型服务和网络后重试。",
        "invalid_plan_json" or "invalid_plan_shape" or "invalid_tool_proposal" or "missing_output_text" =>
            "模型返回的内容不兼容，未执行任何计划。请重试或更换模型。",
        _ when reason.StartsWith("http_5", StringComparison.Ordinal) => "模型服务暂时出错，请稍后重试。",
        _ => "暂时无法完成请求。问题已保留，请检查后台状态或模型设置后重试。"
    };

    public static string ToolTitle(string toolName) => toolName switch
    {
        "git.status" => "查看 Git 状态摘要",
        "workspace.file.read" => "读取指定文本文件",
        _ => toolName
    };
}
