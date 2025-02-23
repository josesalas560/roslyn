﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.MetadataAsSource;
using Microsoft.VisualStudio.Language.Intellisense;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.Peek
{
    internal class DefinitionPeekableItem : PeekableItem
    {
        private readonly Workspace _workspace;
        private readonly ProjectId _projectId;
        private readonly SymbolKey _symbolKey;
        private readonly IMetadataAsSourceFileService _metadataAsSourceFileService;

        public DefinitionPeekableItem(
            Workspace workspace, ProjectId projectId, SymbolKey symbolKey,
            IPeekResultFactory peekResultFactory,
            IMetadataAsSourceFileService metadataAsSourceService)
            : base(peekResultFactory)
        {
            _workspace = workspace;
            _projectId = projectId;
            _symbolKey = symbolKey;
            _metadataAsSourceFileService = metadataAsSourceService;
        }

        public override IEnumerable<IPeekRelationship> Relationships
        {
            get { return SpecializedCollections.SingletonEnumerable(PredefinedPeekRelationships.Definitions); }
        }

        public override IPeekResultSource GetOrCreateResultSource(string relationshipName)
            => new ResultSource(this);

        private sealed class ResultSource : IPeekResultSource
        {
            private readonly DefinitionPeekableItem _peekableItem;

            public ResultSource(DefinitionPeekableItem peekableItem)
                => _peekableItem = peekableItem;

            public void FindResults(string relationshipName, IPeekResultCollection resultCollection, CancellationToken cancellationToken, IFindPeekResultsCallback callback)
            {
                if (relationshipName != PredefinedPeekRelationships.Definitions.Name)
                {
                    return;
                }

                // Note: this is called on a background thread, but we must block the thread since the API doesn't support proper asynchrony.
                var workspace = _peekableItem._workspace;
                var solution = workspace.CurrentSolution;
                var project = solution.GetProject(_peekableItem._projectId);
                var compilation = project.GetCompilationAsync(cancellationToken).WaitAndGetResult_CanCallOnBackground(cancellationToken);

                var symbol = _peekableItem._symbolKey.Resolve(compilation, ignoreAssemblyKey: true, cancellationToken: cancellationToken).Symbol;
                if (symbol == null)
                {
                    callback.ReportFailure(new Exception(EditorFeaturesResources.No_information_found));
                    return;
                }

                var sourceLocations = symbol.Locations.Where(l => l.IsInSource).ToList();

                if (!sourceLocations.Any())
                {
                    // It's a symbol from metadata, so we want to go produce it from metadata
                    var declarationFile = _peekableItem._metadataAsSourceFileService.GetGeneratedFileAsync(project, symbol, signaturesOnly: true, allowDecompilation: false, cancellationToken).WaitAndGetResult_CanCallOnBackground(cancellationToken);
                    var peekDisplayInfo = new PeekResultDisplayInfo(declarationFile.DocumentTitle, declarationFile.DocumentTitle, declarationFile.DocumentTitle, declarationFile.DocumentTitle);
                    var identifierSpan = declarationFile.IdentifierLocation.GetLineSpan().Span;
                    var entityOfInterestSpan = PeekHelpers.GetEntityOfInterestSpan(symbol, workspace, declarationFile.IdentifierLocation, cancellationToken);
                    resultCollection.Add(PeekHelpers.CreateDocumentPeekResult(declarationFile.FilePath, identifierSpan, entityOfInterestSpan, peekDisplayInfo, _peekableItem.PeekResultFactory, isReadOnly: true));
                }

                var processedSourceLocations = 0;

                foreach (var declaration in sourceLocations)
                {
                    var declarationLocation = declaration.GetMappedLineSpan();

                    var entityOfInterestSpan = PeekHelpers.GetEntityOfInterestSpan(symbol, workspace, declaration, cancellationToken);
                    resultCollection.Add(PeekHelpers.CreateDocumentPeekResult(declarationLocation.Path, declarationLocation.Span, entityOfInterestSpan, _peekableItem.PeekResultFactory));
                    callback.ReportProgress(100 * ++processedSourceLocations / sourceLocations.Count);
                }
            }
        }
    }
}
