﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Structure
{
    internal abstract class BlockStructureServiceWithProviders : BlockStructureService
    {
        private readonly Workspace _workspace;
        private readonly ImmutableArray<BlockStructureProvider> _providers;

        protected BlockStructureServiceWithProviders(Workspace workspace)
        {
            _workspace = workspace;
            _providers = GetBuiltInProviders().Concat(GetImportedProviders());
        }

        /// <summary>
        /// Returns the providers always available to the service.
        /// This does not included providers imported via MEF composition.
        /// </summary>
        protected virtual ImmutableArray<BlockStructureProvider> GetBuiltInProviders()
            => ImmutableArray<BlockStructureProvider>.Empty;

        private ImmutableArray<BlockStructureProvider> GetImportedProviders()
        {
            var language = Language;
            var mefExporter = (IMefHostExportProvider)_workspace.Services.HostServices;

            var providers = mefExporter.GetExports<BlockStructureProvider, LanguageMetadata>()
                                       .Where(lz => lz.Metadata.Language == language)
                                       .Select(lz => lz.Value);

            return providers.ToImmutableArray();
        }

        public override async Task<BlockStructure> GetBlockStructureAsync(
            Document document,
            BlockStructureOptions options,
            CancellationToken cancellationToken)
        {
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var context = CreateContext(syntaxTree, options, cancellationToken);

            return GetBlockStructure(context, _providers);
        }

        public BlockStructure GetBlockStructure(
            SyntaxTree syntaxTree,
            in BlockStructureOptions options,
            CancellationToken cancellationToken)
        {
            var context = CreateContext(syntaxTree, options, cancellationToken);
            return GetBlockStructure(context, _providers);
        }

        private static BlockStructureContext CreateContext(
            SyntaxTree syntaxTree,
            in BlockStructureOptions options,
            CancellationToken cancellationToken)
        {
            return new BlockStructureContext(syntaxTree, options, cancellationToken);
        }

        private static BlockStructure GetBlockStructure(
            BlockStructureContext context,
            ImmutableArray<BlockStructureProvider> providers)
        {
            foreach (var provider in providers)
                provider.ProvideBlockStructure(context);

            return CreateBlockStructure(context);
        }

        private static BlockStructure CreateBlockStructure(BlockStructureContext context)
        {
            using var _ = ArrayBuilder<BlockSpan>.GetInstance(out var updatedSpans);
            foreach (var span in context.Spans)
            {
                var updatedSpan = UpdateBlockSpan(span, context.Options);
                updatedSpans.Add(updatedSpan);
            }

            return new BlockStructure(updatedSpans.ToImmutable());
        }

        private static BlockSpan UpdateBlockSpan(BlockSpan blockSpan, in BlockStructureOptions options)
        {
            var type = blockSpan.Type;

            var isTopLevel = BlockTypes.IsDeclarationLevelConstruct(type);
            var isMemberLevel = BlockTypes.IsCodeLevelConstruct(type);
            var isComment = BlockTypes.IsCommentOrPreprocessorRegion(type);

            if ((!options.ShowBlockStructureGuidesForDeclarationLevelConstructs && isTopLevel) ||
                (!options.ShowBlockStructureGuidesForCodeLevelConstructs && isMemberLevel) ||
                (!options.ShowBlockStructureGuidesForCommentsAndPreprocessorRegions && isComment))
            {
                type = BlockTypes.Nonstructural;
            }

            var isCollapsible = blockSpan.IsCollapsible;
            if (isCollapsible)
            {
                if ((!options.ShowOutliningForDeclarationLevelConstructs && isTopLevel) ||
                    (!options.ShowOutliningForCodeLevelConstructs && isMemberLevel) ||
                    (!options.ShowOutliningForCommentsAndPreprocessorRegions && isComment))
                {
                    isCollapsible = false;
                }
            }

            return blockSpan.With(type: type, isCollapsible: isCollapsible);
        }
    }
}
