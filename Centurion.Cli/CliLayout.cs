namespace Centurion.Cli;

/// <summary>
/// CLI 排版常量与计算：统一表格总宽（标题居中与表格视觉一致，
/// 列按内容分配不折行）。宽度 = 终端宽 - 4，限幅 [60, 120]。
/// </summary>
public static class CliLayout
{
    /// <summary>主表格首选总宽（终端重定向/无宽度时按 96 处理）。</summary>
    public static int TableWidth()
    {
        try
        {
            var w = System.Console.WindowWidth;
            return Math.Clamp(w - 4, 60, 120);
        }
        catch (IOException)
        {
            return 96;
        }
        catch (ArgumentOutOfRangeException)
        {
            return 96;
        }
    }
}
