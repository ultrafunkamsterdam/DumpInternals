using Il2CppInspector;
using System.IO;
using System.Linq;
using Il2CppInspector.Structures;
using System.Collections.Generic;

namespace Il2CppDumper.Dumpers
{
    public class PseudoCodeDumper : BaseDumper
    {
        public bool IncludeOffsets { get; set; }
        public PseudoCodeDumper(Il2CppProcessor proc, bool includeOffsets = true) : base(proc) {
            this.IncludeOffsets = includeOffsets;
        }

        public void DumpStrings(string outFile)
        {
            using (var writer = new StreamWriter(new FileStream(outFile, FileMode.Create)))
            {
                foreach (var str in il2cpp.Metadata.Strings)
                {
                    writer.WriteLine(str);
                }
            }
        }

        public override void DumpToFile(string outFile) {
            using (var writer = new StreamWriter(new FileStream(outFile, FileMode.Create))) {
                var enumIdx = this.FindTypeIndex("Enum");

                for (int imageIndex = 0; imageIndex < metadata.Images.Length; imageIndex++) {
                    var imageDef = metadata.Images[imageIndex];
                    writer.Write($"// Image {imageIndex}: {metadata.GetImageName(imageDef)} ({imageDef.typeCount})\n");
                }
                writer.Write("\n");

                // TODO sort sub type
                // var declaring = metadata.GetTypeName(metadata.Types[il2cpp.Code.GetTypeFromTypeIndex(typeDef.declaringTypeIndex).klassIndex]);
                
                var typesByNameSpace = metadata.Types.GroupBy(t => t.namespaceIndex).Select(t => t);
                foreach (var nameSpaceIdx in typesByNameSpace)
                {
                    var nameSpaceName = metadata.GetString(nameSpaceIdx.Key);
                    writer.Write($"namespace {nameSpaceName} {{\n");
                    foreach (var typeDef in nameSpaceIdx)
                    {
                        if (typeDef.parentIndex == enumIdx)
                        {
                            this.WriteEnum(writer, typeDef);
                        }
                        else
                        {
                            this.WriteType(writer, typeDef);
                        }
                    }
                    writer.Write("}\n\n");
                }
            }
        }

        internal void WriteEnum(StreamWriter writer, Il2CppTypeDefinition typeDef)
        {
            writer.Write("\t");
            if ((typeDef.flags & DefineConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == DefineConstants.TYPE_ATTRIBUTE_PUBLIC) writer.Write("public ");
            writer.Write($"enum {metadata.GetTypeName(typeDef)} {{\n");
            var fieldEnd = typeDef.fieldStart + typeDef.field_count;
            for (int i = typeDef.fieldStart + 1; i < fieldEnd; ++i)
            {
                var pField = metadata.Fields[i];
                var defaultValue = this.GetDefaultValue(i);
                writer.Write($"\t\t{metadata.GetString(pField.nameIndex)} = {defaultValue}\n");
            }
            writer.Write("\t}\n\n");
        }

        internal void WriteType(StreamWriter writer, Il2CppTypeDefinition typeDef)
        {
            if ((typeDef.flags & DefineConstants.TYPE_ATTRIBUTE_SERIALIZABLE) != 0) writer.Write("\t[Serializable]\n");

            WriteAttribute(writer, typeDef.customAttributeIndex, "\t");

            var isStruct = false;
            string parent = null;
            if (typeDef.parentIndex >= 0)
            {
                var pType = il2cpp.Code.GetTypeFromTypeIndex(typeDef.parentIndex);
                var name = il2cpp.GetTypeName(pType);
                if (name == "ValueType")
                {
                    isStruct = true;
                }
                else if (name != "object")
                {
                    if (pType.type == Il2CppTypeEnum.IL2CPP_TYPE_CLASS)
                    {
                        var klass = metadata.Types[pType.klassIndex];
                        var parentNameSpace = metadata.GetTypeNamespace(klass);
                        if (parentNameSpace.Length > 0) parentNameSpace += ".";
                        parent = parentNameSpace + name;
                    }
                    else
                    {
                        parent = name;
                    }
                }
            }

            writer.Write("\t");
            if ((typeDef.flags & DefineConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == DefineConstants.TYPE_ATTRIBUTE_PUBLIC) writer.Write("public ");
            else if ((typeDef.flags & DefineConstants.TYPE_ATTRIBUTE_VISIBILITY_MASK) == DefineConstants.TYPE_ATTRIBUTE_NOT_PUBLIC) writer.Write("internal ");
            if ((typeDef.flags & DefineConstants.TYPE_ATTRIBUTE_ABSTRACT) != 0) writer.Write("abstract ");
            if (!isStruct && (typeDef.flags & DefineConstants.TYPE_ATTRIBUTE_SEALED) != 0) writer.Write("sealed ");

            if ((typeDef.flags & DefineConstants.TYPE_ATTRIBUTE_INTERFACE) != 0) writer.Write("interface ");
            else if (isStruct) writer.Write("struct ");
            else writer.Write("class ");

            var nameSpace = metadata.GetTypeNamespace(typeDef);
            if (nameSpace.Length > 0) nameSpace += ".";

            writer.Write($"{nameSpace}{metadata.GetTypeName(typeDef)}");

            // class extends another type or interface
            var extends = new List<string>();
            if (parent != null) extends.Add(parent);
            if (typeDef.interfaces_count > 0)
            {
                for (var i = 0; i < typeDef.interfaces_count; i++)
                {
                    var intTypeIdx = metadata.InterfaceIndices[typeDef.interfacesStart + i];
                    var pType = il2cpp.Code.GetTypeFromTypeIndex(intTypeIdx);
                    extends.Add(il2cpp.GetTypeName(pType));
                }
            }
            if (extends.Count > 0) writer.Write($" : {string.Join(", ", extends)}");

            writer.Write("\n\t{\n");

            if (this.IncludeOffsets && typeDef.delegateWrapperFromManagedToNativeIndex >= 0)
            {
                var nativeIdx = typeDef.delegateWrapperFromManagedToNativeIndex;
                var ptr = il2cpp.Code.ManagedToNative[nativeIdx];
                writer.Write("\t\t// Native method : 0x{0:x}\n", ptr);
            }

            this.WriteFields(writer, typeDef);
            this.WriteMethods(writer, typeDef);
            
            writer.Write("\t}\n\n");
        }

        internal void WriteFields(StreamWriter writer, Il2CppTypeDefinition typeDef)
        {
            if (typeDef.field_count <= 0) return;

            writer.Write("\t\t// Fields\n");
            var fieldEnd = typeDef.fieldStart + typeDef.field_count;
            for (int i = typeDef.fieldStart; i < fieldEnd; ++i)
            {
                var pField = metadata.Fields[i];
                var pType = il2cpp.Code.GetTypeFromTypeIndex(pField.typeIndex);
                var defaultValue = this.GetDefaultValue(i);

                WriteAttribute(writer, pField.customAttributeIndex, "\t\t");

                if ((pType.attrs & DefineConstants.FIELD_ATTRIBUTE_PINVOKE_IMPL) != 0) writer.Write("// pinvoke\n");

                writer.Write("\t\t");
                if ((pType.attrs & DefineConstants.FIELD_ATTRIBUTE_PRIVATE) == DefineConstants.FIELD_ATTRIBUTE_PRIVATE) writer.Write("private ");
                if ((pType.attrs & DefineConstants.FIELD_ATTRIBUTE_PUBLIC) == DefineConstants.FIELD_ATTRIBUTE_PUBLIC) writer.Write("public ");
                if ((pType.attrs & DefineConstants.FIELD_ATTRIBUTE_STATIC) != 0) writer.Write("static ");
                if ((pType.attrs & DefineConstants.FIELD_ATTRIBUTE_INIT_ONLY) != 0) writer.Write("readonly ");

                writer.Write($"{il2cpp.GetFullTypeName(pType)} {metadata.GetString(pField.nameIndex)}");
                if (defaultValue != null) writer.Write($" = {defaultValue}");
                writer.Write(";\n");
            }
        }

        internal void WriteMethods(StreamWriter writer, Il2CppTypeDefinition typeDef)
        {
            if (typeDef.method_count <= 0) return;

            writer.Write("\t\t// Methods\n");
            var methodEnd = typeDef.methodStart + typeDef.method_count;
            for (int i = typeDef.methodStart; i < methodEnd; ++i)
            {
                var methodDef = metadata.Methods[i];

                if (this.IncludeOffsets)
                {
                    if (methodDef.methodIndex >= 0)
                    {
                        var ptr = il2cpp.Code.MethodPointers[methodDef.methodIndex];
                        writer.Write("\t\t// Offset: 0x{0:x}\n", ptr);
                    }
                    else
                    {
                        writer.Write("\t\t// Offset: ?\n");
                    }
                }

                WriteAttribute(writer, methodDef.customAttributeIndex, "\t\t");
                if ((methodDef.flags & DefineConstants.METHOD_ATTRIBUTE_PINVOKE_IMPL) != 0)
                {
                    writer.Write("\t\t[DllImport()]\n");
                }

                writer.Write("\t\t");
                var pReturnType = il2cpp.Code.GetTypeFromTypeIndex(methodDef.returnType);
                if ((methodDef.flags & DefineConstants.METHOD_ATTRIBUTE_MEMBER_ACCESS_MASK) ==
                    DefineConstants.METHOD_ATTRIBUTE_PRIVATE)
                    writer.Write("private ");
                if ((methodDef.flags & DefineConstants.METHOD_ATTRIBUTE_MEMBER_ACCESS_MASK) ==
                    DefineConstants.METHOD_ATTRIBUTE_PUBLIC)
                    writer.Write("public ");
                if ((methodDef.flags & DefineConstants.METHOD_ATTRIBUTE_VIRTUAL) != 0)
                    writer.Write("virtual ");
                if ((methodDef.flags & DefineConstants.METHOD_ATTRIBUTE_STATIC) != 0)
                    writer.Write("static ");
                if ((methodDef.flags & DefineConstants.METHOD_ATTRIBUTE_PINVOKE_IMPL) != 0)
                    writer.Write("extern ");

                var methodName = metadata.GetString(methodDef.nameIndex);
                writer.Write($"{il2cpp.GetFullTypeName(pReturnType)} {methodName}(");
                for (int j = 0; j < methodDef.parameterCount; ++j)
                {
                    Il2CppParameterDefinition pParam = metadata.parameterDefs[methodDef.parameterStart + j];
                    string szParamName = metadata.GetString(pParam.nameIndex);
                    var pType = il2cpp.Code.GetTypeFromTypeIndex(pParam.typeIndex);
                    string szTypeName = il2cpp.GetFullTypeName(pType);
                    if ((pType.attrs & DefineConstants.PARAM_ATTRIBUTE_OPTIONAL) != 0)
                        writer.Write("optional ");
                    if ((pType.attrs & DefineConstants.PARAM_ATTRIBUTE_OUT) != 0)
                        writer.Write("out ");
                    if (j != methodDef.parameterCount - 1)
                    {
                        writer.Write($"{szTypeName} {szParamName}, ");
                    }
                    else
                    {
                        writer.Write($"{szTypeName} {szParamName}");
                    }
                }
                writer.Write(");\n");
            }
        }

        internal void WriteAttribute(StreamWriter writer, int attrIndex, string padding = "")
        {
            if (attrIndex < 0) return;

            var attributeTypeRange = metadata.AttributeInfos[attrIndex];
            for (var i = 0; i < attributeTypeRange.count; i++)
            {
                var typeIndex = metadata.AttributeTypes[attributeTypeRange.start + i];
                writer.Write("{0}[{1}]", padding, il2cpp.GetTypeName(il2cpp.Code.Types[typeIndex]));
                if (this.IncludeOffsets)
                {
                    writer.Write(" // 0x{0:x}", il2cpp.Code.CustomAttributes[attrIndex]);
                }
                writer.Write("\n");
            }
        }
    }
}
