using System;
using System.IO;
using Mono.Cecil;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: Publicize <in.exe> <out.exe>");
    return 2;
}

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
using var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { AssemblyResolver = resolver });

int types = 0, methods = 0, fields = 0;
foreach (var type in module.GetTypes())
{
    if (type.Name == "<Module>") continue;
    if (type.IsNested)
    {
        if (!type.IsNestedPublic) { type.IsNestedPublic = true; types++; }
    }
    else if (!type.IsPublic) { type.IsPublic = true; types++; }

    foreach (var m in type.Methods)
    {
        if (m.IsPublic) continue;
        // Explicit interface implementations stay as they are: their names ("I.M") are not
        // callable identifiers, and publicizing them only invites ambiguity.
        if (m.HasOverrides && m.IsPrivate) continue;
        m.IsPublic = true;
        methods++;
    }
    foreach (var f in type.Fields)
    {
        if (f.IsPublic) continue;
        f.IsPublic = true;
        fields++;
    }
}

module.Write(args[1]);
Console.WriteLine($"publicize: {types} type(s), {methods} method(s), {fields} field(s) opened -> {args[1]}");
return 0;
