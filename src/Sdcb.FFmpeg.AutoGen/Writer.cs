﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using Sdcb.FFmpeg.AutoGen.Definitions;

namespace Sdcb.FFmpeg.AutoGen
{
    internal class Writer
    {
        private readonly IndentedTextWriter _writer;

        public Writer(IndentedTextWriter writer) => _writer = writer;

        public bool SuppressUnmanagedCodeSecurity { get; init; }

        public void WriteMacro(MacroDefinition macro)
        {
            if (macro.IsValid)
            {
                WriteSummary(macro);
                var constOrStatic = macro.IsConst ? "const" : "static readonly";
                WriteLine($"public {constOrStatic} {macro.TypeName} {macro.Name} = {macro.ExpressionText};");
            }
            else
                WriteLine($"// public static {macro.Name} = {macro.ExpressionText};");
        }

        public void WriteMacroEnum(IGrouping<string, MacroDefinition> group, MacroEnumDef enumDef)
        {
            List<MacroDefinition> macros = group.ToList();
            HashSet<string> allTypes = macros
                .Select(x => x.TypeName)
                .Distinct()
                .ToHashSet();
            string typeExpr = DetectBestTypeForEnum(allTypes) switch
            {
                "int" => "",
                var x => $" : {x}",
            };

            Dictionary<string, string> macroShortcutMapping = macros
                .OrderByDescending(k => k.Name.Length)
                .ToDictionary(k => k.Name, v => StringExtensions.EnumNameTransform(v.Name[enumDef.Prefix.Length..]));

            WriteLine($"/// <summary>Macro enum, prefix: {enumDef.Prefix}</summary>");
            if (enumDef.IsFlags) WriteLine($"[Flags]");
            WriteLine($"public enum {enumDef.EnumName}{typeExpr}");
            using (BeginBlock())
            {
                group.ForEach((macro, i) =>
                {
                    WriteSummary(macro);
                    string key = macroShortcutMapping[macro.Name];
                    WriteLine($"{key} = {ExpressionTransform(macro.ExpressionText, macroShortcutMapping)},");
                    if (!i.IsLast)
                    {
                        WriteLine();
                    }
                });
            }


            static string ExpressionTransform(string expression, Dictionary<string, string> mapping)
            {
                foreach (KeyValuePair<string, string> kv in mapping)
                {
                    expression = expression.Replace(kv.Key, kv.Value);
                }
                return expression;
            }

            static string DetectBestTypeForEnum(HashSet<string> allTypes)
            {
                string[] priorities = new[]
                {
                    "ulong",
                    "long",
                    "uint",
                    "int",
                    "ushort",
                    "short",
                };

                foreach (string prior in priorities)
                {
                    if (allTypes.Contains(prior))
                        return prior;
                }
                return "int";
            }
        }

        public void WriteEnumeration(EnumerationDefinition enumeration)
        {
            WriteSummary(enumeration);
            WriteObsoletion(enumeration);
            if (enumeration.IsFlags) WriteLine("[Flags]");
            WriteLine($"public enum {enumeration.Name} : {enumeration.TypeName}");

            using (BeginBlock())
            {
                foreach (var item in enumeration.Items)
                {
                    WriteSummary(item);
                    WriteLine($"{item.Name} = {item.Value},");
                }
            }
        }        

        public void WriteStructure(StructureDefinition structure)
        {
            WriteSummary(structure);
            if (!structure.IsComplete) WriteLine("/// <remarks>This struct is incomplete.</remarks>");
            WriteObsoletion(structure);
            if (structure.IsUnion) WriteLine("[StructLayout(LayoutKind.Explicit)]");
            WriteLine($"public unsafe struct {structure.Name}");

            using (BeginBlock())
                foreach (var field in structure.Fields)
                {
                    WriteSummary(field);
                    WriteObsoletion(field);
                    if (structure.IsUnion) WriteLine("[FieldOffset(0)]");
                    WriteLine($"public {field.FieldType.Name} {StringExtensions.CSharpKeywordTransform(field.Name)};");
                }
        }

        public void WriteFixedArray(FixedArrayDefinition array)
        {
            WriteLine($"public unsafe struct {array.Name}");
            using var _ = BeginBlock();
            var prefix = "_";
            var size = array.Size;
            var elementType = array.ElementType.Name;

            WriteLine($"public const int Size = {size};");

            if (array.IsPrimitive) WritePrimitiveFixedArray(array.Name, elementType, size, prefix);
            else WriteComplexFixedArray(array.Name, elementType, size, prefix);
        }

        public void WriteFunction(ExportFunctionDefinition function)
        {
            WriteSummary(function);
            function.Parameters.ForEach((x, i) => WriteParam(x, x.Name));
            WriteObsoletion(function);
            if (SuppressUnmanagedCodeSecurity)
                WriteLine("[SuppressUnmanagedCodeSecurity]");

            WriteLine($"[DllImport(\"{function.LibraryName}-{function.LibraryVersion}\", EntryPoint = \"{function.Name}\", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]");
            function.ReturnType.Attributes.ToList().ForEach(WriteLine);
            var parameters = GetParameters(function.Parameters);
            WriteLine($"public static extern {function.ReturnType.Name} {function.Name}({parameters});");
        }

        public void WriteFunction(InlineFunctionDefinition function)
        {
            function.ReturnType.Attributes.ToList().ForEach(WriteLine);
            var parameters = GetParameters(function.Parameters);

            WriteSummary(function);
            function.Parameters.ToList().ForEach(x => WriteParam(x, x.Name));
            WriteReturnComment(function.ReturnComment);

            WriteObsoletion(function);
            WriteLine($"public static {function.ReturnType.Name} {function.Name}({parameters})");

            var lines = function.Body.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            lines.ForEach(WriteLineWithoutIntent);
            WriteLine($"// original body hash: {function.OriginalBodyHash}");
            WriteLine();
        }

        public void WriteDelegate(DelegateDefinition @delegate)
        {
            WriteSummary(@delegate);
            @delegate.Parameters.ToList().ForEach(x => WriteParam(x, x.Name));

            var parameters = GetParameters(@delegate.Parameters);
            WriteLine("[UnmanagedFunctionPointer(CallingConvention.Cdecl)]");
            WriteLine($"public unsafe delegate {@delegate.ReturnType.Name} {@delegate.FunctionName} ({parameters});");

            WriteLine($"public unsafe record struct {@delegate.Name}(IntPtr Pointer)");
            using (BeginBlock())
            {
                WriteLine($"public static implicit operator {@delegate.Name}({@delegate.FunctionName} func) => new(func switch");
                using (BeginBlock(inline: true))
                {
                    WriteLine("null => IntPtr.Zero,");
                    WriteLine("_ => Marshal.GetFunctionPointerForDelegate(func)");
                }
                WriteLine(");");
            }
        }

        public void WriteLine()
        {
            _writer.WriteLine();
        }

        public void WriteLine(string line)
        {
            _writer.WriteLine(line);
        }

        public void WriteLineWithoutIntent(string line)
        {
            _writer.WriteLineNoTabs(line);
        }

        public IDisposable BeginBlock(bool inline = false)
        {
            WriteLine("{");
            _writer.Indent++;
            return new End(() =>
            {
                _writer.Indent--;

                if (inline)
                    _writer.Write("}");
                else
                    _writer.WriteLine("}");
            });
        }

        private void WritePrimitiveFixedArray(string arrayName, string elementType, int size, string prefix)
        {
            WriteLine($"public fixed {elementType} {prefix}[{size}];");
            WriteLine();

            // indexer
            WriteLine($"public {elementType} this[uint i]");
            using (BeginBlock())
            {
                string outOfRange = $"throw new ArgumentOutOfRangeException($\"i({{i}}) should < {{Size}}\")";

                WriteLine($"get => i switch");
                using (BeginBlock(inline: true))
                {
                    WriteLine($"< Size => {prefix}[i],");
                    WriteLine($"_ => {outOfRange},");
                }
                WriteLine(";");

                WriteLine($"set => {prefix}[i] = i switch");
                using (BeginBlock(inline: true))
                {
                    WriteLine("< Size => value,");
                    WriteLine($"_ => {outOfRange},");
                }
                WriteLine(";");
            }
            WriteLine();

            // ToArray4
            if (size == 8 && (elementType == "int" || elementType == "ulong"))
            {
                string seq = string.Join(", ", Enumerable.Range(0, 4).Select(i => $"{prefix}[{i}]"));
                WriteLine($"public {elementType}[] ToArray4() => new [] {{ {seq} }};");
                WriteLine();

                string arr4 = arrayName.Replace('8', '4');
                WriteLine($"public static unsafe explicit operator {arr4}({arrayName} me)");
                using (BeginBlock())
                {
                    WriteLine($"{arr4} r = new ();");
                    for (int i = 0; i < 4; ++i)
                    {
                        WriteLine($"r.{prefix}[{i}] = me.{prefix}[{i}];");
                    }
                    WriteLine($"return r;");
                }
            }

            // ToArray
            if (size <= 64)
            {
                string seq = string.Join(", ", Enumerable.Range(0, size).Select(i => $"{prefix}[{i}]"));
                WriteLine($"public {elementType}[] ToArray() => new [] {{ {seq} }};");
                WriteLine();
            }
            else
            {
                var @fixed = $"fixed ({arrayName}* p = &this)";
                WriteLine($"public {elementType}[] ToArray()");
                using (BeginBlock())
                {
                    WriteLine(@fixed);
                    using (BeginBlock())
                    {
                        WriteLine($"var a = new {elementType}[Size];");
                        WriteLine($"for (uint i = 0; i < Size; i++)");
                        using (BeginBlock())
                        {
                            WriteLine($"a[i] = p->{prefix}[i];");
                        }
                        WriteLine("return a;");
                    }

                }
            }
            WriteLine();

            // UpdateFrom
            WriteLine($"public void UpdateFrom({elementType}[] array)");
            using (BeginBlock())
            {
                WriteLine("if (array.Length != Size)");
                using (BeginBlock())
                {
                    WriteLine($"throw new ArgumentOutOfRangeException($\"array size({{array.Length}}) should == {{Size}}\");");
                }
                WriteLine();

                WriteLine($"fixed ({elementType}* p = array)");
                using (BeginBlock())
                {
                    if (size <= 64)
                    {
                        for (int i = 0; i < size; ++i)
                        {
                            WriteLine($"{prefix}[{i}] = p[{i}];");
                        }
                    }
                    else
                    {
                        WriteLine($"for (int i = 0; i < Size; ++i)");
                        using (BeginBlock())
                        {
                            WriteLine($"{prefix}[i] = p[i];");
                        }
                    }
                }
            }
        }

        private void WriteComplexFixedArray(string arrayName, string elementType, int size, string prefix)
        {
            string seq = string.Join(", ", Enumerable.Range(0, size).Select(i => prefix + i));
            WriteLine($"public {elementType} {seq};");
            WriteLine();

            // indexer
            WriteLine($"public {elementType} this[uint i]");
            using (BeginBlock())
            {
                var @fixed = $"fixed ({elementType}* p0 = &{prefix}0)";

                WriteLine($"get");
                using (BeginBlock())
                {
                    WriteLine($"if (i >= Size) throw new ArgumentOutOfRangeException($\"i({{i}}) should < {{Size}}\");");
                    WriteLine(@fixed);
                    using (BeginBlock())
                    {
                        WriteLine(@"return *(p0 + i);");
                    }
                }
                WriteLine($"set");
                using (BeginBlock())
                {
                    WriteLine($"if (i >= Size) throw new ArgumentOutOfRangeException($\"i({{i}}) should < {{Size}}\");");
                    WriteLine(@fixed);
                    using (BeginBlock())
                    {
                        WriteLine(@"*(p0 + i) = value;");
                    }
                }
            }
            WriteLine();

            // ToArray4
            if (size == 8 && elementType == "byte*")
            {
                string seq4 = string.Join(", ", Enumerable.Range(0, 4).Select(i => prefix + i));
                WriteLine($"public {elementType}[] ToArray4() => new [] {{ {seq4} }};");
                WriteLine();

                string arr4 = arrayName.Replace('8', '4');
                WriteLine($"public static unsafe explicit operator {arr4}({arrayName} me)");
                using (BeginBlock())
                {
                    WriteLine($"{arr4} r = new ();");
                    for (int i = 0; i < 4; ++i)
                    {
                        WriteLine($"r.{prefix}{i} = me.{prefix}{i};");
                    }
                    WriteLine($"return r;");
                }
                WriteLine();
            }

            // ToArray
            WriteLine($"public {elementType}[] ToArray() => new [] {{ {seq} }};");
            WriteLine();

            // UpdateFrom
            WriteLine($"public void UpdateFrom({elementType}[] array)");
            using (BeginBlock())
            {
                WriteLine("if (array.Length != Size)");
                using (BeginBlock())
                {
                    WriteLine($"throw new ArgumentOutOfRangeException($\"array size({{array.Length}}) should == {{Size}}\");");
                }
                WriteLine();

                WriteLine($"fixed ({elementType}* p = array)");
                using (BeginBlock())
                {
                    for (int i = 0; i < size; ++i)
                    {
                        WriteLine($"{prefix}{i} = p[{i}];");
                    }
                }
            }

            if (elementType == "void*")
            {
                WriteLine();
                WriteLine($"public unsafe Span<IntPtr> GetPinnableReference()");
                using (BeginBlock())
                    WriteLine($"fixed (void** p = &_0) return new Span<IntPtr>(p, Size); ");
            }
            if (elementType == "byte*")
            {
                WriteLine();
                WriteLine($"public unsafe Span<IntPtr> GetPinnableReference()");
                using (BeginBlock())
                    WriteLine($"fixed (byte** p = &_0) return new Span<IntPtr>(p, Size); ");
            }
        }

        private static string GetParameters(FunctionParameter[] parameters, bool withAttributes = true)
        {
            return string.Join(", ",
                parameters.Select(x =>
                {
                    var sb = new StringBuilder();
                    if (withAttributes && x.Type.Attributes.Any()) sb.Append($"{string.Join("", x.Type.Attributes)} ");
                    if (x.Type.ByReference) sb.Append("ref ");
                    sb.Append($"{x.Type.Name} {StringExtensions.CSharpKeywordTransform(x.Name)}");
                    return sb.ToString();
                }));
        }

        private void WriteSummary(ICanGenerateXmlDoc value)
        {
            if (!string.IsNullOrWhiteSpace(value.XmlDocument)) WriteLine($"/// <summary>{SecurityElement.Escape(value.XmlDocument.Trim())}</summary>");
        }

        private void WriteParam(ICanGenerateXmlDoc value, string name)
        {
            if (!string.IsNullOrWhiteSpace(value.XmlDocument)) WriteLine($"/// <param name=\"{name}\">{SecurityElement.Escape(value.XmlDocument.Trim())}</param>");
        }

        private void WriteReturnComment(string content)
        {
            if (!string.IsNullOrWhiteSpace(content)) WriteLine($"/// <returns>{SecurityElement.Escape(content.Trim())}</returns>");
        }

        private void WriteObsoletion(IObsoletionAware obsoletionAware)
        {
            var obsoletion = obsoletionAware.Obsoletion;
            if (obsoletion.IsObsolete) WriteLine($"[Obsolete(\"{DoubleQuoteEscape(obsoletion.Message)}\")]");
        }

        static string DoubleQuoteEscape(string val) => val.Replace("\"", "\\\"");

        private void Write(string value)
        {
            _writer.Write(value);
        }

        private class End : IDisposable
        {
            private readonly Action _action;

            public End(Action action) => _action = action;

            public void Dispose() => _action();
        }
    }
}