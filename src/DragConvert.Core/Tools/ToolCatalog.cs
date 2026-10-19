using DragConvert.Core.Conversion;

namespace DragConvert.Core.Tools;

/// <summary>Which tools apply to a set of files, with counts, in registration order.</summary>
public sealed class ToolCatalog(IEnumerable<IFileTool> tools)
{
    private readonly IReadOnlyList<IFileTool> _tools = tools.Where(t => t.IsAvailable).ToList();

    public IReadOnlyList<IFileTool> Tools => _tools;

    /// <summary>Tools that apply to at least one file (combining tools need at least two), all-file tools first.</summary>
    public IReadOnlyList<ToolOption> ToolOptionsFor(IReadOnlyCollection<string> inputPaths)
    {
        var formats = inputPaths.Select(MediaFormats.FromPath).ToList();
        var options = new List<ToolOption>();
        foreach (IFileTool tool in _tools)
        {
            int count = formats.Count(f => f is not null && tool.Accepts(f));
            if (count >= (tool.CombinesInputs ? 2 : 1))
            {
                options.Add(new ToolOption(tool, count, inputPaths.Count));
            }
        }

        return options.OrderByDescending(o => o.AppliesToAll).ToList();
    }

    /// <summary>Splits files into those the tool takes and those it skips.</summary>
    public static (IReadOnlyList<string> Accepted, IReadOnlyList<string> Skipped) Plan(IFileTool tool, IEnumerable<string> files)
    {
        var accepted = new List<string>();
        var skipped = new List<string>();
        foreach (string file in files)
        {
            (MediaFormats.FromPath(file) is { } format && tool.Accepts(format) ? accepted : skipped).Add(file);
        }

        return (accepted, skipped);
    }
}
