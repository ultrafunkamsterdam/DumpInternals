using Il2CppInspector;
using Il2CppInspector.Structures;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace Il2CppDumper.Dumpers
{
    internal class Il2CppNestedOf
    {
        public string Name { get; set; }
        public string ItemType { get; set; }
    }

    public class StructDumper : BaseDumper
    {
        private IList<Il2CppTypeDefinition> interestingTypes;
        private int enumIdx = -1;
        private List<GenericIl2CppType> typesToDump = new List<GenericIl2CppType>();
        private List<Il2CppNestedOf> arrayTypesToDump = new List<Il2CppNestedOf>();
        private List<Il2CppNestedOf> repeatingTypesToDump = new List<Il2CppNestedOf>();

        public StructDumper(Il2CppProcessor proc) : base(proc) { }
        
        public override void DumpToFile(string outFile) {
            enumIdx = FindTypeIndex("Enum");

            interestingTypes = metadata.Types.Where(t =>
            {
                var nameSpace = metadata.GetString(t.namespaceIndex);
                var name = metadata.GetString(t.nameIndex);
                return nameSpace.StartsWith("Holo" + "holo.Rpc") ||
                        name == "Result" || name == "Request";

            }).Select(t => t).ToList();

            if (interestingTypes.Count() == 0) return;

            using (var writer = new StreamWriter(new FileStream(outFile, FileMode.Create))) {
                this.WriteHeaders(writer);

                // dump types
                var types = interestingTypes.Where(t => t.parentIndex != enumIdx);
                foreach (var typeDef in types)
                {
                    this.WriteType(writer, typeDef);
                }

                // dump subtypes
                for (var i = 0; i < typesToDump.Count(); i++)
                {
                    var realType = typesToDump[i];
                    if (realType.type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
                    {
                        realType = il2cpp.GetTypeFromGeneric(realType);
                    }
                    var subtypeDef = metadata.Types[realType.klassIndex];
                    if (realType.type == Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE || realType.type == Il2CppTypeEnum.IL2CPP_TYPE_CLASS)
                    {
                        if (!interestingTypes.Any(t => t.nameIndex == subtypeDef.nameIndex))
                        {
                            if (subtypeDef.parentIndex != enumIdx)
                            {
                                this.WriteType(writer, subtypeDef);
                            }
                        }
                    }
                }

                // dump repeating types
                foreach (var pType in repeatingTypesToDump)
                {
                    writer.Write($"struct {pType.Name} : public Il2CppObject\n");
                    writer.Write("{\n");
                    writer.Write($"\t {pType.ItemType} array;\n");
                    writer.Write($"\t int count;\n");
                    writer.Write("}\n\n");
                }

                // dump array types
                foreach (var pType in arrayTypesToDump)
                {
                    writer.Write($"struct {pType.Name} : public Il2CppArray\n");
                    writer.Write("{\n");
                    writer.Write($"\tALIGN_FIELD(8) {pType.ItemType} items[1];\n");
                    writer.Write("}\n\n");
                }
            }
        }

        internal void WriteHeaders(StreamWriter writer)
        {
            writer.Write("struct Il2CppObject\n");
            writer.Write("{\n");
            writer.Write("\tvoid *klass; // Il2CppClass *\n");
            writer.Write("\tvoid *monitor; // MonitorData *\n");
            writer.Write("}\n\n");

            writer.Write("struct Il2CppArray : public Il2CppObject\n");
            writer.Write("{\n");
            writer.Write("\tvoid *bounds;\n");
            writer.Write("\tint max_length;\n");
            writer.Write("}\n\n");

            writer.Write("struct Il2CppString\n");
            writer.Write("{\n");
            writer.Write("\tIl2CppObject object;\n");
            writer.Write("\tint length;\n");
            writer.Write("\tchar16_t *chars;\n");
            writer.Write("}\n\n");
    }

        internal void WriteType(StreamWriter writer, Il2CppTypeDefinition typeDef)
        {
            if ((typeDef.flags & DefineConstants.TYPE_ATTRIBUTE_INTERFACE) != 0) return;
           
            var nameSpace = metadata.GetTypeNamespace(typeDef);
            if (nameSpace.Length > 0) nameSpace += ".";

            var typeName = metadata.GetTypeName(typeDef);
            writer.Write($"struct {typeName}");

            if (typeDef.parentIndex >= 0)
            {
                var pType = il2cpp.Code.GetTypeFromTypeIndex(typeDef.parentIndex);
                var name = il2cpp.GetTypeName(pType);
                if (name == "object")
                {
                    writer.Write($" : public Il2CppObject"); 
                }
                else if (name != "ValueType")
                {
                    writer.Write($" : public {name}");
                }
            }

            writer.Write("\n{\n");

            this.WriteFields(writer, typeDef);

            writer.Write("}\n\n");
        }

        internal void WriteFields(StreamWriter writer, Il2CppTypeDefinition typeDef)
        {
            if (typeDef.field_count <= 0) return;

            var fieldEnd = typeDef.fieldStart + typeDef.field_count;
            for (int i = typeDef.fieldStart; i < fieldEnd; ++i)
            {
                var pField = metadata.Fields[i];
                var pType = il2cpp.Code.GetTypeFromTypeIndex(pField.typeIndex);

                if ((pType.attrs & DefineConstants.FIELD_ATTRIBUTE_STATIC) == 0)
                {
                    var fieldname = metadata.GetString(pField.nameIndex);
                    string typename = "";
                    if (pType.type == Il2CppTypeEnum.IL2CPP_TYPE_CLASS || pType.type == Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
                    {
                        var fieldTypeDef = metadata.Types[pType.klassIndex];
                        if (fieldTypeDef.parentIndex == enumIdx)
                        {
                            typename = "int";
                        }
                        else if ((fieldTypeDef.flags & DefineConstants.TYPE_ATTRIBUTE_INTERFACE) != 0)
                        {
                            typename = "void *";
                        }
                        else
                        {
                            typename = this.GetStructType(il2cpp.GetTypeName(pType));
                        }
                    }
                    else
                    {
                        typename = this.GetStructType(il2cpp.GetTypeName(pType));
                    }

                    writer.Write($"\t{typename} {fieldname};\n");

                    if (pType.type == Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE || pType.type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
                    {
                        this.AddTypeToDump(pType);
                    }
                }
            }
        }

        private void AddTypeToDump(GenericIl2CppType pType)
        {
            if (!typesToDump.Any(t => t.klassIndex == pType.klassIndex))
            {
                typesToDump.Add(pType);
            }
        }

        private void AddArrayTypeToDump(string name, string itemType)
        {
            if (name.EndsWith("*")) name = name.Substring(0, name.Length - 1);

            if (!arrayTypesToDump.Any(t => t.ItemType == itemType))
            {
                arrayTypesToDump.Add(new Il2CppNestedOf()
                {
                    Name = name,
                    ItemType = itemType,
                });
            }
        }

        private void AddRepeatingTypeToDump(string name, string itemType)
        {
            if (name.EndsWith("*")) name = name.Substring(0, name.Length - 1);

            if (!repeatingTypesToDump.Any(t => t.ItemType == itemType))
            {
                repeatingTypesToDump.Add(new Il2CppNestedOf()
                {
                    Name = name,
                    ItemType = itemType,
                });
            }
        }

        internal string GetStructType(string typeName)
        {
            string[] types = { "int", "uint", "long", "ulong" };
            if (typeName == "int" || typeName == "long")
            {
                //
            }
            else if (typeName == "byte")
            {
                typeName = "uint8_t";
            }
            else if (typeName == "uint" || typeName == "ulong")
            {
                typeName = "unsigned " + typeName.Substring(1);
            }
            else if (typeName == "string")
            {
                typeName = "Il2CppString *";
            }
            else if (typeName.StartsWith("FieldCodec`1"))
            {
                typeName = typeName.Substring("FieldCodec`1".Length + 1, typeName.Length - "FieldCodec`1".Length - 2);
                var itemType = this.GetStructType(typeName);
                itemType = itemType.Replace(" ", "");
                typeName = "Il2CppArrayOf" + itemType;
                AddArrayTypeToDump(typeName, itemType);
            }
            else if (typeName.StartsWith("RepeatedField`1"))
            {
                typeName = typeName.Substring("RepeatedField`1".Length + 1, typeName.Length - "RepeatedField`1".Length - 2);
                var itemType = this.GetStructType(typeName);
                itemType = itemType.Replace(" ", "");
                typeName = "RepeatingOf" + itemType;
                AddRepeatingTypeToDump("RepeatingOf" + itemType, "Il2CppArrayOf" + itemType);
                AddArrayTypeToDump("Il2CppArrayOf" + itemType, itemType);
            }
            else if (typeName.EndsWith("[]"))
            {
                typeName = typeName.Substring(0, typeName.Length - 2);
                typeName = typeName.Replace(" ", "");
                AddArrayTypeToDump("Il2CppArrayOf" + typeName, this.GetStructType(typeName));
                typeName = "Il2CppArrayOf" + typeName + "*";
            }
            else
            {
                typeName = typeName + " *";
            }
            
            return typeName;
        }
    }
}
