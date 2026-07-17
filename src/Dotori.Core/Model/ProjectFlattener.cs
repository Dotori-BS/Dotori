using Dotori.Core.Parsing;

namespace Dotori.Core.Model;

/// <summary>
/// Flattens a <see cref="ProjectDecl"/> into a <see cref="FlatProjectModel"/>
/// by applying all condition blocks that match the given <see cref="TargetContext"/>,
/// in order of increasing specificity.
/// </summary>
public static class ProjectFlattener
{
    public static FlatProjectModel Flatten(
        ProjectDecl decl,
        string dotoriPath,
        TargetContext context,
        ProjectDecl? localDecl = null)
    {
        var model = new FlatProjectModel
        {
            Name       = decl.Name,
            ProjectDir = Path.GetDirectoryName(dotoriPath)!,
            DotoriPath = dotoriPath,
        };

        // Collect (specificity, items) pairs; sort ascending so lower specificity
        // is applied first and higher specificity can override.
        var layers = new List<(int Specificity, List<ProjectItem> Items)>
        {
            (0, decl.Items),
        };

        CollectConditionLayers(decl.Items, context, layers, specificityOffset: 0);

        if (localDecl is not null)
        {
            layers.Add((DotoriConstants.LocalSpecificityOffset, localDecl.Items));
            CollectConditionLayers(localDecl.Items, context, layers,
                                   specificityOffset: DotoriConstants.LocalSpecificityOffset);
        }

        layers.Sort((a, b) => a.Specificity.CompareTo(b.Specificity));

        foreach (var (_, items) in layers)
            ApplyItems(model, items, context);

        RuntimeEnforcer.Enforce(model, context.Platform);
        return model;
    }

    // ─── Condition evaluation ──────────────────────────────────────────────

    /// <summary>
    /// Recursively collect all condition blocks that match the context,
    /// recording their specificity.
    /// </summary>
    private static void CollectConditionLayers(
        List<ProjectItem> items,
        TargetContext context,
        List<(int, List<ProjectItem>)> layers,
        int specificityOffset = 0)
    {
        var activeAtoms = context.ActiveAtoms();

        foreach (var item in items)
        {
            if (item is not ConditionBlock cb) continue;

            // A condition matches if ALL its atoms are in the active set.
            if (cb.Condition.Atoms.All(a =>
                    activeAtoms.Contains(a, StringComparer.OrdinalIgnoreCase)))
            {
                layers.Add((cb.Condition.Specificity + specificityOffset, cb.Items));
                // Recurse: nested condition blocks inside a matched block
                CollectConditionLayers(cb.Items, context, layers, specificityOffset);
            }
        }
    }

    // ─── Environment variable expansion ───────────────────────────────────

    private static DependencyItem ExpandDependency(DependencyItem dep)
    {
        var expandedValue = dep.Value switch
        {
            VersionDependency v => (DependencyValue)new VersionDependency(EnvExpander.Expand(v.Version)),
            ComplexDependency c => new ComplexDependency
            {
                Git     = EnvExpander.ExpandNullable(c.Git),
                Tag     = EnvExpander.ExpandNullable(c.Tag),
                Commit  = EnvExpander.ExpandNullable(c.Commit),
                Path    = EnvExpander.ExpandNullable(c.Path),
                Version = EnvExpander.ExpandNullable(c.Version),
                Options = c.Options,
            },
            _ => dep.Value,
        };
        return new DependencyItem(dep.Name, expandedValue);
    }

    // ─── Item application ──────────────────────────────────────────────────

    private static void ApplyItems(FlatProjectModel model, List<ProjectItem> items, TargetContext context)
    {
        foreach (var item in items)
        {
            // Skip condition blocks — they are handled by CollectConditionLayers
            if (item is ConditionBlock) continue;

            switch (item)
            {
                case ProjectTypeProp p:    model.Type        = p.Value; break;
                case StdProp p:            model.Std         = p.Value; break;
                case DescriptionProp p:    model.Description = EnvExpander.Expand(p.Value); break;
                case OptimizeProp p:       model.Optimize    = p.Value; break;
                case DebugInfoProp p:      model.DebugInfo   = p.Value; break;
                case RuntimeLinkProp p:    model.RuntimeLink = p.Value; break;
                case LibcProp p:           model.Libc        = p.Value; break;
                case StdlibProp p:         model.Stdlib      = p.Value; break;
                case LtoProp p:            model.Lto         = p.Value; break;
                case WarningsProp p:       model.Warnings    = p.Value; break;
                case WarningsAsErrorsProp p: model.WarningsAsErrors = p.Value; break;
                case AndroidApiLevelProp p:  model.AndroidApiLevel  = p.Value; break;
                case MacosMinProp p:       model.MacosMin    = EnvExpander.Expand(p.Value); break;
                case IosMinProp p:         model.IosMin      = EnvExpander.Expand(p.Value); break;
                case TvosMinProp p:        model.TvosMin     = EnvExpander.Expand(p.Value); break;
                case WatchosMinProp p:     model.WatchosMin  = EnvExpander.Expand(p.Value); break;
                case ForceCxxProp p:       model.ForceCxx    = p.Value; break;
                case ObjcArcProp p:        model.ObjcArc     = p.Value; break;
                case ForceObjcppProp p:    model.ForceObjcpp = p.Value; break;

                case EmscriptenFlagsProp p:
                    model.EmscriptenFlags.AddRange(p.Flags.Select(EnvExpander.Expand));
                    break;

                case SourcesBlock b when !b.IsModules:
                    foreach (var s in b.Items)
                        model.Sources.Add(new SourceItem(s.IsInclude, EnvExpander.Expand(s.Glob)));
                    break;

                case SourcesBlock b when b.IsModules:
                    foreach (var s in b.Items)
                        model.Modules.Add(new SourceItem(s.IsInclude, EnvExpander.Expand(s.Glob)));
                    if (b.ExportMap.HasValue)
                        model.ModuleExportMap = b.ExportMap.Value;
                    break;

                case HeadersBlock b:
                    foreach (var h in b.Items)
                        model.Headers.Add(new HeaderItem(h.IsPublic, EnvExpander.Expand(h.Path)));
                    break;

                case DefinesBlock b:
                    model.Defines.AddRange(b.Values.Select(EnvExpander.Expand));
                    break;

                case LinksBlock b:
                    model.Links.AddRange(b.Values.Select(EnvExpander.Expand));
                    break;

                case FrameworksBlock b:
                    model.Frameworks.AddRange(b.Values.Select(EnvExpander.Expand));
                    break;

                case FrameworkPathsBlock b:
                    model.FrameworkPaths.AddRange(b.Paths.Select(EnvExpander.Expand));
                    break;

                case CompileFlagsBlock b:
                    model.CompileFlags.AddRange(b.Values.Select(EnvExpander.Expand));
                    break;

                case LinkFlagsBlock b:
                    model.LinkFlags.AddRange(b.Values.Select(EnvExpander.Expand));
                    break;

                case ResourcesBlock b:
                    model.Resources.AddRange(b.Paths.Select(EnvExpander.Expand));
                    break;

                case ManifestProp p:
                    model.Manifest = EnvExpander.Expand(p.Value);
                    break;

                case ToolchainProp p:
                    model.Toolchain = p.Value;
                    break;

                case DependenciesBlock b:
                    // Merge: later entries with same name overwrite earlier ones
                    foreach (var dep in b.Items)
                    {
                        // Skip if this dependency is gated on options that are not all active
                        if (dep.Value is ComplexDependency cd && cd.Options is not null)
                        {
                            bool depOptionsActive = context.EnabledOptions != null
                                && cd.Options.All(o => context.EnabledOptions.Contains(o, StringComparer.OrdinalIgnoreCase));
                            if (!depOptionsActive) continue;
                        }
                        var expandedDep = ExpandDependency(dep);
                        var idx = model.Dependencies.FindIndex(d => d.Name == dep.Name);
                        if (idx >= 0) model.Dependencies[idx] = expandedDep;
                        else model.Dependencies.Add(expandedDep);
                    }
                    break;

                case PchBlock b:
                    model.Pch ??= new PchConfig();
                    if (b.Header  != null) model.Pch.Header  = EnvExpander.Expand(b.Header);
                    if (b.Source  != null) model.Pch.Source  = EnvExpander.Expand(b.Source);
                    if (b.Modules != null) model.Pch.Modules = b.Modules;
                    break;

                case UnityBuildBlock b:
                    model.UnityBuild ??= new UnityBuildConfig();
                    if (b.Enabled   != null) model.UnityBuild.Enabled   = b.Enabled.Value;
                    if (b.BatchSize != null) model.UnityBuild.BatchSize  = b.BatchSize.Value;
                    model.UnityBuild.Exclude.AddRange(b.Exclude.Select(EnvExpander.Expand));
                    break;

                case OutputBlock b:
                    model.Output ??= new OutputConfig();
                    if (b.Binaries  != null) model.Output.Binaries  = EnvExpander.Expand(b.Binaries);
                    if (b.Libraries != null) model.Output.Libraries = EnvExpander.Expand(b.Libraries);
                    if (b.Symbols   != null) model.Output.Symbols   = EnvExpander.Expand(b.Symbols);
                    break;

                case PreBuildBlock b:
                    model.PreBuildCommands.AddRange(b.Commands.Select(EnvExpander.Expand));
                    break;

                case PostBuildBlock b:
                    model.PostBuildCommands.AddRange(b.Commands.Select(EnvExpander.Expand));
                    break;

                case BuildRuleBlock b:
                    model.BuildRules.Add(new BuildRuleConfig(
                        b.Name,
                        EnvExpander.Expand(b.Command!),
                        b.Inputs.Select(EnvExpander.Expand).ToArray(),
                        b.Outputs.Select(EnvExpander.Expand).ToArray()));
                    break;

                case CopyBlock b:
                    foreach (var ci in b.Items)
                        model.CopyItems.Add(new CopyItem(
                            EnvExpander.Expand(ci.From),
                            EnvExpander.Expand(ci.To)));
                    break;

                case OptionBlock b:
                    // Option is active if explicitly enabled, explicitly disabled, or default
                    bool optionActive = context.EnabledOptions != null
                        ? context.EnabledOptions.Contains(b.Name, StringComparer.OrdinalIgnoreCase)
                        : b.Default;
                    if (optionActive)
                    {
                        model.Defines.AddRange(b.Defines.Select(EnvExpander.Expand));
                        foreach (var dep in b.Dependencies)
                        {
                            var expandedDep = ExpandDependency(dep);
                            var idx = model.Dependencies.FindIndex(d => d.Name == dep.Name);
                            if (idx >= 0) model.Dependencies[idx] = expandedDep;
                            else model.Dependencies.Add(expandedDep);
                        }
                    }
                    break;

                case AssemblerBlock b:
                    model.Assembler ??= new AssemblerConfig();
                    if (b.Tool   != AssemblerTool.Auto) model.Assembler.Tool   = b.Tool;
                    if (b.Format != null)               model.Assembler.Format = EnvExpander.Expand(b.Format);
                    foreach (var s in b.Items)
                        model.Assembler.Items.Add(new SourceItem(s.IsInclude, EnvExpander.Expand(s.Glob)));
                    model.Assembler.Flags.AddRange(b.Flags.Select(EnvExpander.Expand));
                    model.Assembler.Defines.AddRange(b.Defines.Select(EnvExpander.Expand));
                    break;
            }
        }
    }
}
