using System.CommandLine;

using MyEmailSearch.Commands;

namespace MyEmailSearch;

public static class SearchCli
{
    public static RootCommand Create(TextReader? input = null, Action<string>? open = null)
    {
        input ??= Console.In;
        open ??= SearchCommand.OpenWithDefaultApplication;

        var global = new GlobalOptions();
        var root = new RootCommand("MyEmailSearch - Search your email archive")
        {
            SearchCommand.Create(global, input, open),
            IndexCommand.Create(global),
            StatusCommand.Create(global),
            RebuildCommand.Create(global, input)
        };
        global.AddTo(root);
        return root;
    }
}
