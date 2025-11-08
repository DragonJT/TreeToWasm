namespace WasmEmitter;

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

interface IExpression { }

record Add(IExpression Left, IExpression Right) : IExpression;

record Mul(IExpression Left, IExpression Right) : IExpression;

record Call(IFunction Function, IExpression[] Args) : IExpression;

record Int(int Value):IExpression;

record Return(IExpression Expression) : IStatement;

record Var(Local Local, IExpression Expression) : IStatement;

record Block(IStatement[] Statements) : IStatement;

record Expr(IExpression Expression) : IStatement;

record While(IExpression Condition, IStatement Statement) : IStatement;

class TreeEmitter : WasmEmitter
{
    static Code[] GetCode(IExpression expression)
    {
        if (expression is Int i)
        {
            return [new(Opcode.i32_const, i.Value)];
        }
        else if (expression is Call c)
        {
            return [.. c.Args.SelectMany(GetCode), new(Opcode.call, c.Function)];
        }
        else
        {
            throw new Exception(expression.GetType().Name);
        }
    }

    protected override Code[] GetCode(IStatement statement)
    {
        if (statement is Block blocK)
        {
            List<Code> code = [];
            foreach (var s in blocK.Statements)
            {
                code.AddRange(GetCode(s));
            }
            return [.. code];
        }
        else if (statement is Return rtn)
        {
            return [.. GetCode(rtn.Expression), new(Opcode.ret, null)];
        }
        else if (statement is Expr expr)
        {
            return GetCode(expr.Expression);
        }
        else
        {
            throw new Exception(statement.GetType().Name);
        }
    }
}