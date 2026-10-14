using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Hollow.NetGen;

/// <summary>A mistake in the message definitions, with the line it is on.</summary>
public sealed class NetGenException(string message) : Exception(message);

/// <summary>
/// Writes the serializers of the network messages (ARCHITECTURE.md §11.2). It reads the definitions file as text, a
/// strict subset of C#: <c>[NetMessage(id)]</c>, <c>[NetStruct]</c> or <c>[NetState]</c> on a
/// <c>public partial struct</c> holding only <c>public</c> fields, each with attributes saying how it is packed. Text
/// in, text out, no compiled code needed: the definitions can change in ways the old generated code wouldn't compile
/// with. No reflection at run time, and no compiler packages to vendor.
/// </summary>
public static partial class NetGenerator
{
    private enum StructKind
    {
        Message,
        Struct,
        State,
    }

    private enum FieldKind
    {
        Bool,
        Bits,
        Bits64,
        Signed,
        Signed64,
        VarUInt,
        VarULong,
        Angle,
        Quantized,
        Float,
        Double,
        String,
        Nested,
        Array,
    }

    private sealed record Field(string Name, string Type, FieldKind Kind, int Bits, string Detail, int Line);

    private sealed record StructDef(string Name, StructKind Kind, int Id, List<Field> Fields, int Line);

    [GeneratedRegex(@"^using\s+[\w.]+\s*;$")]
    private static partial Regex UsingLine();

    [GeneratedRegex(@"^namespace\s+([\w.]+)\s*;$")]
    private static partial Regex NamespaceLine();

    [GeneratedRegex(@"^public\s+(?:readonly\s+)?partial\s+struct\s+(\w+)$")]
    private static partial Regex StructLine();

    [GeneratedRegex(@"^((?:\[[^\]]*\]\s*)*)public\s+([\w.]+(?:\[\])?)\s+(\w+(?:\s*,\s*\w+)*)\s*;$")]
    private static partial Regex FieldLine();

    [GeneratedRegex(@"^(\w+)\s*(?:\((.*)\))?$")]
    private static partial Regex AttributeShape();

    /// <summary>The generated source for a definitions file.</summary>
    public static string Generate(string definitions, string sourceName = "Messages.cs")
    {
        (List<string> usings, string ns, List<StructDef> structs) = Parse(definitions, sourceName);
        return Emit(usings, ns, structs, sourceName);
    }

    // ---------------------------------------------------------------- parsing

    private static (List<string> Usings, string Namespace, List<StructDef> Structs) Parse(string text, string source)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var usings = new List<string>();
        string? ns = null;
        var structs = new List<StructDef>();
        var pending = new List<(string Name, string[] Args)>();
        int i = 0;
        while (i < lines.Length)
        {
            string line = StripComment(lines[i]).Trim();
            int number = i + 1;
            i++;
            if (line.Length == 0)
            {
                continue;
            }
            if (UsingLine().IsMatch(line))
            {
                usings.Add(line);
                continue;
            }
            Match nsMatch = NamespaceLine().Match(line);
            if (nsMatch.Success)
            {
                ns = nsMatch.Groups[1].Value;
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                pending.AddRange(ParseAttributes(line, source, number));
                continue;
            }
            Match structMatch = StructLine().Match(line);
            if (!structMatch.Success)
            {
                throw Error(source, number, $"expected a partial struct, found \"{line}\"");
            }
            string name = structMatch.Groups[1].Value;
            StructKind kind;
            int id = 0;
            if (pending.Any(a => a.Name == "NetMessage"))
            {
                kind = StructKind.Message;
                id = (int)Number(pending.First(a => a.Name == "NetMessage").Args, 0, source, number);
            }
            else if (pending.Any(a => a.Name == "NetState"))
            {
                kind = StructKind.State;
            }
            else if (pending.Any(a => a.Name == "NetStruct"))
            {
                kind = StructKind.Struct;
            }
            else
            {
                throw Error(source, number, $"struct {name} needs [NetMessage(id)], [NetStruct] or [NetState]");
            }
            pending.Clear();
            var def = new StructDef(name, kind, id, new List<Field>(), number);
            i = ParseBody(lines, i, def, source);
            structs.Add(def);
        }
        if (ns == null)
        {
            throw new NetGenException($"{source}: no file-scoped namespace");
        }
        Validate(structs, source);
        return (usings, ns, structs);
    }

    private static int ParseBody(string[] lines, int i, StructDef def, string source)
    {
        while (i < lines.Length && StripComment(lines[i]).Trim().Length == 0)
        {
            i++;
        }
        if (i >= lines.Length || StripComment(lines[i]).Trim() != "{")
        {
            throw Error(source, i + 1, $"expected {{ after struct {def.Name}");
        }
        i++;
        var pending = new List<(string Name, string[] Args)>();
        while (i < lines.Length)
        {
            string line = StripComment(lines[i]).Trim();
            int number = i + 1;
            i++;
            if (line.Length == 0)
            {
                continue;
            }
            if (line == "}")
            {
                return i;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                pending.AddRange(ParseAttributes(line, source, number));
                continue;
            }
            Match m = FieldLine().Match(line);
            if (!m.Success)
            {
                throw Error(source, number, $"only \"[attributes] public Type Name;\" fields are allowed in {def.Name}: \"{line}\"");
            }
            if (m.Groups[1].Value.Length > 0)
            {
                pending.AddRange(ParseAttributes(m.Groups[1].Value.Trim(), source, number));
            }
            string type = m.Groups[2].Value;
            foreach (string name in m.Groups[3].Value.Split(',', StringSplitOptions.TrimEntries))
            {
                def.Fields.Add(Classify(name, type, pending, source, number));
            }
            pending.Clear();
        }
        throw Error(source, lines.Length, $"struct {def.Name} is not closed");
    }

    private static Field Classify(string name, string type, List<(string Name, string[] Args)> attributes, string source, int line)
    {
        string? Has(string attribute) => attributes.Any(a => a.Name == attribute) ? attribute : null;
        string[] Args(string attribute) => attributes.First(a => a.Name == attribute).Args;
        foreach ((string attribute, string[] _) in attributes)
        {
            if (attribute is not ("Bits" or "Signed" or "VarInt" or "Angle" or "Quantize" or "MaxLength" or "MaxCount"))
            {
                throw Error(source, line, $"unknown attribute [{attribute}] on {name}");
            }
        }
        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            if (Has("MaxCount") == null)
            {
                throw Error(source, line, $"array {name} needs [MaxCount(n)]");
            }
            int count = (int)Number(Args("MaxCount"), 0, source, line);
            return new Field(name, type[..^2], FieldKind.Array, BitsFor(count), count.ToString(CultureInfo.InvariantCulture), line);
        }
        if (type == "string")
        {
            if (Has("MaxLength") == null)
            {
                throw Error(source, line, $"string {name} needs [MaxLength(bytes)]");
            }
            return new Field(name, type, FieldKind.String, 0, ((int)Number(Args("MaxLength"), 0, source, line)).ToString(CultureInfo.InvariantCulture), line);
        }
        if (Has("Angle") != null)
        {
            if (type != "ushort")
            {
                throw Error(source, line, $"[Angle] {name} must be a ushort");
            }
            return new Field(name, type, FieldKind.Angle, 16, "", line);
        }
        if (Has("Quantize") != null)
        {
            if (type != "float")
            {
                throw Error(source, line, $"[Quantize] {name} must be a float");
            }
            string[] a = Args("Quantize");
            string detail = string.Create(CultureInfo.InvariantCulture,
                $"{FloatLiteral(Number(a, 0, source, line))}, {FloatLiteral(Number(a, 1, source, line))}");
            return new Field(name, type, FieldKind.Quantized, (int)Number(a, 2, source, line), detail, line);
        }
        if (Has("VarInt") != null)
        {
            return type switch
            {
                "uint" => new Field(name, type, FieldKind.VarUInt, 0, "", line),
                "ulong" => new Field(name, type, FieldKind.VarULong, 0, "", line),
                _ => throw Error(source, line, $"[VarInt] {name} must be a uint or ulong"),
            };
        }
        if (Has("Signed") != null)
        {
            int bits = (int)Number(Args("Signed"), 0, source, line);
            return type switch
            {
                "sbyte" or "short" or "int" => new Field(name, type, FieldKind.Signed, bits, "", line),
                "long" => new Field(name, type, FieldKind.Signed64, bits, "", line),
                _ => throw Error(source, line, $"[Signed] {name} must be a signed integer"),
            };
        }
        if (Has("Bits") != null)
        {
            int bits = (int)Number(Args("Bits"), 0, source, line);
            if (type is "bool" or "float" or "double" or "string" or "sbyte" or "short" or "int" or "long")
            {
                throw Error(source, line, $"[Bits] {name} must be an unsigned integer or an enum");
            }
            return new Field(name, type, type == "ulong" ? FieldKind.Bits64 : FieldKind.Bits, bits, "", line);
        }
        return type switch
        {
            "bool" => new Field(name, type, FieldKind.Bool, 1, "", line),
            "byte" => new Field(name, type, FieldKind.Bits, 8, "", line),
            "ushort" => new Field(name, type, FieldKind.Bits, 16, "", line),
            "uint" => new Field(name, type, FieldKind.Bits, 32, "", line),
            "ulong" => new Field(name, type, FieldKind.Bits64, 64, "", line),
            "sbyte" => new Field(name, type, FieldKind.Signed, 8, "", line),
            "short" => new Field(name, type, FieldKind.Signed, 16, "", line),
            "int" => new Field(name, type, FieldKind.Signed, 32, "", line),
            "long" => new Field(name, type, FieldKind.Signed64, 64, "", line),
            "float" => new Field(name, type, FieldKind.Float, 32, "", line),
            "double" => new Field(name, type, FieldKind.Double, 64, "", line),
            // Anything else must be a struct defined here (checked once all are read); enums need [Bits(n)].
            _ => new Field(name, type, FieldKind.Nested, 0, "", line),
        };
    }

    private static void Validate(List<StructDef> structs, string source)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<int>();
        foreach (StructDef def in structs)
        {
            if (!names.Add(def.Name))
            {
                throw Error(source, def.Line, $"{def.Name} is defined twice");
            }
            if (def.Kind == StructKind.Message && (def.Id is < 1 or > 255 || !ids.Add(def.Id)))
            {
                throw Error(source, def.Line, $"{def.Name}: message IDs are 1 … 255 and unique");
            }
        }
        foreach (StructDef def in structs)
        {
            foreach (Field field in def.Fields)
            {
                if (field.Kind is FieldKind.Nested or FieldKind.Array)
                {
                    StructDef? inner = structs.FirstOrDefault(s => s.Name == field.Type);
                    if (inner == null || inner.Kind == StructKind.Message)
                    {
                        throw Error(source, field.Line, $"{def.Name}.{field.Name}: {field.Type} is not a [NetStruct] or [NetState] here (an enum needs [Bits(n)])");
                    }
                }
                if (def.Kind == StructKind.State && field.Kind is FieldKind.String or FieldKind.Nested or FieldKind.Array)
                {
                    throw Error(source, field.Line, $"{def.Name}.{field.Name}: a [NetState] holds only numbers, flags and enums");
                }
                if (field.Kind is FieldKind.Bits or FieldKind.Signed or FieldKind.Quantized && field.Bits is < 1 or > 32 ||
                    field.Kind is FieldKind.Bits64 or FieldKind.Signed64 && field.Bits is < 1 or > 64)
                {
                    throw Error(source, field.Line, $"{def.Name}.{field.Name}: {field.Bits} bits is out of range");
                }
            }
        }
    }

    private static List<(string Name, string[] Args)> ParseAttributes(string text, string source, int line)
    {
        var result = new List<(string, string[])>();
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }
            if (text[i] != '[')
            {
                throw Error(source, line, $"bad attribute list \"{text}\"");
            }
            int close = text.IndexOf(']', i);
            if (close < 0)
            {
                throw Error(source, line, $"unclosed attribute \"{text}\"");
            }
            foreach (string part in SplitTopLevel(text[(i + 1)..close]))
            {
                Match m = AttributeShape().Match(part.Trim());
                if (!m.Success)
                {
                    throw Error(source, line, $"bad attribute \"{part}\"");
                }
                string[] args = m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0
                    ? m.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries)
                    : Array.Empty<string>();
                result.Add((m.Groups[1].Value, args));
            }
            i = close + 1;
        }
        return result;
    }

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
            }
            else if (text[i] == ',' && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
        yield return text[start..];
    }

    private static double Number(string[] args, int index, string source, int line)
    {
        if (index >= args.Length || !double.TryParse(args[index].TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw Error(source, line, $"attribute argument {index + 1} is missing or not a number");
        }
        return value;
    }

    private static string StripComment(string line)
    {
        int comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment >= 0 ? line[..comment] : line;
    }

    private static int BitsFor(int max)
    {
        int bits = 1;
        while ((1 << bits) <= max)
        {
            bits++;
        }
        return bits;
    }

    private static string FloatLiteral(double value) => value.ToString("R", CultureInfo.InvariantCulture) + "f";

    private static NetGenException Error(string source, int line, string message) => new($"{source}({line}): {message}");
}
