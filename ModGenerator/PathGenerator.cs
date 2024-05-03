using System.Text;
using Microsoft.CodeAnalysis;

namespace Solaestas.tModLoader.ModBuilder.Generators;

[Generator]
public class PathGenerator : ISourceGenerator
{
	public void Execute(GeneratorExecutionContext context)
	{
		var enabled = context.GetProperty("EnablePathGenerator", "true");
		if (enabled != "true")
		{
			return;
		}

		var modName = context.GetProperty("ModName", context.Compilation.Assembly.Name);
		var rootNamespce = context.GetProperty("RootNamespace", modName);
		var typename = context.GetProperty("PathTypeName", "ModAsset");
		var rootNamespace = context.GetProperty("PathNamespace", rootNamespce);
		var prefix = context.GetProperty("PathPrefix", string.Empty);
		if (prefix.Length > 0 && prefix[^1] != '/')
		{
			prefix += '/';
		}

		var source = new StringBuilder();
		var builder = new PathBuilder();
		source.AppendLine($$"""
			// <auto-generated/>
			using Microsoft.Xna.Framework.Graphics;
			using ReLogic.Content;
			using Terraria.ModLoader;
			namespace {{rootNamespace}};

			public static class {{typename}}
			{
				private static AssetRepository _repo;
				static {{typename}}()
				{
					_repo = ModLoader.GetMod("{{modName}}").Assets;
				}
			""");

		var paths = ScanResource(context, out var conflicts);
		ResolveConflict(paths, conflicts);

		foreach (var member in paths.Values)
		{
			if (!CheckValid(member.Name))
			{
				context.ReportDiagnostic(Diagnostic.Create(Descriptors.MB0002, Location.None, member.Path));
				continue;
			}
			builder.Append(source, member, prefix);
		}

		foreach (var list in conflicts.Values)
		{
			var overlap = PathMember.HasOverlap(list);
			foreach (var member in list)
			{
				if (!CheckValid(member.Name))
				{
					context.ReportDiagnostic(Diagnostic.Create(Descriptors.MB0002, Location.None, member.Path));
					continue;
				}
				if (overlap)
				{
					builder.AppendReduceOverlap(source, member, prefix);
				}
				else
				{
					builder.AppendReduceOverlap(source, member, prefix);
				}
			}
		}

		source.AppendLine("}");
		context.AddSource($"{typename}.g.cs", source.ToString());
	}

	private Dictionary<string, PathMember> ScanResource(in GeneratorExecutionContext context, out Dictionary<string, List<PathMember>> conflicts)
	{
		Dictionary<string, PathMember> paths = [];
		conflicts = [];
		foreach (var file in context.AdditionalFiles)
		{
			string pack = context.GetMetadata(file, "Pack", "false");
			if (!bool.TryParse(pack, out var shouldPack) || !shouldPack)
			{
				continue;
			}

			if (!context.TryGetMetadata(file, "ModPath", out var modPath))
			{
				continue;
			}

			var defaultName = Path.GetFileNameWithoutExtension(modPath);
			var member = new PathMember(modPath);
			if (char.IsDigit(defaultName[0]))
			{
				var dirname = Path.GetFileName(Path.GetDirectoryName(modPath));
				defaultName = $"{dirname}_{defaultName}";
				member = PathMember.Increase(member);
			}

			if (paths.TryGetValue(defaultName, out var exist))
			{
				if (!conflicts.TryGetValue(defaultName, out var list))
				{
					conflicts[defaultName] = list = [exist];
				}
				list.Add(modPath);
				continue;
			}
			paths.Add(defaultName, member);
		}

		return paths;
	}

	private void ResolveConflict(Dictionary<string, PathMember> paths, Dictionary<string, List<PathMember>> conflicts)
	{
		static void RecurseResolve(List<PathMember> list)
		{
			for (int i = 0; i < list.Count - 1; i++)
			{
				bool conflict = false;
				PathMember baseMember = list[i];
				ReadOnlySpan<char> baseName = baseMember.Name;
				for (int j = i + 1; j < list.Count; j++)
				{
					PathMember other = list[j];
					if (baseName.SequenceEqual(other.Name))
					{
						list[j] = PathMember.Increase(other);
						conflict = true;
					}
				}
				if (conflict)
				{
					list[i] = PathMember.Increase(baseMember);
					RecurseResolve(list);
					break;
				}
			}
		}
		foreach (var pair in conflicts)
		{
			var defaultName = pair.Key;
			var list = pair.Value;
			for (int i = 0; i < list.Count; i++)
			{
				list[i] = PathMember.Increase(list[i]);
			}
			RecurseResolve(list);
			paths.Remove(defaultName);
		}
	}

	private bool CheckValid(ReadOnlySpan<char> name)
	{
		for (int i = 0; i < name.Length; i++)
		{
			// 反斜杠后续替换为下划线
			var ch = name[i];
			if (ch is (>= 'a' and <= 'z')
				or (>= 'A' and <= 'Z')
				or (>= '0' and <= '9')
				or '_' or '\\')
			{
				continue;
			}
			return false;
		}
		return true;
	}

	public void Initialize(GeneratorInitializationContext context)
	{
	}
}