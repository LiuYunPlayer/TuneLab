using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace TuneLab.I18N;

internal class TomlTranslator : ITranslator
{
    public TomlTranslator(string dictPath) => LoadDict(dictPath);
    public void LoadDict(string dictPath)
    {
        if (!File.Exists(dictPath))
            return;

        string dictData = File.ReadAllText(dictPath);
        if (!Toml.TryToModel(dictData, out model, out var message)) 
            return;
    }

    public string Translate(string text, IEnumerable<string> context)
    {
        object? table = model;
        if (table == null)
            return text;

        foreach (string key in context.Concat([text]))
        {
            if (!((TomlTable)table).TryGetValue(key, out table))
                return text;
        }

        if (table is not string)
            return text;

        return (string)table;
    }

    TomlTable? model;
}
