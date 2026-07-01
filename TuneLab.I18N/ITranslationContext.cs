using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.I18N;

internal interface ITranslationContext
{
    public IEnumerable<string> TranslationContextKeys => [GetType().Name];
}

internal static class ITranslateContextExtension
{
    public static string Tr(this string text, object context)
    {
        return TranslationManager.CurrentTranslator.Translate(text, [context.GetType().Name]);
    }

    public static string Tr(this ITranslationContext translationContext, string text, params string[] context)
    {
        return TranslationManager.CurrentTranslator.Translate(text, translationContext.TranslationContextKeys.Concat(context));
    }

    public static string Tr(this string text, params string[] context)
    {
        return TranslationManager.CurrentTranslator.Translate(text, context);
    }

    public static string Tr(this string text, ITranslationContext context)
    {
        return TranslationManager.CurrentTranslator.Translate(text, context.TranslationContextKeys);
    }
}
