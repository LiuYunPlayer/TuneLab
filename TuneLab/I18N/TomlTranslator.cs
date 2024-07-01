using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ObjectiveC;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace TuneLab.I18N;

internal class TomlTranslator : ITranslator
{
    public TomlTranslator(string dictPath, Dictionary<string, string>? subDict = null) => LoadDict(dictPath, subDict);
    public void LoadDict(string dictPath,Dictionary<string,string>? subDict=null)
    {
        if (!File.Exists(dictPath))
            return;

        string dictData = File.ReadAllText(dictPath);
        if (!Toml.TryToModel(dictData, out model, out var message)) 
            return;
        if (subDict != null) foreach (var item in subDict) AppendDict(item.Key, item.Value);
    }

    public void AppendDict(string context,string dictPath)
    {
        object CombineTable(object model,object subModel)
        {
            if (!(model is TomlTable srcModel)) return subModel;
            foreach(var item in (TomlTable)subModel)
            {
                if (!srcModel.ContainsKey(item.Key)) srcModel.Add(item.Key, item.Value);
                else
                {
                    if(srcModel[item.Key] is TomlTable)
                    {
                        srcModel[item.Key] = CombineTable(srcModel[item.Key], ((TomlTable)subModel)[item.Key]);
                    }else
                    {
                        srcModel[item.Key] = ((TomlTable)subModel)[item.Key];
                    }
                }
            }
            return srcModel;
        }

        if(!File.Exists(dictPath))
            return;

        string dictData = File.ReadAllText(dictPath);
        TomlTable? subModel;
        if (!Toml.TryToModel(dictData, out subModel, out var message))
            return;

        if (!model.ContainsKey(context)) model.Add(context, subModel);
        else model[context] = CombineTable(model[context], subModel);
    }

    public string Translate(string text, IEnumerable<string> context)
    {
        object? table = model;
        if (table == null)
            return text;

        foreach (string key in context.Concat([text]))
        {
            {//fallback
                object? tmp = null;
                ((TomlTable)table).TryGetValue(text, out tmp);
                text = (tmp is string) ? (string)tmp : text;
            }
            if (!((TomlTable)table).TryGetValue(key, out table))
                return text;
        }

        if (table is not string)
            return text;

        return (string)table;
    }

    TomlTable? model;
}
