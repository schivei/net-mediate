using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace NetMediate.Tests.Internals;

public sealed class MediatorServiceBuilderPrivateTests
{
    private static (Type, Type[]) TypeTuple(Type t) => (t, t.GetInterfaces());

    private static MethodInfo GetInterfacesMethod() =>
        typeof(MediatorServiceBuilder).GetMethod("GetInterfaces", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static MethodInfo MapMethod() =>
        typeof(MediatorServiceBuilder).GetMethod("Map", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Dummy message
    public sealed class Msg { }

    // Valid handler implementing multiple valid interfaces for Msg
    public sealed class MultiHandler :
        INotificationHandler<Msg>,
        ICommandHandler<Msg>,
        IRequestHandler<Msg, string>,
        IStreamHandler<Msg, string>,
        IValidationHandler<Msg>
    {
        public Task Handle(Msg _, CancellationToken __ = default) => Task.CompletedTask;
        Task<string> IRequestHandler<Msg, string>.Handle(Msg _, CancellationToken __ = default) => Task.FromResult("ok");
        async IAsyncEnumerable<string> IStreamHandler<Msg, string>.Handle(Msg _, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken __ = default)
        {
            yield return "s";
            await Task.CompletedTask;
        }
        public ValueTask<ValidationResult> ValidateAsync(Msg _, CancellationToken __ = default) => ValueTask.FromResult(ValidationResult.Success!);
    }

    public abstract class AbstractHandler : ICommandHandler<Msg>
    {
        public abstract Task Handle(Msg message, CancellationToken cancellationToken = default);
    }

    public sealed class NoInterfaceHandler { }

    [Fact]
    public void GetInterfaces_Throws_For_AbstractOrNotClass()
    {
        var mi = GetInterfacesMethod();

        var ex = Assert.Throws<TargetInvocationException>(() =>
            mi.Invoke(null, [typeof(AbstractHandler), typeof(Msg)])
        );
        var inner = Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Contains("must be a non-abstract class", inner.Message);
    }

    [Fact]
    public void GetInterfaces_Throws_When_No_Valid_Handler_Interfaces()
    {
        var mi = GetInterfacesMethod();

        var ex = Assert.Throws<TargetInvocationException>(() =>
            mi.Invoke(null, [typeof(NoInterfaceHandler), typeof(Msg)])
        );
        var inner = Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Contains("does not implement any valid handler interfaces", inner.Message);
    }

    [Fact]
    public void GetInterfaces_Returns_Valid_Interfaces()
    {
        var mi = GetInterfacesMethod();
        var result = (Type[])mi.Invoke(null, [typeof(MultiHandler), typeof(Msg)])!;
        Assert.Contains(result, i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>));
        Assert.Contains(result, i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<>));
        Assert.Contains(result, i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));
        Assert.Contains(result, i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamHandler<,>));
        Assert.Contains(result, i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidationHandler<>));
    }

    [Fact]
    public void Map_Throws_When_NoValidInterfaceFound_And_HandlerInterface_Null()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder(services);
        var map = MapMethod();

        var types = new List<(Type, Type[])> { (typeof(NoInterfaceHandler), Type.EmptyTypes) };

        var ex = Assert.Throws<TargetInvocationException>(() =>
            map.Invoke(builder, [types, null])
        );
        var inner = Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Equal("handlerInterface", inner.ParamName);
        Assert.Contains("No valid handler interface found", inner.Message);
    }

    [Fact]
    public void Map_With_Explicit_Interface_Registers_Handler()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder(services);
        var map = MapMethod();

        var tuple = TypeTuple(typeof(MultiHandler));
        var types = new List<(Type, Type[])> { tuple };

        // Provide explicit handler interface to force that branch
        map.Invoke(builder, [types, typeof(INotificationHandler<>)]);

        var serviceType = typeof(INotificationHandler<>).MakeGenericType(typeof(Msg));
        Assert.Contains(services, d => d.ServiceType == serviceType && d.ImplementationType == typeof(MultiHandler));
    }
}