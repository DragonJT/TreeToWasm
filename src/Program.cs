using WasmEmitter;

static class Program
{
    static void Main()
    {
        var tree = new TreeEmitter();
        var printI = tree.Add(new ImportFunction("PrintI", new VoidType(), [new(new IntType(), "i")], "console.log(i);"));
        var main = tree.Add(new ExportFunction("Main", new IntType(), []));
        main.Statement = new Block([
            new Expr(new Call(printI, [new Int(2)])),
            new Return(new Int(4))
        ]);

        var html = tree.Emit(true);
        File.WriteAllText("index.html", html);
    }
}