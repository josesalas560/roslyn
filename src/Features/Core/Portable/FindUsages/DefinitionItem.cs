﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.FindSymbols.Finders;
using Microsoft.CodeAnalysis.MetadataAsSource;
using Microsoft.CodeAnalysis.Navigation;
using Microsoft.CodeAnalysis.Tags;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FindUsages
{
    /// <summary>
    /// Information about a symbol's definition that can be displayed in an editor
    /// and used for navigation.
    /// 
    /// Standard implmentations can be obtained through the various <see cref="DefinitionItem"/>.Create
    /// overloads.
    /// 
    /// Subclassing is also supported for scenarios that fall outside the bounds of
    /// these common cases.
    /// </summary>
    internal abstract partial class DefinitionItem
    {
        /// <summary>
        /// The definition item corresponding to the initial symbol the user was trying to find. This item should get
        /// prominent placement in the final UI for the user.
        /// </summary>
        internal const string Primary = nameof(Primary);

        // Existing behavior is to do up to two lookups for 3rd party navigation for FAR.  One
        // for the symbol itself and one for a 'fallback' symbol.  For example, if we're FARing
        // on a constructor, then the fallback symbol will be the actual type that the constructor
        // is contained within.
        internal const string RQNameKey1 = nameof(RQNameKey1);
        internal const string RQNameKey2 = nameof(RQNameKey2);

        /// <summary>
        /// For metadata symbols we encode information in the <see cref="Properties"/> so we can 
        /// retrieve the symbol later on when navigating.  This is needed so that we can go to
        /// metadata-as-source for metadata symbols.  We need to store the <see cref="SymbolKey"/>
        /// for the symbol and the project ID that originated the symbol.  With these we can correctly recover the symbol.
        /// </summary>
        private const string MetadataSymbolKey = nameof(MetadataSymbolKey);
        private const string MetadataSymbolOriginatingProjectIdGuid = nameof(MetadataSymbolOriginatingProjectIdGuid);
        private const string MetadataSymbolOriginatingProjectIdDebugName = nameof(MetadataSymbolOriginatingProjectIdDebugName);

        /// <summary>
        /// If this item is something that cannot be navigated to.  We store this in our
        /// <see cref="Properties"/> to act as an explicit marker that navigation is not possible.
        /// </summary>
        private const string NonNavigable = nameof(NonNavigable);

        /// <summary>
        /// Descriptive tags from <see cref="WellKnownTags"/>. These tags may influence how the 
        /// item is displayed.
        /// </summary>
        public ImmutableArray<string> Tags { get; }

        /// <summary>
        /// Additional properties that can be attached to the definition for clients that want to
        /// keep track of additional data.
        /// </summary>
        public ImmutableDictionary<string, string> Properties { get; }

        /// <summary>
        /// Additional diplayable properties that can be attached to the definition for clients that want to
        /// display additional data.
        /// </summary>
        public ImmutableDictionary<string, string> DisplayableProperties { get; }

        /// <summary>
        /// The DisplayParts just for the name of this definition.  Generally used only for 
        /// error messages.
        /// </summary>
        public ImmutableArray<TaggedText> NameDisplayParts { get; }

        /// <summary>
        /// The full display parts for this definition.  Displayed in a classified 
        /// manner when possible.
        /// </summary>
        public ImmutableArray<TaggedText> DisplayParts { get; }

        /// <summary>
        /// Where the location originally came from (for example, the containing assembly or
        /// project name).  May be used in the presentation of a definition.
        /// </summary>
        public ImmutableArray<TaggedText> OriginationParts { get; }

        /// <summary>
        /// Additional locations to present in the UI.  A definition may have multiple locations 
        /// for cases like partial types/members.
        /// </summary>
        public ImmutableArray<DocumentSpan> SourceSpans { get; }

        /// <summary>
        /// Whether or not this definition should be presented if we never found any references to
        /// it.  For example, when searching for a property, the FindReferences engine will cascade
        /// to the accessors in case any code specifically called those accessors (can happen in 
        /// cross-language cases).  However, in the normal case where there were no calls specifically
        /// to the accessor, we would not want to display them in the UI.  
        /// 
        /// For most definitions we will want to display them, even if no references were found.  
        /// This property allows for this customization in behavior.
        /// </summary>
        public bool DisplayIfNoReferences { get; }

        internal abstract bool IsExternal { get; }

        // F# uses this
        protected DefinitionItem(
            ImmutableArray<string> tags,
            ImmutableArray<TaggedText> displayParts,
            ImmutableArray<TaggedText> nameDisplayParts,
            ImmutableArray<TaggedText> originationParts,
            ImmutableArray<DocumentSpan> sourceSpans,
            ImmutableDictionary<string, string>? properties,
            bool displayIfNoReferences)
            : this(
                tags,
                displayParts,
                nameDisplayParts,
                originationParts,
                sourceSpans,
                properties,
                ImmutableDictionary<string, string>.Empty,
                displayIfNoReferences)
        {
        }

        protected DefinitionItem(
            ImmutableArray<string> tags,
            ImmutableArray<TaggedText> displayParts,
            ImmutableArray<TaggedText> nameDisplayParts,
            ImmutableArray<TaggedText> originationParts,
            ImmutableArray<DocumentSpan> sourceSpans,
            ImmutableDictionary<string, string>? properties,
            ImmutableDictionary<string, string>? displayableProperties,
            bool displayIfNoReferences)
        {
            Tags = tags;
            DisplayParts = displayParts;
            NameDisplayParts = nameDisplayParts.IsDefaultOrEmpty ? displayParts : nameDisplayParts;
            OriginationParts = originationParts.NullToEmpty();
            SourceSpans = sourceSpans.NullToEmpty();
            Properties = properties ?? ImmutableDictionary<string, string>.Empty;
            DisplayableProperties = displayableProperties ?? ImmutableDictionary<string, string>.Empty;
            DisplayIfNoReferences = displayIfNoReferences;

            if (Properties.ContainsKey(MetadataSymbolKey))
            {
                Contract.ThrowIfFalse(Properties.ContainsKey(MetadataSymbolOriginatingProjectIdGuid));
                Contract.ThrowIfFalse(Properties.ContainsKey(MetadataSymbolOriginatingProjectIdDebugName));
            }
        }

#pragma warning disable CS0612 // Type or member is obsolete - TypeScript
        [Obsolete]
        public virtual bool CanNavigateTo(Workspace workspace, CancellationToken cancellationToken) => false;

        [Obsolete]
        public virtual bool TryNavigateTo(Workspace workspace, bool showInPreviewTab, bool activateTab, CancellationToken cancellationToken) => false;

        public virtual Task<bool> CanNavigateToAsync(Workspace workspace, CancellationToken cancellationToken)
            => Task.FromResult(CanNavigateTo(workspace, cancellationToken));

        [Obsolete]
        public virtual Task<bool> TryNavigateToAsync(Workspace workspace, bool showInPreviewTab, bool activateTab, CancellationToken cancellationToken)
            => Task.FromResult(TryNavigateTo(workspace, showInPreviewTab, activateTab, cancellationToken));

        public virtual Task<bool> TryNavigateToAsync(Workspace workspace, NavigationOptions options, CancellationToken cancellationToken)
            => TryNavigateToAsync(workspace, options.PreferProvisionalTab, options.ActivateTab, cancellationToken);
#pragma warning restore

        public static DefinitionItem Create(
            ImmutableArray<string> tags,
            ImmutableArray<TaggedText> displayParts,
            DocumentSpan sourceSpan,
            ImmutableArray<TaggedText> nameDisplayParts = default,
            bool displayIfNoReferences = true)
        {
            return Create(
                tags, displayParts, ImmutableArray.Create(sourceSpan),
                nameDisplayParts, displayIfNoReferences);
        }

        // Kept around for binary compat with F#/TypeScript.
        public static DefinitionItem Create(
            ImmutableArray<string> tags,
            ImmutableArray<TaggedText> displayParts,
            ImmutableArray<DocumentSpan> sourceSpans,
            ImmutableArray<TaggedText> nameDisplayParts,
            bool displayIfNoReferences)
        {
            return Create(
                tags, displayParts, sourceSpans, nameDisplayParts,
                properties: null, displayableProperties: ImmutableDictionary<string, string>.Empty, displayIfNoReferences: displayIfNoReferences);
        }

        public static DefinitionItem Create(
            ImmutableArray<string> tags,
            ImmutableArray<TaggedText> displayParts,
            ImmutableArray<DocumentSpan> sourceSpans,
            ImmutableArray<TaggedText> nameDisplayParts = default,
            ImmutableDictionary<string, string>? properties = null,
            bool displayIfNoReferences = true)
        {
            return Create(tags, displayParts, sourceSpans, nameDisplayParts, properties, ImmutableDictionary<string, string>.Empty, displayIfNoReferences);
        }

        public static DefinitionItem Create(
            ImmutableArray<string> tags,
            ImmutableArray<TaggedText> displayParts,
            ImmutableArray<DocumentSpan> sourceSpans,
            ImmutableArray<TaggedText> nameDisplayParts = default,
            ImmutableDictionary<string, string>? properties = null,
            ImmutableDictionary<string, string>? displayableProperties = null,
            bool displayIfNoReferences = true)
        {
            if (sourceSpans.Length == 0)
            {
                throw new ArgumentException($"{nameof(sourceSpans)} cannot be empty.");
            }

            var firstDocument = sourceSpans[0].Document;
            var originationParts = ImmutableArray.Create(
                new TaggedText(TextTags.Text, firstDocument.Project.Name));

            return new DefaultDefinitionItem(
                tags, displayParts, nameDisplayParts, originationParts,
                sourceSpans, properties, displayableProperties, displayIfNoReferences);
        }

        internal static DefinitionItem CreateMetadataDefinition(
            ImmutableArray<string> tags,
            ImmutableArray<TaggedText> displayParts,
            ImmutableArray<TaggedText> nameDisplayParts,
            Solution solution,
            ISymbol symbol,
            ImmutableDictionary<string, string>? properties = null,
            bool displayIfNoReferences = true)
        {
            properties ??= ImmutableDictionary<string, string>.Empty;

            var symbolKey = symbol.GetSymbolKey().ToString();

            var projectId = solution.GetOriginatingProjectId(symbol);
            Contract.ThrowIfNull(projectId);

            properties = properties.Add(MetadataSymbolKey, symbolKey)
                                   .Add(MetadataSymbolOriginatingProjectIdGuid, projectId.Id.ToString())
                                   .Add(MetadataSymbolOriginatingProjectIdDebugName, projectId.DebugName ?? "");

            // Find the highest level containing type to show as the "file name". For metadata locations
            // that come from embedded source or SourceLink this could be wrong, as there is no reason
            // to assume a type is defined in a filename that matches, but its _way_ too expensive
            // to try to find the right answer. For metadata-as-source locations though, it will be the same
            // as the synthesized filename, so will make sense in the majority of cases.
            var containingTypeName = MetadataAsSourceHelpers.GetTopLevelContainingNamedType(symbol).Name;
            properties = properties.Add(AbstractReferenceFinder.ContainingTypeInfoPropertyName, containingTypeName);

            var originationParts = GetOriginationParts(symbol);
            return new DefaultDefinitionItem(
                tags, displayParts, nameDisplayParts, originationParts,
                sourceSpans: ImmutableArray<DocumentSpan>.Empty,
                properties: properties,
                displayableProperties: ImmutableDictionary<string, string>.Empty,
                displayIfNoReferences: displayIfNoReferences);
        }

        // Kept around for binary compat with F#/TypeScript.
        public static DefinitionItem CreateNonNavigableItem(
            ImmutableArray<string> tags,
            ImmutableArray<TaggedText> displayParts,
            ImmutableArray<TaggedText> originationParts,
            bool displayIfNoReferences)
        {
            return CreateNonNavigableItem(
                tags, displayParts, originationParts,
                properties: null, displayIfNoReferences: displayIfNoReferences);
        }

        public static DefinitionItem CreateNonNavigableItem(
            ImmutableArray<string> tags,
            ImmutableArray<TaggedText> displayParts,
            ImmutableArray<TaggedText> originationParts = default,
            ImmutableDictionary<string, string>? properties = null,
            bool displayIfNoReferences = true)
        {
            properties ??= ImmutableDictionary<string, string>.Empty;
            properties = properties.Add(NonNavigable, "");

            return new DefaultDefinitionItem(
                tags: tags,
                displayParts: displayParts,
                nameDisplayParts: ImmutableArray<TaggedText>.Empty,
                originationParts: originationParts,
                sourceSpans: ImmutableArray<DocumentSpan>.Empty,
                properties: properties,
                displayableProperties: ImmutableDictionary<string, string>.Empty,
                displayIfNoReferences: displayIfNoReferences);
        }

        internal static ImmutableArray<TaggedText> GetOriginationParts(ISymbol symbol)
        {
            // We don't show an origination location for a namespace because it can span over
            // both metadata assemblies and source projects.
            //
            // Otherwise show the assembly this symbol came from as the Origination of
            // the DefinitionItem.
            if (symbol.Kind != SymbolKind.Namespace)
            {
                var assemblyName = symbol.ContainingAssembly?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (!string.IsNullOrWhiteSpace(assemblyName))
                {
                    return ImmutableArray.Create(new TaggedText(TextTags.Assembly, assemblyName));
                }
            }

            return ImmutableArray<TaggedText>.Empty;
        }
    }
}
