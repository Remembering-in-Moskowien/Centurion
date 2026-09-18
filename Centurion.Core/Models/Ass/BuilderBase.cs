namespace Centurion.Core.Models.Ass;

/// <summary>
/// 构建器基础抽象类，提供流式赋值通用封装（Builder 模式基类）
/// </summary>
/// <typeparam name="TBuilder">当前构建器派生类类型</typeparam>
/// <typeparam name="TProduct">构建完成输出的实体类型</typeparam>
public abstract class BuilderBase<TBuilder, TProduct>
    where TBuilder : BuilderBase<TBuilder, TProduct>
{
    /// <summary>
    /// 为内部字段赋值，返回自身实现链式调用
    /// </summary>
    /// <typeparam name="TValue">字段值类型</typeparam>
    /// <param name="field">待赋值字段引用</param>
    /// <param name="value">新值</param>
    /// <returns>当前构建器实例</returns>
    protected TBuilder Set<TValue>(ref TValue field, TValue value)
    {
        field = value;
        return (TBuilder)this;
    }

    /// <summary>
    /// 执行构建，生成最终实体对象
    /// </summary>
    /// <returns>构建完成的模型</returns>
    public abstract TProduct Build();
}
