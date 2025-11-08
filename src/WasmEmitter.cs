namespace WasmEmitter;

using System.Security.Cryptography.X509Certificates;
using System.Text;

enum Opcode
{
    block = 0x02,
    loop = 0x03,
    br = 0x0c,
    br_if = 0x0d,
    end = 0x0b,
    ret = 0x0f,
    call = 0x10,
    drop = 0x1a,
    get_local = 0x20,
    set_local = 0x21,
    i32_store_8 = 0x3a,
    i32_const = 0x41,
    f32_const = 0x43,
    i32_eqz = 0x45,
    i32_eq = 0x46,
    f32_eq = 0x5b,
    f32_lt = 0x5d,
    f32_gt = 0x5e,
    i32_and = 0x71,
    f32_add = 0x92,
    f32_sub = 0x93,
    f32_mul = 0x94,
    f32_div = 0x95,
    f32_neg = 0x8c,
    i32_trunc_f32_s = 0xa8,
    f32_load = 0x2a,
    f32_store = 0x38,
    i32_store = 0x36,
    i32_load = 0x28,
    i32_mul = 0x6c,
    i32_add = 0x6a,
    i32_sub = 0x6b,
    i32_div_s = 0x6d,
    f32_convert_i32_s = 0xb2,
    i32_lt_s = 0x48,
    i32_gt_s = 0x4a,
    @if = 0x04,
}

enum SectionType
{
    Custom = 0,
    Type = 1,
    Import = 2,
    Func = 3,
    Table = 4,
    Memory = 5,
    Global = 6,
    Export = 7,
    Start = 8,
    Element = 9,
    Code = 10,
    Data = 11
}

enum Valtype
{
    Void = 0x40,
    I32 = 0x7f,
    F32 = 0x7d
}

enum ExportType
{
    Func = 0x00,
    Table = 0x01,
    Mem = 0x02,
    Global = 0x03
}

interface IType
{
    Valtype Valtype { get; }
}

interface IFunction { }

record Code(Opcode Opcode, object? Value=null);

record Parameter(IType Type, string Name);

record Local(IType Type);

record ImportFunction(string Name, IType ReturnType, Parameter[] Parameters, string Code) : IFunction;

interface IStatement { }

record Function(IType ReturnType, Local[] Parameters) : IFunction
{
    public IStatement? Statement = null;
}

record ExportFunction(string Name, IType ReturnType, Local[] Parameters) : IFunction
{
    public IStatement? Statement = null;
}

static class Printer
{
    public static string PrintParameters(Parameter[] parameters, bool printType)
    {
        string result = "(";
        for (var i = 0; i < parameters.Length; i++)
        {
            if (printType) result += parameters[i].Type + " ";
            result += parameters[i].Name;
            if (i < parameters.Length - 1)
            {
                result += ", ";
            }
        }
        result += ")";
        return result;
    }

    public static string PrintImportFunction(this ImportFunction f)
    {
        StringBuilder builder = new();
        builder.AppendLine("import " + f.Name + PrintParameters(f.Parameters, true));
        builder.Append('{');
        builder.AppendLine(f.Code);
        builder.AppendLine("}");
        return builder.ToString();
    }
}

abstract class WasmEmitter
{
    List<ImportFunction> importFunctions = [];
    List<Function> functions = [];
    List<ExportFunction> exportFunctions = [];
    Dictionary<IFunction, uint> functionIDs = [];

    public ImportFunction Add(ImportFunction importFunction)
    {
        importFunctions.Add(importFunction);
        return importFunction;
    }

    public Function Add(Function function)
    {
        functions.Add(function);
        return function;
    }

    public ExportFunction Add(ExportFunction exportFunction)
    {
        exportFunctions.Add(exportFunction);
        return exportFunction;
    }

    protected abstract Code[] GetCode(IStatement statement);

    const byte emptyArray = 0x0;
    const byte functionType = 0x60;

    static byte[] MagicModuleHeader => [0x00, 0x61, 0x73, 0x6d];

    static byte[] ModuleVersion => [0x01, 0x00, 0x00, 0x00];

    static byte[] Ieee754(float value)
    {
        return BitConverter.GetBytes(value);
    }

    static byte[] SignedLEB128(int value)
    {
        List<byte> bytes = [];
        bool more = true;

        while (more)
        {
            byte chunk = (byte)(value & 0x7fL); // extract a 7-bit chunk
            value >>= 7;

            bool signBitSet = (chunk & 0x40) != 0; // sign bit is the msb of a 7-bit byte, so 0x40
            more = !((value == 0 && !signBitSet) || (value == -1 && signBitSet));
            if (more) { chunk |= 0x80; } // set msb marker that more bytes are coming

            bytes.Add(chunk);
        }
        return bytes.ToArray();
    }

    static byte[] UnsignedLEB128(uint value)
    {
        List<byte> bytes = [];
        do
        {
            byte byteValue = (byte)(value & 0x7F); // Extract 7 bits
            value >>= 7; // Shift right by 7 bits

            if (value != 0)
                byteValue |= 0x80; // Set the high bit to indicate more bytes

            bytes.Add(byteValue);
        }
        while (value != 0);
        return [.. bytes];
    }

    static byte[] String(string value)
    {
        List<byte> bytes = [.. UnsignedLEB128((uint)value.Length)];
        foreach (var v in value)
        {
            bytes.Add((byte)v);
        }
        return [.. bytes];
    }

    static byte[] Vector(byte[][] vector)
    {
        return [.. UnsignedLEB128((uint)vector.Length), .. vector.SelectMany(b => b).ToArray()];
    }

    static byte[] Vector(byte[] vector)
    {
        return [.. UnsignedLEB128((uint)vector.Length), .. vector];
    }

    static byte[] Local(Valtype valtype, uint count)
    {
        return [.. UnsignedLEB128(count), (byte)valtype];
    }

    static byte[] Section(SectionType section, byte[][] bytes)
    {
        return [(byte)section, .. Vector(Vector(bytes))];
    }

    static byte[] Return(Valtype type)
    {
        if (type == Valtype.Void)
        {
            return [emptyArray];
        }
        else
        {
            return Vector([(byte)type]);
        }
    }

    byte[] EmitCode(Local[] parameters, IStatement statement)
    {
        HashSet<Local> i32Locals = [];
        HashSet<Local> f32Locals = [];

        void AddLocal(Local local)
        {
            if (local.Type.Valtype == Valtype.I32) i32Locals.Add(local);
            else if (local.Type.Valtype == Valtype.F32) f32Locals.Add(local);
            else throw new Exception("Error expecting i32 or f32");
        }

        var code = GetCode(statement);
        foreach (var c in code)
        {
            if (c.Opcode == Opcode.get_local) AddLocal((Local)c.Value!);
            if (c.Opcode == Opcode.set_local) AddLocal((Local)c.Value!);
        }
        
        Dictionary<Local, uint> localIDs = [];
        uint lid = 0;

        void AddLocalID(Local local)
        {
            localIDs.Add(local, lid); 
            lid++;
        }
        foreach (var p in parameters) AddLocalID(p);
        foreach (var l in i32Locals) AddLocalID(l);
        foreach (var l in f32Locals) AddLocalID(l);

        List<byte[]> localBytes = [];
        if (i32Locals.Count > 0) localBytes.Add(Local(Valtype.I32, (uint)i32Locals.Count));
        if (f32Locals.Count > 0) localBytes.Add(Local(Valtype.F32, (uint)f32Locals.Count));

        List<byte> codeBytes = [];
        foreach(var c in code)
        {
            if (c.Opcode == Opcode.i32_const)
            {
                codeBytes.AddRange([(byte)Opcode.i32_const, .. SignedLEB128((int)c.Value!)]);
            }
            else if (c.Opcode == Opcode.call)
            {
                codeBytes.AddRange([(byte)Opcode.call, ..UnsignedLEB128(functionIDs[(IFunction)c.Value!])]);
            }
            else
            {
                codeBytes.Add((byte)c.Opcode);
            } 
        }
        return Vector([.. Vector([.. localBytes]), .. codeBytes, (byte)Opcode.end]);
    }

    public string Emit(bool memory)
    {
        foreach (var f in importFunctions) functionIDs.Add(f, (uint)functionIDs.Count);
        foreach (var f in functions) functionIDs.Add(f, (uint)functionIDs.Count);
        foreach (var f in exportFunctions) functionIDs.Add(f, (uint)functionIDs.Count);

        List<byte[]> codeSection = [];
        foreach (var f in functions) codeSection.Add(EmitCode(f.Parameters, f.Statement!));
        foreach (var f in exportFunctions) codeSection.Add(EmitCode(f.Parameters, f.Statement!));

        List<byte[]> importSection = [];
        foreach (var f in importFunctions)
        {
            importSection.Add([
                ..String("env"),
                ..String(f.Name),
                (byte)ExportType.Func,
                ..UnsignedLEB128(functionIDs[f])
            ]);
        }

        if (memory)
        {
            importSection.Add([
                ..String("env"),
                ..String("memory"),
                (byte)ExportType.Mem,
                /* limits https://webassembly.github.io/spec/core/binary/types.html#limits -
                indicates a min memory size of one page */
                0x00,
                ..UnsignedLEB128(10),
            ]);
        }

        List<byte[]> typeSection = [];
        foreach (var f in importFunctions)
        {
            typeSection.Add([functionType, .. Vector(f.Parameters.Select(p => (byte)p.Type.Valtype).ToArray()), .. Return(f.ReturnType.Valtype)]);
        }
        foreach (var f in functions)
        {
            typeSection.Add([functionType, .. Vector(f.Parameters.Select(p => (byte)p.Type.Valtype).ToArray()), .. Return(f.ReturnType.Valtype)]);
        }
        foreach (var f in exportFunctions)
        {
            typeSection.Add([functionType, .. Vector(f.Parameters.Select(p => (byte)p.Type.Valtype).ToArray()), .. Return(f.ReturnType.Valtype)]);
        }

        List<byte[]> funcSection = [];
        foreach (var f in functions)
        {
            funcSection.Add(UnsignedLEB128(functionIDs[f]));
        }
        foreach (var f in exportFunctions)
        {
            funcSection.Add(UnsignedLEB128(functionIDs[f]));
        }

        List<byte[]> exportSection = [];
        foreach (var f in exportFunctions)
        {
            exportSection.Add([.. String(f.Name), (byte)ExportType.Func, .. UnsignedLEB128(functionIDs[f])]);
        }

        byte[] wasm = [
            .. MagicModuleHeader,
            .. ModuleVersion,
            .. Section(SectionType.Type, [..typeSection]),
            .. Section(SectionType.Import, [.. importSection]),
            .. Section(SectionType.Func, [..funcSection]),
            .. Section(SectionType.Export, [..exportSection]),
            .. Section(SectionType.Code, [..codeSection])];

        //File.WriteAllBytes("run.wasm", wasm);

        StringBuilder importString = new();
        foreach (var f in importFunctions)
        {
            importString.AppendLine("imports.env." + f.Name + "=function" + Printer.PrintParameters(f.Parameters, false));
            importString.Append('{');
            importString.AppendLine(f.Code);
            importString.AppendLine("}");
        }
        if (memory)
        {
            importString.AppendLine("imports.env.memory = new WebAssembly.Memory({ initial: 10, maximum: 10 });");
        }
        string wasmString = string.Join(",", wasm.Select(b => "0x" + b.ToString("X2")));
        var html = @"
<!DOCTYPE html>
<html>
<head>
  <title>WebAssembly Example</title>
</head>
<body>
  <script>
const wasmBytecode = new Uint8Array([
" + wasmString +
@"]);
var globals = {};
var imports = {};
var exports;
imports.env = {};
" +
importString
+ @"
WebAssembly.instantiate(wasmBytecode, imports)
  .then(module => {
    exports = module.instance.exports;
    console.log(module.instance.exports.Main());
  })
  .catch(error => {
    console.error('Error:', error);
  });
  </script>
</body>
</html>";
        return html;
    }
}