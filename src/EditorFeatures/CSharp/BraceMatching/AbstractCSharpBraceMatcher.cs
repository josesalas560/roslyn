﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using Microsoft.CodeAnalysis.BraceMatching;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.CodeAnalysis.Editor.CSharp.BraceMatching
{
    internal abstract class AbstractCSharpBraceMatcher : AbstractBraceMatcher
    {
        protected AbstractCSharpBraceMatcher(SyntaxKind openBrace, SyntaxKind closeBrace)
            : base(new BraceCharacterAndKind(SyntaxFacts.GetText(openBrace)[0], (int)openBrace),
                   new BraceCharacterAndKind(SyntaxFacts.GetText(closeBrace)[0], (int)closeBrace))
        {
        }
    }
}
