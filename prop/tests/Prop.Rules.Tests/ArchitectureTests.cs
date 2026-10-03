using System.Reflection;

namespace Prop.Rules.Tests;

public sealed class ArchitectureTests
{
    private static readonly Type[] FloatingPointTypes = [typeof(double), typeof(float), typeof(Half)];

    // BannedSymbols.txt cannot catch floating point in declarations, so this checks signatures, fields and locals.
    [Fact]
    public void RuleEngineNeverUsesFloatingPoint()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var violations = new List<string>();

        foreach (var type in typeof(ChallengeRules).Assembly.GetTypes())
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

    private static bool UsesFloatingPoint(Type type) =>
        FloatingPointTypes.Contains(type)
        || (type.HasElementType && UsesFloatingPoint(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericArguments().Any(UsesFloatingPoint));
}
