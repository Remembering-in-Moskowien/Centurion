using System.Net.Http;

namespace Centurion.Core.Tools;

public static class DockerChecker
{
    /// <summary>
    /// 检查 Gentle 服务是否可访问（发送 GET 请求到根路径）。
    /// </summary>
    public static async Task<bool> IsGentleAvailableAsync(string baseUrl)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var url = baseUrl.TrimEnd('/') + "/";
            var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}