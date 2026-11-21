using Microsoft.Extensions.DependencyInjection;

namespace QuickQr.Infrastructure.Tests.Fakes;

/// <summary>
/// Minimal resolver over an <see cref="IServiceCollection"/> (singletons, factories, keyed services, last registration wins)
/// built on <see cref="ActivatorUtilities"/>. The test project only references the DI abstractions, not the container.
/// </summary>
public sealed class TestServiceProvider(IServiceCollection services) : IKeyedServiceProvider, IDisposable
{
    private readonly Dictionary<(Type Type, object? Key), object?> _singletons = [];
    private readonly List<IDisposable> _disposables = [];

    public object? GetService(Type serviceType) => GetKeyedService(serviceType, null);

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IKeyedServiceProvider)) return this;

        var descriptor = services.LastOrDefault(d => d.ServiceType == serviceType && Equals(d.ServiceKey, serviceKey));
        if (descriptor is null) return null;
        if (_singletons.TryGetValue((serviceType, serviceKey), out var existing)) return existing;

        var instance = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationInstance
              ?? descriptor.KeyedImplementationFactory?.Invoke(this, serviceKey)
              ?? ActivatorUtilities.CreateInstance(this, descriptor.KeyedImplementationType!)
            : descriptor.ImplementationInstance
              ?? descriptor.ImplementationFactory?.Invoke(this)
              ?? ActivatorUtilities.CreateInstance(this, descriptor.ImplementationType!);

        if (descriptor.Lifetime == ServiceLifetime.Singleton) _singletons[(serviceType, serviceKey)] = instance;
        if (instance is IDisposable disposable && !ReferenceEquals(instance, descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance))
            _disposables.Add(disposable);
        return instance;
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        GetKeyedService(serviceType, serviceKey) ?? throw new InvalidOperationException($"No service for {serviceType} (key {serviceKey}).");

    public T GetRequired<T>() where T : notnull => (T)GetRequiredKeyedService(typeof(T), null);

    public void Dispose()
    {
        foreach (var disposable in _disposables.Distinct()) disposable.Dispose();
    }
}
