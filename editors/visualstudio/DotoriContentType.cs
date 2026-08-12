using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Dotori.VisualStudio;

internal static class DotoriContentType
{
    internal const string Name = "dotori";

#pragma warning disable CS0649
    [Export]
    [Name(Name)]
    [BaseDefinition("code")]
    internal static ContentTypeDefinition? ContentTypeDefinition;

    [Export]
    [FileExtension(".dotori")]
    [ContentType(Name)]
    internal static FileExtensionToContentTypeDefinition? FileExtensionDefinition;

    [Export]
    [FileName(".dotori")]
    [ContentType(Name)]
    internal static FileExtensionToContentTypeDefinition? FileNameDefinition;
#pragma warning restore CS0649
}
