using System.Diagnostics.CodeAnalysis; // 新增
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Centurion.Cli;

/// <summary>
/// Spectre.Cli type registrar: registers command and dependency types with the Microsoft DI container.
/// </summary>
public sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    /// <summary>
    /// Builds a service provider and returns the matching type resolver.
    /// </summary>
    /// <returns>The resolver used to resolve types at runtime.</returns>
    public ITypeResolver Build()
    {
        return new TypeResolver(services.BuildServiceProvider());
    }

    /// <summary>
    /// Registers a singleton mapping from a service type to an implementation type.
    /// </summary>
    /// <param name="service">The service abstraction type.</param>
    /// <param name="implementation">The concrete implementation type.</param>
    public void Register(Type service,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type implementation)
    {
        services.AddSingleton(service, implementation);
    }

    /// <summary>
    /// Registers a singleton mapping from a service type to an existing instance.
    /// </summary>
    /// <param name="service">The service abstraction type.</param>
    /// <param name="implementation">An already constructed implementation instance.</param>
    public void RegisterInstance(Type service, object implementation)
    {
        services.AddSingleton(service, implementation);
    }

    /// <summary>
    /// Registers a service via a lazy factory; the factory runs only on first resolution.
    /// </summary>
    /// <param name="service">The service abstraction type.</param>
    /// <param name="factory">The factory delegate that creates the implementation instance.</param>
    public void RegisterLazy(Type service, Func<object> factory)
    {
        services.AddSingleton(service, _ => factory());
    }
}

/// <summary>
/// Spectre.Cli type resolver: resolves service instances from the built DI container.
/// </summary>
public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver
{
    /// <summary>
    /// Resolves a service instance of the given type.
    /// </summary>
    /// <param name="type">The type to resolve; null returns null.</param>
    /// <returns>The resolved service instance; null when unregistered.</returns>
    public object? Resolve(Type? type)
    {
        return type == null ? null : provider.GetService(type);
    }
}