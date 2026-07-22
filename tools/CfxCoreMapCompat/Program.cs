using System.Collections;
using System.Dynamic;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: CfxCoreMapCompat /path/to/CitizenFX.Core.Server.dll");
    return 2;
}

var path = Path.GetFullPath(args[0]);
var module = ModuleDefinition.ReadModule(path, new ReaderParameters
{
    ReadWrite = false,
    InMemory = true,
});

var formatter = module.Types.Single(type =>
    type.FullName == "CitizenFX.Core.Serialization.AdjustedPrimitiveObjectFormatter");
var deserialize = formatter.Methods.Single(method =>
    method.Name == "Deserialize" && method.Parameters.Count == 2);
var deserializeMap = deserialize.Body.Instructions.Single(instruction =>
    instruction.Operand is MethodReference method &&
    method.DeclaringType.FullName == "MessagePack.Formatters.PrimitiveObjectFormatter" &&
    method.Name == "DeserializeMap");

var objectType = module.TypeSystem.Object;
var stringType = module.TypeSystem.String;
var dictionaryType = module.ImportReference(typeof(IDictionary));
var dictionaryEnumeratorType = module.ImportReference(typeof(IDictionaryEnumerator));
var dictionaryEntryType = module.ImportReference(typeof(DictionaryEntry));
var stringObjectDictionaryType = module.ImportReference(typeof(IDictionary<string, object>));
var expandoType = module.ImportReference(typeof(ExpandoObject));

var getEnumerator = module.ImportReference(typeof(IDictionary).GetMethod("GetEnumerator")!);
var moveNext = module.ImportReference(typeof(IEnumerator).GetMethod("MoveNext")!);
var getEntry = module.ImportReference(typeof(IDictionaryEnumerator).GetProperty("Entry")!.GetMethod!);
var getKey = module.ImportReference(typeof(DictionaryEntry).GetProperty("Key")!.GetMethod!);
var getValue = module.ImportReference(typeof(DictionaryEntry).GetProperty("Value")!.GetMethod!);
var setItem = module.ImportReference(typeof(IDictionary<string, object>)
    .GetProperty("Item")!.SetMethod!);
var expandoConstructor = module.ImportReference(typeof(ExpandoObject).GetConstructor(Type.EmptyTypes)!);

var convertMap = new MethodDefinition(
    "EnhancedCompat_DynamicMap",
    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
    objectType);
convertMap.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, objectType));
convertMap.Body.InitLocals = true;
formatter.Methods.Add(convertMap);

var source = new VariableDefinition(dictionaryType);
var target = new VariableDefinition(stringObjectDictionaryType);
var enumerator = new VariableDefinition(dictionaryEnumeratorType);
var entry = new VariableDefinition(dictionaryEntryType);
var key = new VariableDefinition(stringType);
convertMap.Body.Variables.Add(source);
convertMap.Body.Variables.Add(target);
convertMap.Body.Variables.Add(enumerator);
convertMap.Body.Variables.Add(entry);
convertMap.Body.Variables.Add(key);

var il = convertMap.Body.GetILProcessor();
var returnOriginal = Instruction.Create(OpCodes.Ldarg_0);
var loopCheck = Instruction.Create(OpCodes.Ldloc, enumerator);
var loopBody = Instruction.Create(OpCodes.Ldloc, enumerator);

il.Emit(OpCodes.Ldarg_0);
il.Emit(OpCodes.Isinst, dictionaryType);
il.Emit(OpCodes.Stloc, source);
il.Emit(OpCodes.Ldloc, source);
il.Emit(OpCodes.Brfalse, returnOriginal);
il.Emit(OpCodes.Newobj, expandoConstructor);
il.Emit(OpCodes.Castclass, stringObjectDictionaryType);
il.Emit(OpCodes.Stloc, target);
il.Emit(OpCodes.Ldloc, source);
il.Emit(OpCodes.Callvirt, getEnumerator);
il.Emit(OpCodes.Stloc, enumerator);
il.Emit(OpCodes.Br, loopCheck);

il.Append(loopBody);
il.Emit(OpCodes.Callvirt, getEntry);
il.Emit(OpCodes.Stloc, entry);
il.Emit(OpCodes.Ldloca, entry);
il.Emit(OpCodes.Call, getKey);
il.Emit(OpCodes.Isinst, stringType);
il.Emit(OpCodes.Stloc, key);
il.Emit(OpCodes.Ldloc, key);
il.Emit(OpCodes.Brfalse, returnOriginal);
il.Emit(OpCodes.Ldloc, target);
il.Emit(OpCodes.Ldloc, key);
il.Emit(OpCodes.Ldloca, entry);
il.Emit(OpCodes.Call, getValue);
il.Emit(OpCodes.Callvirt, setItem);

il.Append(loopCheck);
il.Emit(OpCodes.Callvirt, moveNext);
il.Emit(OpCodes.Brtrue, loopBody);
il.Emit(OpCodes.Ldloc, target);
il.Emit(OpCodes.Ret);
il.Append(returnOriginal);
il.Emit(OpCodes.Ret);

deserialize.Body.GetILProcessor().InsertAfter(
    deserializeMap,
    Instruction.Create(OpCodes.Call, convertMap));

var temporaryPath = path + ".patched";
module.Write(temporaryPath);
File.Move(temporaryPath, path, overwrite: true);

Console.WriteLine($"Enabled legacy dynamic-map compatibility in {path}");
return 0;
