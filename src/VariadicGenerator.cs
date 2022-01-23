using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VariadicGenerics.SourceGenerator
{
    [Generator]
    public sealed class VariadicGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            var receiver = (SyntaxReceiver)context.SyntaxContextReceiver!;
            var variadicMethods = new List<(IMethodSymbol original, int arity)>();

            foreach (var list in receiver!.GetGenericArgumentLists())
            {
                var model = context.Compilation.GetSemanticModel(list.SyntaxTree);
                var info = model.GetSymbolInfo(list.Parent!);
                var symbol = info.CandidateReason == CandidateReason.WrongArity
                    ? info.CandidateSymbols.FirstOrDefault()
                    : info.Symbol;

                if (symbol is IMethodSymbol methodSymbol)
                {
                    if (methodSymbol.IsGenericMethod
                        && methodSymbol.ConstructedFrom is
                            IMethodSymbol originalDefinition
                        && originalDefinition.TypeParameters.SingleOrDefault() is
                            ITypeParameterSymbol genericParameter
                        && genericParameter.GetAttributes().SingleOrDefault(
                            x => x?.AttributeClass?.Name == "VariadicAttribute") is
                            {} attr)
                    {
                        variadicMethods.Add((originalDefinition, list.Arguments.Count));
                    }
                }
            }

            foreach (var (method, arity) in variadicMethods.Distinct())
            {
                var types = string.Join(", ",
                    Enumerable.Range(1, arity)
                        .Select(x => $"T{x}"));
                var constraints = "// No constraints on original method";
                
                if (method.TypeParameters.Any(HasConstraint))
                {
                    constraints = string.Join("\n            ",
                        Enumerable.Range(1, arity)
                            .Select(
                                x => $"where T{x} : {GetConstraints(method.TypeParameters[0])}"));
                }

                var parameters = "";
                if (method.IsExtensionMethod || !method.IsStatic)
                {
                    parameters = string.Join(", ",
                        Enumerable.Range(1, arity)
                            .Select(x => $"T{x} value{x}")
                            .Prepend(
                                $"this {method.ReceiverType!.ToDisplayString()} target"));
                }
                else
                {
                    parameters = string.Join(", ",
                        Enumerable.Range(1, arity)
                            .Select(x => $"T{x} value{x}"));
                }

                var invocations = "";
                if (method.IsExtensionMethod || !method.IsStatic)
                {
                    invocations = string.Join("\n            ",
                        Enumerable.Range(1, arity)
                            .Select(
                                x => $"target.{method.Name}(value{x});"));
                }
                else
                {
                    invocations = string.Join("\n            ",
                        Enumerable.Range(1, arity)
                            .Select(
                                x => $"{method.Name}(value{x});"));
                }

                context.AddSource($"VariadicMethod.{method.Name}.{arity}.cs",
$@"namespace VariadicGenerics
{{
    internal static class Extensions
    {{
        public static {method.ReturnType.ToDisplayString()} {method.Name}<{types}>({parameters})
            {constraints}
        {{
            {invocations}
        }}
    }}
}}");
            }
        }

        private static string GetConstraints(ITypeParameterSymbol symbol)
        {
            return string.Join(", ", Impl(symbol));

            static IEnumerable<string> Impl(ITypeParameterSymbol symbol)
            {
                if (symbol.HasConstructorConstraint)
                    yield return "new()";
                if (symbol.HasNotNullConstraint)
                    yield return "notnull";
                if (symbol.HasReferenceTypeConstraint)
                    yield return "class";
                else if (symbol.HasValueTypeConstraint)
                    yield return "struct";
                else if (symbol.HasUnmanagedTypeConstraint)
                    yield return "unmanaged";

                foreach (var type in symbol.ConstraintTypes)
                    yield return type.ToDisplayString();
            }
        }

        private static bool HasConstraint(ITypeParameterSymbol symbol)
        {
            return symbol.HasConstructorConstraint
                || symbol.HasNotNullConstraint
                || symbol.HasReferenceTypeConstraint
                || symbol.HasUnmanagedTypeConstraint
                || symbol.HasValueTypeConstraint
                || symbol.ConstraintTypes.Any()
                || symbol.ConstraintNullableAnnotations.Any();
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        private sealed class SyntaxReceiver : ISyntaxContextReceiver
        {
            private readonly List<TypeArgumentListSyntax> _typeArgumentLists;

            public SyntaxReceiver()
            {
                _typeArgumentLists = new();
            }

            public IEnumerable<TypeArgumentListSyntax> GetGenericArgumentLists()
                => _typeArgumentLists;

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is TypeArgumentListSyntax typeArguments)
                {
                    _typeArgumentLists.Add(typeArguments);
                }
            }
        }
    }
}