using System.Reflection;
var asm = Assembly.LoadFrom(@"d:\Documents\Repository\Tunelab\tests\packages\DiffSinger\DiffSinger.dll");
System.Console.WriteLine("DLL: " + asm.FullName);
foreach(var t in asm.GetExportedTypes()) {
    foreach(var a in t.GetCustomAttributesData()) {
        if (a.AttributeType.Name.Contains("VoiceEngine"))
            System.Console.WriteLine($"FOUND: {t.FullName} -> engineType='{a.ConstructorArguments[0].Value}'");
    }
}
