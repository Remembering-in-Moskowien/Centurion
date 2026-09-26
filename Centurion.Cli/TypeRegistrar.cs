using System.Diagnostics.CodeAnalysis; // 新增
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Centurion.Cli;

/// <summary>
/// Spectre.Cli 类型注册器：将命令及依赖类型登记到 Microsoft DI 容器。
/// </summary>
public sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    /// <summary>
    /// 构建服务提供器并返回对应的类型解析器。
    /// </summary>
    /// <returns>用于运行时解析类型的解析器。</returns>
    public ITypeResolver Build()
    {
        return new TypeResolver(services.BuildServiceProvider());
    }

    /// <summary>
    /// 以单例方式注册服务类型到实现类型的映射。
    /// </summary>
    /// <param name="service">服务抽象类型。</param>
    /// <param name="implementation">具体实现类型。</param>
    public void Register(Type service,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type implementation)
    {
        services.AddSingleton(service, implementation);
    }

    /// <summary>
    /// 以单例方式注册服务类型到既有实例的映射。
    /// </summary>
    /// <param name="service">服务抽象类型。</param>
    /// <param name="implementation">已构造好的实现实例。</param>
    public void RegisterInstance(Type service, object implementation)
    {
        services.AddSingleton(service, implementation);
    }

    /// <summary>
    /// 以延迟工厂方式注册服务，首次解析时才调用工厂创建实例。
    /// </summary>
    /// <param name="service">服务抽象类型。</param>
    /// <param name="factory">创建实现实例的工厂委托。</param>
    public void RegisterLazy(Type service, Func<object> factory)
    {
        services.AddSingleton(service, _ => factory());
    }
}

/// <summary>
/// Spectre.Cli 类型解析器：从已构建的 DI 容器解析服务实例。
/// </summary>
public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver
{
    /// <summary>
    /// 解析指定类型的服务实例。
    /// </summary>
    /// <param name="type">要解析的类型；为 null 时返回 null。</param>
    /// <returns>解析到的服务实例；未注册时返回 null。</returns>
    public object? Resolve(Type? type)
    {
        return type == null ? null : provider.GetService(type);
    }
}