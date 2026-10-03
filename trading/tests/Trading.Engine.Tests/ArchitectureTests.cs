using System.Reflection;

namespace Trading.Engine.Tests;

public sealed class ArchitectureTests
{
    private static readonly Type[] FloatingPointTypes = [typeof(double), typeof(float), typeof(Half)];

    // BannedSymbols.txt cannot catch floating point in declarations, so this checks signatures, fields and locals.
    [Fact]
    public void EngineNeverUsesFloatingPoint()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var violations = new List<string>();

        foreach (var type in typeof(TradingEngine).Assembly.GetTypes())
        {
            foreach (var field in type.GetFields(all))
            {
                Check(field.FieldType, $"{type.FullName}.{field.Name}");
            }

            foreach (var property in type.GetProperties(all))
            {
                Check(property.PropertyType, $"{type.FullName}.{property.Name}");
            }

            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                var where = $"{type.FullName}.{method.Name}";
                if (method is MethodInfo info)
                {
                    Check(info.ReturnType, where);
                }

                foreach (var parameter in method.GetParameters())
                {
                    Check(parameter.ParameterType, where);
                }

                foreach (var local in method.GetMethodBody()?.LocalVariables ?? [])
                {
                    Check(local.LocalType, where);
                }
            }
        }

        Assert.Empty(violations);

        void Check(Type type, string where)
        {
            if (UsesFloatingPoint(type))
            {
                violations.Add($"{where} uses {type.Name}");
            }
        }
    }

    // The service routes events and commands to accounts through these interfaces. Prices and groups belong to no account.
    [Fact]
    public void EveryAccountEventAndCommandIsMarked()
    {
        var types = typeof(TradingEngine).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }).ToList();

        var unmarkedEvents = types
            .Where(t => t.IsAssignableTo(typeof(Events.EngineEvent)) && t != typeof(Events.InputRejected) && t != typeof(Events.GroupCreated))
            .Where(t => !t.IsAssignableTo(typeof(Events.IAccountEvent)));
        var unmarkedCommands = types
            .Where(t => t.IsAssignableTo(typeof(Inputs.EngineInput)) && t != typeof(Inputs.Quote) && t != typeof(Inputs.CreateGroup))
            .Where(t => !t.IsAssignableTo(typeof(Inputs.IAccountCommand)));

        Assert.Empty(unmarkedEvents);
        Assert.Empty(unmarkedCommands);
    }

    private static bool UsesFloatingPoint(Type type) =>
        FloatingPointTypes.Contains(type)
        || (type.HasElementType && UsesFloatingPoint(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericArguments().Any(UsesFloatingPoint));
}
