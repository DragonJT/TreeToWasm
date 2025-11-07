using WasmEmitter;

namespace Tree;

interface IMember{}

interface IFunction : IMember
{
    uint ID { get; }
}

interface IType
{
    Valtype Valtype { get; }
}

record IntType : IType
{
    public Valtype Valtype => Valtype.I32;
}

record VoidType : IType
{
    public Valtype Valtype => Valtype.Void;
}

record FloatType : IType
{
    public Valtype Valtype => Valtype.F32;
}

interface IExpression
{
    void Emit(WasmEmitter.Function function);
}

interface IStatement
{
    void Emit(WasmEmitter.Function function);
}

class Local(IType type) : IExpression
{
    public IType type = type;
    public uint? id;

    public void Emit(WasmEmitter.Function function)
    {
        function.GetLocal(id!.Value);
    }
}

record Add(IExpression Left, IExpression Right) : IExpression
{
    public void Emit(WasmEmitter.Function function)
    {
        Left.Emit(function);
        Right.Emit(function);
        function.I32Add();
    }
}

record Mul(IExpression Left, IExpression Right) : IExpression
{
    public void Emit(WasmEmitter.Function function)
    {
        Left.Emit(function);
        Right.Emit(function);
        function.I32Mul();
    }
}

record Call(IFunction Function, IExpression[] args) : IExpression
{
    public void Emit(WasmEmitter.Function function)
    {
        foreach(var a in args)
        {
            a.Emit(function);
        }
        function.Call(Function.ID);
    }
}

record Int(int Value) : IExpression
{
    public void Emit(WasmEmitter.Function function)
    {
        function.I32Const(Value);
    }
}

record Return(IExpression Expression) : IStatement
{
    public void Emit(WasmEmitter.Function function)
    {
        Expression.Emit(function);
        function.Ret();
    }
}

record Var(Local Local, IExpression Expression) : IStatement
{
    public void Emit(WasmEmitter.Function function)
    {
        Expression.Emit(function);
        function.SetLocal(Local.id!.Value);
    }
}

record Block(IStatement[] Statements) : IStatement
{
    public void Emit(WasmEmitter.Function function)
    {
        foreach (var s in Statements)
        {
            s.Emit(function);
        }
    }
}

record Expr(IExpression Expression) : IStatement
{
    public void Emit(WasmEmitter.Function function)
    {
        Expression.Emit(function);
    }
}


record Parameter(IType Type, string Name);

class ImportFunction(string name, IType returnType, Parameter[] parameters, string code) : IFunction
{
    uint? id;

    public uint ID
    {
        get => id!.Value;
        set => id = value;
    } 

    public WasmEmitter.ImportFunction Emit()
    {
        return new WasmEmitter.ImportFunction(
            ID,
            name,
            returnType.Valtype,
            [.. parameters.Select(p => new WasmEmitter.Parameter(p.Type.Valtype, p.Name))],
            code);
    }
}

class Function : IFunction
{
    uint? id;
    protected IType returnType;
    protected Local[] parameters;
    IStatement? body;
    List<Local> locals = [];

    public uint ID
    {
        get => id!.Value;
        set => id = value;
    } 

    public Function(IType returnType, Local[] parameters)
    {
        this.returnType = returnType;
        this.parameters = parameters;
    }

    public void Init(Local[] locals, IStatement body)
    {
        this.locals.AddRange(locals);
        this.body = body;
    }

    protected WasmEmitter.Function Emit(WasmEmitter.Function f)
    {
        uint pID = 0;
        foreach (var p in parameters) { p.id = pID; pID++; }

        var i32Locals = locals.Where(l => l.type.Valtype == Valtype.I32).ToArray();
        var f32Locals = locals.Where(l => l.type.Valtype == Valtype.F32).ToArray();
        foreach (var l in i32Locals) { l.id = pID; pID++; }
        foreach (var l in f32Locals) { l.id = pID; pID++; }
        if (i32Locals.Length > 0) f.AddLocals(Valtype.I32, (uint)i32Locals.Length);
        if (f32Locals.Length > 0) f.AddLocals(Valtype.F32, (uint)f32Locals.Length);

        body?.Emit(f);
        return f;
    }

    public virtual WasmEmitter.Function Emit()
    {
        var f = new WasmEmitter.Function(ID, returnType.Valtype, [.. parameters.Select(p => p.type.Valtype)]);
        Emit(f);
        return f;
    }
}

class ExportFunction(string name, IType returnType, Local[] parameters) : Function(returnType, parameters)
{
    public override WasmEmitter.Function Emit()
    {
        var f = new WasmEmitter.ExportFunction(ID, name, returnType.Valtype, [.. parameters.Select(p => p.type.Valtype)]);
        Emit(f);
        return f;
    }
}

class Tree
{
    List<ImportFunction> importFunctions = [];
    List<Function> functions = [];

    public ImportFunction AddImportFunction(string name, IType returnType, Parameter[] parameters, string code)
    {
        var f = new ImportFunction(name, returnType, parameters, code);
        importFunctions.Add(f);
        return f;
    }

    public Function AddFunction(IType returnType, Local[] parameters)
    {
        var f = new Function(returnType, parameters);
        functions.Add(f);
        return f;
    }

    public ExportFunction AddExportFunction(string name, IType returnType, Local[] parameters)
    {
        var f = new ExportFunction(name, returnType, parameters);
        functions.Add(f);
        return f;
    }

    public string Emit()
    {
        uint fID = 0;
        foreach(var f in importFunctions){ f.ID = fID; fID++; }
        foreach(var f in functions){ f.ID = fID; fID++; }
        return WasmEmitter.WasmEmitter.Emit(
            [.. importFunctions.Select(f => f.Emit())],
            [.. functions.Select(f => f.Emit())],
            true);
    }
}