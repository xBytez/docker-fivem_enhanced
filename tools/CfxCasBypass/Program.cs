using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: CfxCasBypass /path/to/CitizenFX.Host.dll");
    return 2;
}

var path = Path.GetFullPath(args[0]);
var module = ModuleDefinition.ReadModule(path, new ReaderParameters
{
    ReadWrite = false,
    InMemory = true,
});

var loadContext = module.Types.Single(type => type.FullName == "CitizenFX.Host.ScriptAssemblyLoadContext");
var constructor = loadContext.Methods.Single(method =>
    method.IsConstructor && !method.IsStatic && method.Parameters.Count == 4);
var sandboxCheck = constructor.Body.Instructions.Single(instruction =>
    instruction.Operand is MethodReference method &&
    method.DeclaringType.FullName == "CitizenFX.Base.NativeApi" &&
    method.Name == "IsSandboxingEnabled");

// Preserve the stack shape of the instance call but force its result to false.
// This selects FiveM's existing "sandbox disabled" path, which installs the
// diagnostic violation handler while leaving P/Invoke and mixed-mode assembly
// rejection in ScriptAssemblyLoadContext.InstrumentAssembly intact.
sandboxCheck.OpCode = OpCodes.Pop;
sandboxCheck.Operand = null;
constructor.Body.GetILProcessor().InsertAfter(sandboxCheck, Instruction.Create(OpCodes.Ldc_I4_0));

var debugHandler = module.Types.Single(type =>
    type.FullName == "DouglasDwyer.CasCore.DebugViolationHandler");
var onViolation = debugHandler.Methods.Single(method => method.Name == "OnViolation");
onViolation.Body = new MethodBody(onViolation);
onViolation.Body.GetILProcessor().Emit(OpCodes.Ret);

// Legacy but otherwise managed dependencies (notably MySqlConnector and
// Dapper) carry UnverifiableCodeAttribute when compiled with unsafe enabled.
// Bypass the host's attribute-level rejection. The downstream IL verifier and
// the explicit P/Invoke and mixed-mode checks still run.
var unverifiableAttributeCheck = constructor.DeclaringType.Methods
    .Single(method => method.Name == "InstrumentAssembly")
    .Body.Instructions.Single(instruction =>
        instruction.Operand is GenericInstanceMethod method &&
        method.ElementMethod.DeclaringType.FullName == "System.Linq.Enumerable" &&
        method.Name == "Any" &&
        method.GenericArguments.Count == 1 &&
        method.GenericArguments[0].FullName == "Mono.Cecil.CustomAttribute");
unverifiableAttributeCheck.OpCode = OpCodes.Pop;
unverifiableAttributeCheck.Operand = null;
var rewriter = constructor.DeclaringType.Methods
    .Single(method => method.Name == "InstrumentAssembly").Body.GetILProcessor();
rewriter.InsertAfter(unverifiableAttributeCheck, Instruction.Create(OpCodes.Pop));
rewriter.InsertAfter(unverifiableAttributeCheck.Next, Instruction.Create(OpCodes.Ldc_I4_0));

var temporaryPath = path + ".patched";
module.Write(temporaryPath);
File.Move(temporaryPath, path, overwrite: true);

var verifierPath = Path.Combine(Path.GetDirectoryName(path)!, "JitIlVerification.dll");
var verifierModule = ModuleDefinition.ReadModule(verifierPath, new ReaderParameters
{
    ReadWrite = false,
    InMemory = true,
});
var verifierType = verifierModule.Types.Single(type =>
    type.FullName == "DouglasDwyer.JitIlVerification.VerifiableAssemblyLoader");
var instrumentAssembly = verifierType.Methods.Single(method =>
    method.Name == "InstrumentAssembly" && method.Parameters.Count == 1);
var originalFirst = instrumentAssembly.Body.Instructions[0];
var instrumentIl = instrumentAssembly.Body.GetILProcessor();
var getAssemblyName = verifierModule.ImportReference(
    typeof(AssemblyDefinition).GetProperty(nameof(AssemblyDefinition.Name))!.GetMethod!);
var getSimpleName = verifierModule.ImportReference(
    typeof(AssemblyNameReference).GetProperty(nameof(AssemblyNameReference.Name))!.GetMethod!);
var stringEquality = verifierModule.ImportReference(typeof(string).GetMethod(
    "op_Equality", new[] { typeof(string), typeof(string) })!);
var allowlistedReturn = Instruction.Create(OpCodes.Ret);

// These database libraries deliberately use optimized, unverifiable managed
// IL. Skip guard injection only for these assemblies. All resource assemblies
// and every other dependency continue through FiveM's verifier.
foreach (var assemblyName in new[]
         {
             "Dapper",
             "Dapper.Contrib",
             "Dapper.Transaction",
             "MySqlConnector",
         })
{
    instrumentIl.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldarg_1));
    instrumentIl.InsertBefore(originalFirst, Instruction.Create(OpCodes.Callvirt, getAssemblyName));
    instrumentIl.InsertBefore(originalFirst, Instruction.Create(OpCodes.Callvirt, getSimpleName));
    instrumentIl.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldstr, assemblyName));
    instrumentIl.InsertBefore(originalFirst, Instruction.Create(OpCodes.Call, stringEquality));
    instrumentIl.InsertBefore(originalFirst, Instruction.Create(OpCodes.Brtrue, allowlistedReturn));
}
instrumentIl.Append(allowlistedReturn);

var verifierTemporaryPath = verifierPath + ".patched";
verifierModule.Write(verifierTemporaryPath);
File.Move(verifierTemporaryPath, verifierPath, overwrite: true);

Console.WriteLine($"Disabled C# CAS policy checks in {Path.GetDirectoryName(path)}; " +
    "IL verification remains enabled except for the managed database allowlist");
return 0;
