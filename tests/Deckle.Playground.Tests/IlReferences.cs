using System.Reflection;
using System.Reflection.Emit;

namespace Deckle.Playground.Tests;

// What an assembly's compiled code writes to or calls, read from its IL. A
// test that must prove "this assembly never touches X" cannot lean on the
// referenced-assembly list once the assembly legitimately references X's
// module for other reasons; the instructions are what tell a read from a write.
//
// It sees direct references only: a write through reflection, or inside a
// method the assembly calls in another module, is out of its reach.
internal static class IlReferences
{
    private const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    // Every static field the assembly stores to, or takes the address of (which
    // is how a ref or out write reaches one), as "Type.Field".
    public static IReadOnlyList<string> StaticFieldWrites(Assembly assembly)
        => Scan(assembly)
            .Where(i => i.OpCode == OpCodes.Stsfld || i.OpCode == OpCodes.Ldsflda)
            .Select(i => ResolveField(i))
            .OfType<FieldInfo>()
            .Select(field => $"{field.DeclaringType!.FullName}.{field.Name}")
            .Distinct()
            .ToList();

    // Every method the assembly calls, or takes a delegate to, as "Type.Method".
    public static IReadOnlyList<string> MethodCalls(Assembly assembly)
        => Scan(assembly)
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt
                     || i.OpCode == OpCodes.Newobj || i.OpCode == OpCodes.Ldftn
                     || i.OpCode == OpCodes.Ldvirtftn)
            .Select(i => ResolveMethod(i))
            .OfType<MethodBase>()
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .Distinct()
            .ToList();

    private readonly record struct Instruction(MethodBase Method, OpCode OpCode, int Token);

    private static IEnumerable<Instruction> Scan(Assembly assembly)
    {
        foreach (var type in LoadableTypes(assembly))
        {
            var methods = type.GetMethods(AllDeclared).Cast<MethodBase>()
                .Concat(type.GetConstructors(AllDeclared));
            foreach (var method in methods)
            {
                byte[]? il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                catch (Exception) { continue; }
                if (il is null) continue;

                for (int at = 0; at < il.Length;)
                {
                    var opCode = il[at] == 0xFE
                        ? OpCodesByValue[(short)(0xFE00 | il[at + 1])]
                        : OpCodesByValue[il[at]];
                    at += opCode.Size;

                    int operandSize = opCode.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget
                            or OperandType.ShortInlineI
                            or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
                        _ => 4,
                    };

                    if (opCode.OperandType is OperandType.InlineField or OperandType.InlineMethod
                        or OperandType.InlineTok)
                        yield return new Instruction(method, opCode, BitConverter.ToInt32(il, at));

                    at += operandSize;
                }
            }
        }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
    }

    private static FieldInfo? ResolveField(Instruction instruction)
    {
        try
        {
            return instruction.Method.Module.ResolveField(
                instruction.Token, TypeArguments(instruction.Method), MethodArguments(instruction.Method));
        }
        catch (Exception) { return null; }
    }

    private static MethodBase? ResolveMethod(Instruction instruction)
    {
        try
        {
            return instruction.Method.Module.ResolveMethod(
                instruction.Token, TypeArguments(instruction.Method), MethodArguments(instruction.Method));
        }
        catch (Exception) { return null; }
    }

    private static Type[]? TypeArguments(MethodBase method)
        => method.DeclaringType is { IsGenericType: true } type ? type.GetGenericArguments() : null;

    private static Type[]? MethodArguments(MethodBase method)
        => method.IsGenericMethod ? method.GetGenericArguments() : null;
}
