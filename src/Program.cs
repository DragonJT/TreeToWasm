using Tree;

static class Program
{
    static void Main()
    {
        var tree = new Tree.Tree();

        var printi = tree.AddImportFunction("PrintI", new VoidType(), [new(new IntType(), "i")], "console.log(i);");

        var main = tree.AddExportFunction("Main", new IntType(), []);
        var a = new Local(new IntType());
        main.Init([a], new Block(
        [
            new Var(a, new Mul(new Int(4), new Int(6))),
            new Expr(new Call(printi, [a])),
            new Return(new Add(a, new Int(6)))
        ]));

        var html = tree.Emit();
        File.WriteAllText("index.html", html);
    }
}