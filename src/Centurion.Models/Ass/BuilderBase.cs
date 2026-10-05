namespace Centurion.Models.Ass;

/// <summary>
/// Base class for builders, providing a fluent assignment wrapper.
/// </summary>
/// <typeparam name="TBuilder">Concrete derived builder type.</typeparam>
/// <typeparam name="TProduct">Product type returned by the builder.</typeparam>
public abstract class BuilderBase<TBuilder, TProduct>
    where TBuilder : BuilderBase<TBuilder, TProduct>
{
    /// <summary>
    /// Assigns a field and returns this builder for chaining.
    /// </summary>
    /// <typeparam name="TValue">Field value type.</typeparam>
    /// <param name="field">Reference to the field to assign.</param>
    /// <param name="value">New value.</param>
    /// <returns>This builder instance.</returns>
    protected TBuilder Set<TValue>(ref TValue field, TValue value)
    {
        field = value;
        return (TBuilder)this;
    }

    /// <summary>
    /// Builds and returns the final product.
    /// </summary>
    /// <returns>The built model.</returns>
    public abstract TProduct Build();
}
