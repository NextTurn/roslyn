﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;

using static Microsoft.CodeAnalysis.CodeGeneration.CodeGenerationHelpers;
using static Microsoft.CodeAnalysis.CSharp.CodeGeneration.CSharpCodeGenerationHelpers;

namespace Microsoft.CodeAnalysis.CSharp.CodeGeneration
{
    internal static class ParameterGenerator
    {
        public static ParameterListSyntax GenerateParameterList(
            ImmutableArray<IParameterSymbol> parameterDefinitions,
            bool isExplicit,
            CodeGenerationOptions options)
        {
            return GenerateParameterList((IEnumerable<IParameterSymbol>)parameterDefinitions, isExplicit, options);
        }

        public static ParameterListSyntax GenerateParameterList(
            IEnumerable<IParameterSymbol> parameterDefinitions,
            bool isExplicit,
            CodeGenerationOptions options)
        {
            var parameters = GetParameters(parameterDefinitions, isExplicit, options);

            return SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters));
        }

        public static BracketedParameterListSyntax GenerateBracketedParameterList(
            ImmutableArray<IParameterSymbol> parameterDefinitions,
            bool isExplicit,
            CodeGenerationOptions options)
        {
            return GenerateBracketedParameterList((IList<IParameterSymbol>)parameterDefinitions, isExplicit, options);
        }

        public static BracketedParameterListSyntax GenerateBracketedParameterList(
            IEnumerable<IParameterSymbol> parameterDefinitions,
            bool isExplicit,
            CodeGenerationOptions options)
        {
            // Bracketed parameter lists come from indexers.  Those don't have type parameters, so we
            // could never have a typeParameterMapping.
            var parameters = GetParameters(parameterDefinitions, isExplicit, options);

            return SyntaxFactory.BracketedParameterList(
                parameters: SyntaxFactory.SeparatedList(parameters));
        }

        internal static ImmutableArray<ParameterSyntax> GetParameters(
            IEnumerable<IParameterSymbol> parameterDefinitions,
            bool isExplicit,
            CodeGenerationOptions options)
        {
            var result = ArrayBuilder<ParameterSyntax>.GetInstance();
            var seenOptional = false;
            var isFirstParam = true;

            foreach (var p in parameterDefinitions)
            {
                var parameter = GetParameter(p, options, isExplicit, isFirstParam, seenOptional);
                result.Add(parameter);
                seenOptional = seenOptional || parameter.Default != null;
                isFirstParam = false;
            }

            return result.ToImmutableAndFree();
        }

        internal static ParameterSyntax GetParameter(IParameterSymbol parameter, CodeGenerationOptions options, bool isExplicit, bool isFirstParam, bool seenOptional)
        {
            var reusableSyntax = GetReuseableSyntaxNodeForSymbol<ParameterSyntax>(parameter, options);
            if (reusableSyntax != null)
            {
                return reusableSyntax;
            }

            var (attributeLists, nullableAnnotation) = GenerateAttributes(parameter, isExplicit, options);
            return SyntaxFactory.Parameter(parameter.Name.ToIdentifierToken())
                    .WithAttributeLists(attributeLists)
                    .WithModifiers(GenerateModifiers(parameter, isFirstParam))
                    .WithType(parameter.Type.WithNullableAnnotation(nullableAnnotation).GenerateTypeSyntax())
                    .WithDefault(GenerateEqualsValueClause(parameter, isExplicit, seenOptional));
        }

        private static SyntaxTokenList GenerateModifiers(
            IParameterSymbol parameter, bool isFirstParam)
        {
            var list = CSharpSyntaxGeneratorInternal.GetParameterModifiers(parameter.RefKind);

            if (isFirstParam &&
                parameter.ContainingSymbol is IMethodSymbol methodSymbol &&
                methodSymbol.IsExtensionMethod)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.ThisKeyword));
            }

            if (parameter.IsParams)
            {
                list = list.Add(SyntaxFactory.Token(SyntaxKind.ParamsKeyword));
            }

            return list;
        }

        private static EqualsValueClauseSyntax GenerateEqualsValueClause(
            IParameterSymbol parameter,
            bool isExplicit,
            bool seenOptional)
        {
            if (!parameter.IsParams && !isExplicit && !parameter.IsRefOrOut())
            {
                if (parameter.HasExplicitDefaultValue || seenOptional)
                {
                    var defaultValue = parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue : null;
                    if (defaultValue is DateTime)
                    {
                        return null;
                    }

                    return SyntaxFactory.EqualsValueClause(
                        GenerateEqualsValueClauseWorker(parameter, defaultValue));
                }
            }

            return null;
        }

        private static ExpressionSyntax GenerateEqualsValueClauseWorker(
            IParameterSymbol parameter,
            object value)
        {
            return ExpressionGenerator.GenerateExpression(parameter.Type, value, canUseFieldReference: true);
        }

        private static (SyntaxList<AttributeListSyntax>, NullableAnnotation) GenerateAttributes(
            IParameterSymbol parameter, bool isExplicit, CodeGenerationOptions options)
        {
            var nullableAnnotation = parameter.NullableAnnotation;

            if (isExplicit)
            {
                return (default, nullableAnnotation);
            }

            var attributes = parameter.GetAttributes();

            AdjustNullableAnnotationByAttributes(parameter.Type, ref attributes, ref nullableAnnotation, isParameter: true);

            if (attributes.Length == 0)
            {
                return (default, nullableAnnotation);
            }

            return (AttributeGenerator.GenerateAttributeLists(attributes, options), nullableAnnotation);
        }
    }
}
